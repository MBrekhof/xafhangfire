using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using xafhangfire.Jobs.Commands;

namespace xafhangfire.Jobs.Tests;

public class JobDispatchServiceTests
{
    private readonly IJobDispatcher _dispatcher = Substitute.For<IJobDispatcher>();
    private readonly JobDispatchService _sut;

    public JobDispatchServiceTests()
    {
        _sut = new JobDispatchService(_dispatcher, NullLogger<JobDispatchService>.Instance);
    }

    [Fact]
    public async Task DispatchByName_DemoLogCommand_DispatchesWithDefaultMessage()
    {
        await _sut.DispatchByNameAsync("DemoLogCommand");

        await _dispatcher.Received(1).DispatchAsync(
            Arg.Is<DemoLogCommand>(c => c.Message == "Manual run"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchByName_DemoLogCommand_WithJson_DeserializesParameters()
    {
        await _sut.DispatchByNameAsync("DemoLogCommand", "{\"Message\":\"Custom\",\"DelaySeconds\":5}");

        await _dispatcher.Received(1).DispatchAsync(
            Arg.Is<DemoLogCommand>(c => c.Message == "Custom" && c.DelaySeconds == 5),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchByName_ListUsersCommand_Dispatches()
    {
        await _sut.DispatchByNameAsync("ListUsersCommand");

        await _dispatcher.Received(1).DispatchAsync(
            Arg.Any<ListUsersCommand>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchByName_GenerateReportCommand_DispatchesWithDefaultReport()
    {
        await _sut.DispatchByNameAsync("GenerateReportCommand");

        await _dispatcher.Received(1).DispatchAsync(
            Arg.Is<GenerateReportCommand>(c => c.ReportName == "Project Status Report"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchByName_SendEmailCommand_Dispatches()
    {
        await _sut.DispatchByNameAsync("SendEmailCommand",
            "{\"To\":\"test@example.com\",\"Subject\":\"Hi\",\"Body\":\"Hello\"}");

        await _dispatcher.Received(1).DispatchAsync(
            Arg.Is<SendEmailCommand>(c => c.To == "test@example.com"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchByName_SendReportEmailCommand_Dispatches()
    {
        await _sut.DispatchByNameAsync("SendReportEmailCommand");

        await _dispatcher.Received(1).DispatchAsync(
            Arg.Is<SendReportEmailCommand>(c => c.ReportName == "Project Status Report"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchByName_SendMailMergeCommand_Dispatches()
    {
        await _sut.DispatchByNameAsync("SendMailMergeCommand");

        await _dispatcher.Received(1).DispatchAsync(
            Arg.Is<SendMailMergeCommand>(c => c.TemplateName == "Welcome Contact"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchByName_UnknownType_ThrowsInvalidOperationException()
    {
        var act = () => _sut.DispatchByNameAsync("NonExistentCommand");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Unknown job type*");
    }

    [Fact]
    public async Task DispatchByName_NullParams_UsesDefaults()
    {
        await _sut.DispatchByNameAsync("DemoLogCommand", null);

        await _dispatcher.Received(1).DispatchAsync(
            Arg.Is<DemoLogCommand>(c => c.Message == "Manual run"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchByName_EmptyParams_UsesDefaults()
    {
        await _sut.DispatchByNameAsync("DemoLogCommand", "  ");

        await _dispatcher.Received(1).DispatchAsync(
            Arg.Is<DemoLogCommand>(c => c.Message == "Manual run"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ScheduleByName_DemoLogCommand_CallsSchedule()
    {
        _sut.ScheduleByName("DemoLogCommand", null, "*/5 * * * *");

        _dispatcher.Received(1).Schedule(
            Arg.Is<DemoLogCommand>(c => c.Message == "Scheduled run"),
            "*/5 * * * *");
    }

    [Fact]
    public void ScheduleByName_UnknownType_DoesNotThrow()
    {
        // ScheduleByName logs a warning but doesn't throw
        var act = () => _sut.ScheduleByName("NonExistentCommand", null, "* * * * *");

        act.Should().NotThrow();
    }
}
