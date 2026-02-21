# Design: Migrate Application Database to PostgreSQL

**Date:** 2026-02-21
**Status:** Approved

## Summary

Migrate the XAF application database from SQL Server LocalDB to PostgreSQL 16 running in Docker Desktop. Consolidate the application database and Hangfire storage into a single PostgreSQL instance and database. Fresh start — no data migration needed (seed data recreates on first run).

## Current State

| Component | Provider | Location |
|-----------|----------|----------|
| App DB | SQL Server LocalDB | `(localdb)\mssqllocaldb`, catalog `xafhangfire` |
| Hangfire DB | PostgreSQL | `duetgpt-postgres` Docker container, `hangfire` database |

## Target State

| Component | Provider | Location |
|-----------|----------|----------|
| App DB | PostgreSQL 16 | `xafhangfire-postgres` Docker container, `xafhangfire` database |
| Hangfire DB | PostgreSQL 16 | Same container, same database (separate `hangfire` schema) |

```
Docker Desktop
└── xafhangfire-postgres (PostgreSQL 16, port 5433)
    └── Database: xafhangfire
        ├── public schema — App tables (EF Core / XAF)
        └── hangfire schema — Hangfire tables
```

## Changes Required

### 1. docker-compose.yml (new file, repo root)

PostgreSQL 16 container:
- Container name: `xafhangfire-postgres`
- Port: 5433 (avoids conflict with existing PostgreSQL on 5432)
- Volume: `xafhangfire-pgdata` for persistence
- Credentials: `xafhangfire` / `xafhangfire`
- Database: `xafhangfire`

### 2. NuGet Package Changes

**Module project:**
- Add: `Npgsql.EntityFrameworkCore.PostgreSQL` (8.0.x, matching EF Core version)
- Remove: `Microsoft.EntityFrameworkCore.SqlServer`
- Remove: `Microsoft.Data.SqlClient`

**Blazor.Server project:**
- No new packages (already has `Hangfire.PostgreSql`)

### 3. Connection Strings (appsettings.json)

Single PostgreSQL connection string used by both app and Hangfire:
```
Host=localhost;Port=5433;Database=xafhangfire;Username=xafhangfire;Password=xafhangfire
```

Remove separate `HangfireConnection`. Remove `EasyTestConnectionString` (SQL Server specific).

### 4. Startup.cs

- Update XAF EF Core config to use `UseNpgsql()` explicitly
- Point Hangfire to same connection string as app

### 5. DbContext

- May need minor adjustments for PostgreSQL compatibility (column type mappings, naming conventions)
- XAF's `UseConnectionString()` extension auto-detects Npgsql if the provider is referenced, but explicit `UseNpgsql()` is more reliable

### 6. Win Project

- Update connection string reference if it has one (WinForms app shares Module)

## What Stays the Same

- All business objects, handlers, jobs, seed data — no changes
- XAF auto-creates schema on first run via `--updateDatabase`
- Development vs Production dispatcher toggle
- Job command/handler registrations

## Risk Assessment

**Low risk.** XAF + EF Core officially support PostgreSQL. This is a POC with seed data that auto-creates. No production data to migrate.

## Success Criteria

1. `docker compose up -d` starts PostgreSQL container
2. `dotnet build xafhangfire.slnx` compiles clean
3. `dotnet run --project ... -- --updateDatabase` creates schema in PostgreSQL
4. Application starts, seed data appears, RunNow dispatches jobs
5. Hangfire dashboard shows jobs in the same PostgreSQL database
