using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.Logging;

namespace xafhangfire.Jobs;

public sealed class HangfireJobDispatcher(
    IBackgroundJobClient jobClient,
    ILogger<HangfireJobDispatcher> logger) : IJobDispatcher
{
    public Task DispatchAsync<TCommand>(
        TCommand command,
        Guid? jobDefinitionId = null,
        CancellationToken cancellationToken = default)
        where TCommand : notnull
    {
        logger.LogInformation(
            "HangfireDispatcher: enqueuing {CommandType}",
            typeof(TCommand).Name);

        jobClient.Enqueue<JobExecutor<TCommand>>(
            executor => executor.RunAsync(command, jobDefinitionId, CancellationToken.None));

        return Task.CompletedTask;
    }

    public void Schedule<TCommand>(TCommand command, string cronExpression, string scheduleId, Guid? jobDefinitionId)
        where TCommand : notnull
    {
        logger.LogInformation(
            "HangfireDispatcher: scheduling {CommandType} as '{JobId}' with cron '{Cron}'",
            typeof(TCommand).Name,
            scheduleId,
            cronExpression);

        RecurringJob.AddOrUpdate<JobExecutor<TCommand>>(
            scheduleId,
            executor => executor.RunAsync(command, jobDefinitionId, CancellationToken.None),
            cronExpression);
    }

    // ponytail: every recurring job in this app is owned by a JobDefinition, so anything not in the
    // keep-set is stale (disabled/deleted definitions, legacy per-command-type ids).
    public void RemoveSchedulesExcept(IReadOnlyCollection<string> keepScheduleIds)
    {
        var keep = new HashSet<string>(keepScheduleIds, StringComparer.Ordinal);

        using var connection = JobStorage.Current.GetConnection();
        foreach (var recurring in connection.GetRecurringJobs())
        {
            if (keep.Contains(recurring.Id))
                continue;

            logger.LogInformation("HangfireDispatcher: removing recurring job '{JobId}'", recurring.Id);
            RecurringJob.RemoveIfExists(recurring.Id);
        }
    }
}
