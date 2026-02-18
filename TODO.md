# TODO — Job Dispatcher POC

## Current Status

**Session 1 (2026-02-18):** All 3 phases + follow-up items 1-3 implemented. Solution builds clean (0 warnings, 0 errors). RunNow action dispatches real jobs. 3 sample JobDefinitions seeded. Recurring jobs sync to Hangfire on startup.

## Phase 1: Core Plumbing — DONE
- [x] Create `xafhangfire.Jobs` class library project
- [x] Implement `IJobHandler<TCommand>` and `IJobDispatcher` interfaces
- [x] Implement `DirectJobDispatcher` and `HangfireJobDispatcher` + `JobExecutor<TCommand>`
- [x] Create `DemoLogCommand` + `DemoLogHandler`
- [x] Create `ListUsersCommand` + `ListUsersHandler`
- [x] Wire Hangfire + DI + config toggle in `Startup.cs`
- [x] `JobTestController` API endpoint for manual triggering
- [x] `AddJobHandler` / `AddJobDispatcher` DI extension methods

## Phase 2: Admin UI — DONE
- [x] `JobDefinition` business object with XAF list/detail views
- [x] `JobDispatchService` — maps JobTypeName string + JSON to command dispatch
- [x] `RunNow` action wired to dispatch real jobs (updates status on success/failure)
- [x] Seed 3 sample JobDefinitions in `Updater.cs`
- [x] `JobSyncService` — syncs enabled JobDefinitions with cron to Hangfire recurring jobs on startup

## Phase 3: Scheduler — DONE (foundation)
- [x] DevExpress Scheduler module added and registered
- [x] `DateRangeResolver` for friendly calendar terms

## Remaining Work
- [ ] Scheduler calendar view bound to JobDefinition as appointments
- [ ] Cron expression → next-run visualization
- [ ] Swap Hangfire.InMemory → SQL Server or SQLite for persistence
- [ ] Email jobs (MailKit)
- [ ] Report generation (DevExpress XtraReport)
- [ ] Mail merge (report + email composition)
- [ ] Rich parameter UI (dynamic forms from command metadata)

## Claude Continuation Instructions

When resuming this project, read these files first:
1. `CLAUDE.md` — project conventions and build commands
2. `TODO.md` — current progress (this file)
3. `docs/plans/2026-02-18-job-dispatcher-design.md` — approved design
4. `job-dispatcher-architecture.md` — original architecture reference
5. `xafhangfire/xafhangfire.Jobs/` — Jobs project (interfaces, dispatchers, handlers, JobDispatchService, DateRangeResolver)
6. `xafhangfire/xafhangfire.Module/BusinessObjects/JobDefinition.cs` — admin entity
7. `xafhangfire/xafhangfire.Module/Controllers/JobSchedulerViewController.cs` — RunNow action
8. `xafhangfire/xafhangfire.Blazor.Server/Startup.cs` — DI wiring
9. `xafhangfire/xafhangfire.Blazor.Server/Services/JobSyncService.cs` — startup sync

Then check the TODO list above to see what's done and what's next.

## Known Gotchas
- DevExpress 25.2 removed `SizeAttribute`. Use `FieldSizeAttribute` from `DevExpress.ExpressApp.DC` instead.
- Module project has no `<Nullable>enable</Nullable>` — don't use `string?` there.
- If IIS Express is running, it may lock DLLs. Kill it before rebuilding.
