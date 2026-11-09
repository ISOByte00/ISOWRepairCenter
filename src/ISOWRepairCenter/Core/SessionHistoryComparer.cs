using System.Text.Json;

namespace ISOWRepairCenter.Core;

/// <summary>Reads completed sessions without modifying them and compares their diagnostic findings.</summary>
public sealed class SessionHistoryComparer
{
    private const long MaximumInputBytes = 4 * 1024 * 1024;
    private static readonly HashSet<string> EventOperations = new(StringComparer.Ordinal) { "storage-events", "critical-events" };
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public async Task<SessionHistoryComparison> CompareAsync(string earlierSessionDirectory, string laterSessionDirectory, CancellationToken cancellationToken = default)
    {
        var earlier = await ReadAsync(earlierSessionDirectory, cancellationToken);
        var later = await ReadAsync(laterSessionDirectory, cancellationToken);
        return Compare(earlier, later);
    }

    public async Task<SessionHistorySession> ReadAsync(string sessionDirectory, CancellationToken cancellationToken = default)
    {
        var issues = new List<SessionHistoryIssue>();
        if (string.IsNullOrWhiteSpace(sessionDirectory) || !Directory.Exists(sessionDirectory))
            return new SessionHistorySession(sessionDirectory ?? string.Empty, Array.Empty<SessionHistoryFinding>(), Array.Empty<SessionHistoryOperation>(), Array.Empty<SessionHistoryEventScope>(), new[] { Issue("session", "SessionDirectoryUnavailable", "The session directory does not exist or is not accessible.") });

        var fullDirectory = Path.GetFullPath(sessionDirectory);
        var results = await ReadArrayAsync<StoredOperation>(fullDirectory, "results.json", "results", issues, cancellationToken);
        var storedFindings = await ReadArrayAsync<StoredFinding>(fullDirectory, "findings.json", "findings", issues, cancellationToken);
        var operations = new List<SessionHistoryOperation>();
        foreach (var result in results)
        {
            if (string.IsNullOrWhiteSpace(result.Id)) { issues.Add(Issue("results.json", "InvalidOperation", "An operation without an Id was ignored.")); continue; }
            operations.Add(new SessionHistoryOperation(result.Id, ParseOutcome(result.Outcome), string.Equals(result.Outcome, "Succeeded", StringComparison.OrdinalIgnoreCase)));
        }

        var findings = new List<SessionHistoryFinding>();
        foreach (var finding in storedFindings)
        {
            if (string.IsNullOrWhiteSpace(finding.Code) || string.IsNullOrWhiteSpace(finding.OperationId))
            {
                issues.Add(Issue("findings.json", "InvalidFinding", "A finding without Code or OperationId was ignored."));
                continue;
            }
            findings.Add(new SessionHistoryFinding(finding.Code, finding.OperationId, ParseSeverity(finding.Severity), ParseConfidence(finding.Confidence)));
        }

        var eventScopes = await ReadEventScopesAsync(fullDirectory, results, issues, cancellationToken);
        return new SessionHistorySession(fullDirectory, findings, operations, eventScopes, issues);
    }

    public SessionHistoryComparison Compare(SessionHistorySession earlier, SessionHistorySession later)
    {
        ArgumentNullException.ThrowIfNull(earlier);
        ArgumentNullException.ThrowIfNull(later);
        var entries = new List<SessionHistoryComparisonEntry>();
        var previous = earlier.Findings.GroupBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        var current = later.Findings.GroupBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);

        foreach (var oldFinding in previous.Values)
        {
            if (current.TryGetValue(oldFinding.Key, out var newFinding))
            {
                var eventComparison = IsEventFinding(oldFinding);
                if (eventComparison && HasRepeatedHistoricalEvent(earlier, later))
                    entries.Add(Entry(oldFinding, newFinding, SessionHistoryChange.HistoricalEventRepeated, "The same collected historical event was present in both sessions; this does not establish a recurring new fault."));
                else if (oldFinding.Severity != newFinding.Severity || oldFinding.Confidence != newFinding.Confidence)
                    entries.Add(Entry(oldFinding, newFinding, SessionHistoryChange.ChangedAssessment, "Severity or confidence changed; no health improvement is inferred."));
                else
                    entries.Add(Entry(oldFinding, newFinding, SessionHistoryChange.Persisting, "The same finding was observed in both sessions."));
                continue;
            }

            if (!WasRelevantDiagnosticCompleted(later, oldFinding))
                entries.Add(Entry(oldFinding, null, SessionHistoryChange.NotComparable, "The later session did not complete the diagnostic needed to compare this finding."));
            else if (IsEventFinding(oldFinding) && !EventScopeIsComparable(earlier, later))
                entries.Add(Entry(oldFinding, null, SessionHistoryChange.NotComparable, "Event-log collection scope was incomplete, failed, or different between sessions."));
            else
                entries.Add(Entry(oldFinding, null, SessionHistoryChange.NotObserved, IsEventFinding(oldFinding)
                    ? "Not observed in the later event-log collection. Historical event absence is not evidence that the underlying issue was resolved."
                    : "Not observed after the later diagnostic completed. This is not a repair or health verdict."));
        }

        foreach (var newFinding in current.Values.Where(x => !previous.ContainsKey(x.Key)))
        {
            if (!WasRelevantDiagnosticCompleted(later, newFinding))
                entries.Add(Entry(null, newFinding, SessionHistoryChange.NotComparable, "The finding was recorded, but its diagnostic did not complete successfully."));
            else
                entries.Add(Entry(null, newFinding, SessionHistoryChange.NewlyObserved, "The finding was observed only in the later session."));
        }

        return new SessionHistoryComparison(earlier, later, entries.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray());
    }

    private async Task<IReadOnlyList<T>> ReadArrayAsync<T>(string directory, string fileName, string label, List<SessionHistoryIssue> issues, CancellationToken token)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path)) { issues.Add(Issue(fileName, "MissingFile", $"Optional {label} data is unavailable.")); return Array.Empty<T>(); }
        try
        {
            if (new FileInfo(path).Length > MaximumInputBytes) { issues.Add(Issue(fileName, "FileTooLarge", $"The file exceeds the {MaximumInputBytes} byte read limit.")); return Array.Empty<T>(); }
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<T[]>(stream, _json, token) ?? Array.Empty<T>();
        }
        catch (JsonException) { issues.Add(Issue(fileName, "InvalidJson", "The JSON could not be parsed.")); return Array.Empty<T>(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { issues.Add(Issue(fileName, "ReadFailed", ex.Message)); return Array.Empty<T>(); }
    }

    private async Task<IReadOnlyList<SessionHistoryEventScope>> ReadEventScopesAsync(string directory, IReadOnlyList<StoredOperation> results, List<SessionHistoryIssue> issues, CancellationToken token)
    {
        var scopes = new List<SessionHistoryEventScope>();
        foreach (var operation in results.Where(x => EventOperations.Contains(x.Id)))
        {
            var records = new HashSet<string>(StringComparer.Ordinal);
            DateTimeOffset? windowStart = null;
            var readable = string.Equals(operation.Outcome, "Succeeded", StringComparison.OrdinalIgnoreCase);
            foreach (var step in operation.Steps ?? Array.Empty<StoredStep>())
            {
                var path = SafeSessionFile(directory, step.OutputFile);
                if (path is null || !File.Exists(path)) { readable = false; continue; }
                try
                {
                    if (new FileInfo(path).Length > MaximumInputBytes) { readable = false; issues.Add(Issue(Path.GetFileName(path), "FileTooLarge", "Event output exceeded the safe read limit.")); continue; }
                    var text = await File.ReadAllTextAsync(path, token);
                    using var document = JsonDocument.Parse(text);
                    var root = document.RootElement;
                    if (!root.TryGetProperty("QuerySucceeded", out var succeeded) || succeeded.ValueKind != JsonValueKind.True || !root.TryGetProperty("Events", out var events) || events.ValueKind != JsonValueKind.Array) { readable = false; continue; }
                    if (root.TryGetProperty("WindowStart", out var start) && DateTimeOffset.TryParse(start.GetString(), out var parsed)) windowStart = parsed;
                    foreach (var entry in events.EnumerateArray())
                    {
                        var provider = GetString(entry, "ProviderName"); var id = GetString(entry, "Id"); var recordId = GetString(entry, "RecordId"); var time = GetString(entry, "TimeCreated");
                        if (provider is not null && id is not null && recordId is not null && time is not null) records.Add($"{provider}|{id}|{recordId}|{time}");
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { readable = false; issues.Add(Issue(Path.GetFileName(path), "EventOutputUnreadable", ex.Message)); }
            }
            scopes.Add(new SessionHistoryEventScope(operation.Id, readable, windowStart, records));
        }
        return scopes;
    }

    private static string? SafeSessionFile(string directory, string? outputFile)
    {
        if (string.IsNullOrWhiteSpace(outputFile)) return null;
        var candidate = Path.Combine(directory, Path.GetFileName(outputFile));
        return Path.GetFullPath(candidate).StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? candidate : null;
    }
    private static bool WasRelevantDiagnosticCompleted(SessionHistorySession session, SessionHistoryFinding finding) =>
        IsEventFinding(finding) ? session.Operations.Where(x => EventOperations.Contains(x.Id)).Any(x => x.Succeeded) : session.Operations.Any(x => x.Id.Equals(finding.OperationId, StringComparison.Ordinal) && x.Succeeded);
    private static bool IsEventFinding(SessionHistoryFinding finding) => finding.OperationId.Equals("event-log-analysis", StringComparison.Ordinal);
    private static bool EventScopeIsComparable(SessionHistorySession first, SessionHistorySession second) =>
        first.EventScopes.Where(x => x.Readable).Select(x => x.OperationId).Order().SequenceEqual(second.EventScopes.Where(x => x.Readable).Select(x => x.OperationId).Order()) && first.EventScopes.Any(x => x.Readable) && second.EventScopes.Any(x => x.Readable);
    private static bool HasRepeatedHistoricalEvent(SessionHistorySession first, SessionHistorySession second) =>
        first.EventScopes.SelectMany(x => x.Records).Intersect(second.EventScopes.SelectMany(x => x.Records), StringComparer.Ordinal).Any();
    private static SessionHistoryComparisonEntry Entry(SessionHistoryFinding? oldFinding, SessionHistoryFinding? newFinding, SessionHistoryChange change, string explanation) => new(oldFinding?.Key ?? newFinding!.Key, oldFinding, newFinding, change, explanation);
    private static SessionHistoryIssue Issue(string file, string code, string message) => new(file, code, message);
    private static string? GetString(JsonElement element, string name) => element.TryGetProperty(name, out var value) ? value.ToString() : null;
    private static FindingSeverity? ParseSeverity(string? value) => Enum.TryParse<FindingSeverity>(value, true, out var parsed) ? parsed : null;
    private static FindingConfidence? ParseConfidence(string? value) => Enum.TryParse<FindingConfidence>(value, true, out var parsed) ? parsed : null;
    private static OperationOutcome? ParseOutcome(string? value) => Enum.TryParse<OperationOutcome>(value, true, out var parsed) ? parsed : null;

    private sealed class StoredOperation { public string? Id { get; set; } public string? Outcome { get; set; } public StoredStep[]? Steps { get; set; } }
    private sealed class StoredStep { public string? OutputFile { get; set; } }
    private sealed class StoredFinding { public string? Code { get; set; } public string? OperationId { get; set; } public string? Severity { get; set; } public string? Confidence { get; set; } }
}

public sealed record SessionHistorySession(string Directory, IReadOnlyList<SessionHistoryFinding> Findings, IReadOnlyList<SessionHistoryOperation> Operations, IReadOnlyList<SessionHistoryEventScope> EventScopes, IReadOnlyList<SessionHistoryIssue> Issues)
{
    public bool IsReadable => Issues.All(x => x.Code is not "SessionDirectoryUnavailable" and not "InvalidJson" and not "ReadFailed");
}
public sealed record SessionHistoryFinding(string Code, string OperationId, FindingSeverity? Severity, FindingConfidence? Confidence)
{ public string Key => $"{OperationId}\u001f{Code}"; }
public sealed record SessionHistoryOperation(string Id, OperationOutcome? Outcome, bool Succeeded);
public sealed record SessionHistoryEventScope(string OperationId, bool Readable, DateTimeOffset? WindowStart, IReadOnlySet<string> Records);
public sealed record SessionHistoryIssue(string File, string Code, string Message);
public enum SessionHistoryChange { NewlyObserved, Persisting, NotObserved, ChangedAssessment, NotComparable, HistoricalEventRepeated }
public sealed record SessionHistoryComparisonEntry(string Key, SessionHistoryFinding? Earlier, SessionHistoryFinding? Later, SessionHistoryChange Change, string Explanation);
public sealed record SessionHistoryComparison(SessionHistorySession Earlier, SessionHistorySession Later, IReadOnlyList<SessionHistoryComparisonEntry> Entries);
