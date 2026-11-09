# Changelog

## Unreleased

- Added structured diagnostic findings with severity, evidence, and recommended next actions.
- Added a Quick Diagnostics profile; longer checks remain available through the custom checklist.
- Removed the false-positive Windows Update "stopped service" finding and forced English DISM diagnostic output.
- Added finding confidence states (Known, Unknown, ManualReviewRequired), conservative disk JSON analysis, multi-step DISM validation, and dashboard status labels.

## 0.4.0

- Renamed solution, projects, namespaces, session storage, and publish output to ISOWRepairCenter.
- Migrated the application to .NET 10 LTS.
- Added structured operation outcomes and live output-file streaming.
- Hardened Windows Update reset cleanup and made event filtering language-independent.
- Removed BCDBoot from the general expert-operation list pending a guarded EFI wizard.
- Removed the unverifiable CHKDSK scheduling card; cancellation now waits for Windows Update cleanup to finish safely.

## 0.3.0

- Renamed the application to ISOByte ISOWRepairCenter.
- Added Turkish and English UI selection.
- Added About and copyright information.
- Added session reports, controlled repair workflows, and NTFS scan execution fixes.
