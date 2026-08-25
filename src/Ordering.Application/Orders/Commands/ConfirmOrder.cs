using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Options;
using Ordering.Application.Abstractions;
using Ordering.Application.Common;
using Ordering.Domain;
using Ordering.Domain.Guardrails;
using Ordering.Domain.Orders;

namespace Ordering.Application.Orders.Commands;

/// <summary>
/// The only 402-gated call. Not an <see cref="ICommand{TResponse}"/>: facilitator
/// I/O must not run inside the pipeline's write transaction. recordSettlement
/// (and expired-at-confirm) open their own short transactions.
/// </summary>
public sealed record ConfirmOrderCommand(
    Guid OrderId,
    string CustomerId,
    string? PaymentHeader,
    string ResourceUrl) : IRequest<Result<OrderDto>>;

public sealed class ConfirmOrderCommandValidator : AbstractValidator<ConfirmOrderCommand>
{
    public ConfirmOrderCommandValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.CustomerId).NotEmpty().MaximumLength(200);
        RuleFor(c => c.ResourceUrl).NotEmpty();
    }
}

public sealed class ConfirmOrderCommandHandler(
    IOrderWriteRepository orders,
    IPaymentRepository payments,
    IUnitOfWork unitOfWork,
    ITransactionManager transactions,
    IFacilitator facilitator,
    IOptions<OrderingOptions> options,
    TimeProvider clock) : IRequestHandler<ConfirmOrderCommand, Result<OrderDto>>
{
    public async Task<Result<OrderDto>> Handle(ConfirmOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(command.OrderId, cancellationToken);
        if (order is null || order.CustomerId != command.CustomerId)
        {
            return Result<OrderDto>.Fail(ErrorKind.NotFound, "Order not found.");
        }

        var existingPayment = await payments.FindByOrderIdAsync(command.OrderId, cancellationToken);
        if (existingPayment is not null)
        {
            return Result<OrderDto>.Ok(OrderMapper.ToDto(order));
        }

        var now = clock.GetUtcNow();
        if (order.Status == OrderStatus.Draft && now >= order.ExpiresAt)
        {
            return await transactions.InTransactionAsync(() => ExpireAtConfirmAsync(command.OrderId, now, cancellationToken), cancellationToken);
        }

        if (order.Status != OrderStatus.Draft)
        {
            return Result<OrderDto>.Ok(OrderMapper.ToDto(order));
        }

        var requirements = BuildRequirements(order, command.ResourceUrl, options.Value);
        if (string.IsNullOrWhiteSpace(command.PaymentHeader))
        {
            return Challenge("X-PAYMENT header is required", requirements);
        }

        var maxOrder = SpendGuardrails.CheckMaxOrderValue(order.Total, new Money(options.Value.MaxOrderValueMinorUnits));
        if (!maxOrder.Passed)
        {
            return Result<OrderDto>.Fail(ErrorKind.GuardrailViolation, maxOrder.Violation!);
        }

        var verification = await facilitator.VerifyAsync(command.PaymentHeader, requirements, cancellationToken);
        if (verification is FacilitatorVerifyResult.Invalid invalid)
        {
            return Challenge(invalid.Reason, requirements);
        }

        var dailyCapError = await RecheckDailyCapAsync(order, options.Value, now, cancellationToken);
        if (dailyCapError is not null)
        {
            return dailyCapError;
        }

        var settlement = await facilitator.SettleAsync(command.PaymentHeader, requirements, cancellationToken);
        if (settlement is FacilitatorSettleResult.Failed failed)
        {
            return Challenge(failed.Reason, requirements);
        }

        var succeeded = (FacilitatorSettleResult.Succeeded)settlement;
        var payloadHash = Sha256Hex(command.PaymentHeader);

        return await transactions.InTransactionAsync(
            () => RecordSettlementAsync(
                command.OrderId,
                succeeded.PayerAddress,
                payloadHash,
                succeeded.TxHash,
                command.ResourceUrl,
                now,
                cancellationToken),
            cancellationToken);
    }

    private async Task<Result<OrderDto>> ExpireAtConfirmAsync(Guid orderId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var order = await orders.GetForUpdateAsync(orderId, cancellationToken);
        if (order is null)
        {
            return Result<OrderDto>.Fail(ErrorKind.NotFound, "Order not found.");
        }

        if (order.Status == OrderStatus.Draft && now >= order.ExpiresAt)
        {
            order.TransitionTo(OrderStatus.Expired, Actor.System, now, "draft TTL elapsed at confirm");
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result<OrderDto>.Fail(ErrorKind.Conflict, "The draft has expired and can no longer be confirmed.");
    }

    private async Task<Result<OrderDto>> RecordSettlementAsync(
        Guid orderId,
        string payerAddress,
        string payloadHash,
        string txHash,
        string resourceUrl,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await payments.AcquirePayerAdvisoryLockAsync(payerAddress, cancellationToken);

        var order = await orders.GetForUpdateAsync(orderId, cancellationToken);
        if (order is null)
        {
            return Result<OrderDto>.Fail(ErrorKind.NotFound, "Order not found.");
        }

        var existing = await payments.FindByOrderIdAsync(orderId, cancellationToken);
        if (existing is not null)
        {
            return Result<OrderDto>.Ok(OrderMapper.ToDto(order));
        }

        if (order.Status == OrderStatus.Draft && now >= order.ExpiresAt)
        {
            order.TransitionTo(OrderStatus.Expired, Actor.System, now, "draft TTL elapsed at confirm");
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<OrderDto>.Fail(ErrorKind.Conflict, "The draft has expired and can no longer be confirmed.");
        }

        if (order.Status != OrderStatus.Draft)
        {
            return Result<OrderDto>.Ok(OrderMapper.ToDto(order));
        }

        var settings = options.Value;
        var maxOrder = SpendGuardrails.CheckMaxOrderValue(order.Total, new Money(settings.MaxOrderValueMinorUnits));
        if (!maxOrder.Passed)
        {
            return Result<OrderDto>.Fail(ErrorKind.GuardrailViolation, maxOrder.Violation!);
        }

        var dailyCapError = await RecheckDailyCapAsync(order, settings, now, cancellationToken);
        if (dailyCapError is not null)
        {
            return dailyCapError;
        }

        var inserted = await payments.TryAddAsync(
            new PaymentRecord(Guid.NewGuid(), order.Id, payerAddress, order.Total.MinorUnits, payloadHash, txHash, now),
            cancellationToken);
        if (!inserted)
        {
            var winner = await payments.FindByOrderIdAsync(orderId, cancellationToken);
            if (winner is not null)
            {
                var current = await orders.GetForUpdateAsync(orderId, cancellationToken);
                return Result<OrderDto>.Ok(OrderMapper.ToDto(current ?? order));
            }

            return Challenge("payment_payload_or_tx_already_settled", BuildRequirements(order, resourceUrl, options.Value));
        }

        order.AssignPayer(payerAddress);
        if (!order.Confirm(txHash, now).Transitioned)
        {
            throw new InvalidOperationException($"Order {orderId} is {order.Status.Name()}, cannot settle.");
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<OrderDto>.Ok(OrderMapper.ToDto(order));
    }

    private async Task<Result<OrderDto>?> RecheckDailyCapAsync(
        Domain.Orders.Order order,
        OrderingOptions settings,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var utcDayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var priorSpend = await orders.GetSpendSinceAsync(order.CustomerId, utcDayStart, order.Id, cancellationToken);
        var dailyCap = SpendGuardrails.CheckDailySpendCap(priorSpend, order.Total, new Money(settings.DailySpendCapMinorUnits));
        return dailyCap.Passed
            ? null
            : Result<OrderDto>.Fail(ErrorKind.GuardrailViolation, dailyCap.Violation!);
    }

    private static ExactPaymentRequirements BuildRequirements(Domain.Orders.Order order, string resourceUrl, OrderingOptions settings) =>
        new(
            Scheme: "exact",
            Network: settings.X402.Network,
            MaxAmountRequired: Usdc.ToAtomicAmount(order.Total),
            Resource: resourceUrl,
            Description: $"order {order.Id}",
            MimeType: "application/json",
            PayTo: settings.X402.PayToAddress,
            MaxTimeoutSeconds: settings.DraftTtlSeconds,
            Asset: settings.X402.Asset,
            Extra: new PaymentRequirementsExtra("USDC", "2"));

    private static Result<OrderDto> Challenge(string error, ExactPaymentRequirements requirements) =>
        Result<OrderDto>.Fail(
            ErrorKind.PaymentRequired,
            error,
            new X402Challenge(X402Challenge.Version, error, [requirements]));

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
