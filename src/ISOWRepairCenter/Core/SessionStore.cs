using System.Text;
using System.Text.Json;

namespace ISOWRepairCenter.Core;

public sealed class SessionStore
{
    public string Root { get; }
    public string SessionDirectory { get; private set; } = string.Empty;

    public SessionStore(string? root = null) => Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ISOWRepairCenter", "Sessions");

    public void Start()
    {
        SessionDirectory = Path.Combine(Root, $"Session_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(SessionDirectory);
    }

    public string StepOutputPath(int number, string operationId) => Path.Combine(SessionDirectory, $"{number:00}_{operationId}.log");

    public async Task SaveAsync(IReadOnlyList<OperationResult> results, IReadOnlyList<DiagnosticFinding>? findings = null)
    {
        if (string.IsNullOrWhiteSpace(SessionDirectory)) throw new InvalidOperationException("A session must be started before saving a report.");
        var json = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented=true });
        await File.WriteAllTextAsync(Path.Combine(SessionDirectory, "results.json"), json, Encoding.UTF8);
        var resolvedFindings = findings ?? DiagnosticAnalyzer.Analyze(results);
        await File.WriteAllTextAsync(Path.Combine(SessionDirectory, "findings.json"), JsonSerializer.Serialize(resolvedFindings, new JsonSerializerOptions { WriteIndented=true }), Encoding.UTF8);
        var text = new StringBuilder("ISOByte ISOWRepairCenter session\n\n");
        if (resolvedFindings.Count > 0)
        {
            text.AppendLine("Diagnostic findings:");
            foreach (var finding in resolvedFindings) text.AppendLine($"  [{finding.Severity}] {finding.Summary}\n    Evidence: {finding.Evidence}\n    Recommendation: {finding.Recommendation}");
            text.AppendLine();
        }
        foreach (var result in results)
        {
            text.AppendLine($"{result.Title}: {result.Outcome}");
            foreach (var step in result.Steps) text.AppendLine($"  [{step.ExitCode?.ToString() ?? "N/A"}] {step.Command} ({step.Duration.TotalSeconds:F1}s) {step.Error}\n    Evidence: {step.Evidence}\n    Output: {step.OutputFile}");
        }
        await File.WriteAllTextAsync(Path.Combine(SessionDirectory, "report.txt"), text.ToString(), Encoding.UTF8);
        var html = "<!doctype html><html><head><meta charset=\"utf-8\"><title>ISOWRepairCenter Report</title><style>body{font-family:Segoe UI;margin:32px;color:#111}pre{white-space:pre-wrap;background:#f3f4f6;padding:16px}</style></head><body><h1>ISOByte ISOWRepairCenter Report</h1><pre>" + System.Net.WebUtility.HtmlEncode(text.ToString()) + "</pre></body></html>";
        await File.WriteAllTextAsync(Path.Combine(SessionDirectory, "report.html"), html, Encoding.UTF8);
    }
}
