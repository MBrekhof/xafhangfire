#nullable enable
using DevExpress.ExpressApp;
using Microsoft.Extensions.Logging;
using xafhangfire.Jobs;
using xafhangfire.Module.BusinessObjects;

namespace xafhangfire.Blazor.Server.Services;

public sealed class XafJobExecutionRecorder(
    INonSecuredObjectSpaceFactory objectSpaceFactory,
    ILogger<XafJobExecutionRecorder> logger) : IJobExecutionRecorder
{
    public Task<Guid> RecordStartAsync(string jobName, string jobTypeName, string? parametersJson, CancellationToken cancellationToken = default)
    {
        try
        {
            using var objectSpace = objectSpaceFactory.CreateNonSecuredObjectSpace<JobExecutionRecord>();

            var record = objectSpace.CreateObject<JobExecutionRecord>();
            record.JobName = jobName;
            record.JobTypeName = jobTypeName;
            record.StartedUtc = DateTime.UtcNow;
            record.Status = JobRunStatus.Running;
            record.ParametersJson = parametersJson ?? string.Empty;

            // Link to JobDefinition if one exists with this name
            var jobDef = objectSpace.FirstOrDefault<JobDefinition>(j => j.Name == jobName);
            if (jobDef != null)
                record.JobDefinition = jobDef;

            objectSpace.CommitChanges();

            logger.LogDebug("Recorded job execution start: {RecordId} for '{JobName}'", record.Id, jobName);
            return Task.FromResult(record.Id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to record job start for '{JobName}' — execution will continue", jobName);
            return Task.FromResult(Guid.Empty);
        }
    }

    public Task RecordCompletionAsync(Guid recordId, CancellationToken cancellationToken = default)
    {
        if (recordId == Guid.Empty) return Task.CompletedTask;

        try
        {
            using var objectSpace = objectSpaceFactory.CreateNonSecuredObjectSpace<JobExecutionRecord>();

            var record = objectSpace.FirstOrDefault<JobExecutionRecord>(r => r.Id == recordId);
            if (record != null)
            {
                record.CompletedUtc = DateTime.UtcNow;
                record.Status = JobRunStatus.Success;
                record.DurationMs = (long)(record.CompletedUtc.Value - record.StartedUtc).TotalMilliseconds;
                objectSpace.CommitChanges();

                logger.LogDebug("Recorded job completion: {RecordId} ({DurationMs}ms)", recordId, record.DurationMs);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to record job completion for {RecordId}", recordId);
        }

        return Task.CompletedTask;
    }

    public Task RecordFailureAsync(Guid recordId, string errorMessage, CancellationToken cancellationToken = default)
    {
        if (recordId == Guid.Empty) return Task.CompletedTask;

        try
        {
            using var objectSpace = objectSpaceFactory.CreateNonSecuredObjectSpace<JobExecutionRecord>();

            var record = objectSpace.FirstOrDefault<JobExecutionRecord>(r => r.Id == recordId);
            if (record != null)
            {
                record.CompletedUtc = DateTime.UtcNow;
                record.Status = JobRunStatus.Failed;
                record.DurationMs = (long)(record.CompletedUtc.Value - record.StartedUtc).TotalMilliseconds;
                record.ErrorMessage = errorMessage;
                objectSpace.CommitChanges();

                logger.LogDebug("Recorded job failure: {RecordId} ({DurationMs}ms)", recordId, record.DurationMs);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to record job failure for {RecordId}", recordId);
        }

        return Task.CompletedTask;
    }
}
