using DevExpress.ExpressApp;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using xafhangfire.Blazor.Server.Services;

namespace xafhangfire.Jobs.Tests;

// JOB-004: email Jobs:FailureAlertRecipients once per failure streak.
public class JobFailureAlertTests
{
    // \r\n like a real ex.ToString() on Windows.
    private const string Error = "System.InvalidOperationException: Report 'X' not found\r\n   at Foo.Bar() in C:\\src\\Foo.cs:line 42";
    private readonly IEmailSender _email = Substitute.For<IEmailSender>();
    private readonly WarningCounter _log = new();

    private XafJobExecutionRecorder Recorder(string? recipients = "ops@example.com", string? threshold = "3")
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jobs:FailureAlertRecipients"] = recipients,
                ["Jobs:FailureAlertThreshold"] = threshold,
            })
            .Build();
        return new XafJobExecutionRecorder(
            Substitute.For<INonSecuredObjectSpaceFactory>(), config, _email, _log);
    }

    private Task Sent(int times) =>
        _email.ReceivedWithAnyArgs(times).SendAsync(default!, default!, default!, default, default, default);

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 0)]
    [InlineData(4, 1)] // past the threshold: still warns, no second email
    public async Task NotAtThreshold_SendsNothing(int failures, int warnings)
    {
        await Recorder().AlertOnRepeatedFailuresAsync("Job", failures, Error);
        await Sent(0);
        _log.Warnings.Should().Be(warnings);
    }

    [Fact]
    public async Task AtThreshold_SendsPlainTextToEachRecipient_WithoutStackTrace()
    {
        await Recorder("a@example.com; b@example.com, c@example.com").AlertOnRepeatedFailuresAsync("Nightly Report", 3, Error);

        foreach (var to in new[] { "a@example.com", "b@example.com", "c@example.com" })
            await _email.Received(1).SendAsync(
                to,
                "Job 'Nightly Report' failed 3 times in a row",
                Arg.Is<string>(b => b.Contains("Nightly Report")
                                    && b.Contains("Consecutive failures: 3")
                                    && b.Contains("Error: System.InvalidOperationException: Report 'X' not found\n")
                                    && !b.Contains('\r')
                                    && !b.Contains("Foo.cs")),
                false,
                null,
                CancellationToken.None);
        await Sent(3);
        _log.Warnings.Should().Be(1);
    }

    [Fact]
    public async Task CustomThreshold_SendsAtThatCountOnly()
    {
        var recorder = Recorder(threshold: "2");
        await recorder.AlertOnRepeatedFailuresAsync("Job", 2, Error);
        await recorder.AlertOnRepeatedFailuresAsync("Job", 3, Error);
        await Sent(1);
        await _email.Received(1).SendAsync("ops@example.com", "Job 'Job' failed 2 times in a row",
            Arg.Any<string>(), false, null, CancellationToken.None);
    }

    [Fact]
    public async Task WrappedException_KeepsTheInnerCause_AndDropsStackFrames()
    {
        string error;
        try
        {
            try { throw new IOException("disk full"); }
            catch (Exception inner) { throw new InvalidOperationException("See the inner exception for details.", inner); }
        }
        catch (Exception ex) { error = ex.ToString(); }

        await Recorder().AlertOnRepeatedFailuresAsync("Job", 3, error);

        await _email.Received(1).SendAsync("ops@example.com", Arg.Any<string>(),
            Arg.Is<string>(b => b.Contains("Error: System.InvalidOperationException: See the inner exception for details. ---> System.IO.IOException: disk full\n")
                                && !b.Contains(" at ")
                                && !b.Contains("End of inner")),
            false, null, CancellationToken.None);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" ; , ")]
    public async Task NoRecipients_SendsNothing(string? recipients)
    {
        await Recorder(recipients).AlertOnRepeatedFailuresAsync("Job", 3, Error);
        await Sent(0);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("0", 1)]
    [InlineData("-1", -1)]
    [InlineData("-1", 1)]
    public async Task ThresholdZeroOrLess_SendsNothing_AndDoesNotWarn(string threshold, int failures)
    {
        await Recorder(threshold: threshold).AlertOnRepeatedFailuresAsync("Job", failures, Error);
        await Sent(0);
        _log.Warnings.Should().Be(0);
    }

    [Fact]
    public async Task MalformedThreshold_DoesNotThrow()
    {
        var act = () => Recorder(threshold: "three").AlertOnRepeatedFailuresAsync("Job", 3, Error);
        await act.Should().NotThrowAsync();
        await Sent(0);
    }

    [Fact]
    public async Task FailingRecipient_DoesNotStopTheOthers_AndDoesNotThrow()
    {
        _email.SendAsync("bad@example.com", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(),
                Arg.Any<IReadOnlyList<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("SMTP down"));

        var act = () => Recorder("bad@example.com;good@example.com").AlertOnRepeatedFailuresAsync("Job", 3, Error);

        await act.Should().NotThrowAsync();
        await Sent(2);
        await _email.Received(1).SendAsync("good@example.com", Arg.Any<string>(), Arg.Any<string>(), false, null, CancellationToken.None);
    }

    [Fact]
    public async Task LongFirstLine_IsTruncated_AndNameLineBreaksAreFlattened()
    {
        await Recorder().AlertOnRepeatedFailuresAsync("Line1\r\nLine2", 3, new string('x', 900));

        await _email.Received(1).SendAsync(
            "ops@example.com",
            "Job 'Line1 Line2' failed 3 times in a row",
            Arg.Is<string>(b => b.Contains("Error: " + new string('x', 500) + "\n") && !b.Contains(new string('x', 501))),
            false, null, CancellationToken.None);
    }

    private sealed class WarningCounter : ILogger<XafJobExecutionRecorder>
    {
        public int Warnings;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Warnings++;
        }
    }
}
