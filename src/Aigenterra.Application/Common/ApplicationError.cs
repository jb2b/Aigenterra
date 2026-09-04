namespace Aigenterra.Application.Common;

public enum ApplicationErrorType
{
    Validation,
    NotFound,
    Conflict
}

public sealed record ApplicationError(
    ApplicationErrorType Type,
    string Code,
    string Message);
