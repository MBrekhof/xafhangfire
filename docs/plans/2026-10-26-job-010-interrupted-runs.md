# JOB-010: mark runs interrupted by a crash or restart

Card: JOB-010 (CARD-2583).

## Problem (reproduced 2026-10-26)

`JobExecutor.RunAsync` (Hangfire) and `DirectJobDispatcher` write a `JobExecutionRecord` with Status Running at
the start of a run, and complete it on success or failure. When the process dies mid-run, nothing completes it.
Hangfire re-runs the job later under a new record, so the old one stays Running forever. After JOB-005's
kill-and-restart test, 20 JOB005 records still have Status = 1; the DB currently holds 21 Running records.
The JobDefinition can be left with LastRunStatus Running as well: in Direct mode nothing re-runs it.

## Change

New static class `xafhangfire.Blazor.Server/Services/InterruptedRuns.cs`; `JobSyncService` calls `Clean` once at
startup, before the schedule sync (as built after the plan review below):

- `cutoffUtc` = this process's start time (`Process.GetCurrentProcess().StartTime.ToUniversalTime()`). A run
  started by this process always has `StartedUtc` ≥ cutoff, so runs Hangfire starts right away at startup
  (queued jobs) are never touched.
- Records: load the records with `Status == Running`; in memory, those with `StartedUtc < cutoffUtc` get
  Status Failed and ErrorMessage "Interrupted: the app stopped while this run was in progress." CompletedUtc and
  DurationMs stay empty: the moment the run died is unknown.
- Definitions: those with `LastRunStatus == Running` and `LastRunUtc < cutoffUtc` get LastRunStatus Failed and the
  same LastRunMessage. LastRunUtc is unchanged.
- `ConsecutiveFailures` is not incremented and no alert is sent: an interruption says nothing about the job.
- The date comparison runs in memory, not in SQL. The columns are `timestamp without time zone` and the app runs
  with legacy timestamps (see JOB-013 for how a DB-side comparison can go wrong); the set of Running rows is small.
- The marking is a pure `internal static (int Records, int Definitions) Mark(IEnumerable<JobExecutionRecord>,
  IEnumerable<JobDefinition>, DateTime cutoffUtc)` so it is unit-testable without a database; `Clean` only loads,
  calls `Mark` and commits.
- Errors are logged and swallowed: a failed cleanup must not stop the schedule sync.

ponytail: assumes one app instance runs jobs (as the recorder's lock does). A second instance started later would
mark the first one's live runs. Upgrade path: store a server id on the record and check Hangfire's server list.

## Cases and how each is verified

| Case | Expected | Verified by |
|------|----------|-------------|
| Running record started before the cutoff | Failed, interrupted message, CompletedUtc/DurationMs empty | unit test |
| Running record started at or after the cutoff | untouched | unit test |
| Success/Failed records | never loaded (status filter) and untouched by `Mark` | unit test |
| Definition Running, LastRunUtc before cutoff | Failed + message, ConsecutiveFailures unchanged | unit test |
| Definition Running, LastRunUtc at or after cutoff | untouched | unit test |
| Definition Running with LastRunUtc null | untouched (no evidence it is stale) | unit test |
| Live: restart the app with the 21 Running rows | 0 Running rows afterwards; the log reports the count | psql + app log |
| Live: a job queued before the restart and run right after it | its new record completes normally | psql |

## Gemini plan review (2026-10-26): nothing blocking, four should-fix points adopted

1. `JobSyncService` is a singleton and `INonSecuredObjectSpaceFactory` is scoped → resolved from a new scope,
   after the existing 5 s delay. `InterruptedRuns` is a static class with no DI registration.
2. A run of the same definition could start between the cleanup's query and its commit and get its LastRun*
   overwritten → the cleanup takes `XafJobExecutionRecorder.DefinitionStatusLock`, and `RecordStartAsync` now takes
   it too (before, only completion and failure did). The `Process` is disposed.
3. `GetObjects<T>()` returns a list, so a `.Where` after it loads the whole history → `GetObjectsQuery<T>()`, status
   filter in SQL.
4. `Mark` returns `(Records, Definitions)` and the cleanup commits when either is non-zero. Added tests: boundary
   (`==` cutoff), Unspecified kind (as Npgsql reads it), a non-zero ConsecutiveFailures kept, finished records keep
   their fields, non-Running definitions untouched, a definition alone counts.

Notes agreed: not counting the interruption is required for JOB-004 (counting it would skip the "exactly at the
threshold" alert); the Win app must not run this (it doesn't, `JobSyncService` is Blazor-only).

## Gemini diff review (2026-10-26): nothing blocking, all adopted

1. Single-item lists didn't catch `break` for `continue` or `= 1` for `++` → `MixedLists_EveryStaleItemIsMarked`.
2. The finished-record test only covered Failed → a theory over Success, Failed and NeverRun that also asserts Status.
3. The Change section used pre-review names → updated.

## Live results (2026-10-26)

1. Restart with the 21 Running records and one definition set Running an hour earlier: the log said "Marked 21 job
   run(s) and 1 job definition(s) interrupted"; afterwards 0 Running records, the definition Failed with the message
   and ConsecutiveFailures 0; CompletedUtc empty, DurationMs 0.
2. 22 DemoLog jobs of 60 s (20 workers busy, 2 queued), app hard-killed 10 s later, restarted: the 2 queued jobs
   started at 21:34:12 UTC, the cleanup ran at 21:34:14 and marked only the 20 killed runs; the 2 completed with
   Success at 21:35:13. The 20 killed Hangfire jobs were deleted afterwards so they don't re-run.
