using DevExpress.ExpressApp;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using xafhangfire.Blazor.Server.Services;
using xafhangfire.Module.BusinessObjects;

namespace xafhangfire.Jobs.Tests;

// JOB-011: a run cancelled through the job's token (Hangfire server stop, job deleted) is recorded as
// interrupted, not as a failure.
public class JobCancellationTests
{
    private readonly IJobHandler<TestCommand> _handler = Substitute.For<IJobHandler<TestCommand>>();
    private readonly IJobScopeInitializer _initializer = Substitute.For<IJobScopeInitializer>();
    private readonly IJobExecutionRecorder _recorder = Substitute.For<IJobExecutionRecorder>();
    private readonly Guid _recordId = Guid.NewGuid();

    public record TestCommand(string Value);

    public JobCancellationTests()
    {
        _recorder.RecordStartAsync(default!, default!, default, default, default).ReturnsForAnyArgs(_recordId);
    }

    // Both places that run a handler have the same catch blocks.
    private Func<CancellationToken, Task> Runner(string kind)
    {
        if (kind == "executor")
        {
            var executor = new JobExecutor<TestCommand>(_handler, _initializer, _recorder, new NullJobProgressReporter());
            return ct => executor.RunAsync(new TestCommand("x"), Guid.NewGuid(), ct);
        }

        var services = new ServiceCollection();
        services.AddSingleton(_handler);
        services.AddSingleton(_initializer);
        services.AddSingleton(_recorder);
        var dispatcher = new DirectJobDispatcher(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), NullLogger<DirectJobDispatcher>.Instance);
        return ct => dispatcher.DispatchAsync(new TestCommand("x"), Guid.NewGuid(), ct);
    }

    private static CancellationToken Cancelled() => new(canceled: true);

    private void HandlerThrows(Exception ex) =>
        _handler.ExecuteAsync(default!, default).ReturnsForAnyArgs(Task.FromException(ex));

    [Theory]
    [InlineData("executor", false)]
    [InlineData("executor", true)]
    [InlineData("dispatcher", false)]
    [InlineData("dispatcher", true)]
    public async Task CancelledToken_RecordsInterrupted_NotFailure_AndRethrows(string kind, bool taskCanceled)
    {
        var token = Cancelled();
        HandlerThrows(taskCanceled ? new TaskCanceledException() : new OperationCanceledException(token));

        await Runner(kind)(token).Invoking(t => t).Should().ThrowAsync<OperationCanceledException>();

        await _recorder.Received(1).RecordInterruptedAsync(_recordId, CancellationToken.None);
        await _recorder.DidNotReceiveWithAnyArgs().RecordFailureAsync(default, default!, default);
        await _recorder.DidNotReceiveWithAnyArgs().RecordCompletionAsync(default, default);
    }

    [Theory]
    [InlineData("executor")]
    [InlineData("dispatcher")]
    public async Task TaskCanceled_WithoutJobCancellation_IsAFailure(string kind)
    {
        // e.g. an HTTP client timeout inside the handler
        HandlerThrows(new TaskCanceledException("timeout"));

        await Runner(kind)(CancellationToken.None).Invoking(t => t).Should().ThrowAsync<TaskCanceledException>();

        await _recorder.Received(1).RecordFailureAsync(_recordId, Arg.Is<string>(s => s.Contains("timeout")), Arg.Any<CancellationToken>());
        await _recorder.DidNotReceiveWithAnyArgs().RecordInterruptedAsync(default, default);
    }

    [Theory]
    [InlineData("executor")]
    [InlineData("dispatcher")]
    public async Task OtherException_WhileCancelled_IsAFailure(string kind)
    {
        HandlerThrows(new InvalidOperationException("boom"));

        await Runner(kind)(Cancelled()).Invoking(t => t).Should().ThrowAsync<InvalidOperationException>();

        await _recorder.Received(1).RecordFailureAsync(_recordId, Arg.Is<string>(s => s.Contains("boom")), CancellationToken.None);
        await _recorder.DidNotReceiveWithAnyArgs().RecordInterruptedAsync(default, default);
    }

    [Fact]
    public void MarkCancelled_SetsRecordAndDefinition_KeepsFailureCount()
    {
        var started = new DateTime(2026, 10, 26, 21, 39, 8, DateTimeKind.Unspecified);
        var completed = new DateTime(2026, 10, 26, 21, 39, 21, DateTimeKind.Utc);
        var definition = new JobDefinition { LastRunStatus = JobRunStatus.Running, ConsecutiveFailures = 2 };
        var record = new JobExecutionRecord { StartedUtc = started, JobDefinition = definition };

        XafJobExecutionRecorder.MarkCancelled(record, completed);

        record.Status.Should().Be(JobRunStatus.Failed);
        record.ErrorMessage.Should().Be(XafJobExecutionRecorder.CancelledMessage);
        record.CompletedUtc.Should().Be(completed);
        record.DurationMs.Should().Be(13_000);
        definition.LastRunStatus.Should().Be(JobRunStatus.Failed);
        definition.LastRunMessage.Should().Be(XafJobExecutionRecorder.CancelledMessage);
        definition.LastRunUtc.Should().Be(completed);
        definition.ConsecutiveFailures.Should().Be(2);
    }

    [Fact]
    public void MarkCancelled_WithoutDefinition_UpdatesRecordOnly()
    {
        var record = new JobExecutionRecord { StartedUtc = DateTime.UtcNow.AddSeconds(-1) };

        XafJobExecutionRecorder.MarkCancelled(record, DateTime.UtcNow);

        record.Status.Should().Be(JobRunStatus.Failed);
        record.JobDefinition.Should().BeNull();
    }

    [Fact]
    public async Task RecordInterrupted_WithoutRecord_DoesNothing()
    {
        var factory = Substitute.For<INonSecuredObjectSpaceFactory>();
        var recorder = new XafJobExecutionRecorder(factory, new ConfigurationBuilder().Build(),
            Substitute.For<IEmailSender>(), NullLogger<XafJobExecutionRecorder>.Instance);

        await recorder.RecordInterruptedAsync(Guid.Empty);

        factory.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task RecordInterrupted_WhenTheDatabaseFails_DoesNotThrow()
    {
        // It runs inside the cancellation catch; an escaping error would replace the
        // OperationCanceledException and stop Hangfire's requeue.
        var factory = Substitute.For<INonSecuredObjectSpaceFactory>();
        factory.CreateNonSecuredObjectSpace(default(Type)!).ReturnsForAnyArgs(_ => throw new InvalidOperationException("DB shutting down"));
        var recorder = new XafJobExecutionRecorder(factory, new ConfigurationBuilder().Build(),
            Substitute.For<IEmailSender>(), NullLogger<XafJobExecutionRecorder>.Instance);

        await recorder.Invoking(r => r.RecordInterruptedAsync(Guid.NewGuid())).Should().NotThrowAsync();
        factory.ReceivedCalls().Should().NotBeEmpty();
    }
}
