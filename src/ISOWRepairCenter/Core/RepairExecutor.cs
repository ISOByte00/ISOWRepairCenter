namespace ISOWRepairCenter.Core;

public sealed class RepairExecutor
{
    private readonly IProcessRunner _runner;
    private readonly SessionStore _store;
    public RepairExecutor(IProcessRunner runner, SessionStore store) { _runner=runner; _store=store; }

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
            if (!steps[^1].Succeeded) return new OperationResult(operation.Id, operation.Title, started, DateTimeOffset.Now, steps[^1].Outcome, steps);
        }

        var index=0;
        foreach (var step in operation.BuildSteps(inputs))
        {
            index++;
            writeLog($"> {step.Preview}");
            var result = await _runner.RunAsync(step, _store.StepOutputPath(operationNumber, $"{operation.Id}_{index}"), writeLog, token);
            steps.Add(result);
            if (!result.Succeeded) break;
        }
        var outcome = steps.Count == 0 ? OperationOutcome.Skipped : steps.FirstOrDefault(x => !x.Succeeded)?.Outcome ?? OperationOutcome.Succeeded;
        return new OperationResult(operation.Id, operation.Title, started, DateTimeOffset.Now, outcome, steps);
    }
}
