# TODO — Job Dispatcher POC

## Current Status

**Session 1 (2026-02-18):** All 3 phases + follow-up items 1-3 implemented. Solution builds clean. RunNow action dispatches real jobs. 3 sample JobDefinitions seeded. Recurring jobs sync to Hangfire on startup. Manually verified working.

**Session 2 (2026-02-19):** PostgreSQL storage for Hangfire. Uses existing `duetgpt-postgres` Docker container with dedicated `hangfire` database. Falls back to in-memory when no connection string configured.

## Completed
- [x] `xafhangfire.Jobs` class library (IJobHandler, IJobDispatcher, DirectJobDispatcher, HangfireJobDispatcher, JobExecutor)
- [x] DemoLogCommand/Handler + ListUsersCommand/Handler
- [x] Hangfire embedded in Blazor.Server with in-memory storage + config toggle
- [x] JobTestController API (`/api/jobs/demo-log`, `/api/jobs/list-users`)
- [x] AddJobHandler/AddJobDispatcher DI extension methods
- [x] JobDefinition entity with XAF list/detail views
- [x] JobDispatchService (string name + JSON → command dispatch)
- [x] RunNow action wired to actually dispatch jobs
- [x] 3 sample JobDefinitions seeded in Updater.cs
- [x] JobSyncService — syncs enabled cron jobs to Hangfire on startup
- [x] DevExpress Scheduler module registered
- [x] DateRangeResolver for friendly calendar terms
- [x] Hangfire PostgreSQL storage (Hangfire.PostgreSql) with in-memory fallback

## Next Session: Priority Order

### 1. Report Generation Jobs (DevExpress XtraReport)
- [ ] Create `GenerateReportCommand(string ReportName, string? Parameters, string OutputFormat)`
- [ ] Create `GenerateReportHandler` using DevExpress XtraReport engine
- [ ] Register handler, add to JobDispatchService switch
- [ ] Seed a sample report JobDefinition
- [ ] Output: generate PDF/Excel to a file or byte array

### 2. ~~Persistent Hangfire Storage (PostgreSQL via Docker)~~ DONE
- [x] Reused existing `duetgpt-postgres` container, created `hangfire` database + user
- [x] Add `Hangfire.PostgreSql` NuGet package to Blazor.Server
- [x] Add PostgreSQL connection string to appsettings (`HangfireConnection`)
- [x] Conditional storage: PostgreSQL when connection string present, in-memory when absent
- [ ] Verify jobs persist across app restarts (manual test)
- [x] Keep in-memory as fallback for dev (no connection string in Development config)

### 3. Future
- [ ] Email jobs (MailKit) — `SendEmailCommand` + handler
- [ ] Mail merge (report + email composition)
- [ ] Scheduler calendar view bound to JobDefinition
- [ ] Cron expression → next-run visualization
- [ ] Rich parameter UI (dynamic forms from command metadata)

## Architecture Decisions
- Hangfire stays **embedded in Blazor.Server** (no separate service). Split later if needed.
- Handlers in `xafhangfire.Jobs` are shared — a future standalone worker just references the same project.
- PostgreSQL for Hangfire storage (Docker Desktop). Application data stays on SQL Server LocalDB.

## Claude Continuation Instructions

When resuming this project, read these files first:
1. `CLAUDE.md` — project conventions and build commands
2. `TODO.md` — current progress (this file)
3. `docs/plans/2026-02-18-job-dispatcher-design.md` — approved design
4. `job-dispatcher-architecture.md` — original architecture reference
5. `xafhangfire/xafhangfire.Jobs/` — all files (interfaces, dispatchers, handlers, JobDispatchService, DateRangeResolver)
6. `xafhangfire/xafhangfire.Module/BusinessObjects/JobDefinition.cs` — admin entity
7. `xafhangfire/xafhangfire.Module/Controllers/JobSchedulerViewController.cs` — RunNow action
8. `xafhangfire/xafhangfire.Blazor.Server/Startup.cs` — DI wiring + Hangfire config
9. `xafhangfire/xafhangfire.Blazor.Server/Services/JobSyncService.cs` — startup sync

Then check the TODO list above to see what's done and what's next.

Build: `dotnet build xafhangfire.slnx` from repo root.

## Known Gotchas
- DevExpress 25.2 removed `SizeAttribute`. Use `FieldSizeAttribute` from `DevExpress.ExpressApp.DC` instead.
- Module project has no `<Nullable>enable</Nullable>` — don't use `string?` there.
- If IIS Express is running, it may lock DLLs. Kill it before rebuilding.
