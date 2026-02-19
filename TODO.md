# TODO — Job Dispatcher POC

## Current Status

**Session 1 (2026-02-18):** All 3 phases + follow-up items 1-3 implemented. Solution builds clean. RunNow action dispatches real jobs. 3 sample JobDefinitions seeded. Recurring jobs sync to Hangfire on startup. Manually verified working.

**Session 2 (2026-02-19):** PostgreSQL storage for Hangfire. Uses existing `duetgpt-postgres` Docker container with dedicated `hangfire` database. Falls back to in-memory when no connection string configured.

**Session 3 (2026-02-19):** Report Generation Jobs implemented. Two XtraReport classes (Project Status Report, Contact List by Organization) with GenerateReportCommand/Handler. Reports registered as predefined reports in XAF and seeded as JobDefinitions.

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
- [x] GenerateReportCommand + GenerateReportHandler (IReportExportService, PDF/XLSX export)
- [x] ProjectStatusReport XtraReport class (landscape, all projects with org/status/dates)
- [x] ContactListByOrgReport XtraReport class (grouped by organization, contact details)
- [x] Predefined reports registered via PredefinedReportsUpdater in Module.cs
- [x] 2 report JobDefinitions seeded in Updater.cs
- [x] CRM seed data (3 organizations, 5 contacts, 4 projects, 10 tasks)

## Next Session: Priority Order

### 1. ~~Report Generation Jobs (DevExpress XtraReport)~~ DONE
- [x] Created `GenerateReportCommand(ReportName, OutputFormat, OutputPath)` in Jobs/Commands
- [x] Created `GenerateReportHandler` in Blazor.Server/Handlers using IReportExportService
- [x] Two XtraReport classes: ProjectStatusReport, ContactListByOrgReport in Module/Reports
- [x] Registered as predefined reports via PredefinedReportsUpdater
- [x] Registered handler + added to JobDispatchService switch
- [x] 2 report JobDefinitions seeded (Project Status + Contact List)

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

10. `xafhangfire/xafhangfire.Module/Reports/` — XtraReport classes (ProjectStatusReport, ContactListByOrgReport)
11. `xafhangfire/xafhangfire.Blazor.Server/Handlers/GenerateReportHandler.cs` — report export handler

Then check the TODO list above to see what's done and what's next.

Build: `dotnet build xafhangfire.slnx` from repo root.

## Known Gotchas
- DevExpress 25.2 removed `SizeAttribute`. Use `FieldSizeAttribute` from `DevExpress.ExpressApp.DC` instead.
- Module project has no `<Nullable>enable</Nullable>` — don't use `string?` there.
- If IIS Express is running, it may lock DLLs. Kill it before rebuilding.
