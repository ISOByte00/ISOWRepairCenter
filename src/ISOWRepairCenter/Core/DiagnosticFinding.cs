namespace ISOWRepairCenter.Core;

public enum FindingSeverity { Information, Warning, Critical }

public sealed record DiagnosticFinding(string Code, FindingSeverity Severity, string Summary, string Evidence, string Recommendation);

public static class DiagnosticAnalyzer
{
    public static IReadOnlyList<DiagnosticFinding> Analyze(IReadOnlyList<OperationResult> results)
    {
        var findings = new List<DiagnosticFinding>();
        foreach (var result in results.Where(x => x.Id is "dirty-bit" or "dism-health" or "sfc-verify" or "physical-disks" or "update-status" or "network-status"))
        {
            var output = string.Join('\n', result.Steps.Select(ReadOutput));
            if (!result.Succeeded)
            {
                findings.Add(new("diagnostic-command-failed", FindingSeverity.Warning, $"{result.Title} did not complete.", result.Outcome.ToString(), "Review the captured command output before attempting a repair."));
                continue;
            }

            switch (result.Id)
            {
                case "dirty-bit" when output.Contains("NOT Dirty", StringComparison.OrdinalIgnoreCase):
                    findings.Add(new("file-system-clean", FindingSeverity.Information, "The system volume is not marked dirty.", "fsutil reported NOT Dirty.", "No file-system repair is indicated by the dirty-bit check."));
                    break;
                case "dirty-bit" when output.Contains("is Dirty", StringComparison.OrdinalIgnoreCase):
                    findings.Add(new("file-system-dirty", FindingSeverity.Warning, "The system volume is marked dirty.", "fsutil reported that the volume is Dirty.", "Back up important files and investigate file-system errors before scheduling a repair."));
                    break;
                case "dism-health" when output.Contains("No component store corruption detected", StringComparison.OrdinalIgnoreCase):
                    findings.Add(new("component-store-healthy", FindingSeverity.Information, "Windows component store is healthy.", "DISM found no component store corruption.", "No DISM repair is indicated by this check."));
                    break;
                case "sfc-verify" when output.Contains("did not find any integrity violations", StringComparison.OrdinalIgnoreCase):
                    findings.Add(new("system-files-healthy", FindingSeverity.Information, "Protected system files passed verification.", "SFC found no integrity violations.", "No SFC repair is indicated by this check."));
                    break;
                case "sfc-verify" when output.Contains("integrity violations", StringComparison.OrdinalIgnoreCase):
                    findings.Add(new("system-files-warning", FindingSeverity.Warning, "SFC reported system-file integrity issues.", "SFC verification output contains integrity violations.", "Run the guided DISM and SFC repair only after reviewing the session report."));
                    break;
                case "physical-disks" when output.Contains("Unhealthy", StringComparison.OrdinalIgnoreCase) || output.Contains("Warning", StringComparison.OrdinalIgnoreCase):
                    findings.Add(new("disk-health-warning", FindingSeverity.Warning, "A physical disk reported a warning or unhealthy state.", "Get-PhysicalDisk output contains Warning or Unhealthy.", "Back up data and check the drive manufacturer's diagnostic tool."));
                    break;
                case "physical-disks":
                    findings.Add(new("disk-health-ok", FindingSeverity.Information, "Physical disk health did not report a warning.", "Get-PhysicalDisk completed without Warning or Unhealthy.", "Continue monitoring disk health and keep backups current."));
                    break;
                case "network-status" when output.Contains("Direct access", StringComparison.OrdinalIgnoreCase):
                    findings.Add(new("proxy-direct", FindingSeverity.Information, "WinHTTP is configured for direct access.", "netsh reported Direct access (no proxy server).", "No proxy reset is indicated by this check."));
                    break;
            }
        }
        return findings;
    }

    private static string ReadOutput(StepResult step)
    {
        try { return File.Exists(step.OutputFile) ? File.ReadAllText(step.OutputFile) : string.Empty; }
        catch { return string.Empty; }
    }
}
