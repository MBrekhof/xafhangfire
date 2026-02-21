#nullable enable

namespace xafhangfire.Jobs;

public interface IJobExecutionRecorder
{
    Task<Guid> RecordStartAsync(string jobName, string jobTypeName, string? parametersJson, CancellationToken cancellationToken = default);
    Task RecordCompletionAsync(Guid recordId, CancellationToken cancellationToken = default);
    Task RecordFailureAsync(Guid recordId, string errorMessage, CancellationToken cancellationToken = default);
}
