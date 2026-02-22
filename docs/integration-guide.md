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

## Step 5: Progress Reporting

Handlers can report progress during long-running operations via `IJobProgressReporter`:

```csharp
public interface IJobProgressReporter
{
    Task ReportProgressAsync(int percent, string? message = null, CancellationToken cancellationToken = default);
    void Initialize(Guid executionRecordId);
}
```

Inject it into handlers and call `ReportProgressAsync` during execution loops. `XafJobProgressReporter` updates the `JobExecutionRecord.ProgressPercent` and `ProgressMessage` fields. Use `NullJobProgressReporter` as a no-op default for tests.

## Step 6: Consecutive Failure Tracking

`JobDefinition` has a `ConsecutiveFailures` counter — incremented by `XafJobExecutionRecorder` on failure, reset on success. When it reaches a configurable threshold (`Jobs:FailureAlertThreshold`, default 3), a warning is logged:

```csharp
if (jobDef.ConsecutiveFailures >= threshold)
    logger.LogWarning("Job '{Name}' has failed {Count} consecutive times", jobDef.Name, jobDef.ConsecutiveFailures);
```

## Step 7: Cron Visualization

`CronHelper` (in Module project) uses Cronos + CronExpressionDescriptor to provide human-readable cron descriptions and next run times. `JobDefinition` exposes two `[NotMapped]` computed properties:

- `CronDescription` — e.g., "Every day at 2:00 AM"
- `NextScheduledRuns` — next 5 occurrences formatted as a multi-line string

## Step 8: Custom Property Editors (Blazor)

### Parameter Editor

The `JobParametersPropertyEditor` replaces the raw JSON textarea with typed form fields. It uses `CommandMetadataProvider` to reflect on command record constructors and discover parameters with types, defaults, and `DataSourceHint` annotations.

**Pattern:** `BlazorPropertyEditorBase` + `ComponentModelBase` + Razor component:
- `JobParametersPropertyEditor.cs` — the editor class, implements `IComplexViewItem` for `XafApplication` access
- `JobParametersFormModel.cs` — `ComponentModelBase` descendant with `Fields`, `ShowRawEditor`, `RawJson` properties
- `JobParametersForm.razor` — renders typed fields (DxSpinEdit, DxCheckBox, DxComboBox, key-value rows, DxMemo)

Connect via `[EditorAlias("JobParametersEditor")]` on the `ParametersJson` property.

### JobTypeName Dropdown

`JobTypeNamePropertyEditor` renders `JobTypeName` as a DxComboBox populated from `CommandMetadataProvider.GetRegisteredTypeNames()`. Same BlazorPropertyEditorBase + ComponentModelBase + Razor pattern.

### DataSourceHint

`CommandParameterMetadata` has a `DataSourceHint` string that drives editor selection:
- `"Reports"` — dropdown populated from `ReportDataV2.DisplayName`
- `"EmailTemplates"` — dropdown populated from `EmailTemplate.Name`
- `"Pdf,Xlsx"` — static comma-separated values for dropdown
- `"ReportParameters"` — key-value editor with report parameter auto-discovery
- `"KeyValue"` — plain key-value editor (add/remove rows)

## Step 9: Report Parameter Auto-Discovery

When a user selects a `ReportName`, the parameter editor auto-discovers report parameters and pre-populates key-value rows.

### Setup

1. Create a `ReportParametersObjectBase` descendant:

```csharp
[DomainComponent]
public class MyReportParameters : ReportParametersObjectBase
{
    public DateTime StartDate { get; set; } = new DateTime(2000, 1, 1);
    public DateTime EndDate { get; set; } = DateTime.Now.AddYears(1);

    public MyReportParameters(IObjectSpaceCreator provider) : base(provider) { }

    protected override IObjectSpace CreateObjectSpace()
        => objectSpaceCreator.CreateObjectSpace(typeof(MyEntity));

    public override CriteriaOperator GetCriteria()
        => CriteriaOperator.Parse("[StartDate] >= ? And [StartDate] <= ?", StartDate, EndDate);

    public override SortProperty[] GetSorting() => null;
}
```

2. Register with the 3-arg `AddPredefinedReport` overload in `Module.cs`:

```csharp
predefinedReportsUpdater.AddPredefinedReport<MyReport>(
    "My Report", typeof(MyEntity), typeof(MyReportParameters));
```

### How Discovery Works

`DiscoverReportParameters` in `JobParametersPropertyEditor`:
1. Queries `ReportDataV2.ParametersObjectTypeName` for the selected report
2. Resolves the type via `Type.GetType` + assembly scan
3. Reflects on public properties (excluding `ReportParametersObjectBase` base properties)
4. Returns `KeyValuePairModel` list with property names and type-appropriate defaults

**Fallback:** If no `ParametersObjectTypeName`, instantiates the report from `PredefinedReportTypeName` and inspects `XtraReport.Parameters`.

### JSON Persistence

After discovery, `RefreshFields` calls `SerializeFieldsToJson()` to re-serialize fields back to JSON and persist via `WriteValue()`. Without this, discovered params are set on model fields but lost on save/reload.

## Step 10: Common Gotchas

| Issue | Solution |
|-------|----------|
| `SizeAttribute` not found | Use `FieldSizeAttribute` from `DevExpress.ExpressApp.DC` (removed in 25.2) |
| `ReportOptions` ambiguous | Don't name a class `ReportOptions` — clashes with `DevExpress.ExpressApp.ReportsV2.ReportOptions` |
| "The user name must not be empty" in background jobs | Implement `IJobScopeInitializer` with a service user |
| Navigation collections fail | Use `ObservableCollection<T>` (not `List<T>`) due to `ChangingAndChangedNotificationsWithOriginalValues` |
| PostgreSQL DateTime issues | Set `Npgsql.EnableLegacyTimestampBehavior = true` in `Startup.ConfigureServices` |
| Connection string for Hangfire | Strip the `EFCoreProvider=PostgreSql;` prefix — Hangfire needs a raw Npgsql string |
| DI scope issues in DirectJobDispatcher | Use `IServiceScopeFactory` to create fresh scopes, not the root `IServiceProvider` |
| DevExpress packages version pinning | Pin to exact version (e.g., 25.2.3) across all 4 csproj files — wildcard versions cause restore issues |
| Model Editor + PostgreSQL | Module needs `Microsoft.EntityFrameworkCore.SqlServer` as design-time workaround. Use XAF's `DesignTimeDbContextFactory<T>` base class |
| DxTextBox in dynamic XAF rendering | DxTextBox shows NullText placeholders when created dynamically in XAF Blazor adapter render cycles. Use plain HTML `<input>` elements instead. Other DxComponents (DxSpinEdit, DxCheckBox, DxComboBox) work fine |
| `[Required]` attribute conflicts | Fully qualify `[System.ComponentModel.DataAnnotations.Required]` in Module — conflicts with `DevExpress.ExpressApp.Model.RequiredAttribute` |

## Step 11: Optional Enhancements

- **Email jobs**: Add MailKit for SMTP, `IEmailSender` interface with log-only fallback
- **Report generation**: Use `IReportExportService` to export XtraReports in handlers
- **Report parameters**: Pass `Dictionary<string, string>` and apply to `XtraReport.Parameters`
- **Date range resolution**: Use `DateRangeResolver` for friendly date terms in parameters
- **Job sync service**: `IHostedService` that syncs enabled `JobDefinition` cron schedules to Hangfire on startup
- **Real-time progress UI**: SignalR push of progress updates to XAF detail view
- **Email failure alerts**: Extend consecutive failure tracking to send alert emails
