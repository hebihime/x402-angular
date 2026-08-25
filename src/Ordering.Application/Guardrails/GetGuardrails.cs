using MediatR;
using Microsoft.Extensions.Options;
using Ordering.Application.Abstractions;
using Ordering.Domain;

namespace Ordering.Application.Guardrails;

/// <summary>
/// Publishes the effective limits so an agent can size a basket before drafting.
/// Reads options only — not the write model, not projections.
/// </summary>
public sealed record GetGuardrailsQuery : IQuery<GuardrailsDto>;

public sealed record GuardrailsDto(
    Money MaxOrderValueMinorUnits,
    Money DailySpendCapMinorUnits,
    string DailySpendCapWindow,
    int DraftTtlSeconds,
    string Asset,
    string Network,
    string PayTo);

internal sealed class GetGuardrailsQueryHandler(IOptions<OrderingOptions> options)
    : IRequestHandler<GetGuardrailsQuery, GuardrailsDto>
{
    public Task<GuardrailsDto> Handle(GetGuardrailsQuery query, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        return Task.FromResult(new GuardrailsDto(
            new Money(settings.MaxOrderValueMinorUnits),
            new Money(settings.DailySpendCapMinorUnits),
            "utc_day",
            settings.DraftTtlSeconds,
            settings.X402.Asset,
            settings.X402.Network,
            settings.X402.PayToAddress));
    }
}
