using Aigenterra.Application.Common;
using Aigenterra.Domain.Executions;

namespace Aigenterra.Application.Executions;

/// <summary>
/// Centralizes conversion and validation of primitive execution inputs.
/// </summary>
internal static class ExecutionInput
{
    public static bool TryCreateRuntimeReference(
        RuntimeReferenceInput? input,
        out RuntimeReference? runtimeReference,
        out ApplicationError? error)
    {
        runtimeReference = null;
        error = null;

        if (input is null)
        {
            return true;
        }

        try
        {
            runtimeReference = new RuntimeReference(
                input.Runtime!,
                input.ResourceKind!,
                input.ExternalId!);
            return true;
        }
        catch (ArgumentException exception)
        {
            error = new ApplicationError(
                ApplicationErrorType.Validation,
                "invalid_runtime_reference",
                exception.Message);
            return false;
        }
    }

    public static bool TryParseTerminalStatus(
        string? outcome,
        out ExecutionStatus status,
        out ApplicationError? error)
    {
        error = null;
        if (Enum.TryParse(outcome, true, out status)
            && status != ExecutionStatus.Running
            && Enum.IsDefined(status))
        {
            return true;
        }

        error = new ApplicationError(
            ApplicationErrorType.Validation,
            "invalid_execution_outcome",
            "Outcome must be completed, failed, or interrupted.");
        return false;
    }

    public static RuntimeReferenceModel? ToModel(RuntimeReference? reference) =>
        reference is null
            ? null
            : new RuntimeReferenceModel(
                reference.Runtime,
                reference.ResourceKind,
                reference.ExternalId);

    public static string ToContractValue<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        value.ToString().ToLowerInvariant();
}
