# PostgreSQL Migration Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Migrate the XAF application database from SQL Server LocalDB to PostgreSQL 16 in Docker, consolidating app and Hangfire into one database.

**Architecture:** XAF's `UseConnectionString()` extension auto-detects the EF Core provider via the `EFCoreProvider=PostgreSql` prefix in the connection string. We swap NuGet packages, update connection strings, and point Hangfire at the same database. Docker Compose provides the PostgreSQL container.

**Tech Stack:** PostgreSQL 16, Docker Compose, Npgsql.EntityFrameworkCore.PostgreSQL 8.0.x, Hangfire.PostgreSql (already installed)

---

### Task 1: Create docker-compose.yml

**Files:**
- Create: `docker-compose.yml` (repo root)

**Step 1: Create docker-compose.yml**

```yaml
services:
  postgres:
    image: postgres:16
    container_name: xafhangfire-postgres
    environment:
      POSTGRES_USER: xafhangfire
      POSTGRES_PASSWORD: xafhangfire
      POSTGRES_DB: xafhangfire
    ports:
      - "5433:5432"
    volumes:
      - xafhangfire-pgdata:/var/lib/postgresql/data
    restart: unless-stopped

volumes:
  xafhangfire-pgdata:
```

**Step 2: Start the container**

Run: `docker compose up -d`
Expected: Container `xafhangfire-postgres` starts, PostgreSQL listening on port 5433.

**Step 3: Verify connectivity**

Run: `docker exec xafhangfire-postgres psql -U xafhangfire -c "SELECT version();"`
Expected: PostgreSQL 16.x version string.

**Step 4: Commit**

```bash
git add docker-compose.yml
git commit -m "infra: add docker-compose for PostgreSQL 16 container"
```

---

### Task 2: Swap NuGet packages in Module project

**Files:**
- Modify: `xafhangfire/xafhangfire.Module/xafhangfire.Module.csproj`

**Step 1: Remove SQL Server packages, add Npgsql**

In `xafhangfire.Module.csproj`, replace:
```xml
<PackageReference Include="Microsoft.Data.SqlClient" Version="6.1.2" />
<PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="8.0.18" />
```

With:
```xml
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.0.11" />
```

Keep `Microsoft.EntityFrameworkCore.InMemory` and `Microsoft.EntityFrameworkCore.Design` — they're still needed.

**Step 2: Restore packages**

Run: `dotnet restore xafhangfire.slnx`
Expected: Restore succeeds with no errors.

**Step 3: Commit**

```bash
git add xafhangfire/xafhangfire.Module/xafhangfire.Module.csproj
git commit -m "deps: swap SqlServer EF Core provider for Npgsql PostgreSQL"
```

---

### Task 3: Update connection strings

**Files:**
- Modify: `xafhangfire/xafhangfire.Blazor.Server/appsettings.json`
- Modify: `xafhangfire/xafhangfire.Win/App.config`

**Step 1: Update Blazor Server appsettings.json**

Replace the `ConnectionStrings` section:

```json
"ConnectionStrings": {
    "ConnectionString": "EFCoreProvider=PostgreSql;Host=localhost;Port=5433;Database=xafhangfire;Username=xafhangfire;Password=xafhangfire;Persist Security Info=True",
    "EasyTestConnectionString": "EFCoreProvider=PostgreSql;Host=localhost;Port=5433;Database=xafhangfire_easytest;Username=xafhangfire;Password=xafhangfire;Persist Security Info=True"
}
```

Notes:
- `EFCoreProvider=PostgreSql` prefix tells XAF which EF Core provider to use.
- `Persist Security Info=True` enables XAF's internal MARS workaround for non-MARS databases.
- `HangfireConnection` removed — Hangfire will use the same `ConnectionString`.

**Step 2: Update Win App.config**

Replace the `connectionStrings` section:

```xml
<connectionStrings>
    <add name="EasyTestConnectionString" connectionString="EFCoreProvider=PostgreSql;Host=localhost;Port=5433;Database=xafhangfire_easytest;Username=xafhangfire;Password=xafhangfire;Persist Security Info=True" />
    <add name="ConnectionString" connectionString="EFCoreProvider=PostgreSql;Host=localhost;Port=5433;Database=xafhangfire;Username=xafhangfire;Password=xafhangfire;Persist Security Info=True" />
</connectionStrings>
```

**Step 3: Commit**

```bash
git add xafhangfire/xafhangfire.Blazor.Server/appsettings.json xafhangfire/xafhangfire.Win/App.config
git commit -m "config: update connection strings to PostgreSQL on port 5433"
```

---

### Task 4: Update Startup.cs — Hangfire + Npgsql timestamp fix

**Files:**
- Modify: `xafhangfire/xafhangfire.Blazor.Server/Startup.cs`

**Step 1: Add Npgsql legacy timestamp switch**

At the very top of `ConfigureServices`, before any other code, add:

```csharp
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
```

This prevents `Cannot write DateTime with Kind=Unspecified to PostgreSQL type 'timestamp with time zone'` errors. XAF and DevExpress entities use `DateTime` (not `DateTimeOffset`), which Npgsql 6+ rejects by default.

**Step 2: Update Hangfire configuration**

Replace the Hangfire section (lines 218-237) so it uses the main connection string instead of a separate `HangfireConnection`:

```csharp
// Hangfire
var connectionString = Configuration.GetConnectionString("ConnectionString");
// Strip EFCoreProvider prefix — Hangfire.PostgreSql needs a raw Npgsql connection string
var hangfireConnectionString = StripEFCoreProvider(connectionString);
services.AddHangfire(config =>
{
    config
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings();

    if (!string.IsNullOrEmpty(hangfireConnectionString))
    {
        config.UsePostgreSqlStorage(options =>
            options.UseNpgsqlConnection(hangfireConnectionString));
    }
    else
    {
        config.UseInMemoryStorage();
    }
});
services.AddHangfireServer();
```

**Step 3: Add StripEFCoreProvider helper**

Add a private static method to the `Startup` class:

```csharp
private static string StripEFCoreProvider(string connectionString)
{
    if (string.IsNullOrEmpty(connectionString))
        return connectionString;

    // Remove the "EFCoreProvider=PostgreSql;" prefix that XAF uses for provider detection
    // Hangfire.PostgreSql needs a raw Npgsql connection string without this prefix
    return System.Text.RegularExpressions.Regex.Replace(
        connectionString,
        @"EFCoreProvider\s*=\s*[^;]+;\s*",
        "",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
}
```

**Step 4: Remove Hangfire.InMemory using if no longer needed**

Remove from the top of the file:
```csharp
using Hangfire.InMemory;
```

Wait — keep the `using Hangfire.InMemory;` since the in-memory fallback is still in the else branch. Only remove it if we remove the fallback entirely. For now, keep it for safety.

**Step 5: Commit**

```bash
git add xafhangfire/xafhangfire.Blazor.Server/Startup.cs
git commit -m "feat: point Hangfire at shared PostgreSQL connection, add timestamp fix"
```

---

### Task 5: Build and verify

**Step 1: Build the solution**

Run: `dotnet build xafhangfire.slnx`
Expected: Build succeeded with 0 errors.

**Step 2: Start the PostgreSQL container (if not already running)**

Run: `docker compose up -d`

**Step 3: Run database update**

Run: `dotnet run --project xafhangfire/xafhangfire.Blazor.Server/xafhangfire.Blazor.Server.csproj -- --updateDatabase --forceUpdate --silent`
Expected: Schema created in PostgreSQL. No errors.

**Step 4: Verify tables exist**

Run: `docker exec xafhangfire-postgres psql -U xafhangfire -c "\dt"`
Expected: List of tables including `ModelDifferences`, `Roles`, `Users`, `JobDefinitions`, `Organizations`, `Contacts`, `Projects`, `ProjectTasks`, `EmailTemplates`, etc.

**Step 5: Commit (if any fixups were needed)**

Only if build issues required code changes.

---

### Task 6: Final commit and update TODO.md

**Files:**
- Modify: `TODO.md`

**Step 1: Update TODO.md**

Add a new session entry:
```markdown
**Session 5 (2026-02-21):** Migrated application database from SQL Server LocalDB to PostgreSQL 16 in Docker. Consolidated app DB and Hangfire storage into single PostgreSQL instance. Added docker-compose.yml for container management.
```

Add to Completed section:
```markdown
- [x] PostgreSQL 16 Docker container via docker-compose.yml (port 5433)
- [x] EF Core provider swapped from SqlServer to Npgsql
- [x] Connection strings updated for both Blazor.Server and Win projects
- [x] Hangfire consolidated into same PostgreSQL database
- [x] Npgsql legacy timestamp behavior enabled for XAF DateTime compatibility
```

**Step 2: Commit**

```bash
git add TODO.md
git commit -m "docs: update TODO with PostgreSQL migration complete"
```
