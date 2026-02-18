namespace xafhangfire.Jobs;

public interface IJobHandler<in TCommand>
{
    Task ExecuteAsync(TCommand command, CancellationToken cancellationToken = default);
}
