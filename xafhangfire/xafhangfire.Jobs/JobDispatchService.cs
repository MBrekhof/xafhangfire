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
        Guid? jobDefinitionId = null,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Dispatching job '{JobType}' with params: {Params}",
            jobTypeName, parametersJson ?? "(none)");

        switch (jobTypeName)
        {
            case nameof(DemoLogCommand):
                await dispatcher.DispatchAsync(Parse(parametersJson, new DemoLogCommand("Manual run")), jobDefinitionId, cancellationToken);
                break;

            case nameof(ListUsersCommand):
                await dispatcher.DispatchAsync(Parse(parametersJson, new ListUsersCommand()), jobDefinitionId, cancellationToken);
                break;

            case nameof(GenerateReportCommand):
                await dispatcher.DispatchAsync(Parse(parametersJson, new GenerateReportCommand("Project Status Report")), jobDefinitionId, cancellationToken);
                break;

            case nameof(SendEmailCommand):
                await dispatcher.DispatchAsync(Parse(parametersJson, new SendEmailCommand("", "Test", "Test email")), jobDefinitionId, cancellationToken);
                break;

            case nameof(SendReportEmailCommand):
                await dispatcher.DispatchAsync(Parse(parametersJson, new SendReportEmailCommand("Project Status Report", "admin@example.com")), jobDefinitionId, cancellationToken);
                break;

            case nameof(SendMailMergeCommand):
                await dispatcher.DispatchAsync(Parse(parametersJson, new SendMailMergeCommand("Welcome Contact")), jobDefinitionId, cancellationToken);
                break;

            default:
                throw new InvalidOperationException($"Unknown job type: '{jobTypeName}'");
        }
    }

    public void ScheduleByName(
        string jobTypeName,
        string? parametersJson,
        string cronExpression,
        string scheduleId,
        Guid? jobDefinitionId)
    {
        logger.LogInformation("Scheduling job '{JobType}' as '{ScheduleId}' with cron '{Cron}'",
            jobTypeName, scheduleId, cronExpression);

        switch (jobTypeName)
        {
            case nameof(DemoLogCommand):
                dispatcher.Schedule(Parse(parametersJson, new DemoLogCommand("Scheduled run")), cronExpression, scheduleId, jobDefinitionId);
                break;

            case nameof(ListUsersCommand):
                dispatcher.Schedule(Parse(parametersJson, new ListUsersCommand()), cronExpression, scheduleId, jobDefinitionId);
                break;

            case nameof(GenerateReportCommand):
                dispatcher.Schedule(Parse(parametersJson, new GenerateReportCommand("Project Status Report")), cronExpression, scheduleId, jobDefinitionId);
                break;

            case nameof(SendEmailCommand):
                dispatcher.Schedule(Parse(parametersJson, new SendEmailCommand("", "Test", "Test email")), cronExpression, scheduleId, jobDefinitionId);
                break;

            case nameof(SendReportEmailCommand):
                dispatcher.Schedule(Parse(parametersJson, new SendReportEmailCommand("Project Status Report", "admin@example.com")), cronExpression, scheduleId, jobDefinitionId);
                break;

            case nameof(SendMailMergeCommand):
                dispatcher.Schedule(Parse(parametersJson, new SendMailMergeCommand("Welcome Contact")), cronExpression, scheduleId, jobDefinitionId);
                break;

            default:
                // Thrown so the synchronizer does not keep (or create) a schedule for it.
                throw new InvalidOperationException($"Cannot schedule unknown job type: '{jobTypeName}'");
        }
    }

    private static T Parse<T>(string? parametersJson, T fallback) =>
        string.IsNullOrWhiteSpace(parametersJson)
            ? fallback
            : JsonSerializer.Deserialize<T>(parametersJson) ?? fallback;
}
