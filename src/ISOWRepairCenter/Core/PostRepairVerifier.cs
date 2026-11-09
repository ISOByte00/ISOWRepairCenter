namespace ISOWRepairCenter.Core;

public enum VerificationOutcome { NotApplicable, Verified, VerificationFailed, Unknown, Cancelled, Deferred }
public enum BaselineOutcome { NotApplicable, Captured, Unknown, Cancelled }
public enum RepairChangeOutcome { NotMeasured, Improved, StillPresent, Indeterminate }

public sealed record RepairVerificationResult(
    string RepairOperationId,
    string RepairOperationTitle,
    VerificationOutcome Outcome,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    IReadOnlyList<OperationResult> Checks,
    IReadOnlyList<DiagnosticFinding> Findings,
    string Summary,
    bool BaselineCaptured = false,
    RepairChangeOutcome ChangeOutcome = RepairChangeOutcome.NotMeasured,
    RepairChangeOutcome DismChangeOutcome = RepairChangeOutcome.NotMeasured,
    RepairChangeOutcome SfcChangeOutcome = RepairChangeOutcome.NotMeasured,
    string ChangeEvidence = "");

public sealed record VerificationCheck(string Id, string Title, IReadOnlyList<CommandStep> Steps);

public sealed record RepairBaselineResult(
    string RepairOperationId,
    string RepairOperationTitle,
    BaselineOutcome Outcome,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    IReadOnlyList<OperationResult> Checks,
    IReadOnlyList<DiagnosticFinding> Findings);

public static class RepairVerificationCatalog
{
    public static IReadOnlyList<VerificationCheck> For(string repairOperationId) => repairOperationId switch
    {
        "system-file-repair" => new[]
        {
            new VerificationCheck("dism-health", "Post-repair DISM verification", new[]
            {
                new CommandStep("DISM.exe", new[] { "/Online", "/Cleanup-Image", "/CheckHealth", "/English" }),
                new CommandStep("DISM.exe", new[] { "/Online", "/Cleanup-Image", "/ScanHealth", "/English" })
            }),
            new VerificationCheck("sfc-verify", "Post-repair SFC verification", new[]
            {
                new CommandStep("sfc.exe", new[] { "/verifyonly" }, OutputEncoding: CommandOutputEncoding.Utf16Le)
            })
        },
        _ => Array.Empty<VerificationCheck>()
    };
}

public sealed class PostRepairVerifier
{
    private readonly IProcessRunner _runner;
    private readonly SessionStore _store;

    public PostRepairVerifier(IProcessRunner runner, SessionStore store) { _runner = runner; _store = store; }

    public async Task<RepairVerificationResult> VerifyAsync(RepairOperation repair, int operationNumber, Action<string> writeLog, CancellationToken token)
    {
        var started = DateTimeOffset.Now;
        var plan = RepairVerificationCatalog.For(repair.Id);
        if (plan.Count == 0) return new RepairVerificationResult(repair.Id, repair.Title, VerificationOutcome.NotApplicable, started, DateTimeOffset.Now, Array.Empty<OperationResult>(), Array.Empty<DiagnosticFinding>(), "No post-repair verification is defined for this operation.");

        var checks = new List<OperationResult>();
        foreach (var check in plan)
        {
            var checkStarted = DateTimeOffset.Now;
            var steps = new List<StepResult>();
            for (var index = 0; index < check.Steps.Count; index++)
            {
                var step = check.Steps[index];
                writeLog($"> {step.Preview}");
                var output = _store.StepOutputPath(operationNumber, $"{repair.Id}_postcheck_{check.Id}_{index + 1}");
                var result = await _runner.RunAsync(step, output, writeLog, token);
                steps.Add(result);
                if (!result.Succeeded) break;
            }
            var checkOutcome = steps.Count == 0 ? OperationOutcome.Skipped : steps.FirstOrDefault(x => !x.Succeeded)?.Outcome ?? OperationOutcome.Succeeded;
            checks.Add(new OperationResult(check.Id, check.Title, checkStarted, DateTimeOffset.Now, checkOutcome, steps));
            if (checkOutcome == OperationOutcome.Cancelled) break;
        }

        var findings = DiagnosticAnalyzer.Analyze(checks);
        var outcome = Classify(checks, findings);
        return new RepairVerificationResult(repair.Id, repair.Title, outcome, started, DateTimeOffset.Now, checks, findings, SummaryFor(outcome));
    }

    private static VerificationOutcome Classify(IReadOnlyList<OperationResult> checks, IReadOnlyList<DiagnosticFinding> findings)
    {
        if (checks.Any(x => x.Cancelled)) return VerificationOutcome.Cancelled;
        if (checks.Count == 0 || checks.Any(x => !x.Succeeded)) return VerificationOutcome.Unknown;
        if (findings.Any(x => x.Code is "component-store-warning" or "system-files-warning")) return VerificationOutcome.VerificationFailed;
        if (findings.Any(x => x.Confidence != FindingConfidence.Known)) return VerificationOutcome.Unknown;
        return findings.Any(x => x.Code == "component-store-healthy") && findings.Any(x => x.Code == "system-files-healthy")
            ? VerificationOutcome.Verified
            : VerificationOutcome.Unknown;
    }

    internal static string SummaryFor(VerificationOutcome outcome, bool baselineCaptured = false) => outcome switch
    {
        VerificationOutcome.Verified when baselineCaptured => "Post-repair checks completed and recognized healthy results. The captured baseline is available for change assessment.",
        VerificationOutcome.Verified => "Post-repair checks completed and recognized healthy results. No comparable pre-repair baseline was captured, so improvement was not measured.",
        VerificationOutcome.VerificationFailed => "Post-repair checks completed, but at least one repaired condition is still present.",
        VerificationOutcome.Cancelled => "Post-repair verification was cancelled before a conclusion was reached.",
        VerificationOutcome.Deferred => "Post-repair verification was deferred because a restart is required before results can be interpreted.",
        _ => "Post-repair verification could not establish a conclusion."
    };
}

public sealed class PreRepairBaselineCapture
{
    private readonly IProcessRunner _runner;
    private readonly SessionStore _store;

    public PreRepairBaselineCapture(IProcessRunner runner, SessionStore store) { _runner = runner; _store = store; }

    public async Task<RepairBaselineResult> CaptureAsync(RepairOperation repair, int operationNumber, Action<string> writeLog, CancellationToken token)
    {
        var started = DateTimeOffset.Now;
        var plan = RepairVerificationCatalog.For(repair.Id);
        if (plan.Count == 0) return new RepairBaselineResult(repair.Id, repair.Title, BaselineOutcome.NotApplicable, started, DateTimeOffset.Now, Array.Empty<OperationResult>(), Array.Empty<DiagnosticFinding>());

        var checks = new List<OperationResult>();
        foreach (var check in plan)
        {
            var checkStarted = DateTimeOffset.Now;
            var steps = new List<StepResult>();
            for (var index = 0; index < check.Steps.Count; index++)
            {
                var step = check.Steps[index];
                writeLog($"> {step.Preview}");
                var output = _store.StepOutputPath(operationNumber, $"{repair.Id}_baseline_{check.Id}_{index + 1}");
                var result = await _runner.RunAsync(step, output, writeLog, token);
                steps.Add(result);
                if (!result.Succeeded) break;
            }
            var checkOutcome = steps.Count == 0 ? OperationOutcome.Skipped : steps.FirstOrDefault(x => !x.Succeeded)?.Outcome ?? OperationOutcome.Succeeded;
            checks.Add(new OperationResult(check.Id, $"Pre-repair {check.Title}", checkStarted, DateTimeOffset.Now, checkOutcome, steps));
            if (checkOutcome == OperationOutcome.Cancelled) break;
        }

        var findings = DiagnosticAnalyzer.Analyze(checks);
        var outcome = checks.Any(x => x.Cancelled) ? BaselineOutcome.Cancelled : checks.Count == 0 || checks.Any(x => !x.Succeeded) || findings.Any(x => x.Confidence != FindingConfidence.Known) ? BaselineOutcome.Unknown : BaselineOutcome.Captured;
        return new RepairBaselineResult(repair.Id, repair.Title, outcome, started, DateTimeOffset.Now, checks, findings);
    }
}

public static class RepairChangeAnalyzer
{
    public static RepairChangeOutcome Compare(RepairBaselineResult? baseline, RepairVerificationResult verification)
    {
        return Assess(baseline, verification).Overall;
    }

    public static RepairVerificationResult Apply(RepairBaselineResult? baseline, RepairVerificationResult verification)
    {
        var assessment = Assess(baseline, verification);
        var baselineCaptured = baseline?.Outcome == BaselineOutcome.Captured;
        return verification with
        {
            BaselineCaptured = baselineCaptured,
            ChangeOutcome = assessment.Overall,
            DismChangeOutcome = assessment.Dism,
            SfcChangeOutcome = assessment.Sfc,
            ChangeEvidence = assessment.Evidence,
            Summary = PostRepairVerifier.SummaryFor(verification.Outcome, baselineCaptured)
        };
    }

    private static ChangeAssessment Assess(RepairBaselineResult? baseline, RepairVerificationResult verification)
    {
        if (baseline?.Outcome != BaselineOutcome.Captured)
            return new(RepairChangeOutcome.NotMeasured, RepairChangeOutcome.NotMeasured, RepairChangeOutcome.NotMeasured, "No complete, recognized pre-repair baseline was captured; change was not measured.");

        var dism = CompareCheck(baseline.Findings, verification.Findings, "component-store-warning", "component-store-healthy");
        var sfc = CompareCheck(baseline.Findings, verification.Findings, "system-files-warning", "system-files-healthy");
        var dimensions = new[] { dism.Outcome, sfc.Outcome };
        var baselineProblems = new[] { dism.BaselineProblem, sfc.BaselineProblem };
        RepairChangeOutcome overall;
        if (!baselineProblems.Any(x => x)) overall = RepairChangeOutcome.Indeterminate;
        else if (dism.NewProblem || sfc.NewProblem) overall = RepairChangeOutcome.Indeterminate;
        else if (dimensions.Any(x => x == RepairChangeOutcome.Indeterminate)) overall = RepairChangeOutcome.Indeterminate;
        else if (dimensions.Any(x => x == RepairChangeOutcome.StillPresent)) overall = RepairChangeOutcome.StillPresent;
        else if (dimensions.Any(x => x == RepairChangeOutcome.Improved)) overall = RepairChangeOutcome.Improved;
        else overall = RepairChangeOutcome.Indeterminate;

        return new(overall, dism.Outcome, sfc.Outcome,
            $"DISM: {dism.Evidence} SFC: {sfc.Evidence} Overall: {overall}.");
    }

    private static CheckChange CompareCheck(IReadOnlyList<DiagnosticFinding> baseline, IReadOnlyList<DiagnosticFinding> verification, string warningCode, string healthyCode)
    {
        var beforeWarning = baseline.Any(x => x.Code == warningCode && x.Confidence == FindingConfidence.Known);
        var beforeHealthy = baseline.Any(x => x.Code == healthyCode && x.Confidence == FindingConfidence.Known);
        var afterWarning = verification.Any(x => x.Code == warningCode && x.Confidence == FindingConfidence.Known);
        var afterHealthy = verification.Any(x => x.Code == healthyCode && x.Confidence == FindingConfidence.Known);
        var afterKnown = afterWarning || afterHealthy;

        if (!afterKnown) return new(RepairChangeOutcome.Indeterminate, beforeWarning, false, "post-check was missing, failed, cancelled, or unrecognized.");
        if (beforeWarning && afterHealthy) return new(RepairChangeOutcome.Improved, true, false, "recognized problem changed to recognized healthy.");
        if (beforeWarning && afterWarning) return new(RepairChangeOutcome.StillPresent, true, false, "recognized problem is still present.");
        if (beforeHealthy && afterWarning) return new(RepairChangeOutcome.Indeterminate, false, true, "a new recognized problem appeared after a healthy baseline.");
        if (beforeHealthy && afterHealthy) return new(RepairChangeOutcome.NotMeasured, false, false, "healthy before and after; this is verification, not improvement.");
        return new(RepairChangeOutcome.Indeterminate, beforeWarning, false, "baseline state was not comparable.");
    }

    private sealed record ChangeAssessment(RepairChangeOutcome Overall, RepairChangeOutcome Dism, RepairChangeOutcome Sfc, string Evidence);
    private sealed record CheckChange(RepairChangeOutcome Outcome, bool BaselineProblem, bool NewProblem, string Evidence);
}
