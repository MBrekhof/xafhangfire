using Hangfire;

namespace xafhangfire.Jobs;

public sealed class JobExecutor<TCommand>(
    IJobHandler<TCommand> handler,
    IJobScopeInitializer scopeInitializer)
{
    [AutomaticRetry(Attempts = 3)]
    public async Task RunAsync(TCommand command, CancellationToken cancellationToken)
    {
        await scopeInitializer.InitializeAsync(cancellationToken);
        await handler.ExecuteAsync(command, cancellationToken);
    }
}
