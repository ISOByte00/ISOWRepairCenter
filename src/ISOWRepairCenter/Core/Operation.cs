namespace ISOWRepairCenter.Core;

public enum OperationCategory { Diagnostic, SafeRepair, ExpertRepair }
public enum OperationOutcome { Succeeded, Failed, Cancelled, VerificationFailed, Skipped }

public sealed record CommandStep(string FileName, IReadOnlyList<string> Arguments, string? DisplayName = null, string? StandardInput = null)
{
    public string Preview => $"{FileName} {string.Join(" ", Arguments.Select(Quote))}";
    private static string Quote(string value) => value.Contains(' ') ? $"\"{value}\"" : value;
}

public sealed class RepairOperation
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required OperationCategory Category { get; init; }
    public required Func<OperationInputs, IReadOnlyList<CommandStep>> BuildSteps { get; init; }
    public bool RequiresRestart { get; init; }
    public bool RequiresRestorePoint { get; init; }
    public bool CreatesBcdBackup { get; init; }
    public string Risk { get; init; } = "Read-only diagnostic.";
    public Func<OperationInputs, string?>? Validate { get; init; }
}

public sealed class OperationInputs
{
    public string DismSource { get; init; } = string.Empty;
    public string WindowsSource { get; init; } = string.Empty;
    public string EfiTarget { get; init; } = string.Empty;
}

public sealed record StepResult(string Name, string Command, int? ExitCode, TimeSpan Duration, OperationOutcome Outcome, string OutputFile, string? Error, string Evidence)
{
    public bool Succeeded => Outcome == OperationOutcome.Succeeded;
    public bool Cancelled => Outcome == OperationOutcome.Cancelled;
}

public sealed record OperationResult(string Id, string Title, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, OperationOutcome Outcome, IReadOnlyList<StepResult> Steps)
{
    public bool Succeeded => Outcome == OperationOutcome.Succeeded;
    public bool Cancelled => Outcome == OperationOutcome.Cancelled;
}
