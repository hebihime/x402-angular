namespace Ordering.Application.Common;

public enum ErrorKind
{
    NotFound,
    Validation,
    GuardrailViolation,
    PaymentFailed,
    PaymentRequired,
    Conflict,
}

public sealed record Error(ErrorKind Kind, string Message, object? Details = null);

/// <summary>
/// Typed command/query outcome. Input validation failures short-circuit as
/// exceptions in the ValidationBehavior (mapped to 400 problem+json); domain
/// outcomes travel as Results.
/// </summary>
public sealed record Result<T>
{
    private Result(bool isSuccess, T? value, Error? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public bool IsSuccess { get; }
    public T? Value { get; }
    public Error? Error { get; }

    public static Result<T> Ok(T value) => new(true, value, null);

    public static Result<T> Fail(ErrorKind kind, string message, object? details = null) =>
        new(false, default, new Error(kind, message, details));
}
