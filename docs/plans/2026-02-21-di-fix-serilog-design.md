# Design: Fix DI Scoping + Add Serilog

**Date:** 2026-02-21
**Status:** Approved

## Problem 1: DI Scoping Bug

`DirectJobDispatcher` is registered as singleton and resolves handlers from the root `IServiceProvider`. Handlers that depend on scoped XAF services (`INonSecuredObjectSpaceFactory`) fail with "Cannot resolve from root provider".

**Fix:** Use `IServiceScopeFactory` and create a scope per dispatch.

## Problem 2: No Structured Logging

App uses default Microsoft console logging only. No file output, no structured logs.

**Fix:** Add Serilog with console + rolling file sinks.

## Changes

1. `DirectJobDispatcher.cs` — inject `IServiceScopeFactory`, create scope per dispatch
2. `Blazor.Server.csproj` — add `Serilog.AspNetCore` package
3. `Program.cs` — configure Serilog as the logging provider
4. `appsettings.json` — add Serilog config section (console + file)
