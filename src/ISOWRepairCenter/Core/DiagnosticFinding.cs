using System.Text.Json;

namespace ISOWRepairCenter.Core;

public enum FindingSeverity { Information, Warning, Critical }
public enum FindingConfidence { Known, Unknown, ManualReviewRequired }

public sealed record DiagnosticFinding(
    string Code,
    FindingSeverity Severity,
    FindingConfidence Confidence,
    string OperationId,
    string OperationTitle,
    string Summary,
    string Evidence,
    string Recommendation);

public static class DiagnosticAnalyzer
{
    public static IReadOnlyList<DiagnosticFinding> Analyze(IReadOnlyList<OperationResult> results) => results
        .Where(IsSupported)
        .SelectMany(AnalyzeResult)
        .OrderBy(SortOrder)
        .ThenBy(x => x.OperationTitle, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static bool IsSupported(OperationResult result) => result.Id is "dirty-bit" or "dism-health" or "sfc-verify" or "physical-disks" or "update-status" or "network-status";

    private static IEnumerable<DiagnosticFinding> AnalyzeResult(OperationResult result)
    {
        if (result.Cancelled)
        {
            yield return Create(result, "diagnostic-cancelled", FindingSeverity.Information, FindingConfidence.Unknown,
                "The diagnostic was cancelled before it could produce a conclusion.", result.Outcome.ToString(), "Run the diagnostic again when the system is idle.");
            yield break;
        }
        if (!result.Succeeded)
        {
            yield return Create(result, "diagnostic-command-failed", FindingSeverity.Warning, FindingConfidence.ManualReviewRequired,
                "The diagnostic command did not complete.", result.Outcome.ToString(), "Review the captured command output before attempting a repair.");
            yield break;
        }

        var outputs = result.Steps.Select(ReadOutput).ToArray();
        if (outputs.Any(string.IsNullOrWhiteSpace))
        {
            yield return Unknown(result, "diagnostic-output-missing", "The diagnostic completed without readable output.");
            yield break;
        }

        foreach (var finding in result.Id switch
        {
            "dirty-bit" => AnalyzeDirtyBit(result, outputs),
            "dism-health" => AnalyzeDism(result, outputs),
            "sfc-verify" => AnalyzeSfc(result, outputs),
            "physical-disks" => AnalyzePhysicalDisks(result, outputs),
            _ => new[] { Unknown(result, "diagnostic-manual-review", "The diagnostic completed, but its output requires manual review.") }
        }) yield return finding;
    }

    private static IReadOnlyList<DiagnosticFinding> AnalyzeDirtyBit(OperationResult result, IReadOnlyList<string> outputs)
    {
        var output = string.Join('\n', outputs);
        if (output.Contains("NOT Dirty", StringComparison.OrdinalIgnoreCase)) return new[] { Create(result, "file-system-clean", FindingSeverity.Information, FindingConfidence.Known, "The system volume is not marked dirty.", "fsutil reported NOT Dirty.", "No file-system repair is indicated by the dirty-bit check.") };
        if (output.Contains("is Dirty", StringComparison.OrdinalIgnoreCase)) return new[] { Create(result, "file-system-dirty", FindingSeverity.Warning, FindingConfidence.Known, "The system volume is marked dirty.", "fsutil reported that the volume is Dirty.", "Back up important files and investigate file-system errors before scheduling a repair.") };
        return new[] { Unknown(result, "file-system-status-unknown", "The dirty-bit output was not recognized.") };
    }

    private static IReadOnlyList<DiagnosticFinding> AnalyzeDism(OperationResult result, IReadOnlyList<string> outputs)
    {
        if (outputs.Count < 2) return new[] { Unknown(result, "component-store-incomplete", "DISM did not return both CheckHealth and ScanHealth output.") };
        if (outputs.Any(NeedsDismRepair)) return new[] { Create(result, "component-store-warning", FindingSeverity.Warning, FindingConfidence.Known, "DISM reported component-store corruption that may be repairable.", "At least one DISM health check reported repairable corruption.", "Review the session report, then use the guided DISM and SFC repair if appropriate.") };
        if (outputs.All(IsDismHealthy)) return new[] { Create(result, "component-store-healthy", FindingSeverity.Information, FindingConfidence.Known, "Windows component store is healthy.", "Both DISM health checks found no component-store corruption.", "No DISM repair is indicated by these checks.") };
        return new[] { Unknown(result, "component-store-status-unknown", "DISM completed, but one or more health outputs were not recognized.") };
    }

    private static IReadOnlyList<DiagnosticFinding> AnalyzeSfc(OperationResult result, IReadOnlyList<string> outputs)
    {
        var output = string.Join('\n', outputs);
        if (output.Contains("did not find any integrity violations", StringComparison.OrdinalIgnoreCase) || output.Contains("herhangi bir bütünlük ihlali bulamadı", StringComparison.OrdinalIgnoreCase))
            return new[] { Create(result, "system-files-healthy", FindingSeverity.Information, FindingConfidence.Known, "Protected system files passed verification.", "SFC reported no integrity violations.", "No SFC repair is indicated by this check.") };
        if (output.Contains("found integrity violations", StringComparison.OrdinalIgnoreCase) || output.Contains("bütünlük ihlalleri buldu", StringComparison.OrdinalIgnoreCase))
            return new[] { Create(result, "system-files-warning", FindingSeverity.Warning, FindingConfidence.Known, "SFC reported system-file integrity issues.", "SFC verification output contains integrity violations.", "Review the session report before running the guided DISM and SFC repair.") };
        return new[] { Unknown(result, "system-files-status-unknown", "SFC output was not recognized in a supported language.") };
    }

    private static IReadOnlyList<DiagnosticFinding> AnalyzePhysicalDisks(OperationResult result, IReadOnlyList<string> outputs)
    {
        try
        {
            using var document = JsonDocument.Parse(string.Join('\n', outputs));
            var disks = document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.EnumerateArray().ToArray() : new[] { document.RootElement };
            if (disks.Length == 0) return new[] { Unknown(result, "disk-health-unknown", "No physical-disk records were returned.") };
            var states = disks.Select(x => new { Health = ReadJsonString(x, "HealthStatus"), Operational = ReadJsonString(x, "OperationalStatus") }).ToArray();
            if (states.Any(x => string.IsNullOrWhiteSpace(x.Health) || string.IsNullOrWhiteSpace(x.Operational))) return new[] { Unknown(result, "disk-health-unknown", "One or more physical-disk records did not include health and operational state.") };
            if (states.Any(x => string.Equals(x.Health, "Unhealthy", StringComparison.OrdinalIgnoreCase) || !string.Equals(x.Operational, "OK", StringComparison.OrdinalIgnoreCase)))
                return new[] { Create(result, "disk-health-critical", FindingSeverity.Critical, FindingConfidence.Known, "A physical disk reported an unhealthy or non-operational state.", "Get-PhysicalDisk returned Unhealthy health or a non-OK operational state.", "Back up data immediately and run the drive manufacturer's diagnostic tool.") };
            if (states.Any(x => !string.Equals(x.Health, "Healthy", StringComparison.OrdinalIgnoreCase)))
                return new[] { Create(result, "disk-health-warning", FindingSeverity.Warning, FindingConfidence.Known, "A physical disk reported a non-healthy state.", "Get-PhysicalDisk returned a health state other than Healthy.", "Back up data and investigate the disk state before running repairs.") };
            return new[] { Create(result, "disk-health-ok", FindingSeverity.Information, FindingConfidence.Known, "All reported physical disks are Healthy and operational.", "Every returned physical disk reported Healthy health and OK operational state.", "Continue monitoring disk health and keep backups current.") };
        }
        catch (JsonException) { return new[] { Unknown(result, "disk-health-unknown", "Physical-disk output was not valid structured data.") }; }
    }

    private static bool IsDismHealthy(string output) => output.Contains("No component store corruption detected", StringComparison.OrdinalIgnoreCase);
    private static bool NeedsDismRepair(string output) => output.Contains("component store is repairable", StringComparison.OrdinalIgnoreCase) || (output.Contains("component store corruption", StringComparison.OrdinalIgnoreCase) && !IsDismHealthy(output));
    private static string? ReadJsonString(JsonElement element, string property) => element.TryGetProperty(property, out var value) ? value.ToString() : null;
    private static DiagnosticFinding Unknown(OperationResult result, string code, string evidence) => Create(result, code, FindingSeverity.Information, FindingConfidence.Unknown, "The diagnostic result could not be determined automatically.", evidence, "Review the captured output manually before deciding whether a repair is needed.");
    private static DiagnosticFinding Create(OperationResult result, string code, FindingSeverity severity, FindingConfidence confidence, string summary, string evidence, string recommendation) => new(code, severity, confidence, result.Id, result.Title, summary, evidence, recommendation);
    private static int SortOrder(DiagnosticFinding finding) => finding.Severity switch { FindingSeverity.Critical => 0, FindingSeverity.Warning => 1, _ when finding.Confidence != FindingConfidence.Known => 2, _ => 3 };

    private static string ReadOutput(StepResult step)
    {
        try { return File.Exists(step.OutputFile) ? File.ReadAllText(step.OutputFile) : string.Empty; }
        catch { return string.Empty; }
    }
}
