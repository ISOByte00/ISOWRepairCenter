using System.Text.Json;
using ISOWRepairCenter.Localization;
using ISOWRepairCenter.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ISOWRepairCenter.Tests;

[TestClass]
public sealed class SessionHistoryViewModelTests
{
    [TestMethod] public async Task No_sessions_shows_empty_state()
    {
        var viewModel = Create(Temp()); await viewModel.RefreshAsync();
        Assert.AreEqual(0, viewModel.Sessions.Count); StringAssert.Contains(viewModel.Status, "No saved sessions");
    }

    [TestMethod] public async Task One_session_is_listed_without_comparison_selection()
    {
        var root = Temp(); WriteSession(root, "Session_20260101_120000_one");
        var viewModel = Create(root); await viewModel.RefreshAsync();
        Assert.AreEqual(1, viewModel.Sessions.Count); Assert.IsNull(viewModel.EarlierSession); Assert.IsFalse(viewModel.CanCompare);
    }

    [TestMethod] public async Task Two_valid_sessions_are_compared_in_date_order()
    {
        var root = Temp(); WriteSession(root, "Session_20260101_120000_old", Finding("old", "dirty-bit")); WriteSession(root, "Session_20260102_120000_new", Finding("old", "dirty-bit"));
        var viewModel = Create(root); await viewModel.RefreshAsync(); await viewModel.CompareAsync();
        Assert.AreEqual("Session_20260102_120000_new", Path.GetFileName(viewModel.LaterSession!.Path)); Assert.AreEqual(1, viewModel.ComparisonEntries.Count);
    }

    [TestMethod] public async Task Broken_json_is_exposed_as_notice_without_crashing()
    {
        var root = Temp(); var session = Path.Combine(root, "Session_20260101_120000_bad"); Directory.CreateDirectory(session); File.WriteAllText(Path.Combine(session, "findings.json"), "{");
        var viewModel = Create(root); await viewModel.RefreshAsync();
        Assert.AreEqual(1, viewModel.Sessions.Count); Assert.IsTrue(viewModel.Issues.Any(x => x.Code == "InvalidJson"));
    }

    [TestMethod] public async Task Incomplete_event_scope_is_shown_as_not_comparable()
    {
        var root = Temp(); WriteSession(root, "Session_20260101_120000_old", Finding("nvme-reset-events", "event-log-analysis"), "storage-events", eventOutput: true); WriteSession(root, "Session_20260102_120000_new", operationId: "critical-events");
        var viewModel = Create(root); await viewModel.RefreshAsync(); await viewModel.CompareAsync();
        Assert.AreEqual("Not comparable", viewModel.ComparisonEntries.Single().ChangeLabel);
    }

    [TestMethod] public async Task Not_observed_label_never_says_resolved()
    {
        var root = Temp(); WriteSession(root, "Session_20260101_120000_old", Finding("issue", "dirty-bit")); WriteSession(root, "Session_20260102_120000_new");
        var viewModel = Create(root); await viewModel.RefreshAsync(); await viewModel.CompareAsync();
        StringAssert.Contains(viewModel.ComparisonEntries.Single().ChangeLabel, "not resolved");
    }

    [TestMethod] public async Task Historical_event_repeat_has_safe_label()
    {
        var root = Temp(); WriteSession(root, "Session_20260101_120000_old", Finding("nvme-reset-events", "event-log-analysis"), "storage-events", true); WriteSession(root, "Session_20260102_120000_new", Finding("nvme-reset-events", "event-log-analysis"), "storage-events", true);
        var viewModel = Create(root); await viewModel.RefreshAsync(); await viewModel.CompareAsync();
        StringAssert.Contains(viewModel.ComparisonEntries.Single().ChangeLabel, "historical");
    }

    [TestMethod] public async Task Changing_selection_uses_the_selected_pair()
    {
        var root = Temp(); WriteSession(root, "Session_20260101_120000_first", Finding("first", "dirty-bit")); WriteSession(root, "Session_20260102_120000_middle", Finding("middle", "dirty-bit")); WriteSession(root, "Session_20260103_120000_last", Finding("last", "dirty-bit"));
        var viewModel = Create(root); await viewModel.RefreshAsync(); viewModel.EarlierSession = viewModel.Sessions[2]; viewModel.LaterSession = viewModel.Sessions[1]; await viewModel.CompareAsync();
        Assert.IsTrue(viewModel.ComparisonEntries.Any(x => x.Finding == "first")); Assert.IsTrue(viewModel.ComparisonEntries.Any(x => x.Finding == "middle")); Assert.IsFalse(viewModel.ComparisonEntries.Any(x => x.Finding == "last"));
    }

    [TestMethod] public async Task Invalid_session_does_not_prevent_valid_sessions_from_loading()
    {
        var root = Temp(); WriteSession(root, "Session_20260101_120000_good"); Directory.CreateDirectory(Path.Combine(root, "Session_20260102_120000_missing"));
        var viewModel = Create(root); await viewModel.RefreshAsync();
        Assert.AreEqual(2, viewModel.Sessions.Count); Assert.IsTrue(viewModel.Issues.Any(x => x.Code == "MissingFile"));
    }

    [TestMethod] public async Task Refreshing_language_relocalizes_the_current_history_status()
    {
        var text = new LocalizedText { Language = "en" };
        var viewModel = new SessionHistoryViewModel(text, Temp());
        await viewModel.RefreshAsync();
        StringAssert.Contains(viewModel.Status, "No saved sessions");

        text.Language = "tr";
        viewModel.RefreshLanguage();

        StringAssert.Contains(viewModel.Status, "Kayıtlı oturum");
    }

    private static SessionHistoryViewModel Create(string root) => new(new LocalizedText { Language = "en" }, root);
    private static void WriteSession(string root, string name, object? finding = null, string operationId = "dirty-bit", bool eventOutput = false)
    {
        var directory = Path.Combine(root, name); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "findings.json"), JsonSerializer.Serialize(finding is null ? Array.Empty<object>() : new[] { finding }));
        var output = eventOutput ? "events.log" : "normal.log";
        File.WriteAllText(Path.Combine(directory, "results.json"), JsonSerializer.Serialize(new[] { new { Id = operationId, Outcome = "Succeeded", Steps = new[] { new { OutputFile = output } } } }));
        if (eventOutput) File.WriteAllText(Path.Combine(directory, output), "{\"QuerySucceeded\":true,\"WindowStart\":\"2026-01-01T00:00:00Z\",\"Events\":[{\"LogName\":\"System\",\"ProviderName\":\"stornvme\",\"Id\":129,\"RecordId\":42,\"TimeCreated\":\"2026-01-02T00:00:00Z\"}]}");
    }
    private static object Finding(string code, string operationId) => new { Code = code, OperationId = operationId, Severity = "Warning", Confidence = "Known" };
    private static string Temp() { var path = Path.Combine(Path.GetTempPath(), "IsoWrcHistoryUi_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
}
