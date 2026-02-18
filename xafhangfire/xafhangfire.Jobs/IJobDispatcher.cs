namespace xafhangfire.Jobs;

public interface IJobDispatcher
{
    Task DispatchAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : notnull;

    void Schedule<TCommand>(TCommand command, string cronExpression)
        where TCommand : notnull;
}
