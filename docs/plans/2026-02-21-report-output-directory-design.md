# Design: Configurable Report Output Directory

**Date:** 2026-02-21
**Status:** Approved

## Problem

Generated reports land in `Directory.GetCurrentDirectory()/reports/` (inside the Blazor.Server project dir). No way to configure the output path without changing code. Temp report files for email attachments go to `Path.GetTempPath()`.

## Fix

Add `Reports:OutputDirectory` to appsettings.json with `IOptions<ReportOptions>` pattern.

## Changes

1. `Jobs/ReportOptions.cs` — options record with `OutputDirectory` property (default: `"reports"`)
2. `Blazor.Server/Handlers/GenerateReportHandler.cs` — inject `IOptions<ReportOptions>`, use configured directory
3. `Blazor.Server/Handlers/SendReportEmailHandler.cs` — inject `IOptions<ReportOptions>`, use configured directory for temp files
4. `Blazor.Server/Startup.cs` — bind `ReportOptions` from config
5. `Blazor.Server/appsettings.json` — add `Reports:OutputDirectory` section
6. `.gitignore` — add `reports/` and `.mcp.json`
