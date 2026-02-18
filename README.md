# xafhangfire

DevExpress XAF application with a pluggable job dispatcher architecture for background task execution via Hangfire.

## Overview

This project demonstrates a **Command/Handler + Pluggable Dispatcher** pattern that separates job business logic from execution strategy. Jobs can run inline (for local development/debugging) or be queued through Hangfire (for production), toggled by configuration.

## Projects

| Project | Description |
|---------|-------------|
| `xafhangfire.Module` | Shared business logic, EF Core entities, XAF module configuration |
| `xafhangfire.Blazor.Server` | Blazor Server web UI + Web API (JWT, OData, Swagger) |
| `xafhangfire.Win` | WinForms desktop UI |
| `xafhangfire.Jobs` | Job dispatcher abstractions, command records, handlers |

## Tech Stack

- .NET 8.0, DevExpress XAF 25.2, EF Core 8.0
- Hangfire with in-memory storage (POC) / SQL Server (production)
- SQL Server LocalDB for application data

## Getting Started

```bash
# Build
dotnet build xafhangfire/xafhangfire.slnx

# Run Blazor Server (https://localhost:5001)
dotnet run --project xafhangfire/xafhangfire.Blazor.Server/xafhangfire.Blazor.Server.csproj

# Update database schema
dotnet run --project xafhangfire/xafhangfire.Blazor.Server/xafhangfire.Blazor.Server.csproj -- --updateDatabase
```

Default dev users: `Admin` and `User` (empty passwords).

## Job Dispatcher

Toggle between direct (inline) and Hangfire execution:

```json
{ "Jobs": { "UseHangfire": false } }   // local dev — breakpoints work
{ "Jobs": { "UseHangfire": true } }    // production — async via Hangfire
```

See [job-dispatcher-architecture.md](job-dispatcher-architecture.md) and [design doc](docs/plans/2026-02-18-job-dispatcher-design.md) for full details.

## Status

See [TODO.md](TODO.md) for current progress and next steps.
