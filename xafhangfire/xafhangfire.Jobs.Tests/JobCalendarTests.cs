using DevExpress.Data.Filtering;
using DevExpress.ExpressApp;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using xafhangfire.Blazor.Server.Services;
using xafhangfire.Module.BusinessObjects;
using xafhangfire.Module.Helpers;

namespace xafhangfire.Jobs.Tests;

// UI-001: the job calendar's run computation, projection and the non-persistent collection wiring.
public class JobCalendarTests
{
    private static readonly DateTime TenUtc = new(2026, 10, 27, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void GetRunsBetween_EveryFiveMinutesOverOneHour_Is12_StartInclusiveEndExclusive()
    {
        var runs = CronHelper.GetRunsBetween("*/5 * * * *", TenUtc, TenUtc.AddHours(1), 100);
        runs.Should().HaveCount(12);
        runs[0].Should().Be(TenUtc);
        runs[^1].Should().Be(TenUtc.AddMinutes(55));
        runs.Should().OnlyContain(r => r.Kind == DateTimeKind.Utc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a cron")]
    [InlineData("0 0 1 1 *")] // once a year, not in this hour
    public void GetRunsBetween_InvalidBlankOrNoRuns_IsEmpty(string cron)
    {
        CronHelper.GetRunsBetween(cron, TenUtc, TenUtc.AddHours(1), 100).Should().BeEmpty();
    }

    [Fact]
    public void GetRunsBetween_RespectsCap()
    {
        CronHelper.GetRunsBetween("* * * * *", TenUtc, TenUtc.AddHours(1), 7).Should().HaveCount(7);
    }

    [Fact]
    public void Build_ShowsEnabledRunsInLocalTime_IncludingOneOverlappingFromBefore()
    {
        var startLocal = new DateTime(2026, 10, 27, 10, 0, 0);
        var enabled = Definition("Every 5", "*/5 * * * *");
        var disabled = Definition("Off", "*/5 * * * *", isEnabled: false);

        var entries = JobCalendar.Build([enabled, disabled], startLocal, startLocal.AddHours(1));

        // 12 runs in the hour plus the 3 in the RunLength before it (the scheduler's criteria drop those that end early).
        entries.Should().HaveCount(15).And.OnlyContain(e => e.JobDefinitionId == enabled.Id);
        entries.Select(e => e.AppointmentId).Should().OnlyHaveUniqueItems();
        var first = entries.MinBy(e => e.StartOn)!;
        first.StartOn.Should().Be(startLocal.AddMinutes(-15));
        first.StartOn.Kind.Should().Be(DateTimeKind.Unspecified);
        first.EndOn.Should().Be(first.StartOn.AddMinutes(5), "a run ends where the next one starts when that is sooner");
        first.Subject.Should().Be("Every 5");
        first.Description.Should().Contain("DemoLogCommand");
    }

    [Fact]
    public void Build_EveryRunEndsAtTheNextOne_IncludingTheLastInTheRange()
    {
        var startLocal = new DateTime(2026, 10, 27, 10, 0, 0);
        var entries = JobCalendar.Build([Definition("Every 5", "*/5 * * * *")], startLocal, startLocal.AddHours(1));
        entries.Should().OnlyContain(e => e.EndOn - e.StartOn == TimeSpan.FromMinutes(5));
        entries.Max(e => e.StartOn).Should().Be(startLocal.AddMinutes(55), "runs at or after the end are not shown");
    }

    [Theory]
    [InlineData(2985, false)] // 15 runs in the RunLength before + 2985 = exactly the cap: nothing hidden
    [InlineData(2986, true)]
    public void Build_MarksTruncationOnlyWhenRunsAreHidden(int minutes, bool marked)
    {
        var startLocal = new DateTime(2026, 10, 27, 0, 0, 0);
        var entries = JobCalendar.Build([Definition("Every minute", "* * * * *")], startLocal, startLocal.AddMinutes(minutes));
        entries.Should().HaveCount(JobCalendar.MaxRunsPerDefinition);
        entries[^1].Subject.EndsWith(JobCalendar.MoreRunsSuffix).Should().Be(marked);
        entries[^1].EndOn.Should().Be(entries[^1].StartOn.AddMinutes(1));
    }

    [Fact]
    public void Build_SparseCron_GetsTheFullRunLength()
    {
        var startLocal = new DateTime(2026, 10, 27, 10, 0, 0);
        var entries = JobCalendar.Build([Definition("Hourly", "0 * * * *")], startLocal, startLocal.AddHours(3));
        entries.Should().NotBeEmpty().And.OnlyContain(e => e.EndOn - e.StartOn == JobCalendar.RunLength);
    }

    [Fact]
    public void Build_CappedDefinition_MarksOnlyTheLastShownRun()
    {
        var startLocal = new DateTime(2026, 10, 27, 0, 0, 0);
        var entries = JobCalendar.Build([Definition("Every minute", "* * * * *")], startLocal, startLocal.AddDays(3));

        entries.Should().HaveCount(JobCalendar.MaxRunsPerDefinition);
        entries.Count(e => e.Subject.EndsWith(JobCalendar.MoreRunsSuffix)).Should().Be(1);
        entries[^1].Subject.Should().EndWith(JobCalendar.MoreRunsSuffix);
    }

    [Fact]
    public void GetInterval_ReadsTheSchedulersBetween_ElseFallsBackToAMonth()
    {
        var now = new DateTime(2026, 10, 27, 14, 30, 0);
        var start = new DateTime(2026, 11, 2);
        var end = new DateTime(2026, 11, 9);

        JobCalendar.GetInterval(SchedulerCriteria(start, end), now).Should().Be((start, end));
        JobCalendar.GetInterval(CriteriaOperator.Parse("1=2"), now).Should().Be((now.Date.AddDays(-1), now.Date.AddDays(31)));
        JobCalendar.GetInterval(null, now).Should().Be((now.Date.AddDays(-1), now.Date.AddDays(31)));
        // Only a Between on StartOn is the interval; a user's own Between on another property is not.
        JobCalendar.GetInterval(new BetweenOperator(nameof(JobCalendarEntry.EndOn), start, end), now)
            .Should().Be((now.Date.AddDays(-1), now.Date.AddDays(31)));
        // The criteria the controller sets when it narrows the interval reads back the same.
        JobCalendar.GetInterval(JobCalendar.IntervalCriteria(start, end), now).Should().Be((start, end));
    }

    [Fact]
    public void CollectionSource_FollowsTheSchedulersCriteria()
    {
        var definition = Definition("Every 5", "*/5 * * * *");
        var persistent = Substitute.For<IObjectSpace>();
        persistent.GetObjectsQuery<JobDefinition>(Arg.Any<bool>()).Returns(_ => new[] { definition }.AsQueryable());
        var factory = Substitute.For<IObjectSpaceFactory>();
        factory.CreateObjectSpace(typeof(JobDefinition)).Returns(persistent);
        var services = new ServiceCollection().AddSingleton(factory).BuildServiceProvider();

        XafTypesInfo.Instance.RegisterEntity(typeof(JobCalendarEntry));
        using var objectSpace = new NonPersistentObjectSpace(XafTypesInfo.Instance);
        JobCalendar.Attach(objectSpace, services);
        using var source = new CollectionSource(objectSpace, typeof(JobCalendarEntry));

        // Before the scheduler renders: 1=2, nothing, and no query.
        source.Criteria["ActiveViewFilter"] = CriteriaOperator.Parse("1=2");
        source.List.Count.Should().Be(0);
        factory.DidNotReceive().CreateObjectSpace(Arg.Any<Type>());

        // Day view of 2026-11-02: the 288 runs of that day plus the 23:55 run before it, which ends at midnight
        // (Between is inclusive); the shaped criteria drop 23:45 and 23:50.
        var day = new DateTime(2026, 11, 2);
        source.Criteria["ActiveViewFilter"] = SchedulerCriteria(day, day.AddDays(1));
        source.List.Count.Should().Be(289);

        // Navigating to the next day (the controller's narrowed criteria) re-fetches for the new interval.
        source.Criteria["ActiveViewFilter"] = JobCalendar.IntervalCriteria(day.AddDays(1), day.AddDays(2));
        source.List.Count.Should().Be(289);
        source.List.Cast<JobCalendarEntry>().Should().OnlyContain(e => e.EndOn >= day.AddDays(1)); // Between is inclusive
        factory.Received(2).CreateObjectSpace(typeof(JobDefinition));
        persistent.Received(2).Dispose();
    }

    // The shape SchedulerListViewControllerBase.FilterDataSourceUsingVisibleInterval builds (26.1).
    private static CriteriaOperator SchedulerCriteria(DateTime start, DateTime end) => CriteriaOperator.Or(
        new BetweenOperator(nameof(JobCalendarEntry.StartOn), start, end),
        new BetweenOperator(nameof(JobCalendarEntry.EndOn), start, end),
        CriteriaOperator.And(
            new BinaryOperator(nameof(JobCalendarEntry.StartOn), start, BinaryOperatorType.LessOrEqual),
            new BinaryOperator(nameof(JobCalendarEntry.EndOn), end, BinaryOperatorType.GreaterOrEqual)),
        CriteriaOperator.And(
            new BinaryOperator(nameof(JobCalendarEntry.Type), 1),
            new BinaryOperator(nameof(JobCalendarEntry.StartOn), end, BinaryOperatorType.LessOrEqual)),
        new BinaryOperator(nameof(JobCalendarEntry.Type), 3)); // AppointmentType.Pattern = 1, ChangedOccurrence = 3

    private static JobDefinition Definition(string name, string cron, bool isEnabled = true) => new()
    {
        Name = name,
        JobTypeName = "DemoLogCommand",
        CronExpression = cron,
        IsEnabled = isEnabled,
    };
}
