# TODO — Job Dispatcher POC

## Current Status

**Session 1 (2026-02-18):** Design approved. Git repo initialized. Starting implementation.

## Phase 1: Core Plumbing
- [ ] Create `xafhangfire.Jobs` class library project
- [ ] Implement `IJobHandler<TCommand>` interface
- [ ] Implement `IJobDispatcher` interface
- [ ] Implement `DirectJobDispatcher`
- [ ] Implement `HangfireJobDispatcher` + `JobExecutor<TCommand>`
- [ ] Create `DemoLogCommand` + `DemoLogHandler`
- [ ] Create `ListUsersCommand` + `ListUsersHandler`
- [ ] Add Hangfire NuGet packages to Blazor.Server
- [ ] Wire DI registration + config toggle in `Startup.cs`
- [ ] Build and verify both dispatcher modes work

## Phase 2: Admin UI
- [ ] Create `JobDefinition` business object in Module
- [ ] Add `DbSet<JobDefinition>` to DbContext
- [ ] Register in Module.cs
- [ ] XAF ListView + DetailView for JobDefinition
- [ ] Wire JobDefinition → Hangfire recurring job sync
- [ ] `JobTestController` API endpoint for manual triggering

## Phase 3: Scheduler
- [ ] DevExpress Scheduler view bound to JobDefinition
- [ ] Cron expression visualization
- [ ] Friendly date term resolution (`DateRangeResolver`)
- [ ] Calendar-relative parameter terms (last-week, next-month, etc.)

## Future (not in POC)
- [ ] Email jobs (MailKit)
- [ ] Report generation (DevExpress XtraReport)
- [ ] Mail merge (report + email composition)
- [ ] Rich parameter UI (dynamic forms from command metadata)
- [ ] Swap Hangfire.InMemory → SQL Server storage

## Claude Continuation Instructions

When resuming this project, read these files first:
1. `CLAUDE.md` — project conventions and build commands
2. `TODO.md` — current progress (this file)
3. `docs/plans/2026-02-18-job-dispatcher-design.md` — approved design
4. `job-dispatcher-architecture.md` — original architecture reference
5. `xafhangfire/xafhangfire.Jobs/` — the new Jobs project (if it exists)
6. `xafhangfire/xafhangfire.Module/BusinessObjects/JobDefinition.cs` — (if it exists)
7. `xafhangfire/xafhangfire.Blazor.Server/Startup.cs` — DI wiring

Then check the TODO list above to see what's done and what's next.
