# ISOByte ISOWRepairCenter

ISOWRepairCenter is an ISOByte Windows 10/11 x64 diagnostic and controlled-repair desktop application. It uses only built-in Windows tools and never starts an operation unless the user selects it and approves the command preview.

## Build

Install the .NET 10 SDK, then run:

```powershell
dotnet restore ISOWRepairCenter.sln
dotnet build ISOWRepairCenter.sln
dotnet run --project src/ISOWRepairCenter
```

The application requests administrator permission at startup.

## Portable executable

Run `Publish-Portable.ps1` from PowerShell to produce a self-contained, single-file x64 executable at `dist\\ISOWRepairCenter\\ISOWRepairCenter.exe`. The generated executable does not require a separately installed .NET runtime.

## What it does

- Diagnostics: disk and NTFS status, event logs, DISM/SFC checks, Windows Update state, network state, BCD inspection, and SrtTrail collection.
- Safe repairs: DISM plus SFC, network-stack reset, and component-store cleanup.
- Expert repairs: guarded Windows Update reset and BCD backup/validation. CHKDSK and BCDBoot are unavailable until their separate validated wizards are implemented.

Expert repairs require a second acknowledgement. The tool tries to create a restore point first, and it creates a BCD export before BCD-related work. If a restore point is unavailable, the user must explicitly decide whether to continue.

## DISM sources

DISM uses Windows Update by default. If Windows Update cannot provide repair files, mount a compatible Windows ISO and enter a matching WIM/ESD source path in the Safe Repairs tab. The application does not download or mount Windows images automatically.

## Reports

Each session is stored in `%ProgramData%\ISOWRepairCenter\Sessions`. It contains live raw command output, `results.json`, `findings.json`, `report.txt`, and `report.html`.

## Safety limits

This application cannot repair failed SSD/HDD hardware, RAM faults, firmware defects, or incorrect manual EFI partition selection. Verify backups before expert repairs and do not interrupt disk or boot work.

## Manual verification matrix

Verify the UI and command preview on a Windows 10 x64 and Windows 11 x64 test machine. Exercise a read-only diagnostic, a deliberately failing command, cancellation of a long-running diagnostic, restore-point failure handling, DISM source validation, BCD backup failure handling, and the restart-required expert-operation prompts. Do not automate real disk or boot repairs in tests.
