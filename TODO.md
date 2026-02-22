# TODO — Job Dispatcher POC

## Current Status

**Session 1 (2026-02-18):** All 3 phases + follow-up items 1-3 implemented. Solution builds clean. RunNow action dispatches real jobs. 3 sample JobDefinitions seeded. Recurring jobs sync to Hangfire on startup. Manually verified working.

**Session 2 (2026-02-19):** PostgreSQL storage for Hangfire. Uses existing `duetgpt-postgres` Docker container with dedicated `hangfire` database. Falls back to in-memory when no connection string configured.

**Session 3 (2026-02-19):** Report Generation Jobs implemented. Two XtraReport classes (Project Status Report, Contact List by Organization) with GenerateReportCommand/Handler. Reports registered as predefined reports in XAF and seeded as JobDefinitions.

**Session 4 (2026-02-19):** Email Jobs implemented with MailKit. Three commands: SendEmail, SendReportEmail (report as attachment), SendMailMerge (template + CRM contacts). IEmailSender with SmtpEmailSender/LogOnlyEmailSender toggle. EmailTemplate XAF entity with placeholder syntax.

**Session 5 (2026-02-21):** Migrated application database from SQL Server LocalDB to PostgreSQL 16 in Docker. Consolidated app DB and Hangfire storage into single PostgreSQL instance (`xafhangfire-postgres` container, port 5433). Added docker-compose.yml. Fixed pre-existing ObservableCollection bug in CRM entities. Fixed DI scoping bug (DirectJobDispatcher uses IServiceScopeFactory). Added Serilog structured logging. Fixed XAF security context for background Hangfire jobs (IJobScopeInitializer + HangfireJob service user). Configurable report output directory via `Reports:OutputDirectory` in appsettings.json.

**Session 6 (2026-02-21):** Report parameters support (Dictionary<string, string> with DateRangeResolver integration). Hangfire dashboard authorization (IDashboardAuthorizationFilter, Administrators-only in production). Job execution history tracking (JobExecutionRecord entity + IJobExecutionRecorder). Updated README with full project documentation. Created integration guide (docs/integration-guide.md). xUnit test project with 44 tests covering DateRangeResolver, JobDispatchService, DirectJobDispatcher, handlers, and LogOnlyEmailSender.

**Session 7 (2026-02-21):** Job progress reporting (IJobProgressReporter interface, XafJobProgressReporter implementation, DemoLogHandler reports progress during loop). Consecutive failure tracking (ConsecutiveFailures counter on JobDefinition, reset on success, incremented on failure, warning logged at configurable threshold).

**Session 8 (2026-02-21):** Cron expression visualization and rich parameter UI. CronHelper uses Cronos (next occurrences) + CronExpressionDescriptor (human-readable text). Two [NotMapped] computed properties on JobDefinition (CronDescription, NextScheduledRuns). CommandMetadataProvider reflects on command record constructors to discover parameters. Custom XAF Blazor property editor (JobParametersPropertyEditor) replaces raw JSON textarea with typed form fields (DxTextBox, DxSpinEdit, DxCheckBox, DxMemo). Falls back to raw JSON for unknown types. Refreshes on JobTypeName change. 19 new tests (63 total).

**Session 9 (2026-02-21):** Job UI improvements. JobTypeName now renders as a DxComboBox dropdown populated from CommandMetadataProvider. CronDescription and NextScheduledRuns refresh live when CronExpression changes (JobDefinitionRefreshController). DateTime fields (LastRunUtc, NextRunUtc, StartedUtc, CompletedUtc) display with HH:mm:ss via [ModelDefault]. System fields (LastRunMessage, ErrorMessage, ParametersJson) made read-only with [EditorBrowsable]. CommandParameterMetadata extended with DataSourceHint — enables ReportName dropdown (from ReportDataV2 DB), OutputFormat dropdown (Pdf,Xlsx), TemplateName dropdown (from EmailTemplate DB). Dictionary parameters (ReportParameters) render as key-value pair rows with add/remove buttons. 6 new tests (69 total).

**Session 10 (2026-02-21):** Execution record UI cleanup + report parameter auto-discovery + version pinning. ProgressPercent/ProgressMessage hidden from execution record views. All execution record fields made read-only (including JobDefinition nav property). Null values excluded from execution record JSON serialization. Report parameter auto-discovery implemented: when user selects a ReportName, the editor loads the XtraReport and inspects its Parameters collection to pre-populate key-value pairs. DataSourceHint changed from "KeyValue" to "ReportParameters" for report parameter fields. ReportParameterHelper updated with Description-based fallback lookup. DevExpress packages pinned from 25.2.* to 25.2.3. Added explicit PostgreSQL provider references to Win and Blazor.Server projects. Added IDesignTimeDbContextFactory for PostgreSQL Model Editor support. 1 new test (70 total).

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
- [x] CronHelper static class (Cronos + CronExpressionDescriptor) — human-readable cron descriptions and next 5 run times
- [x] CronDescription + NextScheduledRuns [NotMapped] computed properties on JobDefinition
- [x] CommandParameterMetadata record + CommandMetadataProvider — reflection-based parameter discovery from command records
- [x] JobParametersPropertyEditor — custom XAF Blazor property editor with typed form fields (DxTextBox, DxSpinEdit, DxCheckBox, DxMemo)
- [x] JobParametersFormModel + JobParametersForm.razor — ComponentModelBase pattern for XAF Blazor rendering
- [x] EditorAlias("JobParametersEditor") on ParametersJson — connects to custom property editor
- [x] JobTypeName change detection via INotifyPropertyChanged — refreshes parameter form dynamically
- [x] Falls back to raw JSON textarea for unknown command types
- [x] 63 tests total (44 original + 13 CronHelper + 6 CommandMetadataProvider)
- [x] JobTypeNamePropertyEditor — DxComboBox dropdown for JobTypeName, populated from CommandMetadataProvider.GetRegisteredTypeNames()
- [x] JobDefinitionRefreshController — live refresh of CronDescription + NextScheduledRuns when CronExpression changes
- [x] [ModelDefault("DisplayFormat", "yyyy-MM-dd HH:mm:ss")] on LastRunUtc, NextRunUtc (JobDefinition) and StartedUtc, CompletedUtc (JobExecutionRecord)
- [x] System fields made read-only: LastRunMessage (JobDefinition), ErrorMessage + ParametersJson (JobExecutionRecord)
- [x] DataSourceHint on CommandParameterMetadata — drives dropdown and key-value editor selection
- [x] ReportName dropdown from ReportDataV2.DisplayName, OutputFormat dropdown (Pdf,Xlsx), TemplateName dropdown from EmailTemplate.Name
- [x] Key-value pair editor for Dictionary parameters (add/remove rows with Key + Value textboxes)
- [x] 69 tests total (63 previous + 6 DataSourceHint tests)
- [x] ProgressPercent + ProgressMessage hidden from JobExecutionRecord detail/list views
- [x] All JobExecutionRecord system fields made read-only (JobName, JobTypeName, StartedUtc, CompletedUtc, Status, DurationMs, JobDefinition)
- [x] Null values excluded from execution record JSON (JsonIgnoreCondition.WhenWritingNull in DirectJobDispatcher + JobExecutor)
- [x] Report parameter auto-discovery from XtraReport definitions (DiscoverReportParameters in JobParametersPropertyEditor)
- [x] DataSourceHint "ReportParameters" (was "KeyValue") for report-specific key-value fields
- [x] ReportParameterHelper fallback to Description-based parameter lookup
- [x] DevExpress packages pinned to 25.2.3 (was 25.2.*)
- [x] Explicit Npgsql.EntityFrameworkCore.PostgreSQL + Microsoft.EntityFrameworkCore.Design in Win and Blazor.Server projects
- [x] DesignTimeDbContextFactory (xafhangfireDesignTimeDbContextFactory) for Model Editor support — uses XAF's base class + SqlServer package workaround
- [x] 70 tests total (69 previous + 1 SendReportEmailCommand hint test)

## Future
- [ ] Scheduler calendar view bound to JobDefinition
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
- Cron visualization uses two NuGet packages — Cronos for computing next occurrences, CronExpressionDescriptor for human-readable text. Both in Module project.
- Command metadata uses reflection on record constructors — registry dictionary maps string names to types. New commands need to be registered in `CommandMetadataProvider`.
- Custom property editor pattern — `BlazorPropertyEditorBase` + `ComponentModelBase` + Razor component. EditorAlias connects entity property to editor class.
- Parameter editor subscribes to `INotifyPropertyChanged` on the current object to detect `JobTypeName` changes and refresh the form dynamically.
- `DataSourceHint` on `CommandParameterMetadata` drives UI rendering: "Reports" and "EmailTemplates" query the DB for dropdown items, "Pdf,Xlsx" splits as static values, "KeyValue" renders add/remove key-value rows.
- `JobTypeNamePropertyEditor` uses same `BlazorPropertyEditorBase` + `ComponentModelBase` + Razor pattern as the parameter editor.
- `JobDefinitionRefreshController` listens to `ObjectSpace.ObjectChanged` — only fires for `CronExpression` changes, calls `View.FindItem().Refresh()` on computed properties.
- `IComplexViewItem` pattern used in `JobParametersPropertyEditor` to access `XafApplication` for creating ObjectSpaces needed by DB-backed dropdown queries.

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
31. `xafhangfire/xafhangfire.Module/Helpers/CronHelper.cs` — cron description + next runs helper
32. `xafhangfire/xafhangfire.Jobs/CommandMetadataProvider.cs` — reflection-based command parameter discovery
33. `xafhangfire/xafhangfire.Jobs/CommandParameterMetadata.cs` — parameter metadata record
34. `xafhangfire/xafhangfire.Blazor.Server/Editors/JobParametersPropertyEditor.cs` — custom XAF Blazor property editor
35. `xafhangfire/xafhangfire.Blazor.Server/Editors/JobParametersFormModel.cs` — component model for parameter editor
36. `xafhangfire/xafhangfire.Blazor.Server/Editors/JobParametersForm.razor` — Razor component for parameter form
37. `docs/plans/2026-02-21-cron-and-parameter-ui-design.md` — cron + parameter UI design doc
38. `xafhangfire/xafhangfire.Blazor.Server/Editors/JobTypeNamePropertyEditor.cs` — JobTypeName dropdown property editor
39. `xafhangfire/xafhangfire.Blazor.Server/Editors/JobTypeNameComboBoxModel.cs` — component model for JobTypeName dropdown
40. `xafhangfire/xafhangfire.Blazor.Server/Editors/JobTypeNameComboBox.razor` — Razor component for JobTypeName dropdown
41. `xafhangfire/xafhangfire.Blazor.Server/Controllers/JobDefinitionRefreshController.cs` — live refresh of computed properties
42. `docs/plans/2026-02-21-job-ui-improvements-plan.md` — job UI improvements plan

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
- Blazor.Server project does NOT have `<Nullable>enable</Nullable>` — don't use `string?` there either.
- Custom property editors use the `BlazorPropertyEditorBase` + `ComponentModelBase` + Razor component pattern. The `ComponentModel` properties must match the Razor component's `[Parameter]` properties by name and type.
- When adding a new command type, register it in `CommandMetadataProvider.CommandTypes` dictionary — otherwise the parameter editor falls back to raw JSON.
- The `JobParametersPropertyEditor` subscribes to `INotifyPropertyChanged` on the current object — this works because XAF EF Core entities use `ChangingAndChangedNotificationsWithOriginalValues` change tracking.
- DevExpress packages are pinned to 25.2.3 (not wildcard). Update all 4 csproj files together when changing versions.
- Both Win and Blazor.Server need explicit `Npgsql.EntityFrameworkCore.PostgreSQL` and `Microsoft.EntityFrameworkCore.Design` references — transitive deps from Module aren't enough for XAF Model Editor design-time.
- **Model Editor requires `Microsoft.EntityFrameworkCore.SqlServer` in Module project** — XAF's `DesignTimeDbContextCreator` defaults to SQL Server regardless of `EFCoreProvider=PostgreSql;` connection string. The Model Editor only loads types, never connects to a DB. Use `DesignTimeDbContextFactory<T>` (XAF's base class), not `IDesignTimeDbContextFactory<T>`.
- XtraReport `Parameter.Name` may be empty for some reports — `DiscoverReportParameters` falls back to `Description`. Debug with breakpoints if keys still empty.
- `[System.ComponentModel.DataAnnotations.Required]` must be fully qualified in Module project — conflicts with `DevExpress.ExpressApp.Model.RequiredAttribute` when both usings are present.

## Handoff Notes (Session 10 → Next Session)

**Status:** All code committed and pushed on `master` (`edbd1ae`). Session 10 addressed user testing feedback + infra fixes.

**Open issues to debug manually next session:**

1. **XAF Model Editor — FIXED (Session 11)** — Root cause: XAF's `DesignTimeDbContextCreator` defaults to SQL Server (`EFCoreMsSqlProviderReflectionHelper`) regardless of `EFCoreProvider=PostgreSql;` connection string. Fix: (a) changed factory to `DesignTimeDbContextFactory<T>` (XAF's base class, not EF Core's `IDesignTimeDbContextFactory`), (b) added `<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>` to Module csproj, (c) added `Microsoft.EntityFrameworkCore.SqlServer` v8.0.18 to Module project as design-time workaround — the Model Editor only loads type info and never connects to the database.

2. **Report parameter auto-discovery: key names are empty** — The editor correctly discovers the right NUMBER of parameters from XtraReport definitions, but `Parameter.Name` comes back empty. Added `Description` fallback but still not working. Next steps:
   - Add temporary debug logging or a breakpoint in `DiscoverReportParameters()` to inspect what `p.Name`, `p.Description`, and `p.Value` actually contain
   - Check the XtraReport design files to see how parameters are defined (Name vs Description)
   - Try iterating with `for (int i = 0; i < report.Parameters.Count; i++)` and inspect via index
   - Consider using `ToString()` or reflection to dump all Parameter properties
   - Test with a simple report that has a manually-defined parameter with an explicit Name

**What was verified working (from user testing):**
- JobTypeName dropdown works
- Report parameter auto-discovery finds the correct number of parameters
- Execution record fields are read-only
- Null values excluded from JSON serialization
- DateTime display formats with HH:mm:ss

**Key files changed in session 10:**
- `xafhangfire.Module/BusinessObjects/JobExecutionRecord.cs` — hidden progress fields, read-only system fields
- `xafhangfire.Module/BusinessObjects/xafhangfireDbContext.cs` — added IDesignTimeDbContextFactory
- `xafhangfire.Blazor.Server/Editors/JobParametersPropertyEditor.cs` — report parameter auto-discovery + ReportName change detection
- `xafhangfire.Blazor.Server/Handlers/ReportParameterHelper.cs` — Description fallback for parameter lookup
- `xafhangfire.Jobs/CommandMetadataProvider.cs` — "KeyValue" → "ReportParameters" hint
- `xafhangfire.Jobs.Tests/CommandMetadataProviderTests.cs` — updated hint tests
- All 4 project csproj files — pinned DevExpress to 25.2.3, added explicit PostgreSQL provider to UI projects
- `xafhangfire.Jobs/DirectJobDispatcher.cs` + `JobExecutor.cs` — WhenWritingNull JSON option
