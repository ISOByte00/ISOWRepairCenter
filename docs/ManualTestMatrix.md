# Manual test matrix

This matrix records behavior that unit tests cannot prove. Do not include personal paths, serial numbers, IP addresses, or full event logs in this file.

| Scenario | Windows 10 x64 | Windows 11 x64 | Expected result |
| --- | --- | --- | --- |
| Quick Diagnostics | Pending | Verified | All five read-only checks run; no repair starts automatically. |
| DISM health checks | Pending | Verified | Both CheckHealth and ScanHealth are interpreted before a healthy finding is emitted. |
| SFC verification output | Pending | Verified | UTF-16LE output is decoded and a known finding is produced when the result is recognized. |
| Physical disk JSON health | Pending | Verified | Healthy/OK data is known; incomplete or malformed data is unknown. |
| Cancelled diagnostic | Pending | Verified | Classified as Unknown, not as a system fault. |
| Structured Event Log analysis | Pending | Verified | Collection failures are ManualReviewRequired; successful empty logs are distinct from unreadable output; recognized storage patterns are reported conservatively. |
| Post-repair command preview | Pending | Verified | The DISM/SFC repair preview includes its separate read-only post-repair verification commands; the user can decline before any command runs. |
| Failed diagnostic | Pending | Pending | Classified as Warning with ManualReviewRequired. |
| Update reset cancellation | Pending | Pending | Cleanup completes before the process exits and the window closes. |
| BCD backup failure | Pending | Pending | Operation fails safely and records the backup failure. |

## Test procedure

1. Use the portable EXE built from the current commit.
2. Run only the listed diagnostic or controlled test scenario.
3. Record the session folder, result state, and whether the report and `findings.json` match the visible result.
4. Never induce disk, boot, or filesystem corruption to create a test case.
