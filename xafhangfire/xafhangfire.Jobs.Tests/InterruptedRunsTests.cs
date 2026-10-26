using FluentAssertions;
using xafhangfire.Blazor.Server.Services;
using xafhangfire.Module.BusinessObjects;

namespace xafhangfire.Jobs.Tests;

// JOB-010: runs left Running by a crashed process are marked interrupted at startup.
public class InterruptedRunsTests
{
    private static readonly DateTime Cutoff = new(2026, 10, 26, 20, 0, 0, DateTimeKind.Utc);
    // Npgsql reads `timestamp without time zone` back as Unspecified (same UTC ticks).
    private static DateTime At(int seconds) => DateTime.SpecifyKind(Cutoff.AddSeconds(seconds), DateTimeKind.Unspecified);

    private static JobExecutionRecord Record(int startedAt, JobRunStatus status = JobRunStatus.Running) =>
        new() { StartedUtc = At(startedAt), Status = status };

    private static JobDefinition Definition(int? lastRunAt, JobRunStatus status = JobRunStatus.Running) =>
        new() { LastRunUtc = lastRunAt is { } s ? At(s) : null, LastRunStatus = status, ConsecutiveFailures = 2, LastRunMessage = "old" };

    [Fact]
    public void RunningRecordStartedBeforeCutoff_IsFailedAsInterrupted()
    {
        var record = Record(-60);

        InterruptedRuns.Mark([record], [], Cutoff).Should().Be((1, 0));

        record.Status.Should().Be(JobRunStatus.Failed);
        record.ErrorMessage.Should().Be(InterruptedRuns.Message);
        record.CompletedUtc.Should().BeNull();
        record.DurationMs.Should().Be(0);
    }

    [Theory]
    [InlineData(0)] // started at the very moment the process started: this process's run
    [InlineData(5)]
    public void RunningRecordStartedAtOrAfterCutoff_IsUntouched(int startedAt)
    {
        var record = Record(startedAt);

        InterruptedRuns.Mark([record], [], Cutoff).Should().Be((0, 0));

        record.Status.Should().Be(JobRunStatus.Running);
        record.ErrorMessage.Should().BeNull();
    }

    [Theory]
    [InlineData(JobRunStatus.Success)]
    [InlineData(JobRunStatus.Failed)]
    [InlineData(JobRunStatus.NeverRun)]
    public void FinishedRecordBeforeCutoff_IsUntouched(JobRunStatus status)
    {
        var record = Record(-60, status);
        record.ErrorMessage = "boom";
        record.CompletedUtc = At(-30);
        record.DurationMs = 30_000;

        InterruptedRuns.Mark([record], [], Cutoff).Should().Be((0, 0));

        record.Status.Should().Be(status);
        record.ErrorMessage.Should().Be("boom");
        record.CompletedUtc.Should().Be(At(-30));
        record.DurationMs.Should().Be(30_000);
    }

    [Fact]
    public void RunningDefinitionBeforeCutoff_IsFailed_FailureCountAndRunTimeKept()
    {
        var definition = Definition(-60);

        InterruptedRuns.Mark([], [definition], Cutoff).Should().Be((0, 1)); // a definition alone still counts

        definition.LastRunStatus.Should().Be(JobRunStatus.Failed);
        definition.LastRunMessage.Should().Be(InterruptedRuns.Message);
        definition.ConsecutiveFailures.Should().Be(2);
        definition.LastRunUtc.Should().Be(At(-60));
    }

    [Fact]
    public void MixedLists_EveryStaleItemIsMarked()
    {
        var records = new[] { Record(5), Record(-60), Record(-10) };
        var definitions = new[] { Definition(5), Definition(-60), Definition(-10) };

        InterruptedRuns.Mark(records, definitions, Cutoff).Should().Be((2, 2));

        records.Select(r => r.Status).Should().Equal(JobRunStatus.Running, JobRunStatus.Failed, JobRunStatus.Failed);
        definitions.Select(d => d.LastRunStatus).Should().Equal(JobRunStatus.Running, JobRunStatus.Failed, JobRunStatus.Failed);
    }

    [Theory]
    [InlineData(0, JobRunStatus.Running)]
    [InlineData(5, JobRunStatus.Running)]
    [InlineData(null, JobRunStatus.Running)]
    [InlineData(-60, JobRunStatus.Success)]
    [InlineData(-60, JobRunStatus.Failed)]
    [InlineData(-60, JobRunStatus.NeverRun)]
    public void OtherDefinitions_AreUntouched(int? lastRunAt, JobRunStatus status)
    {
        var definition = Definition(lastRunAt, status);

        InterruptedRuns.Mark([], [definition], Cutoff).Should().Be((0, 0));

        definition.LastRunStatus.Should().Be(status);
        definition.LastRunMessage.Should().Be("old");
    }
}
