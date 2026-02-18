# Job Dispatcher Architecture: XAF + Hangfire

## Problem

- ViewControllers in XAF directly enqueue Hangfire jobs
- Local dev requires either stopping the server Hangfire service or risking conflicts (no local DBs)
- Debugging through Hangfire is painful
- Some jobs need both scheduling (cron) AND ad-hoc execution with parameters
- Job logic is coupled to the execution strategy

## Solution: Command/Handler + Pluggable Dispatcher

Separate **what** a job does from **how** it runs.

### Project Structure

```
Solution/
├── Shared.Jobs/                    # Command records + handler interfaces + handlers
│   ├── Commands/
│   │   ├── SyncCustomerCommand.cs
│   │   └── DailyInvoiceCommand.cs
│   ├── Handlers/
│   │   ├── SyncCustomerHandler.cs
│   │   └── DailyInvoiceHandler.cs
│   ├── IJobHandler.cs
│   └── IJobDispatcher.cs
├── XAF.App/                        # References Shared.Jobs
│   ├── Controllers/
│   │   └── SyncCustomerViewController.cs
│   └── Startup (registers dispatcher + handlers)
└── Hangfire.Service/               # References Shared.Jobs
    ├── HangfireJobDispatcher.cs
    ├── JobExecutor.cs
    ├── JobRegistration.cs
    └── Startup (registers dispatcher + handlers)
```

### Layer 1: Commands as Records (Shared.Jobs)

Commands are plain records — no Hangfire dependency anywhere in this project.

```csharp
// Shared.Jobs/Commands/SyncCustomerCommand.cs
public record SyncCustomerCommand(string CustomerId, bool FullSync = false);

// Shared.Jobs/Commands/DailyInvoiceCommand.cs
public record DailyInvoiceCommand(DateOnly? InvoiceDate = null);
```

### Layer 2: Handler Interface (Shared.Jobs)

```csharp
// Shared.Jobs/IJobHandler.cs
public interface IJobHandler<in TCommand>
{
    Task ExecuteAsync(TCommand command, CancellationToken cancellationToken = default);
}
```

### Layer 3: Handlers — Pure Business Logic (Shared.Jobs)

```csharp
// Shared.Jobs/Handlers/SyncCustomerHandler.cs
public sealed class SyncCustomerHandler(
    IObjectSpaceFactory objectSpaceFactory,
    ILogger<SyncCustomerHandler> logger) : IJobHandler<SyncCustomerCommand>
{
    public async Task ExecuteAsync(
        SyncCustomerCommand command,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Syncing customer {CustomerId}", command.CustomerId);
        using var objectSpace = objectSpaceFactory.CreateNonSecuredObjectSpace<Customer>();
        // ... actual work
    }
}
```

### Layer 4: Dispatcher Abstraction (Shared.Jobs)

```csharp
// Shared.Jobs/IJobDispatcher.cs
public interface IJobDispatcher
{
    Task DispatchAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default);
    void Schedule<TCommand>(TCommand command, string cronExpression) where TCommand : notnull;
}
```

### Layer 5: Two Dispatcher Implementations

#### Direct (local dev / debugging)

```csharp
// Can live in Shared.Jobs or XAF.App
public sealed class DirectJobDispatcher(IServiceProvider services) : IJobDispatcher
{
    public async Task DispatchAsync<TCommand>(
        TCommand command,
        CancellationToken cancellationToken = default)
    {
        var handler = services.GetRequiredService<IJobHandler<TCommand>>();
        await handler.ExecuteAsync(command, cancellationToken);
    }

    public void Schedule<TCommand>(TCommand command, string cronExpression)
        where TCommand : notnull
    {
        // No-op in local dev — or log a warning
    }
}
```

#### Hangfire (production)

```csharp
// Hangfire.Service/HangfireJobDispatcher.cs
public sealed class HangfireJobDispatcher(IBackgroundJobClient jobClient) : IJobDispatcher
{
    public Task DispatchAsync<TCommand>(
        TCommand command,
        CancellationToken cancellationToken = default)
    {
        jobClient.Enqueue<JobExecutor<TCommand>>(
            x => x.RunAsync(command, CancellationToken.None));
        return Task.CompletedTask;
    }

    public void Schedule<TCommand>(TCommand command, string cronExpression)
        where TCommand : notnull
    {
        var jobId = typeof(TCommand).Name;
        RecurringJob.AddOrUpdate<JobExecutor<TCommand>>(
            jobId,
            x => x.RunAsync(command, CancellationToken.None),
            cronExpression);
    }
}

// Hangfire.Service/JobExecutor.cs
public sealed class JobExecutor<TCommand>(IJobHandler<TCommand> handler)
{
    [AutomaticRetry(Attempts = 3)]
    public async Task RunAsync(TCommand command, CancellationToken cancellationToken)
    {
        await handler.ExecuteAsync(command, cancellationToken);
    }
}
```

### Layer 6: Config-Based Switching

```json
// appsettings.Development.json
{
  "Jobs": {
    "UseHangfire": false
  }
}

// appsettings.Production.json
{
  "Jobs": {
    "UseHangfire": true
  }
}
```

```csharp
// DI registration (shared between XAF and Hangfire service)
if (configuration.GetValue<bool>("Jobs:UseHangfire"))
    services.AddSingleton<IJobDispatcher, HangfireJobDispatcher>();
else
    services.AddSingleton<IJobDispatcher, DirectJobDispatcher>();

// Register all handlers
services.AddTransient<IJobHandler<SyncCustomerCommand>, SyncCustomerHandler>();
services.AddTransient<IJobHandler<DailyInvoiceCommand>, DailyInvoiceHandler>();
```

### Layer 7: XAF ViewController (UI stays in XAF)

```csharp
public sealed class SyncCustomerViewController : ObjectViewController<DetailView, Customer>
{
    private readonly IJobDispatcher _dispatcher;

    protected override void OnActivated()
    {
        base.OnActivated();
        var syncAction = new SimpleAction(this, "SyncCustomer", "Edit")
        {
            Caption = "Sync Customer"
        };
        syncAction.Execute += SyncAction_Execute;
    }

    private async void SyncAction_Execute(object sender, SimpleActionExecuteEventArgs e)
    {
        var customer = (Customer)View.CurrentObject;
        await _dispatcher.DispatchAsync(
            new SyncCustomerCommand(customer.CustomerId));
        Application.ShowViewStrategy.ShowMessage("Sync started");
    }
}
```

### Layer 8: Scheduled Job Registration

```csharp
// Hangfire.Service/JobRegistration.cs
public static class JobRegistration
{
    public static void RegisterScheduledJobs(IJobDispatcher dispatcher)
    {
        dispatcher.Schedule(
            new DailyInvoiceCommand(),
            "0 7 * * *");                                    // Every day at 07:00

        dispatcher.Schedule(
            new SyncCustomerCommand("AAWATER", FullSync: true),
            "0 6 * * 1");                                    // Mondays at 06:00
    }
}
```

---

## Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| `[HangfireJob]` attributes | No | Couples business logic to infrastructure |
| UI location | XAF only | Users are in XAF; Hangfire dashboard is for ops monitoring |
| Local dev strategy | `UseHangfire: false` | Jobs run inline, breakpoints work, no server conflicts |
| Parameterized + scheduled | Records with defaults | Scheduled uses defaults, ad-hoc supplies user params |
| Handler location | Shared project | Same handler code runs in XAF (direct) and Hangfire service |

## Migration Path

1. Create `Shared.Jobs` project, define `IJobHandler<T>` and `IJobDispatcher`
2. Extract existing job logic from ViewControllers into handler classes
3. Define command records for each job
4. Implement `DirectJobDispatcher` — get local dev working first
5. Implement `HangfireJobDispatcher` + `JobExecutor<T>`
6. Update ViewControllers to use `IJobDispatcher` instead of direct Hangfire calls
7. Move scheduled job registration to `JobRegistration.cs`
8. Toggle via appsettings — verify both paths work

## Benefits

- **Debug locally**: `UseHangfire: false`, F5, breakpoint in handler
- **No server conflicts**: Local dev never touches Hangfire
- **Same code path**: Handler runs identically from XAF UI, Hangfire schedule, or unit test
- **Easy testing**: `new SyncCustomerHandler(mock, mock)` — no Hangfire infra needed
- **Incremental migration**: Move one job at a time, existing jobs keep working
