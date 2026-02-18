# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run Commands

```bash
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

There are no test projects yet. Build configurations: Debug, Release, EasyTest.

## Architecture

This is a **DevExpress XAF (eXpressApp Framework)** application using **EF Core** with SQL Server (LocalDB). XAF provides automatic CRUD UI generation, security, reporting, and more from business object definitions.

### Project Structure

- **xafhangfire.Module** — Shared business logic layer. All business objects, DbContext, database migration (Updater), and XAF module configuration live here. Both UI projects reference this.
- **xafhangfire.Blazor.Server** — Web UI (Blazor Server). Also hosts Web API endpoints (JWT auth, OData, reports) and Swagger.
- **xafhangfire.Win** — Desktop UI (WinForms).

### Key Files

| File | Purpose |
|------|---------|
| `Module/BusinessObjects/xafhangfireDbContext.cs` | EF Core DbContext — add `DbSet<T>` here for new entities |
| `Module/Module.cs` | XAF module registration — add `AdditionalExportedTypes` for new business objects |
| `Module/DatabaseUpdate/Updater.cs` | Seed data and schema migrations — creates default users/roles in non-Release builds |
| `Blazor.Server/Startup.cs` | DI registration, XAF module pipeline, auth config, OData/Swagger setup |
| `Blazor.Server/API/` | Web API controllers (AuthenticationController, ReportController) |

### Adding a New Business Object

1. Create entity class in `Module/BusinessObjects/`
2. Add `DbSet<T>` in `xafhangfireEFCoreDbContext`
3. Add `AdditionalExportedTypes.Add(typeof(T))` in `Module.cs` constructor
4. Optionally expose via Web API in `Startup.cs` → `webApiBuilder.ConfigureOptions`

### Job Dispatcher Pattern

See `job-dispatcher-architecture.md` for the planned Command/Handler + pluggable dispatcher architecture:
- **Commands** as records, **Handlers** implement `IJobHandler<TCommand>`
- **IJobDispatcher** switches between `DirectJobDispatcher` (local dev, inline execution) and `HangfireJobDispatcher` (production, background queuing)
- Toggled via `Jobs:UseHangfire` in appsettings

### Security Model

- `ApplicationUser` extends `PermissionPolicyUser` (supports OAuth via `ApplicationUserLoginInfo`)
- Role-based access control via `PermissionPolicyRole` with object/member/type-level permissions
- Permissions reload mode: `NoCache` (fresh permissions on each DbContext access)
- JWT auth for Web API, Cookie auth for Blazor UI
- Default dev users: "User" (Default role), "Admin" (Administrators role) — empty passwords, non-Release only

### EF Core Configuration

The DbContext uses several XAF-specific EF Core conventions:
- `UseDeferredDeletion` — soft delete support
- `UseOptimisticLock` — concurrency control
- `ChangingAndChangedNotificationsWithOriginalValues` — change tracking strategy for XAF proxies
- `PreferFieldDuringConstruction` — property access mode for proxy compatibility

## Tech Stack

- **.NET 8.0**, **DevExpress XAF 25.2.x**, **EF Core 8.0.18**
- **SQL Server LocalDB** (`(localdb)\mssqllocaldb`)
- **Blazor Server** + **SignalR** for web UI
- **Swagger/OpenAPI** at `/swagger` (dev only)
- **OData v4.01** at `/api/odata`
