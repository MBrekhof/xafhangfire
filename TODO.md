# TODO — Job Dispatcher POC

## Current Status

**Session 1 (2026-02-18):** All 3 phases + follow-up items 1-3 implemented. Solution builds clean. RunNow action dispatches real jobs. 3 sample JobDefinitions seeded. Recurring jobs sync to Hangfire on startup. Manually verified working.

**Session 2 (2026-02-19):** PostgreSQL storage for Hangfire. Uses existing `duetgpt-postgres` Docker container with dedicated `hangfire` database. Falls back to in-memory when no connection string configured.

**Session 3 (2026-02-19):** Report Generation Jobs implemented. Two XtraReport classes (Project Status Report, Contact List by Organization) with GenerateReportCommand/Handler. Reports registered as predefined reports in XAF and seeded as JobDefinitions.

**Session 4 (2026-02-19):** Email Jobs implemented with MailKit. Three commands: SendEmail, SendReportEmail (report as attachment), SendMailMerge (template + CRM contacts). IEmailSender with SmtpEmailSender/LogOnlyEmailSender toggle. EmailTemplate XAF entity with placeholder syntax.

**Session 5 (2026-02-21):** Migrated application database from SQL Server LocalDB to PostgreSQL 16 in Docker. Consolidated app DB and Hangfire storage into single PostgreSQL instance (`xafhangfire-postgres` container, port 5433). Added docker-compose.yml. Fixed pre-existing ObservableCollection bug in CRM entities. Fixed DI scoping bug (DirectJobDispatcher uses IServiceScopeFactory). Added Serilog structured logging. Fixed XAF security context for background Hangfire jobs (IJobScopeInitializer + HangfireJob service user). Configurable report output directory via `Reports:OutputDirectory` in appsettings.json.

**Session 6 (2026-02-21):** Report parameters support (Dictionary<string, string> with DateRangeResolver integration). Hangfire dashboard authorization (IDashboardAuthorizationFilter, Administrators-only in production). Job execution history tracking (JobExecutionRecord entity + IJobExecutionRecorder). Updated README with full project documentation. Created integration guide (docs/integration-guide.md). xUnit test project with 44 tests covering DateRangeResolver, JobDispatchService, DirectJobDispatcher, handlers, and LogOnlyEmailSender.

**Session 7 (2026-02-21):** Job progress reporting (IJobProgressReporter interface, XafJobProgressReporter implementation, DemoLogHandler reports progress during loop). Consecutive failure tracking (ConsecutiveFailures counter on JobDefinition, reset on success, incremented on failure, warning logged at configurable threshold).

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
- [x] SendEmailCommand + SendEmailHandler (single email via IEmailSender)
- [x] SendReportEmailCommand + SendReportEmailHandler (report generation + email attachment)
- [x] SendMailMergeCommand + SendMailMergeHandler (template + CRM contacts, placeholder replacement)
- [x] IEmailSender interface with SmtpEmailSender (MailKit) and LogOnlyEmailSender (dev fallback)
- [x] EmailTemplate XAF entity (Name, Subject, BodyHtml with {Placeholder} syntax)
- [x] 2 seed EmailTemplates (Welcome Contact, Project Status Update)
- [x] 2 email JobDefinitions seeded (Welcome Mail Merge, Email Project Status Report)
- [x] Email config section in appsettings.json (empty Host = log-only mode)
- [x] PostgreSQL 16 Docker container via docker-compose.yml (port 5433)
- [x] EF Core provider swapped from SqlServer to Npgsql
- [x] Connection strings updated for both Blazor.Server and Win projects
- [x] Hangfire consolidated into same PostgreSQL database (shared ConnectionString)
- [x] Npgsql legacy timestamp behavior enabled for XAF DateTime compatibility
- [x] Fixed ObservableCollection bug in Organization/Project navigation properties
- [x] Fixed DI scoping bug — DirectJobDispatcher uses IServiceScopeFactory (not root IServiceProvider)
- [x] Serilog structured logging (console + rolling file at `logs/xafhangfire-YYYYMMDD.log`)
- [x] IJobScopeInitializer — authenticates HangfireJob service user in background job scopes
- [x] HangfireJob user + BackgroundJobs role (read-only access for report generation)
- [x] Configurable report output directory (`Reports:OutputDirectory` in appsettings.json, `ReportOutputOptions`)
- [x] Report parameters — `Dictionary<string, string>? ReportParameters` on GenerateReportCommand and SendReportEmailCommand
- [x] ReportParameterHelper — applies parameters to XtraReport with DateRangeResolver integration and type conversion
- [x] Hangfire dashboard auth — `HangfireDashboardAuthFilter` (IDashboardAuthorizationFilter), any authenticated user in dev, Administrators only in production
- [x] JobExecutionRecord entity — tracks every job run with status, duration, error message, parameters snapshot
- [x] IJobExecutionRecorder interface + XafJobExecutionRecorder implementation (INonSecuredObjectSpaceFactory)
- [x] JobExecutor and DirectJobDispatcher wired with execution recording (start/complete/fail)
- [x] JobDefinition → JobExecutionRecord navigation (ObservableCollection)
- [x] README.md — full project documentation with architecture, setup, configuration, job types
- [x] Integration guide — `docs/integration-guide.md` with step-by-step XAF integration instructions
- [x] xUnit test project (`xafhangfire.Jobs.Tests`) — 44 tests covering DateRangeResolver, JobDispatchService, DirectJobDispatcher, SendEmailHandler, DemoLogHandler, LogOnlyEmailSender
- [x] FluentAssertions + NSubstitute for assertions and mocking
- [x] IJobProgressReporter interface + XafJobProgressReporter implementation (updates JobExecutionRecord progress)
- [x] NullJobProgressReporter (no-op default for tests and non-XAF contexts)
- [x] JobExecutor and DirectJobDispatcher initialize progress reporter with execution record ID
- [x] DemoLogHandler reports progress (percent + step message) during delay loop
- [x] ProgressPercent + ProgressMessage fields on JobExecutionRecord
- [x] ConsecutiveFailures counter on JobDefinition (reset on success, incremented on failure)
- [x] XafJobExecutionRecorder updates JobDefinition status (LastRunUtc, LastRunStatus, LastRunMessage) on completion/failure
- [x] Configurable failure alert threshold (`Jobs:FailureAlertThreshold` in appsettings.json, default: 3)
- [x] Warning logged when consecutive failures reach threshold

## Future
- [ ] Scheduler calendar view bound to JobDefinition
- [ ] Cron expression → next-run visualization
- [ ] Rich parameter UI (dynamic forms from command metadata)
- [ ] Expand test coverage (Blazor.Server handlers with mocked IReportExportService, integration tests)
- [ ] Real-time progress UI (SignalR push of progress updates to XAF detail view)
- [ ] Email notifications on repeated failures (extend failure tracking to send alert emails)
- [ ] Verify jobs persist across app restarts (manual test)

## Architecture Decisions
- Hangfire stays **embedded in Blazor.Server** (no separate service). Split later if needed.
- Handlers in `xafhangfire.Jobs` are shared — a future standalone worker just references the same project.
- PostgreSQL 16 for everything — app DB + Hangfire in one Docker container (`xafhangfire-postgres`, port 5433).
- Report parameters use `Dictionary<string, string>` (not strongly typed) — parameters vary per report and are stored as JSON.
- Job execution records use `INonSecuredObjectSpaceFactory` — recorder runs in background context, doesn't need secured access.
- Hangfire dashboard auth uses ASP.NET Core `HttpContext` claims, not XAF security — dashboard is middleware-level.
- Progress reporting is opt-in — handlers accept `IJobProgressReporter` via constructor injection, executors initialize it with the record ID.
- Consecutive failure tracking updates `JobDefinition` inline during recording — no separate background process needed.

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
12. `xafhangfire/xafhangfire.Jobs/IEmailSender.cs` — email sender interface
13. `xafhangfire/xafhangfire.Blazor.Server/Services/SmtpEmailSender.cs` — MailKit SMTP sender
14. `xafhangfire/xafhangfire.Module/BusinessObjects/EmailTemplate.cs` — email template entity
15. `docs/plans/2026-02-19-email-jobs-design.md` — email jobs design doc
16. `docker-compose.yml` — PostgreSQL 16 container definition
17. `docs/plans/2026-02-21-postgresql-migration-design.md` — PostgreSQL migration design
18. `xafhangfire/xafhangfire.Jobs/IJobScopeInitializer.cs` — scope initialization interface
19. `xafhangfire/xafhangfire.Blazor.Server/Services/XafJobScopeInitializer.cs` — XAF auth for background jobs
20. `docs/plans/2026-02-21-hangfire-auth-fix-design.md` — background auth fix design
21. `xafhangfire/xafhangfire.Blazor.Server/Handlers/ReportParameterHelper.cs` — report parameter application
22. `xafhangfire/xafhangfire.Blazor.Server/Services/HangfireDashboardAuthFilter.cs` — dashboard auth
23. `xafhangfire/xafhangfire.Module/BusinessObjects/JobExecutionRecord.cs` — execution history entity
24. `xafhangfire/xafhangfire.Jobs/IJobExecutionRecorder.cs` — execution recording interface
25. `xafhangfire/xafhangfire.Blazor.Server/Services/XafJobExecutionRecorder.cs` — XAF recorder implementation
26. `docs/integration-guide.md` — integration guide for other XAF solutions
27. `xafhangfire/xafhangfire.Jobs/IJobProgressReporter.cs` — progress reporting interface
28. `xafhangfire/xafhangfire.Jobs/NullJobProgressReporter.cs` — no-op progress reporter
29. `xafhangfire/xafhangfire.Blazor.Server/Services/XafJobProgressReporter.cs` — XAF progress reporter implementation
30. `docs/plans/2026-02-21-progress-and-error-notifications-design.md` — progress + failure tracking design

Then check the TODO list above to see what's done and what's next.

Start PostgreSQL: `docker compose up -d` from repo root.
Build: `dotnet build xafhangfire.slnx` from repo root.
DB update: `dotnet run --project xafhangfire/xafhangfire.Blazor.Server/xafhangfire.Blazor.Server.csproj -- --updateDatabase --forceUpdate --silent`

## Known Gotchas
- DevExpress 25.2 removed `SizeAttribute`. Use `FieldSizeAttribute` from `DevExpress.ExpressApp.DC` instead.
- Module project has no `<Nullable>enable</Nullable>` — don't use `string?` there.
- If IIS Express is running, it may lock DLLs. Kill it before rebuilding.
- Navigation collections MUST use `ObservableCollection<T>` (not `List<T>`) due to `ChangingAndChangedNotificationsWithOriginalValues` change tracking strategy.
- PostgreSQL requires `Npgsql.EnableLegacyTimestampBehavior = true` for XAF's `DateTime` properties (set in Startup.cs).
- Connection strings use `EFCoreProvider=PostgreSql;` prefix for XAF auto-detection. Hangfire needs this prefix stripped (see `StripEFCoreProvider` in Startup.cs).
- Background Hangfire jobs need `IJobScopeInitializer` to authenticate as "HangfireJob" user — without it, handlers using `IReportExportService` or `IObjectSpaceFactory` fail with "The user name must not be empty".
- Don't name a class `ReportOptions` — clashes with `DevExpress.ExpressApp.ReportsV2.ReportOptions`. Use `ReportOutputOptions` instead.
