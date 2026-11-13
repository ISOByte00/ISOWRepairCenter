using System.Text.Json;
using ISOWRepairCenter.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ISOWRepairCenter.Tests;

[TestClass]
public sealed class SessionHistoryComparerTests
{
    [TestMethod] public async Task Same_finding_is_persisting() => AssertChange(await CompareAsync(Session("a", Finding("issue", "dirty-bit")), Session("b", Finding("issue", "dirty-bit"))), SessionHistoryChange.Persisting);
    [TestMethod] public async Task New_finding_is_reported() => AssertChange(await CompareAsync(Session("a"), Session("b", Finding("issue", "dirty-bit"))), SessionHistoryChange.NewlyObserved);
    [TestMethod] public async Task Missing_after_completed_diagnostic_is_not_observed_not_resolved() => AssertChange(await CompareAsync(Session("a", Finding("issue", "dirty-bit")), Session("b")), SessionHistoryChange.NotObserved);
    [TestMethod] public async Task Missing_after_unrun_diagnostic_is_not_comparable() => AssertChange(await CompareAsync(Session("a", Finding("issue", "dirty-bit")), Session("b", operationOutcome: "Failed")), SessionHistoryChange.NotComparable);
    [TestMethod] public async Task Severity_change_is_separate() => AssertChange(await CompareAsync(Session("a", Finding("issue", "dirty-bit", "Warning")), Session("b", Finding("issue", "dirty-bit", "Critical"))), SessionHistoryChange.ChangedAssessment);
    [TestMethod] public async Task Confidence_known_to_unknown_is_separate() => AssertChange(await CompareAsync(Session("a", Finding("issue", "dirty-bit", confidence: "Known")), Session("b", Finding("issue", "dirty-bit", confidence: "Unknown"))), SessionHistoryChange.ChangedAssessment);
    [TestMethod] public async Task Invalid_json_is_reported_safely()
    {
        var root = Temp(); File.WriteAllText(Path.Combine(root, "findings.json"), "{");
        var result = await new SessionHistoryComparer().ReadAsync(root);
        Assert.IsTrue(result.Issues.Any(x => x.Code == "InvalidJson"));
    }
    [TestMethod] public async Task Missing_json_is_reported_safely()
    {
        var result = await new SessionHistoryComparer().ReadAsync(Temp());
        Assert.IsTrue(result.Issues.Count(x => x.Code == "MissingFile") >= 2);
    }
    [TestMethod] public async Task Legacy_session_without_optional_files_is_usable() => AssertChange(await CompareAsync(Session("a", Finding("issue", "dirty-bit")), Session("b")), SessionHistoryChange.NotObserved);
    [TestMethod] public async Task Recollected_same_event_is_not_a_new_fault()
    {
        var oldSession = Session("a", Finding("nvme-reset-events", "event-log-analysis"), eventRecord: true);
        var newSession = Session("b", Finding("nvme-reset-events", "event-log-analysis"), eventRecord: true);
        AssertChange(await CompareAsync(oldSession, newSession), SessionHistoryChange.HistoricalEventRepeated);
    }
    [TestMethod] public async Task Recollected_same_ntfs_and_disk_events_are_historical_not_new_faults()
    {
        var ntfsOld = Session("ntfs-old", Finding("ntfs-integrity-events", "event-log-analysis"), eventRecord: true, eventRecords: new[] { Event("Ntfs", 55, 51) });
        var ntfsNew = Session("ntfs-new", Finding("ntfs-integrity-events", "event-log-analysis"), eventRecord: true, eventRecords: new[] { Event("Ntfs", 55, 51) });
        var diskOld = Session("disk-old", Finding("disk-retry-events", "event-log-analysis"), eventRecord: true, eventRecords: new[] { Event("Disk", 153, 62) });
        var diskNew = Session("disk-new", Finding("disk-retry-events", "event-log-analysis"), eventRecord: true, eventRecords: new[] { Event("Disk", 153, 62) });

        AssertChange(await CompareAsync(ntfsOld, ntfsNew), SessionHistoryChange.HistoricalEventRepeated);
        AssertChange(await CompareAsync(diskOld, diskNew), SessionHistoryChange.HistoricalEventRepeated);
    }
    [TestMethod] public async Task Different_event_records_with_the_same_finding_are_not_same_historical_event()
    {
        var oldSession = Session("old", Finding("nvme-reset-events", "event-log-analysis"), eventRecord: true, eventRecords: new[] { Event("stornvme", 129, 71) });
        var newSession = Session("new", Finding("nvme-reset-events", "event-log-analysis"), eventRecord: true, eventRecords: new[] { Event("stornvme", 129, 72) });
        AssertChange(await CompareAsync(oldSession, newSession), SessionHistoryChange.Persisting);
    }
    [TestMethod] public async Task Unrelated_repeated_event_does_not_make_another_finding_historical()
    {
        var oldRecords = new[] { Event("stornvme", 129, 81), Event("Disk", 153, 82) };
        var newRecords = new[] { Event("stornvme", 129, 91), Event("Disk", 153, 82) };
        var oldSession = Session("old", Finding("nvme-reset-events", "event-log-analysis"), eventRecord: true, eventRecords: oldRecords);
        var newSession = Session("new", Finding("nvme-reset-events", "event-log-analysis"), eventRecord: true, eventRecords: newRecords);
        AssertChange(await CompareAsync(oldSession, newSession), SessionHistoryChange.Persisting);
    }
    [TestMethod] public async Task Missing_event_identity_is_not_comparable_and_is_reported()
    {
        var oldSession = Session("old", Finding("nvme-reset-events", "event-log-analysis"), eventRecord: true, eventRecords: new[] { Event("stornvme", 129, 91) });
        var newSession = Session("new", Finding("nvme-reset-events", "event-log-analysis"), eventRecord: true, invalidEventRecord: true);
        var comparison = await CompareAsync(oldSession, newSession);
        AssertChange(comparison, SessionHistoryChange.NotComparable);
        Assert.IsTrue(comparison.Later.Issues.Any(x => x.Code == "InvalidEventRecord"));
    }
    [TestMethod] public async Task Same_record_id_in_different_logs_is_not_the_same_historical_event()
    {
        var oldSession = Session("old", Finding("nvme-reset-events", "event-log-analysis"), eventRecord: true, eventRecords: new[] { Event("System", "stornvme", 129, 101) });
        var newSession = Session("new", Finding("nvme-reset-events", "event-log-analysis"), eventRecord: true, eventRecords: new[] { Event("OtherLog", "stornvme", 129, 101) });
        AssertChange(await CompareAsync(oldSession, newSession), SessionHistoryChange.Persisting);
    }
    [TestMethod] public async Task Missing_operation_id_is_ignored_and_reported()
    {
        var root = Temp();
        File.WriteAllText(Path.Combine(root, "findings.json"), "[]");
        File.WriteAllText(Path.Combine(root, "results.json"), "[{\"Outcome\":\"Succeeded\",\"Steps\":[]}]");
        var read = await new SessionHistoryComparer().ReadAsync(root);
        Assert.IsTrue(read.Issues.Any(x => x.Code == "InvalidOperation"));
    }
    [TestMethod] public async Task Different_event_collection_scope_is_not_comparable()
    {
        var oldSession = Session("a", Finding("nvme-reset-events", "event-log-analysis"), eventRecord: true, eventOperation: "storage-events");
        var newSession = Session("b", operationId: "critical-events");
        AssertChange(await CompareAsync(oldSession, newSession), SessionHistoryChange.NotComparable);
    }
    [TestMethod] public async Task Valid_empty_findings_are_read()
    {
        var read = await new SessionHistoryComparer().ReadAsync(Session("empty"));
        Assert.AreEqual(0, read.Findings.Count); Assert.IsFalse(read.Issues.Any(x => x.Code == "InvalidJson"));
    }

    private static async Task<SessionHistoryComparison> CompareAsync(string oldSession, string newSession) => await new SessionHistoryComparer().CompareAsync(oldSession, newSession);
    private static void AssertChange(SessionHistoryComparison comparison, SessionHistoryChange expected) => Assert.AreEqual(expected, comparison.Entries.Single().Change);
    private static string Session(string name, object? finding = null, string operationId = "dirty-bit", string operationOutcome = "Succeeded", bool eventRecord = false, string? eventOperation = null, EventFixture[]? eventRecords = null, bool invalidEventRecord = false)
    {
        var root = Path.Combine(Temp(), name); Directory.CreateDirectory(root);
        var id = eventOperation ?? (eventRecord ? "storage-events" : operationId);
        object[] findings = finding is null ? Array.Empty<object>() : new[] { finding };
        File.WriteAllText(Path.Combine(root, "findings.json"), JsonSerializer.Serialize(findings));
        var output = eventRecord ? "events.log" : "normal.log";
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new[] { new { Id = id, Outcome = operationOutcome, Steps = new[] { new { OutputFile = output } } } }));
        if (eventRecord)
        {
            object[] records = invalidEventRecord
                ? new object[] { new { ProviderName = "stornvme", Id = 129, RecordId = (long?)null, TimeCreated = "2026-01-02T00:00:00Z" } }
                : (eventRecords ?? new[] { Event("stornvme", 129, 42) }).Select(x => (object)new { x.LogName, x.ProviderName, x.Id, x.RecordId, x.TimeCreated }).ToArray();
            File.WriteAllText(Path.Combine(root, output), JsonSerializer.Serialize(new { QuerySucceeded = true, WindowStart = "2026-01-01T00:00:00Z", Events = records }));
        }
        return root;
    }
    private static object Finding(string code, string operationId, string severity = "Warning", string confidence = "Known") => new { Code = code, OperationId = operationId, Severity = severity, Confidence = confidence };
    private static EventFixture Event(string providerName, int id, long recordId) => Event("System", providerName, id, recordId);
    private static EventFixture Event(string logName, string providerName, int id, long recordId) => new(logName, providerName, id, recordId, "2026-01-02T00:00:00Z");
    private sealed record EventFixture(string LogName, string ProviderName, int Id, long RecordId, string TimeCreated);
    private static string Temp() { var path = Path.Combine(Path.GetTempPath(), "IsoWrcHistory_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
}
