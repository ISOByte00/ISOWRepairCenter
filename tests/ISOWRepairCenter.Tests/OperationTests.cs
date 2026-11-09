using Microsoft.VisualStudio.TestTools.UnitTesting;
using ISOWRepairCenter.Core;

namespace ISOWRepairCenter.Tests;

[TestClass]
public sealed class OperationTests
{
    [TestMethod]
    public void Catalog_has_guarded_expert_operations_without_unsafe_boot_or_disk_repairs()
    {
        var catalog = OperationCatalog.Create();
        Assert.IsTrue(catalog.Any(x => x.Category == OperationCategory.Diagnostic));
        Assert.IsTrue(catalog.Any(x => x.Id == "update-reset"));
        Assert.IsFalse(catalog.Any(x => x.Id == "bcdboot"));
        Assert.IsFalse(catalog.Any(x => x.Id == "chkdsk-fix"));
    }

    [TestMethod]
    public void Runner_uses_direct_argument_execution()
    {
        var command = ProcessRunner.CreateStartInfo(new CommandStep("tool.exe", new[] { "argument with spaces", "%SystemDrive%" }));
        Assert.AreEqual("argument with spaces", command.ArgumentList[0]);
        Assert.IsFalse(command.UseShellExecute);
        Assert.IsTrue(command.RedirectStandardOutput && command.RedirectStandardError);
    }

    [TestMethod]
    public void Critical_event_filter_is_locale_independent()
    {
        var command = OperationCatalog.Create().Single(x => x.Id == "critical-events").BuildSteps(new OperationInputs()).Single();
        Assert.IsTrue(command.Arguments.Last().Contains("$_.Level -in 1,2", StringComparison.Ordinal));
        Assert.IsFalse(command.Arguments.Last().Contains("LevelDisplayName -in", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Update_reset_uses_finally_to_restore_services()
    {
        var command = OperationCatalog.Create().Single(x => x.Id == "update-reset").BuildSteps(new OperationInputs()).Single();
        Assert.IsTrue(command.Arguments.Last().Contains("finally", StringComparison.Ordinal));
        Assert.IsTrue(command.Arguments.Last().Contains("Start-Service", StringComparison.Ordinal));
        Assert.IsTrue(command.Arguments.Last().Contains("did not reach the Running state", StringComparison.Ordinal));
        Assert.IsTrue(command.Arguments.Last().Contains("Windows Update service recovery failed", StringComparison.Ordinal));
        Assert.AreEqual(CancellationStrategy.CompleteBeforeCancellation, command.CancellationStrategy);
    }

    [TestMethod]
    public async Task Executor_classifies_runner_failure()
    {
        var store = CreateStore();
        var executor = new RepairExecutor(new FakeRunner(OperationOutcome.Failed), store);
        var result = await executor.ExecuteAsync(CreateOperation(), new OperationInputs(), 1, _ => { }, CancellationToken.None);
        Assert.AreEqual(OperationOutcome.Failed, result.Outcome);
    }

    [TestMethod]
    public async Task Executor_classifies_runner_success()
    {
        var store = CreateStore();
        var executor = new RepairExecutor(new FakeRunner(OperationOutcome.Succeeded), store);
        var result = await executor.ExecuteAsync(CreateOperation(), new OperationInputs(), 1, _ => { }, CancellationToken.None);
        Assert.AreEqual(OperationOutcome.Succeeded, result.Outcome);
    }

    [TestMethod]
    public async Task Executor_classifies_cancellation()
    {
        var store = CreateStore();
        var executor = new RepairExecutor(new FakeRunner(OperationOutcome.Cancelled), store);
        var result = await executor.ExecuteAsync(CreateOperation(), new OperationInputs(), 1, _ => { }, CancellationToken.None);
        Assert.AreEqual(OperationOutcome.Cancelled, result.Outcome);
    }

    [TestMethod]
    public async Task Session_store_writes_all_report_formats()
    {
        var root = Path.Combine(Path.GetTempPath(), "IsoWrcTest_" + Guid.NewGuid().ToString("N"));
        var store = new SessionStore(root); store.Start();
        await store.SaveAsync(new[] { new OperationResult("test", "Test", DateTimeOffset.Now, DateTimeOffset.Now, OperationOutcome.Succeeded, Array.Empty<StepResult>()) });
        Assert.IsTrue(File.Exists(Path.Combine(store.SessionDirectory, "results.json")));
        Assert.IsTrue(File.Exists(Path.Combine(store.SessionDirectory, "findings.json")));
        Assert.IsTrue(File.Exists(Path.Combine(store.SessionDirectory, "report.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(store.SessionDirectory, "report.html")));
        Directory.Delete(root, true);
    }

    [TestMethod]
    public async Task Session_store_rejects_report_save_before_session_start()
    {
        var store = new SessionStore(Path.Combine(Path.GetTempPath(), "IsoWrcTest_" + Guid.NewGuid().ToString("N")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(Array.Empty<OperationResult>()));
    }

    [TestMethod]
    public void Diagnostic_analyzer_creates_actionable_findings_from_command_output()
    {
        var root = Path.Combine(Path.GetTempPath(), "IsoWrcTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var output = Path.Combine(root, "dirty-bit.log");
        File.WriteAllText(output, "Volume C: is NOT Dirty");
        var result = new OperationResult("dirty-bit", "File system dirty bit", DateTimeOffset.Now, DateTimeOffset.Now, OperationOutcome.Succeeded,
            new[] { new StepResult("fsutil", "fsutil dirty query C:", 0, TimeSpan.Zero, OperationOutcome.Succeeded, output, null, "Completed.") });

        var finding = DiagnosticAnalyzer.Analyze(new[] { result }).Single();

        Assert.AreEqual("file-system-clean", finding.Code);
        Assert.AreEqual(FindingSeverity.Information, finding.Severity);
        Directory.Delete(root, true);
    }

    private static SessionStore CreateStore()
    {
        var store = new SessionStore(Path.Combine(Path.GetTempPath(), "IsoWrcTest_" + Guid.NewGuid().ToString("N")));
        store.Start();
        return store;
    }

    private static RepairOperation CreateOperation() => new()
    {
        Id = "test", Title = "Test", Description = "Test", Category = OperationCategory.Diagnostic,
        BuildSteps = _ => new[] { new CommandStep("fake.exe", Array.Empty<string>()) }
    };

    private sealed class FakeRunner(OperationOutcome outcome) : IProcessRunner
    {
        public Task<StepResult> RunAsync(CommandStep step, string outputFile, Action<string> onOutput, CancellationToken cancellationToken) =>
            Task.FromResult(new StepResult(step.FileName, step.Preview, outcome == OperationOutcome.Succeeded ? 0 : 1, TimeSpan.Zero, outcome, outputFile, null, "Fake result."));
    }
}
