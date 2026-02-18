# Job Dispatcher POC — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Implement a Command/Handler + Pluggable Dispatcher pattern with Hangfire, admin UI for job configuration, and DevExpress Scheduler visualization.

**Architecture:** New `xafhangfire.Jobs` class library holds interfaces, dispatchers, commands, and handlers. `JobDefinition` entity in Module provides persistent admin configuration. Hangfire embedded in Blazor.Server with in-memory storage for POC. Config toggle switches between direct (inline) and Hangfire (queued) execution.

**Tech Stack:** .NET 8.0, Hangfire + Hangfire.InMemory, DevExpress XAF 25.2, EF Core 8.0

**Design doc:** `docs/plans/2026-02-18-job-dispatcher-design.md`

---

## Task 1: Create the xafhangfire.Jobs Class Library

**Files:**
- Create: `xafhangfire/xafhangfire.Jobs/xafhangfire.Jobs.csproj`
- Modify: `xafhangfire.slnx` (add project reference)

**Step 1: Create the project**

```bash
cd C:/projects/xafhangfire
dotnet new classlib -n xafhangfire.Jobs -o xafhangfire/xafhangfire.Jobs --framework net8.0
```

**Step 2: Delete the auto-generated Class1.cs**

Delete `xafhangfire/xafhangfire.Jobs/Class1.cs`

**Step 3: Edit the .csproj to match solution conventions**

Replace `xafhangfire/xafhangfire.Jobs/xafhangfire.Jobs.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <CheckEolTargetFramework>false</CheckEolTargetFramework>
    <Deterministic>false</Deterministic>
    <AssemblyVersion>1.0.*</AssemblyVersion>
    <FileVersion>1.0.0.0</FileVersion>
    <Configurations>Debug;Release;EasyTest</Configurations>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Hangfire.Core" Version="1.8.*" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="8.0.*" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.*" />
  </ItemGroup>
</Project>
```

**Step 4: Add the project to the solution**

Add to `xafhangfire.slnx`:
```xml
  <Project Path="xafhangfire/xafhangfire.Jobs/xafhangfire.Jobs.csproj" />
```

**Step 5: Build to verify**

```bash
dotnet build xafhangfire/xafhangfire.slnx
```

Expected: Build succeeded.

**Step 6: Commit**

```bash
git add xafhangfire/xafhangfire.Jobs/ xafhangfire.slnx
git commit -m "feat: add xafhangfire.Jobs class library project"
```

---

## Task 2: Implement Core Interfaces

**Files:**
- Create: `xafhangfire/xafhangfire.Jobs/IJobHandler.cs`
- Create: `xafhangfire/xafhangfire.Jobs/IJobDispatcher.cs`

**Step 1: Create IJobHandler<TCommand>**

```csharp
// xafhangfire/xafhangfire.Jobs/IJobHandler.cs
namespace xafhangfire.Jobs;

public interface IJobHandler<in TCommand>
{
    Task ExecuteAsync(TCommand command, CancellationToken cancellationToken = default);
}
```

**Step 2: Create IJobDispatcher**

```csharp
// xafhangfire/xafhangfire.Jobs/IJobDispatcher.cs
namespace xafhangfire.Jobs;

public interface IJobDispatcher
{
    Task DispatchAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : notnull;

    void Schedule<TCommand>(TCommand command, string cronExpression)
        where TCommand : notnull;
}
```

**Step 3: Build to verify**

```bash
dotnet build xafhangfire/xafhangfire.Jobs/xafhangfire.Jobs.csproj
```

Expected: Build succeeded.

**Step 4: Commit**

```bash
git add xafhangfire/xafhangfire.Jobs/
git commit -m "feat: add IJobHandler and IJobDispatcher interfaces"
```

---

## Task 3: Implement DirectJobDispatcher

**Files:**
- Create: `xafhangfire/xafhangfire.Jobs/DirectJobDispatcher.cs`

**Step 1: Create DirectJobDispatcher**

```csharp
// xafhangfire/xafhangfire.Jobs/DirectJobDispatcher.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace xafhangfire.Jobs;

public sealed class DirectJobDispatcher(
    IServiceProvider services,
    ILogger<DirectJobDispatcher> logger) : IJobDispatcher
{
    public async Task DispatchAsync<TCommand>(
        TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand : notnull
    {
        logger.LogInformation(
            "DirectDispatcher: executing {CommandType} inline",
            typeof(TCommand).Name);

        var handler = services.GetRequiredService<IJobHandler<TCommand>>();
        await handler.ExecuteAsync(command, cancellationToken);
    }

    public void Schedule<TCommand>(TCommand command, string cronExpression)
        where TCommand : notnull
    {
        logger.LogWarning(
            "DirectDispatcher: ignoring schedule for {CommandType} (cron: {Cron}) — scheduling disabled in direct mode",
            typeof(TCommand).Name,
            cronExpression);
    }
}
```

**Step 2: Build**

```bash
dotnet build xafhangfire/xafhangfire.Jobs/xafhangfire.Jobs.csproj
```

**Step 3: Commit**

```bash
git add xafhangfire/xafhangfire.Jobs/DirectJobDispatcher.cs
git commit -m "feat: add DirectJobDispatcher for inline execution"
```

---

## Task 4: Implement HangfireJobDispatcher and JobExecutor

**Files:**
- Create: `xafhangfire/xafhangfire.Jobs/HangfireJobDispatcher.cs`
- Create: `xafhangfire/xafhangfire.Jobs/JobExecutor.cs`

**Step 1: Create JobExecutor<TCommand>**

This is the Hangfire entry point — Hangfire serializes the command and calls `RunAsync`.

```csharp
// xafhangfire/xafhangfire.Jobs/JobExecutor.cs
using Hangfire;

namespace xafhangfire.Jobs;

public sealed class JobExecutor<TCommand>(IJobHandler<TCommand> handler)
{
    [AutomaticRetry(Attempts = 3)]
    public async Task RunAsync(TCommand command, CancellationToken cancellationToken)
    {
        await handler.ExecuteAsync(command, cancellationToken);
    }
}
```

**Step 2: Create HangfireJobDispatcher**

```csharp
// xafhangfire/xafhangfire.Jobs/HangfireJobDispatcher.cs
using Hangfire;
using Microsoft.Extensions.Logging;

namespace xafhangfire.Jobs;

public sealed class HangfireJobDispatcher(
    IBackgroundJobClient jobClient,
    ILogger<HangfireJobDispatcher> logger) : IJobDispatcher
{
    public Task DispatchAsync<TCommand>(
        TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand : notnull
    {
        logger.LogInformation(
            "HangfireDispatcher: enqueuing {CommandType}",
            typeof(TCommand).Name);

        jobClient.Enqueue<JobExecutor<TCommand>>(
            executor => executor.RunAsync(command, CancellationToken.None));

        return Task.CompletedTask;
    }

    public void Schedule<TCommand>(TCommand command, string cronExpression)
        where TCommand : notnull
    {
        var jobId = typeof(TCommand).Name;

        logger.LogInformation(
            "HangfireDispatcher: scheduling {CommandType} as '{JobId}' with cron '{Cron}'",
            typeof(TCommand).Name,
            jobId,
            cronExpression);

        RecurringJob.AddOrUpdate<JobExecutor<TCommand>>(
            jobId,
            executor => executor.RunAsync(command, CancellationToken.None),
            cronExpression);
    }
}
```

**Step 3: Build**

```bash
dotnet build xafhangfire/xafhangfire.Jobs/xafhangfire.Jobs.csproj
```

**Step 4: Commit**

```bash
git add xafhangfire/xafhangfire.Jobs/HangfireJobDispatcher.cs xafhangfire/xafhangfire.Jobs/JobExecutor.cs
git commit -m "feat: add HangfireJobDispatcher and JobExecutor"
```

---

## Task 5: Create Sample Commands and Handlers

**Files:**
- Create: `xafhangfire/xafhangfire.Jobs/Commands/DemoLogCommand.cs`
- Create: `xafhangfire/xafhangfire.Jobs/Commands/ListUsersCommand.cs`
- Create: `xafhangfire/xafhangfire.Jobs/Handlers/DemoLogHandler.cs`
- Create: `xafhangfire/xafhangfire.Jobs/Handlers/ListUsersHandler.cs`
- Modify: `xafhangfire/xafhangfire.Jobs/xafhangfire.Jobs.csproj` (add DevExpress.ExpressApp reference for ObjectSpace)

**Step 1: Add DevExpress.ExpressApp package to Jobs project**

The `ListUsersHandler` needs `IObjectSpaceFactory`. Add to `xafhangfire.Jobs.csproj`:

```xml
    <PackageReference Include="DevExpress.ExpressApp" Version="25.2.*" />
```

**Step 2: Create DemoLogCommand**

```csharp
// xafhangfire/xafhangfire.Jobs/Commands/DemoLogCommand.cs
namespace xafhangfire.Jobs.Commands;

public record DemoLogCommand(string Message, int DelaySeconds = 3);
```

**Step 3: Create ListUsersCommand**

```csharp
// xafhangfire/xafhangfire.Jobs/Commands/ListUsersCommand.cs
namespace xafhangfire.Jobs.Commands;

public record ListUsersCommand(int MaxResults = 10);
```

**Step 4: Create DemoLogHandler**

```csharp
// xafhangfire/xafhangfire.Jobs/Handlers/DemoLogHandler.cs
using Microsoft.Extensions.Logging;
using xafhangfire.Jobs.Commands;

namespace xafhangfire.Jobs.Handlers;

public sealed class DemoLogHandler(
    ILogger<DemoLogHandler> logger) : IJobHandler<DemoLogCommand>
{
    public async Task ExecuteAsync(
        DemoLogCommand command,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("DemoLogHandler: START — {Message}", command.Message);

        for (var i = 1; i <= command.DelaySeconds; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            logger.LogInformation("DemoLogHandler: working... {Second}/{Total}", i, command.DelaySeconds);
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        logger.LogInformation("DemoLogHandler: DONE — {Message}", command.Message);
    }
}
```

**Step 5: Create ListUsersHandler**

```csharp
// xafhangfire/xafhangfire.Jobs/Handlers/ListUsersHandler.cs
using DevExpress.ExpressApp;
using DevExpress.Persistent.BaseImpl.EF.PermissionPolicy;
using Microsoft.Extensions.Logging;
using xafhangfire.Jobs.Commands;

namespace xafhangfire.Jobs.Handlers;

public sealed class ListUsersHandler(
    INonSecuredObjectSpaceFactory objectSpaceFactory,
    ILogger<ListUsersHandler> logger) : IJobHandler<ListUsersCommand>
{
    public Task ExecuteAsync(
        ListUsersCommand command,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("ListUsersHandler: fetching up to {Max} users", command.MaxResults);

        using var objectSpace = objectSpaceFactory.CreateNonSecuredObjectSpace<PermissionPolicyUser>();
        var users = objectSpace.GetObjects<PermissionPolicyUser>()
            .Take(command.MaxResults)
            .ToList();

        foreach (var user in users)
        {
            logger.LogInformation("ListUsersHandler: found user '{UserName}'", user.UserName);
        }

        logger.LogInformation("ListUsersHandler: done — {Count} users found", users.Count);
        return Task.CompletedTask;
    }
}
```

Note: Uses `INonSecuredObjectSpaceFactory` and `PermissionPolicyUser` because handlers run as background jobs without a logged-in user context. Also needs `DevExpress.Persistent.BaseImpl.EFCore` — add to csproj:

```xml
    <PackageReference Include="DevExpress.Persistent.BaseImpl.EFCore" Version="25.2.*" />
```

**Step 6: Build**

```bash
dotnet build xafhangfire/xafhangfire.Jobs/xafhangfire.Jobs.csproj
```

**Step 7: Commit**

```bash
git add xafhangfire/xafhangfire.Jobs/
git commit -m "feat: add DemoLog and ListUsers commands and handlers"
```

---

## Task 6: Wire Hangfire and DI into Blazor.Server

**Files:**
- Modify: `xafhangfire/xafhangfire.Blazor.Server/xafhangfire.Blazor.Server.csproj` (add package refs + project ref)
- Modify: `xafhangfire/xafhangfire.Blazor.Server/Startup.cs` (DI registration)
- Modify: `xafhangfire/xafhangfire.Blazor.Server/appsettings.json` (add Jobs config)
- Modify: `xafhangfire/xafhangfire.Blazor.Server/appsettings.Development.json` (UseHangfire: false)

**Step 1: Add NuGet packages and project reference to Blazor.Server.csproj**

Add to `<ItemGroup>` with other PackageReferences:

```xml
    <PackageReference Include="Hangfire.AspNetCore" Version="1.8.*" />
    <PackageReference Include="Hangfire.InMemory" Version="1.0.*" />
```

Add new `<ItemGroup>` or extend existing ProjectReference section:

```xml
  <ItemGroup>
    <ProjectReference Include="..\xafhangfire.Module\xafhangfire.Module.csproj" />
    <ProjectReference Include="..\xafhangfire.Jobs\xafhangfire.Jobs.csproj" />
  </ItemGroup>
```

**Step 2: Add Jobs config to appsettings.json**

Add after the existing top-level keys:

```json
  "Jobs": {
    "UseHangfire": true
  }
```

**Step 3: Add Jobs config override to appsettings.Development.json**

Add:

```json
  "Jobs": {
    "UseHangfire": false
  }
```

**Step 4: Register services in Startup.cs ConfigureServices**

Add these `using` statements at top of Startup.cs:

```csharp
using Hangfire;
using Hangfire.InMemory;
using xafhangfire.Jobs;
using xafhangfire.Jobs.Commands;
using xafhangfire.Jobs.Handlers;
```

Add at the end of `ConfigureServices`, before the closing `}`:

```csharp
            // Hangfire
            services.AddHangfire(config => config
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseInMemoryStorage());
            services.AddHangfireServer();

            // Job handlers
            services.AddTransient<IJobHandler<DemoLogCommand>, DemoLogHandler>();
            services.AddTransient<IJobHandler<ListUsersCommand>, ListUsersHandler>();
            services.AddTransient<JobExecutor<DemoLogCommand>>();
            services.AddTransient<JobExecutor<ListUsersCommand>>();

            // Job dispatcher — toggled by config
            if (Configuration.GetValue<bool>("Jobs:UseHangfire"))
            {
                services.AddSingleton<IJobDispatcher, HangfireJobDispatcher>();
            }
            else
            {
                services.AddSingleton<IJobDispatcher, DirectJobDispatcher>();
            }
```

**Step 5: Add Hangfire dashboard and middleware in Startup.cs Configure**

Add after `app.UseAuthorization();` and before `app.UseAntiforgery();`:

```csharp
            app.UseHangfireDashboard("/hangfire");
```

**Step 6: Build**

```bash
dotnet build xafhangfire/xafhangfire.slnx
```

**Step 7: Commit**

```bash
git add xafhangfire/xafhangfire.Blazor.Server/ xafhangfire/xafhangfire.Jobs/
git commit -m "feat: wire Hangfire and job dispatcher DI into Blazor.Server"
```

---

## Task 7: Add JobTestController API Endpoint

**Files:**
- Create: `xafhangfire/xafhangfire.Blazor.Server/API/Jobs/JobTestController.cs`

**Step 1: Create the controller**

```csharp
// xafhangfire/xafhangfire.Blazor.Server/API/Jobs/JobTestController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using xafhangfire.Jobs;
using xafhangfire.Jobs.Commands;

namespace xafhangfire.Blazor.Server.API.Jobs;

[ApiController]
[Route("api/jobs")]
[AllowAnonymous]
public sealed class JobTestController(IJobDispatcher dispatcher) : ControllerBase
{
    [HttpPost("demo-log")]
    public async Task<IActionResult> DemoLog(
        [FromQuery] string message = "Hello from POC",
        [FromQuery] int delaySeconds = 3,
        CancellationToken cancellationToken = default)
    {
        await dispatcher.DispatchAsync(
            new DemoLogCommand(message, delaySeconds),
            cancellationToken);

        return Ok(new { dispatched = "DemoLogCommand", message, delaySeconds });
    }

    [HttpPost("list-users")]
    public async Task<IActionResult> ListUsers(
        [FromQuery] int maxResults = 10,
        CancellationToken cancellationToken = default)
    {
        await dispatcher.DispatchAsync(
            new ListUsersCommand(maxResults),
            cancellationToken);

        return Ok(new { dispatched = "ListUsersCommand", maxResults });
    }
}
```

**Step 2: Build**

```bash
dotnet build xafhangfire/xafhangfire.slnx
```

**Step 3: Commit**

```bash
git add xafhangfire/xafhangfire.Blazor.Server/API/Jobs/
git commit -m "feat: add JobTestController API for manual job triggering"
```

---

## Task 8: Smoke Test — Run the Application

**Step 1: Run the Blazor Server app**

```bash
dotnet run --project xafhangfire/xafhangfire.Blazor.Server/xafhangfire.Blazor.Server.csproj
```

**Step 2: Verify the following endpoints work**

- `https://localhost:5001` — XAF Blazor app loads
- `https://localhost:5001/hangfire` — Hangfire dashboard loads
- `https://localhost:5001/swagger` — Swagger shows `api/jobs/demo-log` and `api/jobs/list-users`

**Step 3: Test direct dispatcher (Development mode, UseHangfire: false)**

```bash
curl -k -X POST "https://localhost:5001/api/jobs/demo-log?message=test&delaySeconds=2"
```

Expected: returns `{"dispatched":"DemoLogCommand","message":"test","delaySeconds":2}`
Check server console logs — should see DemoLogHandler messages inline.

**Step 4: Test with Hangfire (set UseHangfire: true in appsettings.Development.json temporarily)**

Change `appsettings.Development.json` → `"UseHangfire": true`, restart, POST again.
Check Hangfire dashboard — job should appear in Succeeded tab.
Revert to `"UseHangfire": false`.

**Step 5: Commit TODO update**

Update `TODO.md` to mark Phase 1 items as complete.

```bash
git add TODO.md
git commit -m "docs: mark Phase 1 complete in TODO"
```

---

## Task 9: Create JobDefinition Business Object

**Files:**
- Create: `xafhangfire/xafhangfire.Module/BusinessObjects/JobDefinition.cs`
- Modify: `xafhangfire/xafhangfire.Module/BusinessObjects/xafhangfireDbContext.cs` (add DbSet)
- Modify: `xafhangfire/xafhangfire.Module/Module.cs` (add AdditionalExportedTypes)

**Step 1: Create JobRunStatus enum and JobDefinition entity**

```csharp
// xafhangfire/xafhangfire.Module/BusinessObjects/JobDefinition.cs
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;

namespace xafhangfire.Module.BusinessObjects;

public enum JobRunStatus
{
    [Description("Never Run")]
    NeverRun = 0,
    Running = 1,
    Success = 2,
    Failed = 3
}

[DefaultClassOptions]
[NavigationItem("Jobs")]
[DefaultProperty(nameof(Name))]
[DomainComponent]
public class JobDefinition
{
    [Key]
    [VisibleInDetailView(false), VisibleInListView(false)]
    public virtual Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public virtual string Name { get; set; } = string.Empty;

    [Required]
    public virtual string JobTypeName { get; set; } = string.Empty;

    [Size(SizeAttribute.Unlimited)]
    [EditorAlias("StringPropertyEditor")]
    public virtual string? ParametersJson { get; set; }

    public virtual string? CronExpression { get; set; }

    public virtual bool IsEnabled { get; set; } = true;

    [VisibleInDetailView(true), VisibleInListView(true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public virtual DateTime? LastRunUtc { get; set; }

    [VisibleInDetailView(true), VisibleInListView(true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public virtual DateTime? NextRunUtc { get; set; }

    [VisibleInDetailView(true), VisibleInListView(true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public virtual JobRunStatus LastRunStatus { get; set; } = JobRunStatus.NeverRun;

    [Size(SizeAttribute.Unlimited)]
    [VisibleInListView(false)]
    public virtual string? LastRunMessage { get; set; }
}
```

**Step 2: Add DbSet to xafhangfireDbContext.cs**

Add after the existing `DbSet<HCategory>` line:

```csharp
        public DbSet<JobDefinition> JobDefinitions { get; set; }
```

**Step 3: Register in Module.cs**

Add in the `xafhangfireModule` constructor, after the existing `AdditionalExportedTypes` lines:

```csharp
            AdditionalExportedTypes.Add(typeof(xafhangfire.Module.BusinessObjects.JobDefinition));
```

**Step 4: Build**

```bash
dotnet build xafhangfire/xafhangfire.slnx
```

**Step 5: Commit**

```bash
git add xafhangfire/xafhangfire.Module/
git commit -m "feat: add JobDefinition business object with XAF UI"
```

---

## Task 10: Add DI Registration Helper for Job Handler Discovery

**Files:**
- Create: `xafhangfire/xafhangfire.Jobs/JobServiceCollectionExtensions.cs`
- Modify: `xafhangfire/xafhangfire.Blazor.Server/Startup.cs` (simplify registration)

**Step 1: Create extension method**

```csharp
// xafhangfire/xafhangfire.Jobs/JobServiceCollectionExtensions.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace xafhangfire.Jobs;

public static class JobServiceCollectionExtensions
{
    public static IServiceCollection AddJobDispatcher(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (configuration.GetValue<bool>("Jobs:UseHangfire"))
        {
            services.AddSingleton<IJobDispatcher, HangfireJobDispatcher>();
        }
        else
        {
            services.AddSingleton<IJobDispatcher, DirectJobDispatcher>();
        }

        return services;
    }

    public static IServiceCollection AddJobHandler<TCommand, THandler>(
        this IServiceCollection services)
        where THandler : class, IJobHandler<TCommand>
    {
        services.AddTransient<IJobHandler<TCommand>, THandler>();
        services.AddTransient<JobExecutor<TCommand>>();
        return services;
    }
}
```

**Step 2: Simplify Startup.cs registration**

Replace the job-related DI block added in Task 6 with:

```csharp
            // Job dispatcher + handlers
            services.AddJobDispatcher(Configuration);
            services.AddJobHandler<DemoLogCommand, DemoLogHandler>();
            services.AddJobHandler<ListUsersCommand, ListUsersHandler>();
```

**Step 3: Build**

```bash
dotnet build xafhangfire/xafhangfire.slnx
```

**Step 4: Commit**

```bash
git add xafhangfire/xafhangfire.Jobs/JobServiceCollectionExtensions.cs xafhangfire/xafhangfire.Blazor.Server/Startup.cs
git commit -m "refactor: add AddJobHandler extension for cleaner DI registration"
```

---

## Task 11: Scheduler View for JobDefinition (Phase 3)

> **Note:** This task adds a DevExpress Scheduler module reference and a ViewController that provides
> a scheduler-style view of JobDefinitions. XAF auto-generates ListViews and DetailViews for
> JobDefinition from Task 9. This task adds the calendar visualization on top.

**Files:**
- Modify: `xafhangfire/xafhangfire.Module/xafhangfire.Module.csproj` (add Scheduler package)
- Modify: `xafhangfire/xafhangfire.Blazor.Server/xafhangfire.Blazor.Server.csproj` (add Blazor Scheduler package)
- Modify: `xafhangfire/xafhangfire.Module/Module.cs` (add Scheduler module)
- Create: `xafhangfire/xafhangfire.Module/Controllers/JobSchedulerViewController.cs`

**Step 1: Add Scheduler NuGet packages**

Add to `xafhangfire.Module.csproj`:
```xml
    <PackageReference Include="DevExpress.ExpressApp.Scheduler" Version="25.2.*" />
```

Add to `xafhangfire.Blazor.Server.csproj`:
```xml
    <PackageReference Include="DevExpress.ExpressApp.Scheduler.Blazor" Version="25.2.*" />
```

**Step 2: Register Scheduler module in Module.cs**

Add in `xafhangfireModule` constructor:
```csharp
            RequiredModuleTypes.Add(typeof(DevExpress.ExpressApp.Scheduler.SchedulerModuleBase));
```

And add `.AddScheduler()` in `Startup.cs` inside the `builder.Modules` chain:
```csharp
                    .AddScheduler()
```

**Step 3: Create SchedulerViewController**

```csharp
// xafhangfire/xafhangfire.Module/Controllers/JobSchedulerViewController.cs
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;

namespace xafhangfire.Module.Controllers;

public sealed class JobSchedulerViewController : ViewController
{
    private readonly SimpleAction _runNowAction;

    public JobSchedulerViewController()
    {
        TargetObjectType = typeof(BusinessObjects.JobDefinition);

        _runNowAction = new SimpleAction(this, "RunJobNow", "Edit")
        {
            Caption = "Run Now",
            ConfirmationMessage = "Run this job immediately?",
            SelectionDependencyType = SelectionDependencyType.RequireSingleObject
        };
        _runNowAction.Execute += RunNowAction_Execute;
    }

    private void RunNowAction_Execute(object? sender, SimpleActionExecuteEventArgs e)
    {
        var job = (BusinessObjects.JobDefinition)e.CurrentObject;
        // Dispatch will be wired via DI in a follow-up task when
        // IJobDispatcher is accessible from the Module project.
        // For now, this action serves as a placeholder for the UI.
        Application.ShowViewStrategy.ShowMessage($"Job '{job.Name}' ({job.JobTypeName}) triggered.");
    }
}
```

**Step 4: Build**

```bash
dotnet build xafhangfire/xafhangfire.slnx
```

**Step 5: Commit**

```bash
git add xafhangfire/xafhangfire.Module/ xafhangfire/xafhangfire.Blazor.Server/
git commit -m "feat: add Scheduler module and RunNow action for JobDefinition"
```

---

## Task 12: DateRangeResolver for Friendly Date Terms

**Files:**
- Create: `xafhangfire/xafhangfire.Jobs/DateRangeResolver.cs`

**Step 1: Create DateRangeResolver**

```csharp
// xafhangfire/xafhangfire.Jobs/DateRangeResolver.cs
namespace xafhangfire.Jobs;

public readonly record struct DateRange(DateOnly Start, DateOnly End);

public static class DateRangeResolver
{
    public static DateRange Resolve(string term, DateOnly? relativeTo = null)
    {
        var today = relativeTo ?? DateOnly.FromDateTime(DateTime.Today);

        return term.ToLowerInvariant().Trim() switch
        {
            "today" => new DateRange(today, today),
            "yesterday" => new DateRange(today.AddDays(-1), today.AddDays(-1)),
            "this-week" => WeekRange(today, 0),
            "last-week" => WeekRange(today, -1),
            "next-week" => WeekRange(today, 1),
            "this-month" => MonthRange(today, 0),
            "last-month" => MonthRange(today, -1),
            "next-month" => MonthRange(today, 1),
            "this-quarter" => QuarterRange(today, 0),
            "last-quarter" => QuarterRange(today, -1),
            "this-year" => new DateRange(
                new DateOnly(today.Year, 1, 1),
                new DateOnly(today.Year, 12, 31)),
            "last-year" => new DateRange(
                new DateOnly(today.Year - 1, 1, 1),
                new DateOnly(today.Year - 1, 12, 31)),
            _ => throw new ArgumentException($"Unknown date term: '{term}'", nameof(term))
        };
    }

    private static DateRange WeekRange(DateOnly today, int weekOffset)
    {
        var diff = (int)today.DayOfWeek - (int)DayOfWeek.Monday;
        if (diff < 0) diff += 7;
        var monday = today.AddDays(-diff + weekOffset * 7);
        return new DateRange(monday, monday.AddDays(6));
    }

    private static DateRange MonthRange(DateOnly today, int monthOffset)
    {
        var first = new DateOnly(today.Year, today.Month, 1).AddMonths(monthOffset);
        var last = first.AddMonths(1).AddDays(-1);
        return new DateRange(first, last);
    }

    private static DateRange QuarterRange(DateOnly today, int quarterOffset)
    {
        var currentQuarter = (today.Month - 1) / 3;
        var targetQuarter = currentQuarter + quarterOffset;
        var targetYear = today.Year + (targetQuarter < 0 ? -1 : targetQuarter / 4);
        targetQuarter = ((targetQuarter % 4) + 4) % 4;
        var firstMonth = targetQuarter * 3 + 1;
        var first = new DateOnly(targetYear, firstMonth, 1);
        var last = first.AddMonths(3).AddDays(-1);
        return new DateRange(first, last);
    }
}
```

**Step 2: Build**

```bash
dotnet build xafhangfire/xafhangfire.Jobs/xafhangfire.Jobs.csproj
```

**Step 3: Commit**

```bash
git add xafhangfire/xafhangfire.Jobs/DateRangeResolver.cs
git commit -m "feat: add DateRangeResolver for friendly calendar terms"
```

---

## Task 13: Final Build, Push, and Session Closeout

**Step 1: Full solution build**

```bash
dotnet build xafhangfire/xafhangfire.slnx
```

**Step 2: Update TODO.md**

Mark completed items, note what's done, what remains for next session.

**Step 3: Update CLAUDE.md**

Add the Jobs project to the key files table and any new conventions.

**Step 4: Commit and push**

```bash
git add -A
git commit -m "docs: update TODO and CLAUDE.md with Phase 1-3 progress"
git push origin master
```

**Step 5: Verify GitHub**

```bash
gh repo view --web
```

---

## Session Continuation Notes

If this becomes a multi-session effort, the next session should:

1. Read `TODO.md` for current status
2. Read `docs/plans/2026-02-18-job-dispatcher-design.md` for design context
3. Run `dotnet build xafhangfire/xafhangfire.slnx` to verify clean state
4. Pick up remaining unchecked items from TODO.md

Likely follow-up work:
- Wire the "Run Now" action to actually dispatch via `IJobDispatcher` (requires Module → Jobs project reference)
- Sync `JobDefinition` entities to Hangfire recurring jobs on app startup
- Seed sample JobDefinition records in `Updater.cs`
- DevExpress Scheduler visual binding for the calendar view
- Swap Hangfire.InMemory → SQL Server or SQLite for persistence
