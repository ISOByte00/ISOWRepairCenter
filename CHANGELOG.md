# Changelog

## Unreleased

- Began v0.6 structured Event Log analysis: storage and critical System events now use a locale-independent JSON schema, with conservative NVMe reset, NTFS integrity, disk retry, de-duplication, and temporal-overlap findings.
- Hardened Event Log collection failures, RecordId-based de-duplication, and NVMe burst detection.
- Added an instant Diagnostics search box without changing selections or run behavior.
- Added a localized Dashboard finding summary and severity-specific visual labels.
- Added evidence-linked guided repair suggestions for DISM and SFC integrity findings; suggestions never select or start a repair.
- Added a separate, testable post-repair verification model for the DISM/SFC repair workflow; it is not yet wired to execute from the UI.
- Wired the DISM/SFC post-repair verification into the user-approved Safe Repairs flow, command preview, session report, and separate verification JSON report.

## 0.5.0

- Added structured diagnostic findings with severity, evidence, and recommended next actions.
- Added a Quick Diagnostics profile; longer checks remain available through the custom checklist.
- Removed the false-positive Windows Update "stopped service" finding and forced English DISM diagnostic output.
- Added finding confidence states (Known, Unknown, ManualReviewRequired), conservative disk JSON analysis, multi-step DISM validation, and dashboard status labels.
- Fixed SFC output decoding for UTF-16LE console output.
- Added an anonymized diagnostic fixture corpus and a manual Windows test matrix.
- Detect Windows 11 from its build number instead of the legacy Windows 10 product name.

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
