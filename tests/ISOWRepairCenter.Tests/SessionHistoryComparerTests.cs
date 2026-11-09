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
    private static string Session(string name, object? finding = null, string operationId = "dirty-bit", string operationOutcome = "Succeeded", bool eventRecord = false, string? eventOperation = null)
    {
        var root = Path.Combine(Temp(), name); Directory.CreateDirectory(root);
        var id = eventOperation ?? operationId;
        object[] findings = finding is null ? Array.Empty<object>() : new[] { finding };
        File.WriteAllText(Path.Combine(root, "findings.json"), JsonSerializer.Serialize(findings));
        var output = eventRecord ? "events.log" : "normal.log";
        File.WriteAllText(Path.Combine(root, "results.json"), JsonSerializer.Serialize(new[] { new { Id = id, Outcome = operationOutcome, Steps = new[] { new { OutputFile = output } } } }));
        if (eventRecord) File.WriteAllText(Path.Combine(root, output), "{\"QuerySucceeded\":true,\"WindowStart\":\"2026-01-01T00:00:00Z\",\"Events\":[{\"ProviderName\":\"stornvme\",\"Id\":129,\"RecordId\":42,\"TimeCreated\":\"2026-01-02T00:00:00Z\"}]}");
        return root;
    }
    private static object Finding(string code, string operationId, string severity = "Warning", string confidence = "Known") => new { Code = code, OperationId = operationId, Severity = severity, Confidence = confidence };
    private static string Temp() { var path = Path.Combine(Path.GetTempPath(), "IsoWrcHistory_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
}
