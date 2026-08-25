using Ordering.Application.Common;

namespace Ordering.Api.Http;

/// <summary>Maps typed command results onto HTTP. Endpoints stay thin: send a request, map the result.</summary>
public static class ResultHttp
{
    public static IResult ToHttpResult<T>(this Result<T> result, int successStatus = StatusCodes.Status200OK)
    {
        if (result.IsSuccess)
        {
            return Results.Json(result.Value, statusCode: successStatus);
        }

        var (status, title) = result.Error!.Kind switch
        {
            ErrorKind.NotFound => (StatusCodes.Status404NotFound, "Not found"),
            ErrorKind.Validation => (StatusCodes.Status400BadRequest, "Invalid request"),
            ErrorKind.GuardrailViolation => (StatusCodes.Status422UnprocessableEntity, "Guardrail violation"),
            ErrorKind.PaymentFailed => (StatusCodes.Status402PaymentRequired, "Payment failed"),
            ErrorKind.PaymentRequired => (StatusCodes.Status402PaymentRequired, "Payment required"),
            ErrorKind.Conflict => (StatusCodes.Status409Conflict, "Conflict"),
            _ => (StatusCodes.Status500InternalServerError, "Error"),
        };

        if (result.Error.Kind == ErrorKind.PaymentRequired && result.Error.Details is not null)
        {
            return Results.Json(result.Error.Details, statusCode: StatusCodes.Status402PaymentRequired);
        }

        return Results.Problem(title: title, detail: result.Error.Message, statusCode: status);
    }
}
