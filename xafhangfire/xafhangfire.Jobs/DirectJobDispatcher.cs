using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace xafhangfire.Jobs;

public sealed class DirectJobDispatcher(
    IServiceScopeFactory scopeFactory,
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

        using var scope = scopeFactory.CreateScope();

        var initializer = scope.ServiceProvider.GetService<IJobScopeInitializer>();
        if (initializer != null)
            await initializer.InitializeAsync(cancellationToken);

        var recorder = scope.ServiceProvider.GetService<IJobExecutionRecorder>();
        var commandType = typeof(TCommand).Name;
        string? parametersJson = null;
        try { parametersJson = JsonSerializer.Serialize(command); } catch { /* best-effort */ }

        var recordId = recorder != null
            ? await recorder.RecordStartAsync(commandType, commandType, parametersJson, cancellationToken)
            : Guid.Empty;
        try
        {
            var handler = scope.ServiceProvider.GetRequiredService<IJobHandler<TCommand>>();
            await handler.ExecuteAsync(command, cancellationToken);
            if (recorder != null) await recorder.RecordCompletionAsync(recordId, cancellationToken);
        }
        catch (Exception ex)
        {
            if (recorder != null) await recorder.RecordFailureAsync(recordId, ex.ToString(), cancellationToken);
            throw;
        }
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
