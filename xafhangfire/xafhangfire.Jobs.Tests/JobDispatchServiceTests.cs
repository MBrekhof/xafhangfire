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
            Arg.Any<Guid?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchByName_DemoLogCommand_WithJson_DeserializesParameters()
    {
        await _sut.DispatchByNameAsync("DemoLogCommand", "{\"Message\":\"Custom\",\"DelaySeconds\":5}");

        await _dispatcher.Received(1).DispatchAsync(
            Arg.Is<DemoLogCommand>(c => c.Message == "Custom" && c.DelaySeconds == 5),
            Arg.Any<Guid?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchByName_ListUsersCommand_Dispatches()
    {
        await _sut.DispatchByNameAsync("ListUsersCommand");

        await _dispatcher.Received(1).DispatchAsync(
            Arg.Any<ListUsersCommand>(),
            Arg.Any<Guid?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchByName_GenerateReportCommand_DispatchesWithDefaultReport()
    {
        await _sut.DispatchByNameAsync("GenerateReportCommand");

        await _dispatcher.Received(1).DispatchAsync(
            Arg.Is<GenerateReportCommand>(c => c.ReportName == "Project Status Report"),
            Arg.Any<Guid?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchByName_SendEmailCommand_Dispatches()
    {
        await _sut.DispatchByNameAsync("SendEmailCommand",
            "{\"To\":\"test@example.com\",\"Subject\":\"Hi\",\"Body\":\"Hello\"}");

        await _dispatcher.Received(1).DispatchAsync(
            Arg.Is<SendEmailCommand>(c => c.To == "test@example.com"),
            Arg.Any<Guid?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchByName_SendReportEmailCommand_Dispatches()
    {
        await _sut.DispatchByNameAsync("SendReportEmailCommand");

        await _dispatcher.Received(1).DispatchAsync(
            Arg.Is<SendReportEmailCommand>(c => c.ReportName == "Project Status Report"),
            Arg.Any<Guid?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchByName_SendMailMergeCommand_Dispatches()
    {
        await _sut.DispatchByNameAsync("SendMailMergeCommand");

        await _dispatcher.Received(1).DispatchAsync(
            Arg.Is<SendMailMergeCommand>(c => c.TemplateName == "Welcome Contact"),
            Arg.Any<Guid?>(),
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
            Arg.Any<Guid?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchByName_EmptyParams_UsesDefaults()
    {
        await _sut.DispatchByNameAsync("DemoLogCommand", "  ");

        await _dispatcher.Received(1).DispatchAsync(
            Arg.Is<DemoLogCommand>(c => c.Message == "Manual run"),
            Arg.Any<Guid?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ScheduleByName_DemoLogCommand_CallsSchedule()
    {
        var id = Guid.NewGuid();

        _sut.ScheduleByName("DemoLogCommand", null, "*/5 * * * *", "jobdef-1", id);

        _dispatcher.Received(1).Schedule(
            Arg.Is<DemoLogCommand>(c => c.Message == "Scheduled run"),
            "*/5 * * * *",
            "jobdef-1",
            id);
    }

    [Fact]
    public void ScheduleByName_UnknownType_Throws()
    {
        // Thrown so the schedule synchronizer does not keep a schedule for it.
        var act = () => _sut.ScheduleByName("NonExistentCommand", null, "* * * * *", "jobdef-1", Guid.NewGuid());

        act.Should().Throw<InvalidOperationException>();
    }

    public static TheoryData<string> AllJobTypes =>
        new(CommandMetadataProvider.GetRegisteredTypeNames());

    // JOB-002: every command type must carry the JobDefinition id to the dispatcher.
    [Theory]
    [MemberData(nameof(AllJobTypes))]
    public async Task DispatchByName_EveryType_PassesJobDefinitionId(string jobTypeName)
    {
        var id = Guid.NewGuid();

        await _sut.DispatchByNameAsync(jobTypeName, null, id);

        _dispatcher.ReceivedCalls().Should().ContainSingle(c =>
            c.GetMethodInfo().Name == nameof(IJobDispatcher.DispatchAsync)
            && (Guid?)c.GetArguments()[1] == id);
    }

    // JOB-001: every command type must be scheduled under the per-definition id, not its type name.
    [Theory]
    [MemberData(nameof(AllJobTypes))]
    public void ScheduleByName_EveryType_PassesScheduleIdAndJobDefinitionId(string jobTypeName)
    {
        var id = Guid.NewGuid();

        _sut.ScheduleByName(jobTypeName, null, "0 8 * * *", "jobdef-abc", id);

        _dispatcher.ReceivedCalls().Should().ContainSingle(c =>
            c.GetMethodInfo().Name == nameof(IJobDispatcher.Schedule)
            && (string)c.GetArguments()[2]! == "jobdef-abc"
            && (Guid?)c.GetArguments()[3] == id);
    }
}
