# Fix plan: review findings (2026-10-26)

Cards (ContextBoard project xafhangfire): AUTH-002, JOB-001, JOB-002, JOB-003, JOB-006,
SER-000, SER-001, SER-002, SER-003, RPT-001. AUTH-001 and AUTH-003 were refuted by repro and are closed.

Order of implementation = order of sections. Each section lists the cases it must handle and
the check that proves it (unit test in `xafhangfire.Jobs.Tests`, or a live check against the
running app + Postgres where XAF/Hangfire make a unit test impractical).

---

## 1. AUTH-002 — HangfireJob service user has an empty password (reproduced)

Repro (2026-10-26): `POST /api/Authentication/Authenticate {"userName":"HangfireJob","password":""}`
→ 200 + JWT; that token → `POST /api/jobs/demo-log` → 200.

Root cause: `Updater.UpdateDatabaseAfterUpdateSchema` creates "HangfireJob" with `EmptyPassword`,
inside `#if !RELEASE` (so Release builds on a fresh DB have no service user at all and background
jobs fail with "user name must not be empty").

Change (`Module/DatabaseUpdate/Updater.cs`):
- Move `CreateBackgroundJobsRole()` + the HangfireJob user block **outside** `#if !RELEASE`
  into `EnsureBackgroundJobsServiceUser()`.
- New user: `userManager.CreateUser<ApplicationUser>(ObjectSpace, "HangfireJob", RandomPassword(), …)`.
- Existing user (DBs seeded before this fix): if `user.ComparePassword("")` is true, call
  `user.SetPassword(RandomPassword())` (dxdocs: `PermissionPolicyUser.SetPassword(String)`, EF, 25.2).
- `RandomPassword()` = `Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))`; never stored
  or logged. Nobody needs it: `XafJobScopeInitializer` signs in with `SignInManager.SignIn(user)`,
  no password.
- Admin/User empty passwords stay as-is (XAF template default, non-Release only) — out of scope.

Cases: fresh DB Debug; fresh DB Release; existing DB with empty-password HangfireJob; existing DB
after fix (no re-rotation needed, but harmless); role already exists.

Check (live): `--updateDatabase --forceUpdate --silent`, then the repro → 401; a Hangfire job
(scheduled demo / report job) still runs as HangfireJob (JobExecutionRecord Success, no
"user name must not be empty" in the log).

---

## 2. Job identity: JOB-002, JOB-001, JOB-003, JOB-006

One root cause: nothing carries the JobDefinition's identity through the pipeline.
`RunNow`/`JobSyncService` → `JobDispatchService` → `IJobDispatcher` → `JobExecutor` → recorder only
know `typeof(TCommand).Name`.

### 2a. Thread `Guid? jobDefinitionId` through the pipeline (JOB-002)

Signatures (compiler finds every caller):
- `IJobDispatcher.DispatchAsync<T>(T command, Guid? jobDefinitionId = null, CancellationToken ct = default)`
- `IJobDispatcher.Schedule<T>(T command, string cronExpression, string scheduleId, Guid? jobDefinitionId)`
- `IJobDispatcher.RemoveSchedulesExcept(IReadOnlyCollection<string> keepScheduleIds)` (2c)
- `JobExecutor<T>.RunAsync(T command, Guid? jobDefinitionId, CancellationToken ct)`
- `IJobExecutionRecorder.RecordStartAsync(string jobName, string jobTypeName, string? parametersJson, Guid? jobDefinitionId, CancellationToken ct = default)`
- `JobDispatchService.DispatchByNameAsync(string jobTypeName, string? parametersJson = null, Guid? jobDefinitionId = null, CancellationToken ct = default)`
- `JobDispatchService.ScheduleByName(string jobTypeName, string? parametersJson, string cronExpression, string scheduleId, Guid? jobDefinitionId)`

Callers to update: `JobSchedulerViewController` (RunNow), `JobSyncService`, `JobTestController`
(passes no id → old behaviour), `HangfireJobDispatcher`, `DirectJobDispatcher`, `JobExecutor`, tests.

`XafJobExecutionRecorder.RecordStartAsync`:
- id given → `FirstOrDefault<JobDefinition>(j => j.Id == id)`; link it, `record.JobName = def.Name`,
  and set `def.LastRunUtc = StartedUtc; def.LastRunStatus = Running` (the recorder now owns all
  LastRun* writes — needed by 2d).
- id null or not found → old name lookup (JobTestController, ad-hoc dispatches).

Hangfire compatibility: `RunAsync` gains a parameter, so jobs already **enqueued** with the old
2-arg signature cannot be performed after deploy (Hangfire marks them failed). Accepted: POC, dev DB.
Recurring jobs are re-registered under new ids by 2c, which also deletes the old ones.

Tests: JobDispatchService passes id to `DispatchAsync`/`Schedule` (every command type via one
theory); DirectJobDispatcher passes id to recorder; JobExecutor passes id to recorder (new test).

### 2b. Per-definition recurring job id (JOB-001)

`scheduleId = $"jobdef-{job.Id:N}"` built by the synchronizer (2c); `HangfireJobDispatcher.Schedule`
uses it verbatim. Two definitions of the same command type → two recurring jobs.

### 2c. Reconcile Hangfire with the JobDefinition table (JOB-003)

- Move the sync body out of `JobSyncService` into `JobScheduleSynchronizer : IJobScheduleSynchronizer`
  (interface in Jobs: `void Sync();`), registered singleton; `JobSyncService` (hosted, startup)
  just calls it. A lock serialises concurrent calls.
- `Sync()`: load definitions (non-secured OS); for each `IsEnabled && cron != ""` →
  `ScheduleByName(type, json, cron, scheduleId, id)`; then
  `dispatcher.RemoveSchedulesExcept(desiredIds)`.
- `HangfireJobDispatcher.RemoveSchedulesExcept`: `JobStorage.Current.GetConnection().GetRecurringJobs()`
  → `RecurringJob.RemoveIfExists(id)` for every id not kept. Hangfire recurring jobs in this app are
  owned by JobDefinitions (nothing else calls `AddOrUpdate`), so this also deletes the legacy
  per-type ids ("DemoLogCommand", …).
- `DirectJobDispatcher.RemoveSchedulesExcept`: no-op (scheduling disabled in direct mode).
- Runtime trigger: new `Module/Controllers/JobDefinitionScheduleSyncController : ObjectViewController<ObjectView, JobDefinition>`
  subscribes `ObjectSpace.Committed` in `OnActivated` (unsubscribe before `base.OnDeactivated`) →
  `Application.ServiceProvider.GetService<IJobScheduleSynchronizer>()?.Sync()` in try/catch
  (logged; a sync failure must not break the save). Covers create, edit (cron/params/enabled),
  disable and delete from both ListView and DetailView. WinForms has no synchronizer → no-op.
- Not covered: JobDefinition edits through the OData Web API (no ObjectSpace.Committed in the UI).
  Startup sync still reconciles them on the next restart. Noted on the card.

Check (live, Hangfire mode): seed 2 GenerateReportCommand definitions with cron → `hangfire.set`
holds two `jobdef-…` ids; disable one → its id gone; delete one → gone; legacy "DemoLogCommand" id gone
after startup.

### 2d. RunNow must not overwrite the recorder (JOB-006)

`JobSchedulerViewController.RunNowAction_Execute` (stays `async void`, XAF Blazor pattern, whole
body in try/catch):
1. If `ObjectSpace.IsModified` → `ObjectSpace.CommitChanges()` first (a new, unsaved definition must
   exist in the DB for the recorder to link it; also preserves the old behaviour that RunNow saved).
2. `var startedUtc = DateTime.UtcNow; await DispatchByNameAsync(job.JobTypeName, job.ParametersJson, job.Id)`.
3. After the await: `if (View == null || View.IsDisposed) return;` → `ObjectSpace.Refresh()` →
   message. No LastRun* writes on success (recorder owns them).
4. catch: `ObjectSpace.Refresh()`; re-read `View.CurrentObject`; only if the recorder did not record
   this run (`LastRunUtc == null || LastRunUtc < startedUtc`, i.e. dispatch-time failure such as bad
   JSON) set Failed + message and commit. Show the message either way.

Cases: Direct mode success/failure (recorder ran inline), Hangfire mode (enqueued, recorder runs
later), ListView vs DetailView, unsaved new object, optimistic-lock conflict avoided by Refresh.

Check (live): RunNow in the UI (Playwright C#) on "Demo Log Job" → definition shows Success after
refresh, ExecutionHistory has the record.

---

## 3. Parameter serialization: SER-000 (umbrella), SER-001, SER-002, SER-003

Root cause (SER-000): `JobParametersPropertyEditor.RefreshFields` re-serializes all fields and calls
`WriteValue()` on **load** (`ReadValueCore`), so any value the typed field model cannot represent is
rewritten just by opening the record.

### 3a. SER-000 — no write-back on load
- `RefreshFields(string json, bool persist)`. `ReadValueCore` → `persist: false`. JobTypeName change
  and ReportName change (user actions) → `persist: true` (keeps the Session 14 behaviour that newly
  discovered params land in the JSON).
- Report date parameters default to **empty** instead of today (`GetDefaultValueString`), so a
  freshly discovered date field shows nothing and the JSON has nothing — UI and JSON agree.

### 3b. SER-001 — blank json field
`JobParametersJsonSerializer` `case "json"`: `if (string.IsNullOrWhiteSpace(field.StringValue)) break;`
Invalid JSON keeps the current fallback (store the string) — the user sees the raw text and the
dispatch error, nothing is silently lost.

### 3c. SER-002 — relative date terms
- `DateRangeResolver.Terms` (public static list of the supported terms; test asserts each resolves).
- `ParameterFieldModel.RelativeTerm` (string, null = specific date).
- Load: value parses as date → `DateTimeValue`; else value is a known term → `RelativeTerm`; else
  keep raw text in `StringValue` (never dropped).
- Serialize group "date": `RelativeTerm ?? DateTimeValue?.ToString("yyyy-MM-dd") ?? StringValue`, empty → omitted.
- Razor "date": date picker + a `<select>` "Specific date / today / last-week / last-month …".
  Choosing a term clears the date and disables the picker; choosing "Specific date" re-enables it.
  Plain HTML elements (DxTextBox does not render dynamically in this editor — known gotcha).

### 3d. SER-003 — empty lookup / missing parameters
- Serializer omits empty group values (date with nothing, lookup/string empty). int/decimal/bool
  always have a value and are kept.
- Reports tolerate a missing/null parameter:
  - `ContactListByOrgReport.FilterString = "?Organization Is Null Or [Organization.Id] = ?Organization"`.
  - `ProjectStatusReport.FilterString = "(?StartDate Is Null Or [StartDate] >= ?StartDate) And (?EndDate Is Null Or [StartDate] <= ?EndDate)"`.
  - When a referenced `?param` is not supplied at all (seeded job never opened), behaviour of an
    undefined parameter in FilterString must be verified first (unit test: XtraReport over a
    List<T> data source, FilterString referencing an undefined parameter, `CreateDocument()`).
    If it throws or filters everything out, `ReportParameterHelper` creates a null-valued
    parameter for each `?name` referenced in `report.FilterString` that is still missing after
    applying the supplied values (type `object`/string, `Value = null`) — verify null semantics in
    the same test. DevExpress semantics to be confirmed via dxdocs before coding.

Tests: serializer — empty json omitted; empty group lookup omitted; date term round-trips; date
with unparseable text kept; load→serialize with no edits leaves JSON unchanged for a JSON that
contains a term, an empty json field and an ISO date (the SER-000 round-trip test).

Check (live, Playwright C#): open "Generate Project Status Report" with `"StartDate":"last-month"`
→ dropdown shows last-month, JSON unchanged after open+save; select a specific date → JSON has the date.

---

## 4. RPT-001 — report output path collisions

- `GenerateReportHandler`, `OutputPath` null: `{ReportName}_{yyyyMMdd-HHmmss}_{guid:N[..8]}.{ext}`
  in the configured output directory. Explicit `OutputPath` unchanged (caller's choice).
- `SendReportEmailHandler`: export into `Path.Combine(Path.GetTempPath(), "xafhangfire", Guid.NewGuid().ToString("N"))`
  as `{ReportName}.{ext}` (attachment keeps a readable name); delete the directory in `finally`
  after all recipients are sent.

Tests: GenerateReportHandler twice with mocked `IReportExportService` → two distinct files;
SendReportEmailHandler → `IEmailSender` received a path under the temp dir and the file is gone afterwards.

---

## Gemini plan review (2026-10-26) — all adopted

Blocking:
1. `ComparePassword` is an explicit `IAuthenticationStandardUser` implementation → cast. Resolve
   `UserManager` inside `EnsureBackgroundJobsServiceUser()`. Also re-add the BackgroundJobs role if missing.
2. `JobStorage.Current.GetConnection()` is `IDisposable` → `using`; `using Hangfire.Storage;`.
3. RunNow: commit pending changes **before** the dispatch try/catch (a failed validation must not be
   followed by a Refresh that discards the new object); disposed-guard in catch too; re-fetch the
   definition by captured id (ListView `CurrentObject` can be null after Refresh); "recorder did not
   run" = `LastRunUtc == previousLastRunUtc` (no timestamp comparison — Postgres µs truncation).
4. Date field handlers clear `StringValue` when a date or term is chosen; terms matched case-insensitively.
5. Undefined `?param` in FilterString evaluates to null in 25.2 (Gemini ran it) → no placeholder
   parameters. `ReportParameterHelper` skips empty values; an existing parameter with an empty value
   gets `AllowNull = true, Value = null` (a non-AllowNull parameter silently coerces null to ""/Guid.Empty).

Non-blocking, adopted: keep a 2-arg `JobExecutor.RunAsync` overload (Hangfire's scheduler fires
legacy recurring jobs before the 5 s startup sync); recorder falls back to the name lookup only when
no id was passed; sync adds a schedule id to the keep-set only after `ScheduleByName` succeeded
(invalid cron → old schedule removed); clearing ReportName also refreshes fields; field loading moves
into `JobParametersJsonSerializer.LoadFields` so the SER-000 round-trip is unit-testable;
`GenerateReportHandler` treats whitespace `OutputPath` as unset; temp-dir cleanup is best-effort.

## Out of scope (own cards)
RPT-002, UI-001, TEST-001, UI-002, JOB-004, JOB-005 — feature/chore cards, handled after this batch.
