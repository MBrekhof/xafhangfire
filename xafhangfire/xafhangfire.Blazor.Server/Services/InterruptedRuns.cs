using System.Diagnostics;
using DevExpress.ExpressApp;
using Microsoft.Extensions.Logging;
using xafhangfire.Module.BusinessObjects;

namespace xafhangfire.Blazor.Server.Services;

/// <summary>
/// JOB-010: a run that was in progress when the app died is never completed by its executor.
/// At startup, everything still Running from before this process started is marked interrupted.
/// </summary>
// ponytail: assumes one app instance runs jobs (as the recorder's lock does); a second instance started
// later would mark the first one's live runs. Upgrade path: store a server id on the record.
internal static class InterruptedRuns
{
    internal const string Message = "Interrupted: the app stopped while this run was in progress.";

    public static void Clean(INonSecuredObjectSpaceFactory objectSpaceFactory, ILogger logger)
    {
        // Every run this process starts has StartedUtc >= its start time.
        using var process = Process.GetCurrentProcess();
        var cutoffUtc = process.StartTime.ToUniversalTime();

        // Same lock as the recorder's LastRun* writes, so a run starting right now isn't overwritten.
        lock (XafJobExecutionRecorder.DefinitionStatusLock)
        {
            using var objectSpace = objectSpaceFactory.CreateNonSecuredObjectSpace<JobExecutionRecord>();
            var records = objectSpace.GetObjectsQuery<JobExecutionRecord>()
                .Where(r => r.Status == JobRunStatus.Running).ToList();
            var definitions = objectSpace.GetObjectsQuery<JobDefinition>()
                .Where(d => d.LastRunStatus == JobRunStatus.Running).ToList();

            var (recordCount, definitionCount) = Mark(records, definitions, cutoffUtc);
            if (recordCount + definitionCount == 0) return;

            objectSpace.CommitChanges();
            logger.LogWarning("Marked {Records} job run(s) and {Definitions} job definition(s) interrupted by the previous app stop",
                recordCount, definitionCount);
        }
    }

    // The dates are compared here, in memory: the candidate rows are few (status Running) and
    // DateTime comparison ignores Kind; both sides are UTC ticks.
    // ConsecutiveFailures is left alone: an interruption says nothing about the job (and must not
    // shift the JOB-004 alert streak).
    internal static (int Records, int Definitions) Mark(
        IEnumerable<JobExecutionRecord> records, IEnumerable<JobDefinition> definitions, DateTime cutoffUtc)
    {
        var recordCount = 0;
        foreach (var record in records)
        {
            if (record.Status != JobRunStatus.Running || record.StartedUtc >= cutoffUtc) continue;
            record.Status = JobRunStatus.Failed;
            record.ErrorMessage = Message; // CompletedUtc/DurationMs stay empty: when it died is unknown
            recordCount++;
        }

        var definitionCount = 0;
        foreach (var definition in definitions)
        {
            if (definition.LastRunStatus != JobRunStatus.Running
                || definition.LastRunUtc is not { } lastRun || lastRun >= cutoffUtc) continue;
            definition.LastRunStatus = JobRunStatus.Failed;
            definition.LastRunMessage = Message;
            definitionCount++;
        }

        return (recordCount, definitionCount);
    }
}
