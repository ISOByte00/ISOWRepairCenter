# Session History Comparison

`SessionHistoryComparer` is a UI-independent, read-only Core API. `ReadAsync(sessionDirectory)` returns the parsed session, operation coverage, event-log scope, and explicit read issues. `CompareAsync(earlier, later)` returns a JSON-serializable `SessionHistoryComparison`.

Findings are matched only by the stable `OperationId + Code` key. Localized title, summary, evidence, and recommendation text are deliberately never matching keys. The comparison states are `NewlyObserved`, `Persisting`, `NotObserved`, `ChangedAssessment`, `NotComparable`, and `HistoricalEventRepeated`.

`NotObserved` is not `Resolved`: it means a later, successful relevant diagnostic did not emit the old finding. It is never a repair, health, or improvement conclusion. If the relevant later diagnostic was not successful, the result is `NotComparable`. Changes in severity or confidence are `ChangedAssessment`; `Unknown` and `ManualReviewRequired` do not support a positive health conclusion.

Event-log findings receive extra safeguards. Their scope is derived from successful `storage-events` and `critical-events` results plus structured event payloads. Different, failed, or unreadable collection scopes yield `NotComparable`. When the same provider/id/record-id/timestamp is recollected, it is `HistoricalEventRepeated`, not evidence of a new recurring fault. Event messages are never exported by this API.

The reader supports v0.5 session arrays and treats `baselines.json`, `verifications.json`, and `safety.json` as optional. Missing, malformed, unreadable, oversized, or incomplete files become `SessionHistoryIssue` records rather than exceptions. JSON files are capped at 4 MiB; raw event output is read only from a file name resolved inside its session directory. No session file is changed.

A future Dashboard can call `CompareAsync`, serialize the returned record graph, show `Issues` and operation coverage, and present each entry's `Explanation`. It must not relabel `NotObserved` as resolved or infer health from uncertain findings.
