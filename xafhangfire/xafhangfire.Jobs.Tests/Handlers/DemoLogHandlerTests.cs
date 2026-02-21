using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using xafhangfire.Jobs.Commands;
using xafhangfire.Jobs.Handlers;

namespace xafhangfire.Jobs.Tests.Handlers;

public class DemoLogHandlerTests
{
    private readonly DemoLogHandler _sut = new(NullLogger<DemoLogHandler>.Instance);

    [Fact]
    public async Task ExecuteAsync_CompletesSuccessfully()
    {
        var command = new DemoLogCommand("Test message", DelaySeconds: 0);

        var act = () => _sut.ExecuteAsync(command);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_RespectsCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var command = new DemoLogCommand("Test", DelaySeconds: 5);

        var act = () => _sut.ExecuteAsync(command, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteAsync_WithDelay_WaitsExpectedDuration()
    {
        var command = new DemoLogCommand("Test", DelaySeconds: 1);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await _sut.ExecuteAsync(command);
        sw.Stop();

        sw.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(900));
    }
}
