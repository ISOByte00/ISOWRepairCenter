namespace WindowsRepairCenter.Core;

public static class OperationCatalog
{
    public static IReadOnlyList<RepairOperation> Create() => new List<RepairOperation>
    {
        Diagnostic("system-overview", "System overview", "Windows version, installed updates, and logical volumes.",
            Ps("Get-ComputerInfo | Select-Object WindowsProductName,WindowsVersion,OsBuildNumber,OsArchitecture | Format-List; Get-Volume | Format-Table DriveLetter,FileSystem,HealthStatus,SizeRemaining,Size -AutoSize")),
        Diagnostic("physical-disks", "Physical disk health", "Physical disk media type, operational state, and storage health.",
            Ps("Get-PhysicalDisk | Format-Table FriendlyName,MediaType,HealthStatus,OperationalStatus,Size -AutoSize")),
        Diagnostic("dirty-bit", "File system dirty bit", "Checks whether the system volume is marked for file system repair.",
            Cmd("fsutil.exe", "dirty", "query", "%SystemDrive%")),
        Diagnostic("chkdsk-scan", "Online NTFS scan", "Runs the non-destructive CHKDSK online scan on the system volume.",
            Ps("chkdsk.exe $env:SystemDrive /scan")),
        Diagnostic("dism-health", "Component store health", "Runs DISM CheckHealth and ScanHealth without changing files.",
            Cmd("DISM.exe", "/Online", "/Cleanup-Image", "/CheckHealth"), Cmd("DISM.exe", "/Online", "/Cleanup-Image", "/ScanHealth")),
        Diagnostic("sfc-verify", "System file verification", "Runs SFC verification without repairing system files.", Cmd("sfc.exe", "/verifyonly")),
        Diagnostic("storage-events", "Storage and NTFS events", "Collects recent disk, NTFS, storage, and volume-manager System events.",
            Ps("Get-WinEvent -FilterHashtable @{LogName='System';StartTime=(Get-Date).AddDays(-14)} -ErrorAction SilentlyContinue | Where-Object {$_.ProviderName -match 'disk|ntfs|stor|volmgr|stornvme|iastor'} | Select-Object -First 100 TimeCreated,Id,ProviderName,LevelDisplayName,Message | Format-List")),
        Diagnostic("critical-events", "Critical system events", "Collects recent critical and error events from the System log.",
            Ps("Get-WinEvent -FilterHashtable @{LogName='System';StartTime=(Get-Date).AddDays(-14)} -ErrorAction SilentlyContinue | Where-Object {$_.LevelDisplayName -in 'Critical','Error'} | Select-Object -First 100 TimeCreated,Id,ProviderName,Message | Format-List")),
        Diagnostic("update-status", "Windows Update status", "Checks Windows Update service state and recent client events.",
            Ps("Get-Service wuauserv,bits,cryptsvc | Format-Table Name,Status,StartType -AutoSize; Get-WinEvent -LogName 'Microsoft-Windows-WindowsUpdateClient/Operational' -MaxEvents 50 -ErrorAction SilentlyContinue | Select-Object TimeCreated,Id,LevelDisplayName,Message | Format-List")),
        Diagnostic("network-status", "Network status", "Captures adapters, IP configuration, DNS configuration, and proxy state.",
            Cmd("ipconfig.exe", "/all"), Cmd("netsh.exe", "winhttp", "show", "proxy")),
        Diagnostic("boot-diagnostics", "Boot diagnostics", "Reads BCD configuration and SrtTrail log without changing boot files.",
            Cmd("bcdedit.exe", "/enum", "all"), Ps("$p=Join-Path $env:windir 'System32\\LogFiles\\Srt\\SrtTrail.txt'; if(Test-Path $p){Get-Content -LiteralPath $p -Tail 200}else{'SrtTrail.txt was not found.'}")),
        new RepairOperation { Id="system-file-repair", Title="Repair system image and files", Category=OperationCategory.SafeRepair, Description="Uses Windows Update as the default DISM source, then runs SFC /scannow.", Risk="Changes Windows component-store and protected system files. A restart may be required.", BuildSteps=i => string.IsNullOrWhiteSpace(i.DismSource) ? new[] { Cmd("DISM.exe", "/Online", "/Cleanup-Image", "/RestoreHealth"), Cmd("sfc.exe", "/scannow") } : new[] { Cmd("DISM.exe", "/Online", "/Cleanup-Image", "/RestoreHealth", $"/Source:{i.DismSource}", "/LimitAccess"), Cmd("sfc.exe", "/scannow") } },
        new RepairOperation { Id="network-reset", Title="Reset network stack", Category=OperationCategory.SafeRepair, Description="Resets Winsock, TCP/IP, DNS cache, and WinHTTP proxy settings.", RequiresRestart=true, Risk="Removes custom proxy and network-stack settings; restart is required.", BuildSteps=_ => new[] { Cmd("netsh.exe", "winsock", "reset"), Cmd("netsh.exe", "int", "ip", "reset"), Cmd("ipconfig.exe", "/flushdns"), Cmd("netsh.exe", "winhttp", "reset", "proxy") } },
        new RepairOperation { Id="component-cleanup", Title="Component store cleanup", Category=OperationCategory.SafeRepair, Description="Cleans superseded component-store files.", Risk="Removes superseded update components; do not interrupt the operation.", BuildSteps=_ => new[] { Cmd("DISM.exe", "/Online", "/Cleanup-Image", "/StartComponentCleanup") } },
        new RepairOperation { Id="update-reset", Title="Reset Windows Update components", Category=OperationCategory.ExpertRepair, RequiresRestorePoint=true, RequiresRestart=true, Description="Stops update services, preserves old caches by renaming them, then starts services again.", Risk="Update downloads are cleared and a restart is required.", BuildSteps=_ => new[] { Cmd("net.exe", "stop", "wuauserv"), Cmd("net.exe", "stop", "bits"), Cmd("net.exe", "stop", "cryptsvc"), Ps("$d=Join-Path $env:windir 'SoftwareDistribution'; if(Test-Path $d){Rename-Item $d ('SoftwareDistribution.old.'+(Get-Date -Format yyyyMMddHHmmss))}; $c=Join-Path $env:windir 'System32\\catroot2'; if(Test-Path $c){Rename-Item $c ('catroot2.old.'+(Get-Date -Format yyyyMMddHHmmss))}"), Cmd("net.exe", "start", "cryptsvc"), Cmd("net.exe", "start", "bits"), Cmd("net.exe", "start", "wuauserv") } },
        new RepairOperation { Id="chkdsk-fix", Title="Schedule CHKDSK repair", Category=OperationCategory.ExpertRepair, RequiresRestorePoint=true, RequiresRestart=true, Description="Schedules CHKDSK /f on the system volume for the next restart.", Risk="Repairs file-system metadata during restart. Save work and allow Windows to finish the check.", BuildSteps=_ => new[] { new CommandStep("chkdsk.exe", new[] { "%SystemDrive%", "/f" }, "Schedule CHKDSK repair", "Y") } },
        new RepairOperation { Id="bcd-backup", Title="Back up and validate BCD", Category=OperationCategory.ExpertRepair, RequiresRestorePoint=true, CreatesBcdBackup=true, Description="Exports the current BCD store before validating its entries.", Risk="Creates a rollback artifact but does not alter the active boot configuration.", BuildSteps=_ => new[] { Cmd("bcdedit.exe", "/enum", "all") } },
        new RepairOperation { Id="bcdboot", Title="Rebuild boot files with BCDBoot", Category=OperationCategory.ExpertRepair, RequiresRestorePoint=true, CreatesBcdBackup=true, RequiresRestart=true, Description="Rebuilds boot files from a selected Windows directory to a selected EFI target.", Risk="Incorrect source or EFI target can make Windows unbootable. Verify both values before continuing.", Validate=i => ValidateBcdBoot(i), BuildSteps=i => new[] { Cmd("bcdboot.exe", i.WindowsSource, "/s", i.EfiTarget, "/f", "UEFI") } }
    };

    private static RepairOperation Diagnostic(string id, string title, string description, params CommandStep[] steps) => new() { Id=id, Title=title, Description=description, Category=OperationCategory.Diagnostic, BuildSteps=_ => steps };
    private static CommandStep Cmd(string file, params string[] args) => new(file, args);
    private static CommandStep Ps(string script) => Cmd("powershell.exe", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script);
    private static string? ValidateBcdBoot(OperationInputs inputs)
    {
        if (string.IsNullOrWhiteSpace(inputs.WindowsSource) || string.IsNullOrWhiteSpace(inputs.EfiTarget)) return "Windows source and EFI target are required for BCDBoot.";
        if (!Path.GetFileName(inputs.WindowsSource.TrimEnd('\\', '/')).Equals("Windows", StringComparison.OrdinalIgnoreCase) || !Directory.Exists(inputs.WindowsSource)) return "Windows source must be an existing Windows directory, for example C:\\Windows.";
        if (!Directory.Exists(inputs.EfiTarget)) return "EFI target must be an existing drive letter mounted by the user, for example S:.";
        return null;
    }
}
