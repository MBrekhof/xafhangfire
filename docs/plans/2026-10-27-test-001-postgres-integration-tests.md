# TEST-001: Postgres integration tests — plan

Card: TEST-001 (CARD-2560), "Expand test coverage — Blazor.Server handlers and integration tests".
Martin's decisions (2026-10-27): **1a** — add integration tests against the Postgres container; the handler half
is already covered (`Handlers/ReportHandlerOutputTests.cs` mocks `IReportExportService` for both report
handlers) and closes as done. **2a** — when Postgres is unreachable the integration tests are skipped, so
`dotnet test` still passes with Docker stopped.

Why: every test in the suite runs without a database. JOB-013 (finished jobs never left `hangfire.jobqueue`
under Npgsql's legacy timestamp switch) was a Postgres-level bug that the whole suite missed, and JOB-008's
re-test needs Hangfire running on the real database.

## What gets added (xafhangfire.Jobs.Tests, new folder `Integration/`)

1. `PostgresFactAttribute : FactAttribute` — its constructor sets `Skip` ("Postgres on localhost:5433 is not
   reachable; run docker compose up -d") when a login to the `postgres` database fails (3 s timeout; a Postgres
   that is still starting up counts as unreachable). The check runs once per process (`Lazy<bool>`). xunit 2.9 has no runtime skip, so this is the dependency-free way.
2. `PostgresDatabase` — a collection fixture (`[CollectionDefinition("Postgres")]`) shared by both test classes,
   so they run one after the other against one database:
   - Connection string is a constant in code, matching docker-compose.yml (user/password `xafhangfire`),
     database **`xafhangfire_inttest`**. Never the dev database `xafhangfire` (the EasyTest database is left
     alone too).
   - Constructor: when Postgres is unreachable, do nothing (fixtures may be built even when every test skips).
     Otherwise build the context exactly as XAF does (`UseConnectionString("EFCoreProvider=PostgreSql;…")`,
     which adds Npgsql + change-tracking proxies), then `EnsureDeleted()` + `EnsureCreated()`: a clean database
     each run. Left in place after the run for inspection; the next run recreates it.
3. `DateTimeRoundTripTests` (EF on Postgres, the UPG-001 convention end to end):
   - For a Utc, a Local and an Unspecified `DateTime` (12:00 on 2026-10-27): save a `Project` (`DueDate`,
     `DateTime?`) and a `JobExecutionRecord` (`StartedUtc`, `DateTime`), both created with `CreateProxy<T>()`.
   - Read them back in a new context: same ticks, Kind Unspecified, for both columns.
   - The raw column (`select "DueDate"::text`) is `2026-10-27 12:00:00` for all three — the wall clock is stored
     as written, no UTC shift.
   - A LINQ filter `Where(p => p.DueDate == value)` with each kind as the parameter finds the row (the original
     WIN-001 repro also failed on an Unspecified filter parameter).
4. `HangfireQueueTests` (Hangfire.PostgreSql 1.21.1 on the test database, the JOB-013 regression):
   - `PostgreSqlStorage` on the test database (`PrepareSchemaIfNecessary` on, `QueuePollInterval` 200 ms),
     a `BackgroundJobClient` and a `BackgroundJobServer` (1 worker) built on that storage, not on
     `JobStorage.Current` (no global state).
   - Enqueue a static no-op job; wait (≤ 30 s) for state Succeeded; then wait (≤ 10 s) until
     `hangfire.jobqueue` has no row for that job id. Under JOB-013 the row stayed forever.
   - Server disposed in `using`.

No production code changes. No new packages: Hangfire.PostgreSql and Npgsql come through the existing
Blazor.Server project reference (to be confirmed at build; if not, reference Hangfire.PostgreSql 1.21.1).

## Proving the tests can fail (mutations, reverted)

- EF: remove the two `ConfigureConventions` lines → the Local/Unspecified writes throw (Npgsql's
  "Cannot write DateTime with Kind=… to PostgreSQL type 'timestamp with time zone'").
- Hangfire: a `[ModuleInitializer]` in the test assembly that sets `Npgsql.EnableLegacyTimestampBehavior` →
  the job succeeds but its `jobqueue` row stays (JOB-013).
- Skip: point the port constant at 5434 → both classes report Skipped, nothing throws, the run passes.

## Cases checked before hand-off (self-review)

- Postgres down → skipped, fixture is a no-op (mutation 3).
- Dev data untouched → separate database name; `EnsureDeleted` only ever targets the constant `xafhangfire_inttest`.
- Parallel classes → one xunit collection, so the fixture's drop/create can't race the Hangfire test.
- Hangfire schema → created by the storage in the freshly created database; nothing shared with the dev schema.
- Stale state between runs → the database is dropped at the start of every run.
- Flakiness → bounded waits with polling, no fixed sleeps; failure messages name the job id and last state.

## Gemini plan review (2026-10-27) — settled

1. The JOB-013 test is blind when the machine's UTC offset equals the Postgres session's (legacy mode reads
   `fetchedat` in the machine zone and writes it back in the session zone). Reproduced: legacy switch on + session
   `Timezone=Europe/Amsterdam` on this W. Europe machine → the test passed. Fixed: the Hangfire connection pins a
   session zone that differs from the machine's (`UTC`, or `Etc/GMT-2` on a UTC machine); both branches fail under
   the legacy switch.
2. Only `DateTime?` was covered. Adopted: the round trip also writes `JobExecutionRecord.StartedUtc` (`DateTime`).
   Removing only the `Properties<DateTime>()` convention line now fails the test.
3. Guard before `EnsureDeleted`: adopted (refuses any database but `xafhangfire_inttest`).
4. `jobqueue.jobid` is `bigint`: the id is bound as `long`.
5. Rows of the three kinds collide on the same timestamp: every query is scoped by the saved row's `Id`.
6. Obsolete `PostgreSqlStorage(string, …)`: uses `NpgsqlConnectionFactory`; one plain connection string, the XAF
   form derived from it. Packages come through the Blazor.Server reference (confirmed by the build).
7. One reachability check shared by the attribute and the fixture: adopted (`PostgresDatabase.IsAvailable`).
Also adopted: the round trip updates the saved rows and checks the stored text again (an UPDATE, not only INSERT).

Result: both tests pass in ~1 s; all three mutations fail them (convention lines removed, legacy switch in either
session-zone branch); with no Postgres on the port both are Skipped. Full suite 180 passed.

## Gemini diff review (2026-10-27) — settled

1. Kind was asserted only on `StartedUtc`, not on `DueDate`. Reproduced: a `DateTime?` converter that reads back
   Kind=Utc passed the test. Fixed (Kind asserted on both); the mutation now fails it.
2. The TCP probe says "available" while Postgres is still starting (Docker's port proxy accepts first), so the
   fixture would throw instead of skipping. The side note (an "incomplete startup packet" line per run in the
   container log) did not reproduce: none in the log. While reproducing, a worse probe bug showed up: against a
   plain IPv4 listener on 127.0.0.1 the TcpClient probe of `localhost` reported unreachable (it timed out on ::1),
   so a Postgres listening on IPv4 only would have skipped every test silently. Fixed both: the probe is a real
   login to the `postgres` database; `57P03` (starting up) counts as unreachable, any other Postgres error runs the
   tests so a wrong password fails loudly. Checked: real Postgres → 2 passed; a non-Postgres IPv4 listener → it
   logs the probe's connection and both tests skip; a closed port → both skip.
3. Plan text still said `StartDate`/`DueDate` and a TCP probe: updated above.

## Out of scope

Integration tests for the job sync service, execution recorder and interrupted-runs cleanup (option 1c, not
chosen); JOB-008's behaviour itself (its own card reuses this harness).
