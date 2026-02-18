using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using xafhangfire.Jobs;
using xafhangfire.Module.BusinessObjects;

namespace xafhangfire.Blazor.Server.Services;

public sealed class JobSyncService(
    IServiceProvider serviceProvider,
    ILogger<JobSyncService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for the XAF application to fully initialize before querying ObjectSpace.
        // Without this delay, INonSecuredObjectSpaceFactory may not be available yet.
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        try
        {
            SyncRecurringJobs();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to sync JobDefinitions to Hangfire recurring jobs");
        }
    }

    private void SyncRecurringJobs()
    {
        using var scope = serviceProvider.CreateScope();
        var objectSpaceFactory = scope.ServiceProvider.GetRequiredService<INonSecuredObjectSpaceFactory>();
        var dispatchService = scope.ServiceProvider.GetRequiredService<JobDispatchService>();

        using var objectSpace = objectSpaceFactory.CreateNonSecuredObjectSpace<JobDefinition>();
        var enabledJobs = objectSpace.GetObjects<JobDefinition>()
            .Where(j => j.IsEnabled && !string.IsNullOrWhiteSpace(j.CronExpression))
            .ToList();

        logger.LogInformation("JobSyncService: found {Count} enabled jobs with cron schedules", enabledJobs.Count);

        foreach (var job in enabledJobs)
        {
            try
            {
                dispatchService.ScheduleByName(job.JobTypeName, job.ParametersJson, job.CronExpression);
                logger.LogInformation("JobSyncService: registered recurring job '{Name}' ({Type}) with cron '{Cron}'",
                    job.Name, job.JobTypeName, job.CronExpression);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "JobSyncService: failed to register recurring job '{Name}'", job.Name);
            }
        }
    }
}
