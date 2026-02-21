# Job Progress & Error Notifications Design

**Date:** 2026-02-21
**Goal:** Add progress reporting for long-running jobs and consecutive failure tracking on JobDefinition.

## Feature 1: Job Progress Reporting

**Interface:** `IJobProgressReporter` in Jobs project.
**Implementation:** `XafJobProgressReporter` in Blazor.Server — updates `JobExecutionRecord` via `INonSecuredObjectSpaceFactory`.
**Opt-in:** Handlers accept `IJobProgressReporter` as constructor parameter. DemoLogHandler demonstrates usage.

### Changes
- `Jobs/IJobProgressReporter.cs` — `Task ReportAsync(int percent, string? message)`
- `Module/BusinessObjects/JobExecutionRecord.cs` — add `ProgressPercent`, `ProgressMessage`
- `Blazor.Server/Services/XafJobProgressReporter.cs` — writes progress to record
- `Jobs/JobExecutor.cs` — register `IJobProgressReporter` scoped to current record ID
- `Jobs/DirectJobDispatcher.cs` — same
- `Jobs/Handlers/DemoLogHandler.cs` — report progress during loop

## Feature 2: Consecutive Failure Tracking

**Approach:** `XafJobExecutionRecorder` updates `JobDefinition` status fields after every run. Tracks `ConsecutiveFailures` counter, logs warning when threshold exceeded.

### Changes
- `Module/BusinessObjects/JobDefinition.cs` — add `ConsecutiveFailures` (int)
- `Blazor.Server/Services/XafJobExecutionRecorder.cs` — update JobDefinition on completion/failure
- `Blazor.Server/appsettings.json` — `Jobs:FailureAlertThreshold` (default: 3)
