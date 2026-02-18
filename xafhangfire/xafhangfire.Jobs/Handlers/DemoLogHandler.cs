using Microsoft.Extensions.Logging;
using xafhangfire.Jobs.Commands;

namespace xafhangfire.Jobs.Handlers;

public sealed class DemoLogHandler(
    ILogger<DemoLogHandler> logger) : IJobHandler<DemoLogCommand>
{
    public async Task ExecuteAsync(
        DemoLogCommand command,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("DemoLogHandler: START — {Message}", command.Message);

        for (var i = 1; i <= command.DelaySeconds; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            logger.LogInformation("DemoLogHandler: working... {Second}/{Total}", i, command.DelaySeconds);
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        logger.LogInformation("DemoLogHandler: DONE — {Message}", command.Message);
    }
}
