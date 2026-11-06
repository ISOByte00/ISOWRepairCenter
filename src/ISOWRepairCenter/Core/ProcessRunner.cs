using System.Diagnostics;

namespace ISOWRepairCenter.Core;

public interface IProcessRunner
{
    Task<StepResult> RunAsync(CommandStep step, string outputFile, Action<string> onOutput, CancellationToken cancellationToken);
}

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<StepResult> RunAsync(CommandStep step, string outputFile, Action<string> onOutput, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.Now;
        Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
        using var writer = new StreamWriter(new FileStream(outputFile, FileMode.Create, FileAccess.Write, FileShare.Read), new System.Text.UTF8Encoding(false)) { AutoFlush = true };
        var writeLock = new object();
        using var process = new Process { StartInfo = CreateStartInfo(step), EnableRaisingEvents = true };
        void Receive(object? _, DataReceivedEventArgs e)
        {
            if (e.Data is null) return;
            lock (writeLock) writer.WriteLine(e.Data);
            onOutput(e.Data);
        }

        process.OutputDataReceived += Receive;
        process.ErrorDataReceived += Receive;
        try
        {
            if (!process.Start()) throw new InvalidOperationException("The command process could not be started.");
            if (step.StandardInput is not null)
            {
                await process.StandardInput.WriteLineAsync(step.StandardInput);
                process.StandardInput.Close();
            }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            try { await process.WaitForExitAsync(cancellationToken); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                return new StepResult(step.DisplayName ?? step.FileName, step.Preview, null, DateTimeOffset.Now-started, OperationOutcome.Cancelled, outputFile, "Cancelled by user.", "Process tree was terminated before completion.");
            }

            var failed = process.ExitCode != 0;
            var evidence = failed ? $"The tool returned exit code {process.ExitCode}; inspect the captured output." : "The tool returned exit code 0; rerun the matching diagnostic to verify the repair state.";
            return new StepResult(step.DisplayName ?? step.FileName, step.Preview, process.ExitCode, DateTimeOffset.Now-started, failed ? OperationOutcome.Failed : OperationOutcome.Succeeded, outputFile, failed ? $"Exit code {process.ExitCode}." : null, evidence);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            lock (writeLock) writer.WriteLine(ex);
            return new StepResult(step.DisplayName ?? step.FileName, step.Preview, null, DateTimeOffset.Now-started, OperationOutcome.Failed, outputFile, ex.Message, "The process could not be started or completed.");
        }
        finally
        {
            process.OutputDataReceived -= Receive;
            process.ErrorDataReceived -= Receive;
        }
    }

    public static ProcessStartInfo CreateStartInfo(CommandStep step)
    {
        var info = new ProcessStartInfo { FileName=ResolveExecutable(step.FileName), UseShellExecute=false, CreateNoWindow=true, RedirectStandardInput=step.StandardInput is not null, RedirectStandardOutput=true, RedirectStandardError=true, StandardOutputEncoding=System.Text.Encoding.UTF8, StandardErrorEncoding=System.Text.Encoding.UTF8 };
        foreach (var argument in step.Arguments) info.ArgumentList.Add(Environment.ExpandEnvironmentVariables(argument));
        return info;
    }

    private static string ResolveExecutable(string fileName)
    {
        if (Path.IsPathFullyQualified(fileName)) return fileName;
        var systemCandidate = Path.Combine(Environment.SystemDirectory, fileName);
        return File.Exists(systemCandidate) ? systemCandidate : fileName;
    }
}
