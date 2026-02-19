using System.Text.Json;
using Microsoft.Extensions.Logging;
using xafhangfire.Jobs.Commands;

namespace xafhangfire.Jobs;

public sealed class JobDispatchService(
    IJobDispatcher dispatcher,
    ILogger<JobDispatchService> logger)
{
    public async Task DispatchByNameAsync(
        string jobTypeName,
        string? parametersJson = null,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Dispatching job '{JobType}' with params: {Params}",
            jobTypeName, parametersJson ?? "(none)");

        switch (jobTypeName)
        {
            case nameof(DemoLogCommand):
                var demoCmd = string.IsNullOrWhiteSpace(parametersJson)
                    ? new DemoLogCommand("Manual run")
                    : JsonSerializer.Deserialize<DemoLogCommand>(parametersJson)
                      ?? new DemoLogCommand("Manual run");
                await dispatcher.DispatchAsync(demoCmd, cancellationToken);
                break;

            case nameof(ListUsersCommand):
                var listCmd = string.IsNullOrWhiteSpace(parametersJson)
                    ? new ListUsersCommand()
                    : JsonSerializer.Deserialize<ListUsersCommand>(parametersJson)
                      ?? new ListUsersCommand();
                await dispatcher.DispatchAsync(listCmd, cancellationToken);
                break;

            case nameof(GenerateReportCommand):
                var reportCmd = string.IsNullOrWhiteSpace(parametersJson)
                    ? new GenerateReportCommand("Project Status Report")
                    : JsonSerializer.Deserialize<GenerateReportCommand>(parametersJson)
                      ?? new GenerateReportCommand("Project Status Report");
                await dispatcher.DispatchAsync(reportCmd, cancellationToken);
                break;

            default:
                throw new InvalidOperationException($"Unknown job type: '{jobTypeName}'");
        }
    }

    public void ScheduleByName(
        string jobTypeName,
        string? parametersJson,
        string cronExpression)
    {
        logger.LogInformation("Scheduling job '{JobType}' with cron '{Cron}'",
            jobTypeName, cronExpression);

        switch (jobTypeName)
        {
            case nameof(DemoLogCommand):
                var demoCmd = string.IsNullOrWhiteSpace(parametersJson)
                    ? new DemoLogCommand("Scheduled run")
                    : JsonSerializer.Deserialize<DemoLogCommand>(parametersJson)
                      ?? new DemoLogCommand("Scheduled run");
                dispatcher.Schedule(demoCmd, cronExpression);
                break;

            case nameof(ListUsersCommand):
                var listCmd = string.IsNullOrWhiteSpace(parametersJson)
                    ? new ListUsersCommand()
                    : JsonSerializer.Deserialize<ListUsersCommand>(parametersJson)
                      ?? new ListUsersCommand();
                dispatcher.Schedule(listCmd, cronExpression);
                break;

            case nameof(GenerateReportCommand):
                var reportScheduleCmd = string.IsNullOrWhiteSpace(parametersJson)
                    ? new GenerateReportCommand("Project Status Report")
                    : JsonSerializer.Deserialize<GenerateReportCommand>(parametersJson)
                      ?? new GenerateReportCommand("Project Status Report");
                dispatcher.Schedule(reportScheduleCmd, cronExpression);
                break;

            default:
                logger.LogWarning("Cannot schedule unknown job type: '{JobType}'", jobTypeName);
                break;
        }
    }
}
