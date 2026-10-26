#nullable enable

namespace xafhangfire.Jobs;

public interface IJobExecutionRecorder
{
    /// <param name="jobDefinitionId">Links the record to its JobDefinition; when null the recorder falls back to matching <paramref name="jobName"/>.</param>
    Task<Guid> RecordStartAsync(string jobName, string jobTypeName, string? parametersJson, Guid? jobDefinitionId, CancellationToken cancellationToken = default);
    Task RecordCompletionAsync(Guid recordId, CancellationToken cancellationToken = default);
    Task RecordFailureAsync(Guid recordId, string errorMessage, CancellationToken cancellationToken = default);
}
