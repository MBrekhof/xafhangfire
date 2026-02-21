#nullable enable

namespace xafhangfire.Jobs;

public sealed class NullJobProgressReporter : IJobProgressReporter
{
    public void Initialize(Guid executionRecordId) { }
    public Task ReportAsync(int percentComplete, string? message = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
