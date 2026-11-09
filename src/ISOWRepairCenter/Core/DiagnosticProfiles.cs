namespace ISOWRepairCenter.Core;

public static class DiagnosticProfiles
{
    private static readonly HashSet<string> QuickOperationIds = new(StringComparer.Ordinal)
    {
        "system-overview", "physical-disks", "dirty-bit", "update-status", "network-status"
    };

    public static bool IsQuickDiagnostic(RepairOperation operation) => QuickOperationIds.Contains(operation.Id);
}
