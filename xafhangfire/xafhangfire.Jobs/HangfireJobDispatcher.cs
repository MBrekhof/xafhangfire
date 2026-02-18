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
