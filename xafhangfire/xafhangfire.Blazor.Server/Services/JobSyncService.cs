using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using xafhangfire.Jobs;

namespace xafhangfire.Blazor.Server.Services;

/// <summary>Runs the schedule sync once at startup.</summary>
public sealed class JobSyncService(
    IJobScheduleSynchronizer synchronizer,
    ILogger<JobSyncService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for the XAF application to fully initialize before querying ObjectSpace.
        // Without this delay, INonSecuredObjectSpaceFactory may not be available yet.
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        try
        {
            synchronizer.Sync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to sync JobDefinitions to recurring jobs");
        }
    }
}
