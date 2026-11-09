namespace ISOWRepairCenter.Core;

public enum VerificationOutcome { NotApplicable, Verified, VerificationFailed, Unknown, Cancelled }

public sealed record RepairVerificationResult(
    string RepairOperationId,
    string RepairOperationTitle,
    VerificationOutcome Outcome,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    IReadOnlyList<OperationResult> Checks,
    IReadOnlyList<DiagnosticFinding> Findings,
    string Summary,
    bool BaselineCaptured = false);

public sealed record VerificationCheck(string Id, string Title, IReadOnlyList<CommandStep> Steps);

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

    private static string SummaryFor(VerificationOutcome outcome) => outcome switch
    {
        VerificationOutcome.Verified => "Post-repair checks completed and recognized healthy results. No pre-repair baseline was captured, so improvement was not measured.",
        VerificationOutcome.VerificationFailed => "Post-repair checks completed, but at least one repaired condition is still present.",
        VerificationOutcome.Cancelled => "Post-repair verification was cancelled before a conclusion was reached.",
        _ => "Post-repair verification could not establish a conclusion."
    };
}
