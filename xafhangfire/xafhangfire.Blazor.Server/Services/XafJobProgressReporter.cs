#nullable enable
using DevExpress.ExpressApp;
using Microsoft.Extensions.Logging;
using xafhangfire.Jobs;
using xafhangfire.Module.BusinessObjects;

namespace xafhangfire.Blazor.Server.Services;

public sealed class XafJobProgressReporter(
    INonSecuredObjectSpaceFactory objectSpaceFactory,
    ILogger<XafJobProgressReporter> logger) : IJobProgressReporter
{
    private Guid _recordId;

    public void Initialize(Guid executionRecordId)
    {
        _recordId = executionRecordId;
    }

    public Task ReportAsync(int percentComplete, string? message = null, CancellationToken cancellationToken = default)
    {
        if (_recordId == Guid.Empty)
            return Task.CompletedTask;

        try
        {
            using var objectSpace = objectSpaceFactory.CreateNonSecuredObjectSpace<JobExecutionRecord>();
            var record = objectSpace.FirstOrDefault<JobExecutionRecord>(r => r.Id == _recordId);
            if (record != null)
            {
                record.ProgressPercent = Math.Clamp(percentComplete, 0, 100);
                record.ProgressMessage = message ?? string.Empty;
                objectSpace.CommitChanges();

                logger.LogDebug("Progress updated: {RecordId} → {Percent}% {Message}", _recordId, percentComplete, message);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to update progress for {RecordId}", _recordId);
        }

        return Task.CompletedTask;
    }
}
