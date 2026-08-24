using FluentValidation;
using MediatR;

namespace Ordering.Application.Behaviors;

/// <summary>
/// Pipeline step 2: FluentValidation on the request shape. Failures throw and
/// are mapped to 400 application/problem+json by the API's exception handler,
/// short-circuiting before idempotency and transaction work.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(request, cancellationToken);
            if (!result.IsValid)
            {
                throw new ValidationException(result.Errors);
            }
        }

        return await next();
    }
}
