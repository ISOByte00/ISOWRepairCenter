namespace WindowsRepairCenter.Core;

public sealed class RepairExecutor
{
    private readonly ProcessRunner _runner;
    private readonly SessionStore _store;
    public RepairExecutor(ProcessRunner runner, SessionStore store) { _runner=runner; _store=store; }

    public async Task<OperationResult> ExecuteAsync(RepairOperation operation, OperationInputs inputs, int operationNumber, Action<string> writeLog, CancellationToken token)
    {
        var validationError = operation.Validate?.Invoke(inputs);
        if (validationError is not null) throw new InvalidOperationException(validationError);
        var started = DateTimeOffset.Now;
        var steps = new List<StepResult>();
        if (operation.CreatesBcdBackup)
        {
            var backup = Path.Combine(_store.SessionDirectory, $"BCD_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.bcd");
            var backupStep = new CommandStep("bcdedit.exe", new[] { "/export", backup }, "BCD backup");
            writeLog($"> {backupStep.Preview}");
            steps.Add(await _runner.RunAsync(backupStep, _store.StepOutputPath(operationNumber, operation.Id+"_backup"), writeLog, token));
            if (steps[^1].ExitCode != 0) return new OperationResult(operation.Id, operation.Title, started, DateTimeOffset.Now, false, steps[^1].Cancelled, steps);
        }

        var index=0;
        foreach (var step in operation.BuildSteps(inputs))
        {
            index++;
            writeLog($"> {step.Preview}");
            var result = await _runner.RunAsync(step, _store.StepOutputPath(operationNumber, $"{operation.Id}_{index}"), writeLog, token);
            steps.Add(result);
            if (result.Cancelled || result.ExitCode != 0) break;
        }
        var cancelled = steps.Any(x => x.Cancelled);
        return new OperationResult(operation.Id, operation.Title, started, DateTimeOffset.Now, !cancelled && steps.Count > 0 && steps.All(x => x.ExitCode == 0), cancelled, steps);
    }
}
