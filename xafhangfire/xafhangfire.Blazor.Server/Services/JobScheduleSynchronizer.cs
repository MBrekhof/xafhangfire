using DevExpress.ExpressApp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using xafhangfire.Jobs;
using xafhangfire.Module.BusinessObjects;

namespace xafhangfire.Blazor.Server.Services;

/// <summary>
/// Reconciles recurring jobs with the JobDefinition table: one schedule per enabled definition with
/// a cron expression, everything else removed. Runs at startup and after every JobDefinition save
/// or delete in the UI (JobDefinitionScheduleSyncController).
/// </summary>
public sealed class JobScheduleSynchronizer(
    IServiceScopeFactory scopeFactory,
    IJobDispatcher dispatcher,
    ILogger<JobScheduleSynchronizer> logger) : IJobScheduleSynchronizer
{
    // ponytail: one global lock; syncs are rare (startup + admin saves) and touch a handful of rows.
    private readonly object syncLock = new();

    public static string ScheduleIdFor(Guid jobDefinitionId) => $"jobdef-{jobDefinitionId:N}";

    public void Sync()
    {
        lock (syncLock)
        {
            using var scope = scopeFactory.CreateScope();
            var objectSpaceFactory = scope.ServiceProvider.GetRequiredService<INonSecuredObjectSpaceFactory>();
            var dispatchService = scope.ServiceProvider.GetRequiredService<JobDispatchService>();

            using var objectSpace = objectSpaceFactory.CreateNonSecuredObjectSpace<JobDefinition>();
            var scheduledJobs = objectSpace.GetObjects<JobDefinition>()
                .Where(j => j.IsEnabled && !string.IsNullOrWhiteSpace(j.CronExpression))
                .ToList();

            var keep = new List<string>();
            foreach (var job in scheduledJobs)
            {
                var scheduleId = ScheduleIdFor(job.Id);
                try
                {
                    dispatchService.ScheduleByName(job.JobTypeName, job.ParametersJson, job.CronExpression, scheduleId, job.Id);
                    // Only kept once scheduling succeeded: an edit to an invalid cron or bad
                    // parameters removes the old schedule instead of leaving it firing.
                    keep.Add(scheduleId);
                    logger.LogInformation("JobScheduleSynchronizer: scheduled '{Name}' ({Type}) as '{ScheduleId}' with cron '{Cron}'",
                        job.Name, job.JobTypeName, scheduleId, job.CronExpression);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "JobScheduleSynchronizer: failed to schedule '{Name}'", job.Name);
                }
            }

            dispatcher.RemoveSchedulesExcept(keep);
            logger.LogInformation("JobScheduleSynchronizer: {Count} recurring job(s) active", keep.Count);
        }
    }
}
