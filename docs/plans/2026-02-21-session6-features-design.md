# Session 6 Features Design

**Date:** 2026-02-21
**Goal:** Add report parameters, Hangfire dashboard auth, job execution history, and project documentation.

---

## Feature 1: Report Parameters

**Problem:** Reports are generated without any filter parameters. Users want to pass parameters like date ranges, organization names, or other filters to reports.

**Approach:** Add an optional `Dictionary<string, string>` to report commands. Handlers pass these to the XtraReport's `Parameters` collection. The existing `DateRangeResolver` handles friendly date terms.

### Changes

**`Jobs/Commands/GenerateReportCommand.cs`** — Add `ReportParameters` dictionary:
```csharp
public record GenerateReportCommand(
    string ReportName,
    string OutputFormat = "Pdf",
    string? OutputPath = null,
    Dictionary<string, string>? ReportParameters = null);
```

**`Jobs/Commands/SendReportEmailCommand.cs`** — Same addition:
```csharp
public record SendReportEmailCommand(
    string ReportName,
    string Recipients,
    string OutputFormat = "Pdf",
    string? Subject = null,
    string? BodyText = null,
    Dictionary<string, string>? ReportParameters = null);
```

**`Blazor.Server/Handlers/GenerateReportHandler.cs`** — Apply parameters to report before export:
```csharp
ApplyReportParameters(report, command.ReportParameters);
```

Helper method resolves DateRangeResolver terms and sets XtraReport parameters.

**`Blazor.Server/Handlers/SendReportEmailHandler.cs`** — Same parameter application.

**Seed data** — Update report JobDefinition seeds to include sample ReportParameters demonstrating date range usage.

---

## Feature 2: Hangfire Dashboard Auth

**Problem:** The Hangfire dashboard at `/hangfire` is open to anyone with no access control.

**Approach:** Implement `IDashboardAuthorizationFilter`. In development, allow all authenticated users. In production, require Administrators role. Unauthenticated users are always denied.

### Changes

**`Blazor.Server/Services/HangfireDashboardAuthFilter.cs`** (new):
- Implements `IDashboardAuthorizationFilter`
- Checks `HttpContext.User` for authentication and "Administrators" role
- In development mode, allows any authenticated user

**`Blazor.Server/Startup.cs`**:
- Pass `DashboardOptions` with auth filter to `UseHangfireDashboard()`

---

## Feature 3: Job Execution History

**Problem:** Job run history is only visible in Hangfire dashboard or logs. Need a first-class XAF entity for browsing, filtering, and auditing job runs.

**Approach:** New `JobExecutionRecord` entity with navigation from `JobDefinition`. A recording decorator wraps job execution in both `DirectJobDispatcher` and `JobExecutor`.

### Changes

**`Module/BusinessObjects/JobExecutionRecord.cs`** (new):
```csharp
public class JobExecutionRecord
{
    Guid Id
    string JobName          // Job name (denormalized for history)
    string JobTypeName      // Command type name
    DateTime StartedUtc
    DateTime? CompletedUtc
    JobRunStatus Status     // Reuse existing enum
    string ErrorMessage     // Stack trace on failure
    long DurationMs         // Computed from Started/Completed
    string ParametersJson   // Snapshot of parameters used
}
```

**`Module/BusinessObjects/JobDefinition.cs`** — Add `ObservableCollection<JobExecutionRecord> ExecutionHistory` navigation.

**`Module/BusinessObjects/xafhangfireDbContext.cs`** — Add `DbSet<JobExecutionRecord>`.

**`Module/Module.cs`** — Add `AdditionalExportedTypes`.

**`Jobs/IJobExecutionRecorder.cs`** (new) — Interface for recording job start/complete/fail.

**`Blazor.Server/Services/XafJobExecutionRecorder.cs`** (new) — Writes records via `INonSecuredObjectSpaceFactory`.

**`Jobs/JobExecutor.cs`** — Wrap handler execution with recorder calls.

**`Jobs/DirectJobDispatcher.cs`** — Same wrapper.

**`Blazor.Server/Startup.cs`** — Register `IJobExecutionRecorder`.

**`Module/DatabaseUpdate/Updater.cs`** — Add `JobExecutionRecord` read permissions to BackgroundJobs role.

---

## Feature 4: README + Integration Guide

**`README.md`** — Project overview, architecture diagram (ASCII), setup instructions, feature list.

**`docs/integration-guide.md`** — Step-by-step guide for integrating the job dispatcher pattern into an existing DevExpress XAF solution.

---

## Architecture Decisions

- Report parameters use `Dictionary<string, string>` (not strongly typed) because parameters vary per report and are stored as JSON in `ParametersJson`.
- Job execution records use `INonSecuredObjectSpaceFactory` to write (not secured) because the recorder runs in background job context.
- Hangfire auth filter uses ASP.NET Core `HttpContext` claims, not XAF's security system, because the dashboard is middleware-level.
- Execution recorder is in the Jobs project as an interface, with XAF implementation in Blazor.Server, following the existing pattern (like `IJobScopeInitializer`).
