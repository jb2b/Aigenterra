namespace Aigenterra.Application.Common;

/// <summary>
/// Classifies expected application failures independently of transport concerns.
/// </summary>
public enum ApplicationErrorType
{
    Validation,
    NotFound,
    Conflict
}

/// <summary>
/// Describes an expected failure returned by an application use case.
/// </summary>
public sealed record ApplicationError(
    ApplicationErrorType Type,
    string Code,
    string Message);
