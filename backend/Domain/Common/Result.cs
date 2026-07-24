namespace FinanceApp.Domain.Common;

public enum ErrorType { Validation, NotFound, Unsupported, Conflict }

public readonly record struct Error(ErrorType Type, string Message)
{
    public static Error Validation(string m) => new(ErrorType.Validation, m);
    public static Error NotFound(string m) => new(ErrorType.NotFound, m);
    public static Error Unsupported(string m) => new(ErrorType.Unsupported, m);
    public static Error Conflict(string m) => new(ErrorType.Conflict, m);
}

/// Explicit operation outcome: success with a value, or failure with an error.
/// Used instead of exceptions for EXPECTED failures (validation, not found, unsupported).
public sealed class Result<T>
{
    public bool IsSuccess { get; private init; }
    public T? Value { get; private init; }
    public Error Error { get; private init; }

    public static Result<T> Ok(T value) => new() { IsSuccess = true, Value = value };
    public static Result<T> Fail(Error error) => new() { IsSuccess = false, Error = error };

    public static implicit operator Result<T>(Error error) => Fail(error);
}
