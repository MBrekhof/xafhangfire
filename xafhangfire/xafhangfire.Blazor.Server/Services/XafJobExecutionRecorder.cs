#nullable enable
using DevExpress.ExpressApp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using xafhangfire.Jobs;
using xafhangfire.Module.BusinessObjects;

namespace xafhangfire.Blazor.Server.Services;

public sealed class XafJobExecutionRecorder(
    INonSecuredObjectSpaceFactory objectSpaceFactory,
    IConfiguration configuration,
    IEmailSender emailSender,
    ILogger<XafJobExecutionRecorder> logger) : IJobExecutionRecorder
{
    // Serializes JobDefinition status updates so two failing runs can't both alert or lose an increment.
    // ponytail: process-wide lock; one app instance runs the Hangfire server. Use an atomic SQL
    // "ConsecutiveFailures = ConsecutiveFailures + 1 RETURNING" if it ever scales out.
    // Also taken by InterruptedRuns (JOB-010) at startup.
    internal static readonly object DefinitionStatusLock = new();

    private int FailureAlertThreshold => configuration.GetValue("Jobs:FailureAlertThreshold", 3);

    public Task<Guid> RecordStartAsync(string jobName, string jobTypeName, string? parametersJson, Guid? jobDefinitionId, CancellationToken cancellationToken = default)
    {
        try
        {
            lock (DefinitionStatusLock)
            {
                using var objectSpace = objectSpaceFactory.CreateNonSecuredObjectSpace<JobExecutionRecord>();

                var record = objectSpace.CreateObject<JobExecutionRecord>();
                record.JobName = jobName;
                record.JobTypeName = jobTypeName;
                record.StartedUtc = DateTime.UtcNow;
                record.Status = JobRunStatus.Running;
                record.ParametersJson = parametersJson ?? string.Empty;

                // Link by id when the caller knows its definition. The name lookup is only for ad-hoc
                // dispatches: a deleted definition must not fall back to an unrelated namesake.
                var jobDef = jobDefinitionId is { } id
                    ? objectSpace.FirstOrDefault<JobDefinition>(j => j.Id == id)
                    : objectSpace.FirstOrDefault<JobDefinition>(j => j.Name == jobName);
                if (jobDef != null)
                {
                    record.JobDefinition = jobDef;
                    record.JobName = jobDef.Name;
                    // The recorder owns all LastRun* writes (RunNow no longer stamps them).
                    jobDef.LastRunUtc = record.StartedUtc;
                    jobDef.LastRunStatus = JobRunStatus.Running;
                    jobDef.LastRunMessage = string.Empty; // a previous failure's message doesn't describe this run
                }

                objectSpace.CommitChanges();

                logger.LogDebug("Recorded job execution start: {RecordId} for '{JobName}'", record.Id, jobName);
                return Task.FromResult(record.Id);
            }
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
            lock (DefinitionStatusLock)
            {
                using var objectSpace = objectSpaceFactory.CreateNonSecuredObjectSpace<JobExecutionRecord>();

                var record = objectSpace.FirstOrDefault<JobExecutionRecord>(r => r.Id == recordId);
                if (record != null)
                {
                    record.CompletedUtc = DateTime.UtcNow;
                    record.Status = JobRunStatus.Success;
                    record.DurationMs = (long)(record.CompletedUtc.Value - record.StartedUtc).TotalMilliseconds;

                    // Update JobDefinition — reset consecutive failures on success
                    if (record.JobDefinition != null)
                    {
                        record.JobDefinition.LastRunUtc = record.CompletedUtc;
                        record.JobDefinition.LastRunStatus = JobRunStatus.Success;
                        record.JobDefinition.LastRunMessage = string.Empty;
                        record.JobDefinition.ConsecutiveFailures = 0;
                    }

                    objectSpace.CommitChanges();

                    logger.LogDebug("Recorded job completion: {RecordId} ({DurationMs}ms)", recordId, record.DurationMs);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to record job completion for {RecordId}", recordId);
        }

        return Task.CompletedTask;
    }

    public async Task RecordFailureAsync(Guid recordId, string errorMessage, CancellationToken cancellationToken = default)
    {
        if (recordId == Guid.Empty) return;

        (string JobName, int Failures)? alert = null;
        try
        {
            lock (DefinitionStatusLock)
            {
                using var objectSpace = objectSpaceFactory.CreateNonSecuredObjectSpace<JobExecutionRecord>();

                var record = objectSpace.FirstOrDefault<JobExecutionRecord>(r => r.Id == recordId);
                if (record != null)
                {
                    record.CompletedUtc = DateTime.UtcNow;
                    record.Status = JobRunStatus.Failed;
                    record.DurationMs = (long)(record.CompletedUtc.Value - record.StartedUtc).TotalMilliseconds;
                    record.ErrorMessage = errorMessage;

                    // Update JobDefinition — increment consecutive failures
                    var definition = record.JobDefinition;
                    if (definition != null)
                    {
                        definition.LastRunUtc = record.CompletedUtc;
                        definition.LastRunStatus = JobRunStatus.Failed;
                        definition.LastRunMessage = errorMessage.Length > 500
                            ? errorMessage[..500]
                            : errorMessage;
                        definition.ConsecutiveFailures++;
                    }

                    objectSpace.CommitChanges();
                    if (definition != null)
                        alert = (definition.Name, definition.ConsecutiveFailures);

                    logger.LogDebug("Recorded job failure: {RecordId} ({DurationMs}ms)", recordId, record.DurationMs);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to record job failure for {RecordId}", recordId);
        }

        // After the ObjectSpace is gone: no DB connection is held while SMTP runs.
        if (alert is { } a)
            await AlertOnRepeatedFailuresAsync(a.JobName, a.Failures, errorMessage);
    }

    /// <summary>
    /// Warns from the threshold on, and emails Jobs:FailureAlertRecipients once per failure streak
    /// (when the count reaches the threshold exactly). Never throws: the caller is reporting a job's
    /// own exception, which must not be replaced (JOB-004).
    /// </summary>
    internal async Task AlertOnRepeatedFailuresAsync(string jobName, int consecutiveFailures, string errorMessage)
    {
        try
        {
            var threshold = FailureAlertThreshold;
            if (threshold <= 0 || consecutiveFailures < threshold)
                return;

            logger.LogWarning(
                "Job '{JobName}' has failed {ConsecutiveFailures} consecutive times (threshold: {Threshold})",
                jobName, consecutiveFailures, threshold);

            var recipients = configuration["Jobs:FailureAlertRecipients"];
            if (consecutiveFailures != threshold || string.IsNullOrWhiteSpace(recipients))
                return;

            var name = (jobName ?? string.Empty).ReplaceLineEndings(" ");
            var subject = $"Job '{name}' failed {consecutiveFailures} times in a row";
            var body =
                $"Job: {name}\n" +
                $"Consecutive failures: {consecutiveFailures}\n" +
                $"Time (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}\n" +
                $"Error: {FirstLine(errorMessage)}\n\n" +
                "The full error is on the job's execution record.";

            foreach (var to in recipients.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    // The failure may be a cancellation; the alert still has to go out.
                    await emailSender.SendAsync(to, subject, body, isHtml: false, cancellationToken: CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to send the failure alert for job '{JobName}' to {Recipient}", jobName, to);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send the failure alert for job '{JobName}'", jobName);
        }
    }

    // The caller passes ex.ToString(). Keep "Type: message" plus any " ---> Inner: message" lines that
    // follow it (the real cause of a wrapper exception); stop at the first stack frame.
    private static string FirstLine(string message)
    {
        var lines = (message ?? string.Empty).Split('\n')
            .Select(l => l.Trim())
            .TakeWhile(l => l.Length > 0
                            && !l.StartsWith("at ", StringComparison.Ordinal)
                            && !l.StartsWith("--- End of", StringComparison.Ordinal));
        var summary = string.Join(" ", lines);
        return summary.Length > 500 ? summary[..500] : summary;
    }
}
