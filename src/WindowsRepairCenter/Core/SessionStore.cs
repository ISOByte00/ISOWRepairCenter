using System.Text;
using System.Text.Json;

namespace WindowsRepairCenter.Core;

public sealed class SessionStore
{
    public string Root { get; }
    public string SessionDirectory { get; private set; } = string.Empty;

    public SessionStore(string? root = null) => Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WindowsRepairCenter", "Sessions");

    public void Start()
    {
        SessionDirectory = Path.Combine(Root, $"Session_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(SessionDirectory);
    }

    public string StepOutputPath(int number, string operationId) => Path.Combine(SessionDirectory, $"{number:00}_{operationId}.log");

    public async Task SaveAsync(IReadOnlyList<OperationResult> results)
    {
        var json = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented=true });
        await File.WriteAllTextAsync(Path.Combine(SessionDirectory, "results.json"), json, Encoding.UTF8);
        var text = new StringBuilder("Windows Repair Center session\n\n");
        foreach (var result in results)
        {
            text.AppendLine($"{result.Title}: {(result.Succeeded ? "Succeeded" : result.Cancelled ? "Cancelled" : "Failed")}");
            foreach (var step in result.Steps) text.AppendLine($"  [{step.ExitCode?.ToString() ?? "N/A"}] {step.Command} ({step.Duration.TotalSeconds:F1}s) {step.Error}\n    Evidence: {step.Evidence}\n    Output: {step.OutputFile}");
        }
        await File.WriteAllTextAsync(Path.Combine(SessionDirectory, "report.txt"), text.ToString(), Encoding.UTF8);
        var html = "<!doctype html><html><head><meta charset=\"utf-8\"><title>Windows Repair Center Report</title><style>body{font-family:Segoe UI;margin:32px;color:#111}pre{white-space:pre-wrap;background:#f3f4f6;padding:16px}</style></head><body><h1>Windows Repair Center Report</h1><pre>" + System.Net.WebUtility.HtmlEncode(text.ToString()) + "</pre></body></html>";
        await File.WriteAllTextAsync(Path.Combine(SessionDirectory, "report.html"), html, Encoding.UTF8);
    }
}
