# Design: Fix XAF Security Context for Background Hangfire Jobs

**Date:** 2026-02-21
**Status:** Approved

## Problem

Report-related Hangfire jobs (`GenerateReportCommand`, `SendReportEmailCommand`) fail with:
```
UserFriendlySecurityException: The user name must not be empty.
```

Root cause: `IReportExportService.LoadReport` internally calls `SecuredObjectSpaceFactory.CreateObjectSpace()` which requires an authenticated user. Background Hangfire threads have no authenticated user context.

## Fix

1. **`IJobScopeInitializer`** interface in Jobs project — called before each job handler runs
2. **`XafJobScopeInitializer`** in Blazor.Server — uses XAF's `UserManager` + `SignInManager` to authenticate as a "HangfireJob" service user in the current scope
3. **"HangfireJob" user** with "BackgroundJobs" role (read-only access to report data types)
4. Both `DirectJobDispatcher` and `JobExecutor` call the initializer before executing handlers

## Changes

1. `Jobs/IJobScopeInitializer.cs` — new interface
2. `Blazor.Server/Services/XafJobScopeInitializer.cs` — XAF implementation
3. `Jobs/DirectJobDispatcher.cs` — call initializer after creating scope
4. `Jobs/JobExecutor.cs` — call initializer before handler
5. `Module/DatabaseUpdate/Updater.cs` — add BackgroundJobs role + HangfireJob user
6. `Blazor.Server/Startup.cs` — register IJobScopeInitializer as scoped
