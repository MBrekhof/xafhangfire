using System.Text.Json;
using Hangfire;

namespace xafhangfire.Jobs;

public sealed class JobExecutor<TCommand>(
    IJobHandler<TCommand> handler,
    IJobScopeInitializer scopeInitializer,
    IJobExecutionRecorder executionRecorder,
    IJobProgressReporter progressReporter)
{
    [AutomaticRetry(Attempts = 3)]
    public async Task RunAsync(TCommand command, CancellationToken cancellationToken)
    {
        await scopeInitializer.InitializeAsync(cancellationToken);

        var commandType = typeof(TCommand).Name;
        string? parametersJson = null;
        try { parametersJson = JsonSerializer.Serialize(command); } catch { /* best-effort */ }

        var recordId = await executionRecorder.RecordStartAsync(commandType, commandType, parametersJson, cancellationToken);
        progressReporter.Initialize(recordId);
        try
        {
            await handler.ExecuteAsync(command, cancellationToken);
            await executionRecorder.RecordCompletionAsync(recordId, cancellationToken);
        }
        catch (Exception ex)
        {
            await executionRecorder.RecordFailureAsync(recordId, ex.ToString(), cancellationToken);
            throw;
        }
    }
}
