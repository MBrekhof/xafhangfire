# Job Dispatcher Architecture — POC Design

**Date:** 2026-02-18
**Status:** Approved
**Scope:** POC / MOTTA — prove the pattern, build the admin UI

## Problem

ViewControllers in XAF directly enqueue Hangfire jobs. Local dev requires either stopping the
server Hangfire service or risking conflicts. Debugging through Hangfire is painful. Job logic
is coupled to the execution strategy. Admins have no way to configure job parameters or schedules.

## Solution

Command/Handler pattern with pluggable dispatcher, backed by a persistent `JobDefinition` entity
and DevExpress Scheduler UI for admin configuration.

## Architecture

### Core Abstractions (xafhangfire.Jobs — new class library, no XAF dependency)

```
IJobHandler<TCommand>        — executes business logic for a command
IJobDispatcher               — dispatches commands (direct or via Hangfire)
DirectJobDispatcher          — resolves handler from DI, executes inline (local dev)
HangfireJobDispatcher        — enqueues to Hangfire (production)
JobExecutor<TCommand>        — Hangfire entry point, resolves and calls handler
```

### Data Model (xafhangfire.Module)

```
JobDefinition (XAF Business Object)
├── Name                : string        "Daily Customer Sync"
├── JobTypeName         : string        "SyncCustomerCommand"
├── ParametersJson      : string        '{"CustomerId":"AAWATER","FullSync":true}'
├── CronExpression      : string        "0 7 * * *"
├── IsEnabled           : bool
├── LastRunUtc          : DateTime?
├── NextRunUtc          : DateTime?     (computed from cron)
├── LastRunStatus       : enum          Success / Failed / Running
├── LastRunMessage      : string?
```

### Config Toggle

```json
{ "Jobs": { "UseHangfire": false } }   // local dev — inline execution
{ "Jobs": { "UseHangfire": true } }    // production — Hangfire queuing
```

### Hangfire Storage

Hangfire.InMemory for POC (zero setup). Swap to SQL Server for production — one-line change.

### UI Layers

1. **ListView** — table of all JobDefinitions with status, next run, last result
2. **DetailView** — edit job type, JSON parameters, cron expression, enable/disable
3. **Scheduler View** — DevExpress Scheduler showing jobs as recurring calendar appointments

### Parameter Handling

- Parameters stored as JSON in `ParametersJson`
- Calendar-relative terms ("last-week", "next-month") stored as strings, resolved at execution time
  by `DateRangeResolver` in the Jobs project
- Future: parameter metadata attributes on command records to drive dynamic forms

## Sample Jobs (POC)

1. **DemoLogCommand** — logs a message, sleeps, proves dispatcher plumbing
2. **ListUsersCommand** — reads users via `IObjectSpaceFactory`, proves XAF data access in handlers

## Build Phases

| Phase | Deliverable |
|-------|-------------|
| 1     | xafhangfire.Jobs project + interfaces + dispatchers + 2 sample handlers |
| 2     | JobDefinition entity + XAF list/detail views + Hangfire wiring in Startup |
| 3     | Scheduler view + cron visualization + friendly date terms |

## Future Extensions (not in POC)

- **Email jobs** — `SendEmailCommand` + MailKit handler
- **Report generation** — `GenerateReportCommand` + DevExpress XtraReport handler
- **Mail merge** — `MailMergeCommand` composing report + email handlers
- **Rich parameter UI** — dynamic forms driven by command metadata attributes

Adding any new job type = new command record + handler + DI registration. No dispatcher or UI changes.

## Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Single Jobs project | Yes | POC simplicity; split later if needed |
| Hangfire.InMemory | For POC | Zero setup, swap to SQL Server later |
| Hangfire embedded in Blazor.Server | Yes | No separate service for POC |
| JSON parameters | For now | Flexible, works for any command type |
| Calendar terms resolved at runtime | Yes | Stored config stays stable |
