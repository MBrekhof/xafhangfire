#nullable enable

namespace xafhangfire.Jobs;

public interface IJobProgressReporter
{
    void Initialize(Guid executionRecordId);
    Task ReportAsync(int percentComplete, string? message = null, CancellationToken cancellationToken = default);
}
