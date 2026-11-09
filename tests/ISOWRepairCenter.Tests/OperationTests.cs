using Microsoft.VisualStudio.TestTools.UnitTesting;
using ISOWRepairCenter.Core;
using ISOWRepairCenter.ViewModels;

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
    public void Quick_profile_contains_only_short_read_only_checks()
    {
        var quickOperations = OperationCatalog.Create().Where(DiagnosticProfiles.IsQuickDiagnostic).ToList();
        CollectionAssert.AreEquivalent(new[] { "system-overview", "physical-disks", "dirty-bit", "update-status", "network-status" }, quickOperations.Select(x => x.Id).ToArray());
        Assert.IsTrue(quickOperations.All(x => x.Category == OperationCategory.Diagnostic));
    }

    [TestMethod]
    public void Dism_diagnostic_forces_invariant_english_output()
    {
        var steps = OperationCatalog.Create().Single(x => x.Id == "dism-health").BuildSteps(new OperationInputs());
        Assert.IsTrue(steps.All(x => x.Arguments.Contains("/English")));
    }

    [TestMethod]
    public void Sfc_diagnostic_uses_utf16_output_decoding()
    {
        var step = OperationCatalog.Create().Single(x => x.Id == "sfc-verify").BuildSteps(new OperationInputs()).Single();
        var startInfo = ProcessRunner.CreateStartInfo(step);
        Assert.AreEqual(CommandOutputEncoding.Utf16Le, step.OutputEncoding);
        Assert.AreEqual(System.Text.Encoding.Unicode.WebName, startInfo.StandardOutputEncoding?.WebName);
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
        var finding = Analyze("dirty-bit", OperationOutcome.Succeeded, "Volume C: is NOT Dirty").Single();

        Assert.AreEqual("file-system-clean", finding.Code);
        Assert.AreEqual(FindingSeverity.Information, finding.Severity);
        Assert.AreEqual(FindingConfidence.Known, finding.Confidence);
    }

    [TestMethod]
    public void Diagnostic_analyzer_does_not_treat_empty_or_unexpected_output_as_healthy()
    {
        var empty = Analyze("physical-disks", OperationOutcome.Succeeded, string.Empty).Single();
        var unexpected = Analyze("sfc-verify", OperationOutcome.Succeeded, "Unexpected localized output").Single();

        Assert.AreEqual(FindingConfidence.Unknown, empty.Confidence);
        Assert.AreEqual(FindingConfidence.Unknown, unexpected.Confidence);
    }

    [TestMethod]
    public void Diagnostic_analyzer_keeps_failed_and_cancelled_checks_out_of_healthy_state()
    {
        var failed = Analyze("dirty-bit", OperationOutcome.Failed, "failure").Single();
        var cancelled = Analyze("dirty-bit", OperationOutcome.Cancelled, "partial").Single();

        Assert.AreEqual(FindingConfidence.ManualReviewRequired, failed.Confidence);
        Assert.AreEqual(FindingSeverity.Warning, failed.Severity);
        Assert.AreEqual(FindingConfidence.Unknown, cancelled.Confidence);
        Assert.AreEqual(FindingSeverity.Information, cancelled.Severity);
    }

    [TestMethod]
    public void Diagnostic_analyzer_recognizes_turkish_sfc_output()
    {
        var finding = Analyze("sfc-verify", OperationOutcome.Succeeded, "Windows Kaynak Koruması herhangi bir bütünlük ihlali bulamadı.").Single();

        Assert.AreEqual("system-files-healthy", finding.Code);
        Assert.AreEqual(FindingConfidence.Known, finding.Confidence);
    }

    [TestMethod]
    public void Diagnostic_analyzer_requires_complete_structured_disk_health_data()
    {
        var healthy = Analyze("physical-disks", OperationOutcome.Succeeded, "[{\"HealthStatus\":\"Healthy\",\"OperationalStatus\":\"OK\"}]").Single();
        var unhealthy = Analyze("physical-disks", OperationOutcome.Succeeded, "[{\"HealthStatus\":\"Unhealthy\",\"OperationalStatus\":\"OK\"}]").Single();
        var incomplete = Analyze("physical-disks", OperationOutcome.Succeeded, "[{\"HealthStatus\":\"Healthy\"}]").Single();

        Assert.AreEqual(FindingConfidence.Known, healthy.Confidence);
        Assert.AreEqual(FindingSeverity.Information, healthy.Severity);
        Assert.AreEqual(FindingSeverity.Critical, unhealthy.Severity);
        Assert.AreEqual(FindingConfidence.Unknown, incomplete.Confidence);
    }

    [TestMethod]
    public void Diagnostic_analyzer_requires_both_dism_health_steps()
    {
        var healthy = Analyze("dism-health", OperationOutcome.Succeeded, "No component store corruption detected.", "No component store corruption detected.").Single();
        var incomplete = Analyze("dism-health", OperationOutcome.Succeeded, "No component store corruption detected.").Single();

        Assert.AreEqual(FindingConfidence.Known, healthy.Confidence);
        Assert.AreEqual(FindingConfidence.Unknown, incomplete.Confidence);
    }

    [TestMethod]
    public void Findings_are_sorted_by_severity_then_confidence()
    {
        var results = new[]
        {
            CreateResult("sfc-verify", OperationOutcome.Succeeded, "Unexpected output"),
            CreateResult("physical-disks", OperationOutcome.Succeeded, "[{\"HealthStatus\":\"Unhealthy\",\"OperationalStatus\":\"OK\"}]"),
            CreateResult("dirty-bit", OperationOutcome.Succeeded, "Volume C: is Dirty")
        };
        var findings = DiagnosticAnalyzer.Analyze(results);

        Assert.AreEqual("disk-health-critical", findings[0].Code);
        Assert.AreEqual("file-system-dirty", findings[1].Code);
        Assert.AreEqual(FindingConfidence.Unknown, findings[2].Confidence);
    }

    [TestMethod]
    public void Finding_view_model_localizes_labels_without_changing_confidence()
    {
        var finding = new DiagnosticFinding("file-system-clean", FindingSeverity.Information, FindingConfidence.Known, "dirty-bit", "File system dirty bit", "English", "Evidence", "Recommendation");
        var viewModel = new DiagnosticFindingViewModel(finding, "tr");

        Assert.AreEqual("Bilgi", viewModel.SeverityLabel);
        Assert.AreEqual("Doğrulandı", viewModel.ConfidenceLabel);
        Assert.AreEqual("Dosya sistemi dirty bit durumu", viewModel.OperationTitle);
    }

    private static IReadOnlyList<DiagnosticFinding> Analyze(string id, OperationOutcome outcome, params string[] outputs)
    {
        var root = Path.Combine(Path.GetTempPath(), "IsoWrcTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var result = CreateResult(id, outcome, outputs, root);
            return DiagnosticAnalyzer.Analyze(new[] { result });
        }
        finally { Directory.Delete(root, true); }
    }

    private static OperationResult CreateResult(string id, OperationOutcome outcome, string output, string? root = null) => CreateResult(id, outcome, new[] { output }, root);
    private static OperationResult CreateResult(string id, OperationOutcome outcome, string[] outputs, string? root = null)
    {
        root ??= Path.Combine(Path.GetTempPath(), "IsoWrcTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var steps = outputs.Select((output, index) =>
        {
            var path = Path.Combine(root, $"{index}.log");
            File.WriteAllText(path, output);
            return new StepResult("test", "test", outcome == OperationOutcome.Succeeded ? 0 : 1, TimeSpan.Zero, outcome, path, null, "Test.");
        }).ToArray();
        return new OperationResult(id, id, DateTimeOffset.Now, DateTimeOffset.Now, outcome, steps);
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
