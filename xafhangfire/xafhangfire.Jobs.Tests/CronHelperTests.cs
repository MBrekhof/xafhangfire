using FluentAssertions;
using xafhangfire.Module.Helpers;

namespace xafhangfire.Jobs.Tests;

public class CronHelperTests
{
    [Theory]
    [InlineData("*/5 * * * *", "Every 5 minutes")]
    [InlineData("0 9 * * 1-5", "At 09:00 AM, Monday through Friday")]
    [InlineData("0 0 1 * *", "At 12:00 AM, on day 1 of the month")]
    public void GetDescription_ValidCron_ReturnsHumanReadable(string cron, string expected)
    {
        var result = CronHelper.GetDescription(cron);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetDescription_EmptyOrNull_ReturnsEmptyString(string? cron)
    {
        CronHelper.GetDescription(cron).Should().BeEmpty();
    }

    [Fact]
    public void GetDescription_InvalidCron_ReturnsErrorMessage()
    {
        var result = CronHelper.GetDescription("not-a-cron");
        result.Should().StartWith("Invalid:");
    }

    [Fact]
    public void GetNextRuns_ValidCron_ReturnsRequestedCount()
    {
        var result = CronHelper.GetNextRuns("*/5 * * * *", 5);
        result.Should().HaveCount(5);
    }

    [Fact]
    public void GetNextRuns_ValidCron_RunsAreInFuture()
    {
        var now = DateTime.UtcNow;
        var result = CronHelper.GetNextRuns("*/5 * * * *", 3);
        result.Should().AllSatisfy(dt => dt.Should().BeAfter(now));
    }

    [Fact]
    public void GetNextRuns_EmptyCron_ReturnsEmpty()
    {
        CronHelper.GetNextRuns("", 5).Should().BeEmpty();
    }

    [Fact]
    public void GetNextRuns_InvalidCron_ReturnsEmpty()
    {
        CronHelper.GetNextRuns("bad", 5).Should().BeEmpty();
    }

    [Fact]
    public void FormatNextRuns_ValidCron_ReturnsFormattedString()
    {
        var result = CronHelper.FormatNextRuns("*/5 * * * *", 3);
        result.Should().NotBeEmpty();
        result.Should().Contain("\n");
    }

    [Fact]
    public void FormatNextRuns_EmptyCron_ReturnsEmptyString()
    {
        CronHelper.FormatNextRuns("", 3).Should().BeEmpty();
    }
}
