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
    string Recommendation,
    string? SuggestedOperationId = null);

public static class DiagnosticAnalyzer
{
    public static IReadOnlyList<DiagnosticFinding> Analyze(IReadOnlyList<OperationResult> results) => results
        .Where(x => IsSupported(x) && !IsEventLog(x))
        .SelectMany(AnalyzeResult)
        .Concat(AnalyzeEventLogs(results.Where(IsEventLog).ToArray()))
        .OrderBy(SortOrder)
        .ThenBy(x => x.OperationTitle, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static bool IsSupported(OperationResult result) => result.Id is "dirty-bit" or "dism-health" or "sfc-verify" or "physical-disks" or "update-status" or "network-status" or "storage-events" or "critical-events";
    private static bool IsEventLog(OperationResult result) => result.Id is "storage-events" or "critical-events";

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

    private static IEnumerable<DiagnosticFinding> AnalyzeEventLogs(IReadOnlyList<OperationResult> results)
    {
        var events = new List<EventRecord>();
        var hasUnreadableOutput = false;
        foreach (var result in results)
        {
            if (result.Cancelled || !result.Succeeded)
            {
                foreach (var finding in AnalyzeResult(result)) yield return finding;
                continue;
            }

            var outputs = result.Steps.Select(ReadOutput).ToArray();
            if (outputs.Any(string.IsNullOrWhiteSpace) || !TryReadEventPayload(string.Join('\n', outputs), out var payload))
            {
                hasUnreadableOutput = true;
                yield return Unknown(result, "event-log-status-unknown", "Event log output was missing or did not match the structured event schema.");
                continue;
            }
            if (!payload.QuerySucceeded)
            {
                hasUnreadableOutput = true;
                yield return Create(result, "event-log-query-failed", FindingSeverity.Warning, FindingConfidence.ManualReviewRequired, "The event log query could not be completed.", "The structured collection reported QuerySucceeded=false.", "Review the captured error and rerun the diagnostic with administrator access before deciding whether a repair is needed.");
                continue;
            }
            events.AddRange(payload.Events);
        }

        var distinctEvents = events.DistinctBy(x => (x.LogName, x.RecordId)).ToArray();
        if (distinctEvents.Length == 0)
        {
            if (results.Any(x => x.Succeeded) && !hasUnreadableOutput) yield return CreateEventFinding("event-log-no-matching-patterns", FindingSeverity.Information, FindingConfidence.Known, "No supported storage or file-system warning patterns were found in the selected event logs.", "The structured event log collection was empty.", "This does not replace regular backups or hardware monitoring.");
            yield break;
        }

        var nvmeResets = distinctEvents.Where(x => x.ProviderName.Equals("stornvme", StringComparison.OrdinalIgnoreCase) && x.Id == 129).OrderBy(x => x.TimeCreated).ToArray();
        var ntfsIntegrity = distinctEvents.Where(x => x.ProviderName.Equals("ntfs", StringComparison.OrdinalIgnoreCase) && x.Id is 55 or 57).OrderBy(x => x.TimeCreated).ToArray();
        var diskRetries = distinctEvents.Where(x => x.ProviderName.Equals("disk", StringComparison.OrdinalIgnoreCase) && x.Id == 153).OrderBy(x => x.TimeCreated).ToArray();

        if (nvmeResets.Length > 0)
        {
            var recurring = IsRecurring(nvmeResets);
            yield return CreateEventFinding("nvme-reset-events", recurring ? FindingSeverity.Warning : FindingSeverity.Information, FindingConfidence.ManualReviewRequired,
                recurring ? "Repeated NVMe controller reset events were recorded." : "An NVMe controller reset event was recorded.",
                DescribeEvents(nvmeResets), "Review recent storage-driver, firmware, power-management, and hardware changes. Keep current backups; this event alone does not prove drive failure.");
        }
        if (ntfsIntegrity.Length > 0)
            yield return CreateEventFinding("ntfs-integrity-events", FindingSeverity.Warning, FindingConfidence.ManualReviewRequired, "NTFS integrity-related events were recorded.", DescribeEvents(ntfsIntegrity), "Back up important files, review the event details, and run the read-only NTFS scan before scheduling any repair. Historical events do not prove current corruption.");
        if (diskRetries.Length > 0)
            yield return CreateEventFinding("disk-retry-events", FindingSeverity.Warning, FindingConfidence.ManualReviewRequired, "Disk I/O retry events were recorded.", DescribeEvents(diskRetries), "Review storage connections, drivers, firmware, and disk health. Keep backups current; this event does not identify a root cause by itself.");
        if (nvmeResets.Length > 0 && ntfsIntegrity.Length > 0 && HasTemporalOverlap(nvmeResets, ntfsIntegrity))
            yield return CreateEventFinding("storage-event-temporal-overlap", FindingSeverity.Warning, FindingConfidence.ManualReviewRequired, "NVMe reset and NTFS integrity events occurred close together in time.", "At least one stornvme Event ID 129 and one Ntfs Event ID 55 or 57 were within 10 minutes.", "Review the timestamps and full event messages together. Temporal proximity can guide investigation but does not establish causation.");
        if (nvmeResets.Length == 0 && ntfsIntegrity.Length == 0 && diskRetries.Length == 0)
            yield return CreateEventFinding("event-log-no-matching-patterns", FindingSeverity.Information, FindingConfidence.Known, "No supported storage or file-system warning patterns were found in the selected event logs.", $"{distinctEvents.Length} structured events were collected, but none matched the current conservative rules.", "Review raw event output if you are investigating a specific issue.");
    }

    private static bool TryReadEventPayload(string output, out EventLogPayload payload)
    {
        payload = new EventLogPayload(false, Array.Empty<EventRecord>());
        try
        {
            using var document = JsonDocument.Parse(output);
            if (!document.RootElement.TryGetProperty("SchemaVersion", out var schemaVersion) || !schemaVersion.TryGetInt32(out var version) || version != 1 || !document.RootElement.TryGetProperty("QuerySucceeded", out var succeeded) || succeeded.ValueKind is not JsonValueKind.True and not JsonValueKind.False || !document.RootElement.TryGetProperty("Events", out var entries) || entries.ValueKind != JsonValueKind.Array) return false;
            var parsed = new List<EventRecord>();
            foreach (var entry in entries.EnumerateArray())
            {
                var provider = ReadJsonString(entry, "ProviderName");
                var logName = ReadJsonString(entry, "LogName");
                if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(logName) || !entry.TryGetProperty("RecordId", out var recordIdValue) || !recordIdValue.TryGetInt64(out var recordId) || !entry.TryGetProperty("Id", out var idValue) || !idValue.TryGetInt32(out var id) || !DateTimeOffset.TryParse(ReadJsonString(entry, "TimeCreated"), out var timeCreated)) return false;
                parsed.Add(new EventRecord(logName, recordId, provider, id, timeCreated));
            }
            payload = new EventLogPayload(succeeded.GetBoolean(), parsed);
            return true;
        }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private static bool IsRecurring(IReadOnlyList<EventRecord> events) => events.Count >= 3 && events.Skip(2).Select((current, index) => current.TimeCreated - events[index].TimeCreated).Any(span => span <= TimeSpan.FromMinutes(10));
    private static bool HasTemporalOverlap(IReadOnlyList<EventRecord> first, IReadOnlyList<EventRecord> second) => first.Any(x => second.Any(y => (x.TimeCreated - y.TimeCreated).Duration() <= TimeSpan.FromMinutes(10)));
    private static string DescribeEvents(IReadOnlyList<EventRecord> events) => $"{events.Count} event(s): {string.Join(", ", events.Take(3).Select(x => $"{x.ProviderName} {x.Id} at {x.TimeCreated:yyyy-MM-dd HH:mm:ss zzz}"))}{(events.Count > 3 ? " …" : string.Empty)}";
    private static DiagnosticFinding CreateEventFinding(string code, FindingSeverity severity, FindingConfidence confidence, string summary, string evidence, string recommendation) => new(code, severity, confidence, "event-log-analysis", "Event log analysis", summary, evidence, recommendation);
    private sealed record EventLogPayload(bool QuerySucceeded, IReadOnlyList<EventRecord> Events);
    private sealed record EventRecord(string LogName, long RecordId, string ProviderName, int Id, DateTimeOffset TimeCreated);

    private static bool IsDismHealthy(string output) => output.Contains("No component store corruption detected", StringComparison.OrdinalIgnoreCase);
    private static bool NeedsDismRepair(string output) => output.Contains("component store is repairable", StringComparison.OrdinalIgnoreCase) || (output.Contains("component store corruption", StringComparison.OrdinalIgnoreCase) && !IsDismHealthy(output));
    private static string? ReadJsonString(JsonElement element, string property) => element.TryGetProperty(property, out var value) ? value.ToString() : null;
    private static DiagnosticFinding Unknown(OperationResult result, string code, string evidence) => Create(result, code, FindingSeverity.Information, FindingConfidence.Unknown, "The diagnostic result could not be determined automatically.", evidence, "Review the captured output manually before deciding whether a repair is needed.");
    private static DiagnosticFinding Create(OperationResult result, string code, FindingSeverity severity, FindingConfidence confidence, string summary, string evidence, string recommendation) => new(code, severity, confidence, result.Id, result.Title, summary, evidence, recommendation, SuggestedOperationFor(code));
    private static string? SuggestedOperationFor(string findingCode) => findingCode is "component-store-warning" or "system-files-warning" ? "system-file-repair" : null;
    private static int SortOrder(DiagnosticFinding finding) => finding.Severity switch { FindingSeverity.Critical => 0, FindingSeverity.Warning => 1, _ when finding.Confidence != FindingConfidence.Known => 2, _ => 3 };

    private static string ReadOutput(StepResult step)
    {
        try { return File.Exists(step.OutputFile) ? File.ReadAllText(step.OutputFile) : string.Empty; }
        catch { return string.Empty; }
    }
}
