namespace ISOWRepairCenter.Core;

public static class OperationCatalog
{
    public static IReadOnlyList<RepairOperation> Create() => new List<RepairOperation>
    {
        Diagnostic("system-overview", "System overview", "Windows version, installed updates, and logical volumes.",
            Ps("Get-ComputerInfo | Select-Object WindowsProductName,WindowsVersion,OsBuildNumber,OsArchitecture | Format-List; Get-Volume | Format-Table DriveLetter,FileSystem,HealthStatus,SizeRemaining,Size -AutoSize")),
        Diagnostic("physical-disks", "Physical disk health", "Physical disk media type, operational state, and storage health.",
            Ps("Get-PhysicalDisk | Select-Object FriendlyName,MediaType,HealthStatus,OperationalStatus,Size | ConvertTo-Json -Compress")),
        Diagnostic("dirty-bit", "File system dirty bit", "Checks whether the system volume is marked for file system repair.",
            Cmd("fsutil.exe", "dirty", "query", "%SystemDrive%")),
        Diagnostic("chkdsk-scan", "Online NTFS scan", "Runs the non-destructive CHKDSK online scan on the system volume.",
            Ps("chkdsk.exe $env:SystemDrive /scan")),
        Diagnostic("dism-health", "Component store health", "Runs DISM CheckHealth and ScanHealth without changing files.",
            Cmd("DISM.exe", "/Online", "/Cleanup-Image", "/CheckHealth", "/English"), Cmd("DISM.exe", "/Online", "/Cleanup-Image", "/ScanHealth", "/English")),
        Diagnostic("sfc-verify", "System file verification", "Runs SFC verification without repairing system files.", new CommandStep("sfc.exe", new[] { "/verifyonly" }, OutputEncoding: CommandOutputEncoding.Utf16Le)),
        Diagnostic("storage-events", "Storage and NTFS events", "Collects recent disk, NTFS, storage, and volume-manager System events.",
            Ps("Get-WinEvent -FilterHashtable @{LogName='System';StartTime=(Get-Date).AddDays(-14)} -ErrorAction SilentlyContinue | Where-Object {$_.ProviderName -match 'disk|ntfs|stor|volmgr|stornvme|iastor'} | Select-Object -First 100 TimeCreated,Id,ProviderName,LevelDisplayName,Message | Format-List")),
        Diagnostic("critical-events", "Critical system events", "Collects recent critical and error events from the System log.",
            Ps("Get-WinEvent -FilterHashtable @{LogName='System';StartTime=(Get-Date).AddDays(-14)} -ErrorAction SilentlyContinue | Where-Object {$_.Level -in 1,2} | Select-Object -First 100 TimeCreated,Id,ProviderName,LevelDisplayName,Message | Format-List")),
        Diagnostic("update-status", "Windows Update status", "Checks Windows Update service state and recent client events.",
            Ps("Get-Service wuauserv,bits,cryptsvc | Format-Table Name,Status,StartType -AutoSize; Get-WinEvent -LogName 'Microsoft-Windows-WindowsUpdateClient/Operational' -MaxEvents 50 -ErrorAction SilentlyContinue | Select-Object TimeCreated,Id,LevelDisplayName,Message | Format-List")),
        Diagnostic("network-status", "Network status", "Captures adapters, IP configuration, DNS configuration, and proxy state.",
            Cmd("ipconfig.exe", "/all"), Cmd("netsh.exe", "winhttp", "show", "proxy")),
        Diagnostic("boot-diagnostics", "Boot diagnostics", "Reads BCD configuration and SrtTrail log without changing boot files.",
            Cmd("bcdedit.exe", "/enum", "all"), Ps("$p=Join-Path $env:windir 'System32\\LogFiles\\Srt\\SrtTrail.txt'; if(Test-Path $p){Get-Content -LiteralPath $p -Tail 200}else{'SrtTrail.txt was not found.'}")),
        new RepairOperation { Id="system-file-repair", Title="Repair system image and files", Category=OperationCategory.SafeRepair, Description="Uses Windows Update as the default DISM source, then runs SFC /scannow.", Risk="Changes Windows component-store and protected system files. A restart may be required.", BuildSteps=i => string.IsNullOrWhiteSpace(i.DismSource) ? new[] { Cmd("DISM.exe", "/Online", "/Cleanup-Image", "/RestoreHealth"), Cmd("sfc.exe", "/scannow") } : new[] { Cmd("DISM.exe", "/Online", "/Cleanup-Image", "/RestoreHealth", $"/Source:{i.DismSource}", "/LimitAccess"), Cmd("sfc.exe", "/scannow") } },
        new RepairOperation { Id="network-reset", Title="Reset network stack", Category=OperationCategory.SafeRepair, Description="Resets Winsock, TCP/IP, DNS cache, and WinHTTP proxy settings.", RequiresRestart=true, Risk="Removes custom proxy and network-stack settings; restart is required.", BuildSteps=_ => new[] { Cmd("netsh.exe", "winsock", "reset"), Cmd("netsh.exe", "int", "ip", "reset"), Cmd("ipconfig.exe", "/flushdns"), Cmd("netsh.exe", "winhttp", "reset", "proxy") } },
        new RepairOperation { Id="component-cleanup", Title="Component store cleanup", Category=OperationCategory.SafeRepair, Description="Cleans superseded component-store files.", Risk="Removes superseded update components; do not interrupt the operation.", BuildSteps=_ => new[] { Cmd("DISM.exe", "/Online", "/Cleanup-Image", "/StartComponentCleanup") } },
        new RepairOperation { Id="update-reset", Title="Reset Windows Update components", Category=OperationCategory.ExpertRepair, RequiresRestorePoint=true, RequiresRestart=true, Description="Safely stops update services, preserves old caches, and restores services that were running before the operation.", Risk="Update downloads are cleared and a restart is required. Cancellation waits for service cleanup to finish.", BuildSteps=_ => new[] { Ps(UpdateResetScript, CancellationStrategy.CompleteBeforeCancellation) } },
        new RepairOperation { Id="bcd-backup", Title="Back up and validate BCD", Category=OperationCategory.ExpertRepair, RequiresRestorePoint=true, CreatesBcdBackup=true, Description="Exports the current BCD store before validating its entries.", Risk="Creates a rollback artifact but does not alter the active boot configuration.", BuildSteps=_ => new[] { Cmd("bcdedit.exe", "/enum", "all") } },
        // BCDBoot is deliberately unavailable until the guarded EFI wizard is implemented.
    };

    private static RepairOperation Diagnostic(string id, string title, string description, params CommandStep[] steps) => new() { Id=id, Title=title, Description=description, Category=OperationCategory.Diagnostic, BuildSteps=_ => steps };
    private static CommandStep Cmd(string file, params string[] args) => new(file, args);
    private static CommandStep Ps(string script, CancellationStrategy cancellationStrategy = CancellationStrategy.Immediate) => new("powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script }, CancellationStrategy: cancellationStrategy);
    private const string UpdateResetScript = """
        $ErrorActionPreference='Stop'
        $names='wuauserv','bits','cryptsvc'
        $wasRunning=@{}
        foreach($name in $names){$wasRunning[$name]=(Get-Service -Name $name).Status -eq 'Running'}
        function Rename-Cache([string]$path,[string]$name){
          if(!(Test-Path -LiteralPath $path)){return}
          for($attempt=1;$attempt -le 3;$attempt++){
            try { Rename-Item -LiteralPath $path -NewName ($name+'.old.'+(Get-Date -Format yyyyMMddHHmmss)) -ErrorAction Stop; return }
            catch { if($attempt -eq 3){throw}; Start-Sleep -Seconds 2 }
          }
        }
        try {
          foreach($name in $names){if($wasRunning[$name]){Stop-Service -Name $name -Force -ErrorAction Stop}}
          Rename-Cache (Join-Path $env:windir 'SoftwareDistribution') 'SoftwareDistribution'
          Rename-Cache (Join-Path $env:windir 'System32\\catroot2') 'catroot2'
        }
        finally {
          $restartErrors=@()
          foreach($name in $names){
            if(!$wasRunning[$name]){continue}
            try {
              Start-Service -Name $name -ErrorAction Stop
              if((Get-Service -Name $name -ErrorAction Stop).Status -ne 'Running'){throw "Service $name did not reach the Running state."}
            }
            catch {$restartErrors += $_.Exception.Message}
          }
          if($restartErrors.Count -gt 0){throw "Windows Update service recovery failed: $($restartErrors -join '; ')"}
        }
        """;
}
