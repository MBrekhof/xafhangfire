# Integration Guide: Job Dispatcher for DevExpress XAF

This guide explains how to integrate the Command/Handler + Pluggable Dispatcher pattern from this project into your own DevExpress XAF solution.

## Prerequisites

- DevExpress XAF 25.2+ with EF Core
- .NET 8.0+
- Hangfire NuGet packages

## Step 1: Create the Jobs Class Library

Create a new class library project (e.g., `YourApp.Jobs`) with no XAF dependency. This keeps job logic portable.

```bash
dotnet new classlib -n YourApp.Jobs
```

Add the Hangfire NuGet package:

```bash
dotnet add YourApp.Jobs package Hangfire.Core
```

### Core Interfaces

Copy these interfaces from `xafhangfire.Jobs`:

**IJobHandler.cs** — Each command type gets a handler:
```csharp
public interface IJobHandler<in TCommand>
{
    Task ExecuteAsync(TCommand command, CancellationToken cancellationToken = default);
}
```

**IJobDispatcher.cs** — Dispatch commands for execution:
```csharp
public interface IJobDispatcher
{
    Task DispatchAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default) where TCommand : notnull;
    void Schedule<TCommand>(TCommand command, string cronExpression) where TCommand : notnull;
}
```

**IJobScopeInitializer.cs** — Initialize DI scope for background jobs:
```csharp
public interface IJobScopeInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
```

**IJobExecutionRecorder.cs** — Track job runs:
```csharp
public interface IJobExecutionRecorder
{
    Task<Guid> RecordStartAsync(string jobName, string jobTypeName, string? parametersJson, CancellationToken cancellationToken = default);
    Task RecordCompletionAsync(Guid recordId, CancellationToken cancellationToken = default);
    Task RecordFailureAsync(Guid recordId, string errorMessage, CancellationToken cancellationToken = default);
}
```

### Dispatchers

**DirectJobDispatcher** — Executes commands inline (for development/debugging):
```csharp
public sealed class DirectJobDispatcher(
    IServiceScopeFactory scopeFactory,
    ILogger<DirectJobDispatcher> logger) : IJobDispatcher
{
    public async Task DispatchAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : notnull
    {
        using var scope = scopeFactory.CreateScope();

        var initializer = scope.ServiceProvider.GetService<IJobScopeInitializer>();
        if (initializer != null)
            await initializer.InitializeAsync(cancellationToken);

        var handler = scope.ServiceProvider.GetRequiredService<IJobHandler<TCommand>>();
        await handler.ExecuteAsync(command, cancellationToken);
    }

    public void Schedule<TCommand>(TCommand command, string cronExpression) where TCommand : notnull
    {
        logger.LogWarning("DirectDispatcher: ignoring schedule — scheduling disabled in direct mode");
    }
}
```

**HangfireJobDispatcher** — Queues via Hangfire:
```csharp
public sealed class HangfireJobDispatcher(
    IBackgroundJobClient jobClient,
    ILogger<HangfireJobDispatcher> logger) : IJobDispatcher
{
    public Task DispatchAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : notnull
    {
        jobClient.Enqueue<JobExecutor<TCommand>>(
            executor => executor.RunAsync(command, CancellationToken.None));
        return Task.CompletedTask;
    }

    public void Schedule<TCommand>(TCommand command, string cronExpression)
        where TCommand : notnull
    {
        var jobId = typeof(TCommand).Name;
        RecurringJob.AddOrUpdate<JobExecutor<TCommand>>(
            jobId,
            executor => executor.RunAsync(command, CancellationToken.None),
            cronExpression);
    }
}
```

**JobExecutor** — The entry point Hangfire calls (resolved per-scope):
```csharp
public sealed class JobExecutor<TCommand>(
    IJobHandler<TCommand> handler,
    IJobScopeInitializer scopeInitializer,
    IJobExecutionRecorder executionRecorder)
{
    [AutomaticRetry(Attempts = 3)]
    public async Task RunAsync(TCommand command, CancellationToken cancellationToken)
    {
        await scopeInitializer.InitializeAsync(cancellationToken);

        var recordId = await executionRecorder.RecordStartAsync(
            typeof(TCommand).Name, typeof(TCommand).Name, null, cancellationToken);
        try
        {
            await handler.ExecuteAsync(command, cancellationToken);
            await executionRecorder.RecordCompletionAsync(recordId, cancellationToken);
        }
        catch (Exception ex)
        {
            await executionRecorder.RecordFailureAsync(recordId, ex.ToString(), cancellationToken);
            throw;
        }
    }
}
```

### DI Extension Methods

```csharp
public static class JobServiceCollectionExtensions
{
    public static IServiceCollection AddJobDispatcher(this IServiceCollection services, IConfiguration config)
    {
        var useHangfire = config.GetValue<bool>("Jobs:UseHangfire");
        if (useHangfire)
            services.AddTransient<IJobDispatcher, HangfireJobDispatcher>();
        else
            services.AddTransient<IJobDispatcher, DirectJobDispatcher>();
        return services;
    }

    public static IServiceCollection AddJobHandler<TCommand, THandler>(this IServiceCollection services)
        where THandler : class, IJobHandler<TCommand>
    {
        services.AddTransient<IJobHandler<TCommand>, THandler>();
        services.AddTransient<JobExecutor<TCommand>>();
        return services;
    }
}
```

## Step 2: Create Your Commands and Handlers

Commands are simple records in `Jobs/Commands/`:

```csharp
public record GenerateInvoiceCommand(int OrderId, string OutputFormat = "Pdf");
```

Handlers implement `IJobHandler<T>`:

```csharp
public sealed class GenerateInvoiceHandler(
    ILogger<GenerateInvoiceHandler> logger) : IJobHandler<GenerateInvoiceCommand>
{
    public async Task ExecuteAsync(GenerateInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Generating invoice for order {OrderId}", command.OrderId);
        // Your business logic here
    }
}
```

## Step 3: XAF Module Integration

### JobDefinition Entity

Create a `JobDefinition` entity in your Module project for admin-configurable jobs:

```csharp
[DefaultClassOptions]
[NavigationItem("Jobs")]
public class JobDefinition
{
    [Key] public virtual Guid Id { get; set; } = Guid.NewGuid();
    [Required] public virtual string Name { get; set; } = string.Empty;
    [Required] public virtual string JobTypeName { get; set; } = string.Empty;
    [FieldSize(FieldSizeAttribute.Unlimited)]
    public virtual string ParametersJson { get; set; }
    public virtual string CronExpression { get; set; }
    public virtual bool IsEnabled { get; set; } = true;
    public virtual DateTime? LastRunUtc { get; set; }
    public virtual JobRunStatus LastRunStatus { get; set; } = JobRunStatus.NeverRun;
}
```

### RunNow Action

Create a `ViewController` with a `SimpleAction` to dispatch jobs on demand:

```csharp
public class JobSchedulerViewController : ObjectViewController<DetailView, JobDefinition>
{
    public JobSchedulerViewController()
    {
        var runNow = new SimpleAction(this, "RunJobNow", "Edit")
        {
            Caption = "Run Now",
            ToolTip = "Dispatch this job immediately"
        };
        runNow.Execute += RunNow_Execute;
    }

    private async void RunNow_Execute(object sender, SimpleActionExecuteEventArgs e)
    {
        var job = (JobDefinition)View.CurrentObject;
        var service = Application.ServiceProvider.GetRequiredService<JobDispatchService>();
        await service.DispatchByNameAsync(job.JobTypeName, job.ParametersJson);
        // Update UI feedback...
    }
}
```

### JobExecutionRecord Entity

Track every job run for auditing:

```csharp
[DefaultClassOptions]
[NavigationItem("Jobs")]
public class JobExecutionRecord
{
    [Key] public virtual Guid Id { get; set; } = Guid.NewGuid();
    public virtual string JobName { get; set; } = string.Empty;
    public virtual string JobTypeName { get; set; } = string.Empty;
    public virtual DateTime StartedUtc { get; set; }
    public virtual DateTime? CompletedUtc { get; set; }
    public virtual JobRunStatus Status { get; set; } = JobRunStatus.Running;
    [FieldSize(FieldSizeAttribute.Unlimited)]
    public virtual string ErrorMessage { get; set; }
    public virtual long DurationMs { get; set; }
    public virtual JobDefinition JobDefinition { get; set; }
}
```

## Step 4: Blazor.Server Wiring

### Startup.cs Configuration

```csharp
// Hangfire storage
services.AddHangfire(config =>
{
    config.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(options =>
            options.UseNpgsqlConnection(connectionString));
});
services.AddHangfireServer();

// Job infrastructure
services.AddScoped<IJobScopeInitializer, XafJobScopeInitializer>();
services.AddScoped<IJobExecutionRecorder, XafJobExecutionRecorder>();

// Job dispatcher + handlers
services.AddJobDispatcher(Configuration);
services.AddJobHandler<GenerateInvoiceCommand, GenerateInvoiceHandler>();
services.AddTransient<JobDispatchService>();
services.AddHostedService<JobSyncService>();
```

### Hangfire Dashboard Auth

```csharp
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireDashboardAuthFilter(env.IsDevelopment()) }
});
```

### XAF Background Auth (Critical!)

Background Hangfire jobs run outside an HTTP request — there's no authenticated user. If your handlers use `IObjectSpaceFactory` (secured), `IReportExportService`, or any XAF service that requires authentication, you need `IJobScopeInitializer`:

```csharp
public sealed class XafJobScopeInitializer(
    INonSecuredObjectSpaceFactory objectSpaceFactory,
    UserManager userManager,
    SignInManager signInManager) : IJobScopeInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        using var objectSpace = objectSpaceFactory.CreateNonSecuredObjectSpace<ApplicationUser>();
        var user = userManager.FindUserByName<ApplicationUser>(objectSpace, "HangfireJob");
        if (user != null)
            signInManager.SignIn(user);
        return Task.CompletedTask;
    }
}
```

Create the `HangfireJob` user in your `Updater.cs` with a role that has read-only access to the types your handlers need.

## Step 5: Common Gotchas

| Issue | Solution |
|-------|----------|
| `SizeAttribute` not found | Use `FieldSizeAttribute` from `DevExpress.ExpressApp.DC` (removed in 25.2) |
| `ReportOptions` ambiguous | Don't name a class `ReportOptions` — clashes with `DevExpress.ExpressApp.ReportsV2.ReportOptions` |
| "The user name must not be empty" in background jobs | Implement `IJobScopeInitializer` with a service user |
| Navigation collections fail | Use `ObservableCollection<T>` (not `List<T>`) due to `ChangingAndChangedNotificationsWithOriginalValues` |
| PostgreSQL DateTime issues | Set `Npgsql.EnableLegacyTimestampBehavior = true` in `Startup.ConfigureServices` |
| Connection string for Hangfire | Strip the `EFCoreProvider=PostgreSql;` prefix — Hangfire needs a raw Npgsql string |
| DI scope issues in DirectJobDispatcher | Use `IServiceScopeFactory` to create fresh scopes, not the root `IServiceProvider` |

## Step 6: Optional Enhancements

- **Email jobs**: Add MailKit for SMTP, `IEmailSender` interface with log-only fallback
- **Report generation**: Use `IReportExportService` to export XtraReports in handlers
- **Report parameters**: Pass `Dictionary<string, string>` and apply to `XtraReport.Parameters`
- **Date range resolution**: Use `DateRangeResolver` for friendly date terms in parameters
- **Job sync service**: `IHostedService` that syncs enabled `JobDefinition` cron schedules to Hangfire on startup
