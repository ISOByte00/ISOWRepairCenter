namespace WindowsRepairCenter.Core;

public sealed class RestorePointService
{
    private readonly ProcessRunner _runner;
    public RestorePointService(ProcessRunner runner) => _runner = runner;

    public Task<StepResult> TryCreateAsync(string outputFile, Action<string> log, CancellationToken token)
    {
        var description = $"Windows Repair Center {DateTime.Now:yyyy-MM-dd HH:mm}";
        var script = $"Checkpoint-Computer -Description '{description}' -RestorePointType 'MODIFY_SETTINGS'";
        return _runner.RunAsync(new CommandStep("powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script }, "Create restore point"), outputFile, log, token);
    }
}
