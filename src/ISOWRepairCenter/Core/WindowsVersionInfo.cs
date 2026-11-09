using Microsoft.Win32;

namespace ISOWRepairCenter.Core;

public sealed record WindowsVersionInfo(string DisplayName, int BuildNumber, int Revision)
{
    public static WindowsVersionInfo Current()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var productName = key?.GetValue("ProductName")?.ToString() ?? "Windows";
        var displayVersion = key?.GetValue("DisplayVersion")?.ToString();
        var build = key?.GetValue("CurrentBuildNumber")?.ToString();
        var revision = key?.GetValue("UBR")?.ToString();
        return FromRegistryValues(productName, build, displayVersion, revision);
    }

    public static WindowsVersionInfo FromRegistryValues(string productName, string? build, string? displayVersion, string? revision)
    {
        _ = int.TryParse(build, out var buildNumber);
        _ = int.TryParse(revision, out var revisionNumber);
        var family = buildNumber >= 22000 ? "Windows 11" : "Windows 10";
        var detectedName = productName.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase)
            ? family + productName["Windows 10".Length..]
            : productName;
        var versionSuffix = string.IsNullOrWhiteSpace(displayVersion) ? string.Empty : $" {displayVersion}";
        return new WindowsVersionInfo($"{detectedName}{versionSuffix} (build {buildNumber}.{revisionNumber})", buildNumber, revisionNumber);
    }
}
