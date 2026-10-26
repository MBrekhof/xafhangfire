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
        Guid? jobDefinitionId = null,
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
        var reporter = scope.ServiceProvider.GetService<IJobProgressReporter>();
        var commandType = typeof(TCommand).Name;
        string? parametersJson = null;
        try { parametersJson = JsonSerializer.Serialize(command, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }); } catch { /* best-effort */ }

        var recordId = recorder != null
            ? await recorder.RecordStartAsync(commandType, commandType, parametersJson, jobDefinitionId, cancellationToken)
            : Guid.Empty;
        reporter?.Initialize(recordId);
        try
        {
            var handler = scope.ServiceProvider.GetRequiredService<IJobHandler<TCommand>>();
            await handler.ExecuteAsync(command, cancellationToken);
            if (recorder != null) await recorder.RecordCompletionAsync(recordId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancelled through the caller's token: not a failure (JOB-011).
            if (recorder != null) await recorder.RecordInterruptedAsync(recordId, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            if (recorder != null) await recorder.RecordFailureAsync(recordId, ex.ToString(), CancellationToken.None);
            throw;
        }
    }

    public void Schedule<TCommand>(TCommand command, string cronExpression, string scheduleId, Guid? jobDefinitionId)
        where TCommand : notnull
    {
        logger.LogWarning(
            "DirectDispatcher: ignoring schedule for {CommandType} (cron: {Cron}) — scheduling disabled in direct mode",
            typeof(TCommand).Name,
            cronExpression);
    }

    // Scheduling is disabled in direct mode, so there is nothing to remove.
    public void RemoveSchedulesExcept(IReadOnlyCollection<string> keepScheduleIds) { }
}
