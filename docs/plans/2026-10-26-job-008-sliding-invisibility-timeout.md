# JOB-008: resume interrupted jobs within ~5 minutes instead of 30

Card: JOB-008 (CARD-2579). Decision (Martin, 2026-10-26, option a): sliding invisibility timeout, 5 minutes.

## Problem (reproduced in JOB-005)

When the app dies while jobs run, Hangfire.PostgreSql leaves them `Processing` with `fetchedat` set. Another
worker re-fetches them only once `fetchedat` is older than `InvisibilityTimeout`. The package default (1.21.1) is
30 minutes, with `UseSlidingInvisibilityTimeout = false`. JOB-005 measured this: killed at 20:16:30 UTC, re-fetched
at ~20:46.

## Change

`xafhangfire.Blazor.Server/Startup.cs` currently has:

```csharp
config.UsePostgreSqlStorage(options => options.UseNpgsqlConnection(hangfireConnectionString));
```

Change it to the non-obsolete overload `UsePostgreSqlStorage(Action<PostgreSqlBootstrapperOptions>, PostgreSqlStorageOptions)`,
checked by reflection on the 1.21.1 package:

```csharp
config.UsePostgreSqlStorage(
    options => options.UseNpgsqlConnection(hangfireConnectionString),
    new PostgreSqlStorageOptions
    {
        // JOB-008: a crashed worker's jobs are picked up again after 5 minutes, not 30. Sliding keeps
        // refreshing fetchedat for jobs that are still running, so a long job isn't handed out twice.
        UseSlidingInvisibilityTimeout = true,
        InvisibilityTimeout = TimeSpan.FromMinutes(5),
    });
```

The values live in code, not config: there is no reason to tune them per deployment (YAGNI).

Every other storage option stays at its default. `new PostgreSqlStorageOptions()` carries the same defaults as
the options object the single-argument overload builds, including `PrepareSchemaIfNecessary`, the schema name and
the queue poll interval. The plan review should confirm this.

## Why sliding is required

Without sliding, a 5-minute timeout hands every job running longer than 5 minutes to a second worker, so the
job runs twice. With sliding, each `PostgreSqlFetchedJob` registers with the storage's `PostgreSqlHeartbeatProcess`
(a server component that `AddHangfireServer()` starts). That process checks every second and runs
`ExecuteKeepAliveQueryIfRequired`, which refreshes `fetchedat` every `InvisibilityTimeout / 5` (60 s here) while
the job runs. Only a dead process stops refreshing it.

Cost: one small UPDATE per running job per minute.

## Cases and how each is verified

| Case | Expected | Verified by |
|------|----------|-------------|
| One job longer than the timeout (420 s DemoLog), app alive | Runs exactly once; `fetchedat` advances at ~60 s; the queue row is gone after Succeeded | Live: sample `hangfire.jobqueue.fetchedat` at ~10 s and ~75 s; at the end, 1 START log line, Succeeded, and 0 `jobqueue` rows for the job |
| App hard-killed > 65 s into running jobs (after the first keep-alive) | Re-fetched ≤ 5 min 15 s after the last `fetchedat` (timeout + 15 s poll), not 30 min | Live: DemoLog jobs of 120 s, kill at ~70 s, restart; compare the last `fetchedat` before the kill with the second START log line, then Succeeded ~120 s later |
| Queued jobs and recurring schedules after restart | Unchanged (JOB-005 behaviour) | Same live run |
| `UseHangfire = false` / in-memory storage branch | Untouched | Code read: the change is inside the PostgreSQL branch only |
| Existing tests | Pass | `dotnet test` |

No unit test: this is storage configuration that only a live database exercises, and the live runs above are the
check.

## Gemini plan review (2026-10-26): nothing blocking

Confirmed: the single-argument overload delegates to `UsePostgreSqlStorage(configure, new PostgreSqlStorageOptions())`,
so only the two properties change. Adopted:

1. Kill after the first keep-alive, and measure time to re-fetch, not time to Succeeded (case 2 above).
2. Observe `fetchedat` advancing, and check the queue row is deleted after success (case 1 above). This also covers
   the review's concern that the `WHERE fetchedat = @FetchedAt` match could fail after a keep-alive round trip under
   `EnableLegacyTimestampBehavior`: `RemoveFromQueue` and `Requeue` use the same match. A graceful-stop run is not
   added: Ctrl+C can't be sent to a background process here, and the same match is what case 1 checks.
3. The mechanism is the storage's heartbeat process, not a per-job timer (text above corrected).

Side findings, carded: JOB-010 (an interrupted run's execution record stays Running, reproduced), JOB-011 (a graceful
shutdown is recorded as a failure, unverified), JOB-012 (a recurring job can overlap itself, unverified).

## Live case 1 failed (2026-10-26): blocked by JOB-013

With the change in place, a 420 s DemoLog job (Hangfire job 144) started twice, at 22:02:27 and at 22:07:36
local time. At 22:03:28 Hangfire logged "Background job identifier '144' was fetched by another worker, will not
execute keep alive". The keep-alive matches the queue row on `fetchedat = @fetchedAt`. Legacy timestamps make
Npgsql read `fetchedat` as Local (+01:00) and write it back as `timestamp`, the UTC session never matches, and the
job became visible again after 5 minutes. The Startup.cs change was reverted. Root cause and fix options are on
JOB-013 (CARD-2589); revisit this plan once JOB-013 is fixed.

## Revisit after UPG-001 (2026-10-27)

JOB-013 is fixed: UPG-001 (`fb3a5dd`) dropped `Npgsql.EnableLegacyTimestampBehavior`, and TEST-001's
`HangfireQueueTests.FinishedJob_LeavesTheQueue` (`a0c8d69`) guards the `fetchedat` match on delete against a
real Postgres. Hangfire.PostgreSql is still 1.21.1. The Startup.cs change above is reapplied unchanged.

New since the first attempt: `Jobs.Tests/Integration/` runs against the Docker Postgres, so the case that failed
live (keep-alive can't match its own `fetchedat`, the job runs twice) gets an automated test next to the JOB-013
one:

`HangfireQueueTests.SlidingTimeout_LongJobRunsOnce`: storage with `UseSlidingInvisibilityTimeout = true`,
`InvisibilityTimeout = 3 s` (keep-alive every 600 ms), `QueuePollInterval = 200 ms`, the same pinned session zone
as the JOB-013 test; one job that sleeps 8 s and increments a static counter; a server with 2 workers. Expect:
Succeeded, the counter is 1, and the queue row is gone. Without sliding (or with a broken keep-alive match) the
second worker re-fetches the job after 3 s and the counter reaches 2, so the test fails. That failure is checked
once by flipping `UseSlidingInvisibilityTimeout` to false locally before the commit.

The test exercises the storage mechanism with the short timeout, not Startup's 5-minute values; those are a
two-line code read, and the live runs (cases 1 and 2) remain the check that the app uses them.

Cases table additions:

| Case | Expected | Verified by |
|------|----------|-------------|
| Long job, sliding on, short timeout, 2 workers | Runs once, queue row deleted | `SlidingTimeout_LongJobRunsOnce` |
| Same, sliding off | Runs twice (proves the test can fail) | Local flip, not committed |

### Gemini plan review (2026-10-27): approved, one should-fix

1. Adopted (should-fix): the job increments the counter on entry, before the sleep, and the test resets it before
   enqueueing. Incrementing after the sleep lets the test pass with sliding off: worker 1 finishes first, sets
   Succeeded, and on its next fetch deletes the expired row, so the counter reads 1 when the test checks.
2. Adopted (nit): with a 3 s timeout the keep-alive fires about once a second, not every 600 ms, because
   `PostgreSqlHeartbeatProcess` sleeps 1 s per pass. That still refreshes `fetchedat` with at least 1.4 s to spare.
   Confirmed: `new BackgroundJobServer(options, storage)` starts the heartbeat process when sliding is on.
3. Adopted (nit): the test clears `hangfire.jobqueue` before it starts, so a leftover row from a failed test can't
   be picked up by the next one.

Nothing found for the Startup.cs change: the other options keep their defaults, and no app code touches
`hangfire.*` or `fetchedat`.

### Results (2026-10-27)

- `SlidingTimeout_LongJobRunsOnce` passes; with `UseSlidingInvisibilityTimeout = false` it fails with
  "found 2" (local flip, reverted).
- Live case 1 (420 s DemoLog, Hangfire job 194): `fetchedat` advanced 18:27:39 → 18:28:40 → 18:29:40 UTC
  (keep-alive every ~60 s), one Processing state, Succeeded at 18:34:46, queue row gone.
- Live case 2 (three 120 s jobs 196-198): last keep-alive 18:36:00, app hard-killed at 18:36:11. All three were
  re-fetched at 18:41:09 (5 min 9 s after the last `fetchedat`, was 30 min) and succeeded at 18:43:12; no queue
  rows left. A stray copy of the test script also restarted the app once at 18:40:43; that doesn't change the
  measurement, since the re-fetch depends only on the age of `fetchedat`.
- Full suite: 181/181. Gemini diff review: nothing found.

## Out of scope

Worker count, the queue poll interval and the retry policy (JOB-004 raised the `[AutomaticRetry(Attempts = 3)]`
interaction separately).
