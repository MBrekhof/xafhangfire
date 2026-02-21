using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace xafhangfire.Jobs.Tests;

public class DirectJobDispatcherTests
{
    private readonly IJobHandler<TestCommand> _handler = Substitute.For<IJobHandler<TestCommand>>();
    private readonly IJobScopeInitializer _initializer = Substitute.For<IJobScopeInitializer>();
    private readonly IJobExecutionRecorder _recorder = Substitute.For<IJobExecutionRecorder>();
    private readonly DirectJobDispatcher _sut;

    public record TestCommand(string Value);

    public DirectJobDispatcherTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_handler);
        services.AddSingleton<IJobScopeInitializer>(_initializer);
        services.AddSingleton<IJobExecutionRecorder>(_recorder);
        var provider = services.BuildServiceProvider();

        _sut = new DirectJobDispatcher(provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DirectJobDispatcher>.Instance);

        _recorder.RecordStartAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Guid.NewGuid());
    }

    [Fact]
    public async Task DispatchAsync_CallsInitializerBeforeHandler()
    {
        var callOrder = new List<string>();
        _initializer.InitializeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(_ => callOrder.Add("init"));
        _handler.ExecuteAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(_ => callOrder.Add("handler"));

        await _sut.DispatchAsync(new TestCommand("test"));

        callOrder.Should().ContainInOrder("init", "handler");
    }

    [Fact]
    public async Task DispatchAsync_PassesCommandToHandler()
    {
        var command = new TestCommand("hello");

        await _sut.DispatchAsync(command);

        await _handler.Received(1).ExecuteAsync(command, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_RecordsStartAndCompletion()
    {
        var recordId = Guid.NewGuid();
        _recorder.RecordStartAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(recordId);

        await _sut.DispatchAsync(new TestCommand("test"));

        await _recorder.Received(1).RecordStartAsync(
            "TestCommand", "TestCommand", Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _recorder.Received(1).RecordCompletionAsync(recordId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_OnFailure_RecordsFailureAndRethrows()
    {
        var recordId = Guid.NewGuid();
        _recorder.RecordStartAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(recordId);
        _handler.ExecuteAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("boom")));

        var act = () => _sut.DispatchAsync(new TestCommand("test"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        await _recorder.Received(1).RecordFailureAsync(recordId, Arg.Is<string>(s => s.Contains("boom")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Schedule_LogsWarning_DoesNotThrow()
    {
        var act = () => _sut.Schedule(new TestCommand("test"), "*/5 * * * *");

        act.Should().NotThrow();
    }
}
