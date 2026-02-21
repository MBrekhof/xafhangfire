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

        var handler = scope.ServiceProvider.GetRequiredService<IJobHandler<TCommand>>();
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
