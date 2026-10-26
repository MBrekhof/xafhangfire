using System.Text.Json;
using Hangfire;

namespace xafhangfire.Jobs;

public sealed class JobExecutor<TCommand>(
    IJobHandler<TCommand> handler,
    IJobScopeInitializer scopeInitializer,
    IJobExecutionRecorder executionRecorder,
    IJobProgressReporter progressReporter)
{
    // Kept for Hangfire jobs serialized before jobDefinitionId existed (recurring jobs can fire
    // before the startup sync re-registers them with the new signature).
    [AutomaticRetry(Attempts = 3)]
    public Task RunAsync(TCommand command, CancellationToken cancellationToken)
        => RunAsync(command, null, cancellationToken);

    [AutomaticRetry(Attempts = 3)]
    public async Task RunAsync(TCommand command, Guid? jobDefinitionId, CancellationToken cancellationToken)
    {
        await scopeInitializer.InitializeAsync(cancellationToken);

        var commandType = typeof(TCommand).Name;
        string? parametersJson = null;
        try { parametersJson = JsonSerializer.Serialize(command, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }); } catch { /* best-effort */ }

        var recordId = await executionRecorder.RecordStartAsync(commandType, commandType, parametersJson, jobDefinitionId, cancellationToken);
        progressReporter.Initialize(recordId);
        try
        {
            await handler.ExecuteAsync(command, cancellationToken);
            await executionRecorder.RecordCompletionAsync(recordId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Server stop (Hangfire requeues the job) or job deleted: not a failure (JOB-011).
            // The job's token is already cancelled, so the recorder gets None.
            await executionRecorder.RecordInterruptedAsync(recordId, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            // None: the token may be cancelled (another exception during a stop); the failure must still be recorded.
            await executionRecorder.RecordFailureAsync(recordId, ex.ToString(), CancellationToken.None);
            throw;
        }
    }
}
