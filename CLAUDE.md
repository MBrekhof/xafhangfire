# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run Commands

```bash
# Start PostgreSQL (required before running the app)
docker compose up -d

# Build entire solution
dotnet build xafhangfire/xafhangfire.slnx

# Build specific project
dotnet build xafhangfire/xafhangfire.Blazor.Server/xafhangfire.Blazor.Server.csproj
dotnet build xafhangfire/xafhangfire.Win/xafhangfire.Win.csproj
dotnet build xafhangfire/xafhangfire.Module/xafhangfire.Module.csproj

# Run the Blazor Server app (https://localhost:5001)
dotnet run --project xafhangfire/xafhangfire.Blazor.Server/xafhangfire.Blazor.Server.csproj

# Database update (schema migration)
dotnet run --project xafhangfire/xafhangfire.Blazor.Server/xafhangfire.Blazor.Server.csproj -- --updateDatabase
dotnet run --project xafhangfire/xafhangfire.Blazor.Server/xafhangfire.Blazor.Server.csproj -- --updateDatabase --forceUpdate --silent
```

```bash
# Run tests
dotnet test xafhangfire/xafhangfire.Jobs.Tests/xafhangfire.Jobs.Tests.csproj
```

`Jobs.Tests/Integration/` (TEST-001) runs against the Docker Postgres in a throwaway `xafhangfire_inttest` database, dropped and recreated each run; without Postgres those tests report Skipped.

Build configurations: Debug, Release, EasyTest.

## Architecture

This is a **DevExpress XAF (eXpressApp Framework)** application using **EF Core** with **PostgreSQL 16** (Docker). XAF provides automatic CRUD UI generation, security, reporting, and more from business object definitions.

### Project Structure

- **xafhangfire.Module** — Shared business logic layer. All business objects, DbContext, database migration (Updater), and XAF module configuration live here. Both UI projects reference this.
- **xafhangfire.Jobs** — Job dispatcher abstractions, command records, handlers, and utilities. No XAF UI dependency. Referenced by Blazor.Server.
- **xafhangfire.Blazor.Server** — Web UI (Blazor Server). Also hosts Web API endpoints (JWT auth, OData, reports), Swagger, and Hangfire dashboard.
- **xafhangfire.Win** — Desktop UI (WinForms).

### Key Files

| File | Purpose |
|------|---------|
| `Module/BusinessObjects/xafhangfireDbContext.cs` | EF Core DbContext — add `DbSet<T>` here for new entities |
| `Module/Module.cs` | XAF module registration — add `AdditionalExportedTypes` for new business objects |
| `Module/DatabaseUpdate/Updater.cs` | Seed data and schema migrations — creates default users/roles in non-Release builds |
| `Blazor.Server/Startup.cs` | DI registration, XAF module pipeline, auth config, OData/Swagger setup |
| `Blazor.Server/API/` | Web API controllers (AuthenticationController, ReportController, JobTestController) |
| `Jobs/IJobHandler.cs` | Handler interface — implement for each command type |
| `Jobs/IJobDispatcher.cs` | Dispatcher interface — DirectJobDispatcher (dev) or HangfireJobDispatcher (prod) |
| `Jobs/JobServiceCollectionExtensions.cs` | `AddJobDispatcher()` and `AddJobHandler<T,H>()` DI helpers |
| `Jobs/DateRangeResolver.cs` | Resolves friendly terms like "last-week" to DateRange |
| `Module/BusinessObjects/JobDefinition.cs` | Admin-configurable job entity (name, type, params, cron, status) |
| `Module/Controllers/JobSchedulerViewController.cs` | RunNow action for JobDefinition |

### Adding a New Business Object

1. Create entity class in `Module/BusinessObjects/`
2. Add `DbSet<T>` in `xafhangfireEFCoreDbContext`
3. Add `AdditionalExportedTypes.Add(typeof(T))` in `Module.cs` constructor
4. Optionally expose via Web API in `Startup.cs` → `webApiBuilder.ConfigureOptions`

### Job Dispatcher Pattern (Implemented)

Command/Handler + pluggable dispatcher. See `job-dispatcher-architecture.md` for rationale and `docs/plans/2026-02-18-job-dispatcher-design.md` for full design.

- **Commands** as records in `Jobs/Commands/`, **Handlers** implement `IJobHandler<TCommand>` in `Jobs/Handlers/`
- **IJobDispatcher** switches between `DirectJobDispatcher` (local dev, inline execution) and `HangfireJobDispatcher` (production, background queuing)
- Toggled via `Jobs:UseHangfire` in appsettings (currently true in both `appsettings.json` and `appsettings.Development.json`; set it false locally for inline, debuggable runs)
- Hangfire.PostgreSql runs with a 5-minute sliding invisibility timeout (JOB-008, Startup.cs): jobs a crashed or killed app was running are picked up again 5 minutes after their last keep-alive, while running jobs refresh `fetchedat` every minute so they are never handed out twice
- Hangfire dashboard at `/hangfire`, job test API at `/api/jobs/`
- Register new handlers: `services.AddJobHandler<MyCommand, MyHandler>()` in Startup.cs

### Adding a New Job

1. Create command record in `Jobs/Commands/` (e.g., `public record MyCommand(string Param);`)
2. Create handler in `Jobs/Handlers/` implementing `IJobHandler<MyCommand>`
3. Register in `Blazor.Server/Startup.cs`: `services.AddJobHandler<MyCommand, MyHandler>()`
4. Let the job's cancellation propagate (JOB-014): a catch-all around awaited work needs
   `when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)`, and a loop that swallows
   per-item errors ends with `cancellationToken.ThrowIfCancellationRequested()`. A swallowed cancellation is recorded as
   Success instead of interrupted. Don't leave half-written output files behind (see `GenerateReportHandler`).

### DevExpress 26.1 Gotchas

- `FrameworkSettingsCompatibilityMode.Latest` hashes passwords with SHA512-600K only; both Program.cs files call `PasswordHashing.UseMixed()` (Module/Helpers) right after it so existing SHA1-20K hashes keep verifying.
- `WinApplication.UseOldTemplates` no longer exists.

- `SizeAttribute` was removed. Use `FieldSizeAttribute` from `DevExpress.ExpressApp.DC` with `FieldSizeAttribute.Unlimited`.
- **Model Editor + PostgreSQL:** XAF's `DesignTimeDbContextCreator` defaults to SQL Server (`EFCoreMsSqlProviderReflectionHelper`) regardless of the `EFCoreProvider=PostgreSql;` connection string prefix. The Module project must reference `Microsoft.EntityFrameworkCore.SqlServer` as a design-time workaround — the Model Editor only loads type info and never actually connects to the database. The design-time factory uses `DesignTimeDbContextFactory<T>` (from `DevExpress.ExpressApp.EFCore.DesignTime`), not EF Core's raw `IDesignTimeDbContextFactory<T>`.
  That package also pulls in `Microsoft.Data.SqlClient` → a vulnerable `System.Security.Cryptography.Pkcs`, so Module
  pins Pkcs 10.0.14 (SEC-001); drop the pin together with the workaround.
- `Jobs.Tests` has a `FrameworkReference` to `Microsoft.AspNetCore.App` (SEC-001): it runs Blazor.Server code, and the
  reference lets NuGet prune packages the framework ships (a vulnerable `System.Security.Cryptography.Xml`). Don't add
  `Microsoft.Extensions.*` PackageReferences there (NU1510).
- A `NullReferenceException` at `MainPageBase.get_Application` / `OnAfterRenderAsync` in the log is DevExpress-internal
  noise when a page is left mid-render (UPG-002, closed by-design); nothing user-visible.

### Security Model

- `ApplicationUser` extends `PermissionPolicyUser` (supports OAuth via `ApplicationUserLoginInfo`)
- Role-based access control via `PermissionPolicyRole` with object/member/type-level permissions
- Permissions reload mode: `NoCache` (fresh permissions on each DbContext access)
- JWT auth for Web API, Cookie auth for Blazor UI
- Default dev users: "User" (Default role), "Admin" (Administrators role) — empty passwords, non-Release only

### Database

- **PostgreSQL 16** in Docker (`xafhangfire-postgres` container, port 5433)
- `docker-compose.yml` at repo root — `docker compose up -d` to start
- Single database `xafhangfire` holds both app tables and Hangfire tables
- Connection strings use `EFCoreProvider=PostgreSql;` prefix for XAF auto-detection
- **No `Npgsql.EnableLegacyTimestampBehavior`** (dropped in UPG-001: it broke Hangfire.PostgreSql's `fetchedat` matching). Instead `xafhangfireEFCoreDbContext.ConfigureConventions` maps every `DateTime`/`DateTime?` to `timestamp without time zone` with `UnspecifiedKindConverter` (Kind dropped on write), so Utc, Local and Unspecified values all store as written. Don't put `DateTime.UtcNow`/`Now` *inside* a LINQ predicate against these columns (translates to `now()`, a timestamptz, and throws) — capture it in a variable first. `Upg001Tests` fails if a DateTime property escapes the convention.

### EF Core Configuration

The DbContext uses several XAF-specific EF Core conventions:
- `UseDeferredDeletion` — soft delete support
- `UseOptimisticLock` — concurrency control
- `ChangingAndChangedNotificationsWithOriginalValues` — change tracking strategy for XAF proxies (navigation collections MUST use `ObservableCollection<T>`)
- `PreferFieldDuringConstruction` — property access mode for proxy compatibility

## Tech Stack

- **.NET 10** (`net10.0`, Win `net10.0-windows`), **DevExpress XAF 26.1.6**, **EF Core 10.0.14**, **Npgsql EF 10.0.3**
- **PostgreSQL 16** via Docker (`localhost:5433`)
- **Blazor Server** + **SignalR** for web UI
- **Swagger/OpenAPI** at `/swagger` (dev only)
- **OData v4.01** at `/api/odata`
