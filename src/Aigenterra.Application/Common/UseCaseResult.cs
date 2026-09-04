namespace Aigenterra.Application.Common;

/// <summary>
/// Represents either a successful use-case value or an expected application error.
/// </summary>
public sealed record UseCaseResult<T>
{
    private UseCaseResult(T? value, ApplicationError? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }

    public ApplicationError? Error { get; }

    public bool IsSuccess => Error is null;

    public static UseCaseResult<T> Success(T value) => new(value, null);

    public static UseCaseResult<T> Failure(
        ApplicationErrorType type,
        string code,
        string message) =>
        new(default, new ApplicationError(type, code, message));
}
