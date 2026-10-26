# JOB-011: a cancelled run is recorded as interrupted, not as a job failure

Card: JOB-011 (CARD-2584).

## Problem (reproduced 2026-10-26)

`JobExecutor.RunAsync` (Hangfire) and `DirectJobDispatcher.DispatchAsync` catch every exception and call
`RecordFailureAsync`, which marks the record Failed, sets the definition's LastRun* to Failed and increments
`ConsecutiveFailures` (and can send the JOB-004 alert). A cancellation of the job's token is not a job failure:
Hangfire cancels it on a graceful server stop (and requeues the job) or when the job is deleted.

Repro: app started, one DemoLog job of 120 s enqueued (JOB011-1, Hangfire job 173), Ctrl+C sent to the app 13 s
later (scratchpad `ctrlc/ctrlc.cs`, AttachConsole + GenerateConsoleCtrlEvent). Hangfire logged "Worker stop
requested while processing background job '173'. It will be re-queued." The execution record ended with
Status = 3 (Failed) and ErrorMessage "System.Threading.Tasks.TaskCanceledException: A task was canceled.".
(The requeue itself failed silently: JOB-013, effect 3.)

## Change

- `IJobExecutionRecorder` gets `Task RecordInterruptedAsync(Guid recordId, CancellationToken cancellationToken = default)`.
- `XafJobExecutionRecorder.RecordInterruptedAsync`, under `DefinitionStatusLock`: record → Status Failed,
  CompletedUtc = now, DurationMs, ErrorMessage = `InterruptedRuns.Message`-style text "Cancelled: the app stopped or
  the job was cancelled while it was running."; definition → LastRunUtc = CompletedUtc, LastRunStatus Failed,
  LastRunMessage = the same text. `ConsecutiveFailures` unchanged, no alert (same reasoning as JOB-010).
  Swallows and logs its own errors, like the other Record* methods.
- `JobExecutor.RunAsync` and `DirectJobDispatcher.DispatchAsync`: a new catch before the general one,
  `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)` → `RecordInterruptedAsync`
  with `CancellationToken.None` (the job's token is already cancelled), then `throw;` so Hangfire still requeues.
- A `TaskCanceledException` whose token was *not* the job's (e.g. an HTTP client timeout inside a handler) still
  counts as a failure: the filter checks the job's token, not the exception type alone.

Out of scope, carded as JOB-014 (CARD-2591): GenerateReportHandler and SendReportEmailHandler call
`ExportReportAsync` without the token (they pass it to `CopyToAsync`/`SendAsync`), ListUsersHandler ignores it, and
SendMailMergeHandler's per-recipient catch swallows a cancellation (a cancel during the last send ends as Success).

## Cases and how each is verified

| Case | Expected | Verified by |
|------|----------|-------------|
| Handler throws OperationCanceledException, job token cancelled (JobExecutor) | RecordInterruptedAsync, not RecordFailureAsync; exception rethrown | unit test |
| Same through DirectJobDispatcher | same | unit test |
| Handler throws TaskCanceledException, job token NOT cancelled | RecordFailureAsync (a real failure) | unit test |
| Handler throws another exception while the token is cancelled | RecordFailureAsync | unit test |
| RecordInterruptedAsync gets the job's token? | No: CancellationToken.None | unit test (Arg check) |
| Recorder: record + definition fields, ConsecutiveFailures kept, no email | as above | covered by a pure helper test, like InterruptedRuns.Mark |
| Live: Ctrl+C during a 120 s DemoLog job | record Failed with the "Cancelled" message, not the exception; log shows the requeue line | psql + app log |

## Gemini plan review (2026-10-26): nothing blocking

It confirmed against Hangfire.Core 1.8: the job's CancellationToken is cancelled on server stop (the rethrown
OperationCanceledException leads to a requeue, no Failed state, no AutomaticRetry attempt) and on abort by
ServerJobCancellationWatcher, e.g. the job deleted (JobAbortedException, removed from the queue). An
OperationCanceledException with the token not cancelled becomes a normal failure with retries.

1. The recorder part was unspecified → `XafJobExecutionRecorder.CancelledMessage` and
   `internal static MarkCancelled(record, completedUtc)`; `RecordInterruptedAsync` returns at once for `Guid.Empty`
   (start not recorded) like the other Record* methods; a record without a definition is tested.
2. The caller cases are tested against both JobExecutor and DirectJobDispatcher, including "completion not
   recorded".
3. SendMailMergeHandler swallows a cancellation inside its per-recipient catch → JOB-014, not reproduced (needs an
   ObjectSpace with TypesInfo for a handler test).
4. The report handlers' token use was overstated → JOB-014 and the line above say which call ignores it.

## Gemini diff review (2026-10-26): nothing blocking, both notes adopted

1. The error-swallowing path of `RecordInterruptedAsync` was untested; it matters because an escaping error would
   replace the OperationCanceledException and stop the requeue → test with an ObjectSpace factory that throws.
2. `RecordFailureAsync` got the job's token, which may already be cancelled → `CancellationToken.None` in both
   callers (the recorder ignores it today), asserted in the "other exception while cancelled" test.

## Live result (2026-10-26)

After the change, the same Ctrl+C test (JOB011B, Hangfire job 175): "Worker stop requested … It will be re-queued.",
the record ended Failed with "Cancelled: the app stopped or the job was cancelled while it was running." and
DurationMs 12576. The leftover Hangfire jobs 173 and 175 were deleted afterwards.
