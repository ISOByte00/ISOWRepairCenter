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
    public void Event_log_commands_emit_structured_locale_independent_json()
    {
        var commands = OperationCatalog.Create().Where(x => x.Id is "storage-events" or "critical-events").Select(x => x.BuildSteps(new OperationInputs()).Single().Arguments.Last()).ToArray();

        Assert.IsTrue(commands.All(x => x.Contains("ConvertTo-Json", StringComparison.Ordinal)));
        Assert.IsTrue(commands.All(x => x.Contains("SchemaVersion=1", StringComparison.Ordinal)));
        Assert.IsTrue(commands.All(x => x.Contains("QuerySucceeded", StringComparison.Ordinal) && x.Contains("RecordId", StringComparison.Ordinal)));
        Assert.IsTrue(commands.All(x => x.Contains("-ErrorAction Stop", StringComparison.Ordinal)));
        Assert.IsTrue(commands.All(x => !x.Contains("LevelDisplayName", StringComparison.Ordinal)));
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
    public void Windows_11_is_detected_from_build_number_not_legacy_product_name()
    {
        var version = WindowsVersionInfo.FromRegistryValues("Windows 10 Pro", "26300", "24H2", "9818");
        Assert.AreEqual(26300, version.BuildNumber);
        Assert.AreEqual("Windows 11 Pro 24H2 (build 26300.9818)", version.DisplayName);
    }

    [TestMethod]
    public void Windows_10_is_preserved_for_pre_windows_11_builds()
    {
        var version = WindowsVersionInfo.FromRegistryValues("Windows 10 Pro", "19045", "22H2", "5000");
        Assert.AreEqual("Windows 10 Pro 22H2 (build 19045.5000)", version.DisplayName);
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
        Assert.IsTrue(File.Exists(Path.Combine(store.SessionDirectory, "verifications.json")));
        Assert.IsTrue(File.Exists(Path.Combine(store.SessionDirectory, "baselines.json")));
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
    public void Event_log_analyzer_marks_recurring_nvme_resets_for_manual_review()
    {
        var output = EventPayload((1, "2026-11-09T08:10:12+03:00", 129, "stornvme"), (2, "2026-11-09T08:10:32+03:00", 129, "stornvme"), (3, "2026-11-09T08:10:52+03:00", 129, "stornvme"));
        var finding = Analyze("storage-events", OperationOutcome.Succeeded, output).Single();

        Assert.AreEqual("nvme-reset-events", finding.Code);
        Assert.AreEqual(FindingSeverity.Warning, finding.Severity);
        Assert.AreEqual(FindingConfidence.ManualReviewRequired, finding.Confidence);
    }

    [TestMethod]
    public void Event_log_analyzer_deduplicates_and_does_not_infer_causation_without_time_overlap()
    {
        var storage = EventPayload((1, "2026-11-09T08:00:00+03:00", 129, "stornvme"));
        var critical = EventPayload((1, "2026-11-09T08:00:00+03:00", 129, "stornvme"), (2, "2026-11-09T12:00:00+03:00", 55, "Ntfs"));
        var findings = DiagnosticAnalyzer.Analyze(new[] { CreateResult("storage-events", OperationOutcome.Succeeded, storage), CreateResult("critical-events", OperationOutcome.Succeeded, critical) });

        Assert.AreEqual(1, findings.Count(x => x.Code == "nvme-reset-events"));
        Assert.IsTrue(findings.Any(x => x.Code == "ntfs-integrity-events"));
        Assert.IsFalse(findings.Any(x => x.Code == "storage-event-temporal-overlap"));
    }

    [TestMethod]
    public void Event_log_analyzer_keeps_invalid_or_empty_structured_output_unknown()
    {
        var invalid = Analyze("storage-events", OperationOutcome.Succeeded, "not json").Single();
        var wrongSchema = Analyze("storage-events", OperationOutcome.Succeeded, "[]").Single();
        var empty = Analyze("storage-events", OperationOutcome.Succeeded, EventPayload()).Single();
        var failedQuery = Analyze("storage-events", OperationOutcome.Succeeded, "{\"SchemaVersion\":1,\"QuerySucceeded\":false,\"Events\":[]}").Single();

        Assert.AreEqual(FindingConfidence.Unknown, invalid.Confidence);
        Assert.AreEqual(FindingConfidence.Unknown, wrongSchema.Confidence);
        Assert.AreEqual("event-log-no-matching-patterns", empty.Code);
        Assert.AreEqual(FindingConfidence.Known, empty.Confidence);
        Assert.AreEqual("event-log-query-failed", failedQuery.Code);
        Assert.AreEqual(FindingConfidence.ManualReviewRequired, failedQuery.Confidence);
    }

    [TestMethod]
    public void Event_log_analyzer_detects_a_recurring_cluster_even_when_later_events_are_far_apart()
    {
        var output = EventPayload((1, "2026-11-09T08:00:00+03:00", 129, "stornvme"), (2, "2026-11-09T08:03:00+03:00", 129, "stornvme"), (3, "2026-11-09T08:06:00+03:00", 129, "stornvme"), (4, "2026-11-10T12:00:00+03:00", 129, "stornvme"));
        var finding = Analyze("storage-events", OperationOutcome.Succeeded, output).Single();

        Assert.AreEqual(FindingSeverity.Warning, finding.Severity);
    }

    [TestMethod]
    public async Task Post_repair_verifier_keeps_verified_and_failed_results_separate_from_repair_execution()
    {
        var store = CreateStore();
        var repair = OperationCatalog.Create().Single(x => x.Id == "system-file-repair");
        var verified = await new PostRepairVerifier(new VerificationRunner("Windows Resource Protection did not find any integrity violations."), store).VerifyAsync(repair, 1, _ => { }, CancellationToken.None);
        var failed = await new PostRepairVerifier(new VerificationRunner("Windows Resource Protection found integrity violations."), store).VerifyAsync(repair, 2, _ => { }, CancellationToken.None);

        Assert.AreEqual(VerificationOutcome.Verified, verified.Outcome);
        Assert.AreEqual(VerificationOutcome.VerificationFailed, failed.Outcome);
        Assert.AreEqual(2, verified.Checks.Count);
        Assert.IsTrue(failed.Findings.Any(x => x.Code == "system-files-warning"));
    }

    [TestMethod]
    public async Task Post_repair_verifier_classifies_cancelled_failed_and_incomplete_checks_without_claiming_success()
    {
        var store = CreateStore();
        var repair = OperationCatalog.Create().Single(x => x.Id == "system-file-repair");
        var cancelled = await new PostRepairVerifier(new OutcomeRunner(OperationOutcome.Cancelled), store).VerifyAsync(repair, 1, _ => { }, CancellationToken.None);
        var failed = await new PostRepairVerifier(new OutcomeRunner(OperationOutcome.Failed), store).VerifyAsync(repair, 2, _ => { }, CancellationToken.None);
        var incomplete = await new PostRepairVerifier(new VerificationRunner(string.Empty), store).VerifyAsync(repair, 3, _ => { }, CancellationToken.None);

        Assert.AreEqual(VerificationOutcome.Cancelled, cancelled.Outcome);
        Assert.AreEqual(VerificationOutcome.Unknown, failed.Outcome);
        Assert.AreEqual(VerificationOutcome.Unknown, incomplete.Outcome);
    }

    [TestMethod]
    public async Task Pre_repair_baseline_and_change_analysis_do_not_claim_improvement_without_comparable_evidence()
    {
        var store = CreateStore();
        var repair = OperationCatalog.Create().Single(x => x.Id == "system-file-repair");
        var baseline = await new PreRepairBaselineCapture(new VerificationRunner("Windows Resource Protection found integrity violations."), store).CaptureAsync(repair, 1, _ => { }, CancellationToken.None);
        var verified = await new PostRepairVerifier(new VerificationRunner("Windows Resource Protection did not find any integrity violations."), store).VerifyAsync(repair, 2, _ => { }, CancellationToken.None);
        var unresolved = await new PostRepairVerifier(new VerificationRunner("Windows Resource Protection found integrity violations."), store).VerifyAsync(repair, 3, _ => { }, CancellationToken.None);

        Assert.AreEqual(BaselineOutcome.Captured, baseline.Outcome);
        Assert.AreEqual(RepairChangeOutcome.Improved, RepairChangeAnalyzer.Compare(baseline, verified));
        Assert.AreEqual(RepairChangeOutcome.StillPresent, RepairChangeAnalyzer.Compare(baseline, unresolved));
        Assert.AreEqual(RepairChangeOutcome.NotMeasured, RepairChangeAnalyzer.Compare(null, verified));
    }

    [TestMethod]
    public async Task Pre_repair_baseline_cancellation_or_unknown_output_prevents_comparison()
    {
        var store = CreateStore();
        var repair = OperationCatalog.Create().Single(x => x.Id == "system-file-repair");
        var cancelled = await new PreRepairBaselineCapture(new OutcomeRunner(OperationOutcome.Cancelled), store).CaptureAsync(repair, 1, _ => { }, CancellationToken.None);
        var unknown = await new PreRepairBaselineCapture(new VerificationRunner(string.Empty), store).CaptureAsync(repair, 2, _ => { }, CancellationToken.None);
        var verification = await new PostRepairVerifier(new VerificationRunner("Windows Resource Protection did not find any integrity violations."), store).VerifyAsync(repair, 3, _ => { }, CancellationToken.None);

        Assert.AreEqual(BaselineOutcome.Cancelled, cancelled.Outcome);
        Assert.AreEqual(BaselineOutcome.Unknown, unknown.Outcome);
        Assert.AreEqual(RepairChangeOutcome.NotMeasured, RepairChangeAnalyzer.Compare(unknown, verification));
    }

    [TestMethod]
    public async Task Session_store_records_verification_result_and_baseline_state()
    {
        var store = CreateStore();
        var verification = new RepairVerificationResult("system-file-repair", "Repair system image and files", VerificationOutcome.Verified, DateTimeOffset.Now, DateTimeOffset.Now, Array.Empty<OperationResult>(), Array.Empty<DiagnosticFinding>(), "Healthy checks.");
        await store.SaveAsync(Array.Empty<OperationResult>(), verifications: new[] { verification });

        Assert.IsTrue(File.Exists(Path.Combine(store.SessionDirectory, "verifications.json")));
        StringAssert.Contains(File.ReadAllText(Path.Combine(store.SessionDirectory, "report.txt")), "Pre-repair baseline: not captured");
    }

    [TestMethod]
    public async Task Session_store_records_restore_point_result_when_attempted()
    {
        var store = CreateStore();
        var restorePoint = new StepResult("Create restore point", "powershell", 1, TimeSpan.Zero, OperationOutcome.Failed, "restore.log", "Disabled.", "Restore point creation failed.");
        await store.SaveAsync(Array.Empty<OperationResult>(), restorePoint: restorePoint);

        Assert.IsTrue(File.Exists(Path.Combine(store.SessionDirectory, "safety.json")));
        StringAssert.Contains(File.ReadAllText(Path.Combine(store.SessionDirectory, "report.txt")), "System restore point: Failed");
    }

    [TestMethod]
    public void User_preferences_persist_supported_language_and_fall_back_safely()
    {
        var path = Path.Combine(Path.GetTempPath(), "IsoWrcTest_" + Guid.NewGuid().ToString("N"), "preferences.json");
        var preferences = new UserPreferencesStore(path);

        Assert.AreEqual("tr", preferences.LoadLanguage());
        preferences.SaveLanguage("en");
        Assert.AreEqual("en", preferences.LoadLanguage());
        preferences.SaveLanguage("unsupported");
        Assert.AreEqual("en", preferences.LoadLanguage());
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
        Assert.AreEqual("#86EFAC", viewModel.SeverityColor);
        Assert.AreEqual("Doğrulandı", viewModel.ConfidenceLabel);
        Assert.AreEqual("Dosya sistemi dirty bit durumu", viewModel.OperationTitle);
    }

    [TestMethod]
    public void Captured_fixture_corpus_distinguishes_healthy_problem_and_unknown_outcomes()
    {
        var dismHealthy = Analyze("dism-health", OperationOutcome.Succeeded, Fixture("dism-healthy.txt"), Fixture("dism-healthy.txt")).Single();
        var dismRepairable = Analyze("dism-health", OperationOutcome.Succeeded, Fixture("dism-healthy.txt"), Fixture("dism-repairable.txt")).Single();
        var sfcHealthy = Analyze("sfc-verify", OperationOutcome.Succeeded, Fixture("sfc-healthy.txt")).Single();
        var diskHealthy = Analyze("physical-disks", OperationOutcome.Succeeded, Fixture("physical-disk-healthy.json")).Single();
        var diskUnhealthy = Analyze("physical-disks", OperationOutcome.Succeeded, Fixture("physical-disk-unhealthy.json")).Single();
        var unknown = Analyze("sfc-verify", OperationOutcome.Succeeded, Fixture("unrecognized-output.txt")).Single();

        Assert.AreEqual(FindingConfidence.Known, dismHealthy.Confidence);
        Assert.AreEqual(FindingSeverity.Warning, dismRepairable.Severity);
        Assert.AreEqual("system-file-repair", dismRepairable.SuggestedOperationId);
        Assert.AreEqual(FindingConfidence.Known, sfcHealthy.Confidence);
        Assert.IsNull(sfcHealthy.SuggestedOperationId);
        Assert.AreEqual(FindingConfidence.Known, diskHealthy.Confidence);
        Assert.AreEqual(FindingSeverity.Critical, diskUnhealthy.Severity);
        Assert.AreEqual(FindingConfidence.Unknown, unknown.Confidence);
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

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static string EventPayload(params (long RecordId, string TimeCreated, int Id, string ProviderName)[] events) => System.Text.Json.JsonSerializer.Serialize(new
    {
        SchemaVersion = 1,
        QuerySucceeded = true,
        Events = events.Select(x => new { LogName = "System", x.RecordId, x.TimeCreated, x.Id, x.ProviderName, Level = 3, Message = "fixture" })
    });

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

    private sealed class VerificationRunner(string sfcOutput) : IProcessRunner
    {
        public Task<StepResult> RunAsync(CommandStep step, string outputFile, Action<string> onOutput, CancellationToken cancellationToken)
        {
            var output = step.FileName.Equals("sfc.exe", StringComparison.OrdinalIgnoreCase) ? sfcOutput : "No component store corruption detected.";
            File.WriteAllText(outputFile, output);
            return Task.FromResult(new StepResult(step.FileName, step.Preview, 0, TimeSpan.Zero, OperationOutcome.Succeeded, outputFile, null, "Fake result."));
        }
    }

    private sealed class OutcomeRunner(OperationOutcome outcome) : IProcessRunner
    {
        public Task<StepResult> RunAsync(CommandStep step, string outputFile, Action<string> onOutput, CancellationToken cancellationToken)
        {
            File.WriteAllText(outputFile, "fixture");
            return Task.FromResult(new StepResult(step.FileName, step.Preview, outcome == OperationOutcome.Succeeded ? 0 : 1, TimeSpan.Zero, outcome, outputFile, null, "Fake result."));
        }
    }
}
