namespace xafhangfire.Jobs;

public interface IJobDispatcher
{
    /// <param name="jobDefinitionId">The JobDefinition this run belongs to; null for ad-hoc dispatches.</param>
    Task DispatchAsync<TCommand>(TCommand command, Guid? jobDefinitionId = null, CancellationToken cancellationToken = default)
        where TCommand : notnull;

    /// <param name="scheduleId">Stable id of the recurring job, one per JobDefinition.</param>
    void Schedule<TCommand>(TCommand command, string cronExpression, string scheduleId, Guid? jobDefinitionId)
        where TCommand : notnull;

    /// <summary>Removes every recurring job whose id is not in <paramref name="keepScheduleIds"/>.</summary>
    void RemoveSchedulesExcept(IReadOnlyCollection<string> keepScheduleIds);
}
