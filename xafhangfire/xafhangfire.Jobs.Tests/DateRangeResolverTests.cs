using FluentAssertions;

namespace xafhangfire.Jobs.Tests;

public class DateRangeResolverTests
{
    private static readonly DateOnly ReferenceDate = new(2026, 3, 18); // Wednesday

    [Theory]
    [InlineData("today", "2026-03-18", "2026-03-18")]
    [InlineData("yesterday", "2026-03-17", "2026-03-17")]
    [InlineData("this-week", "2026-03-16", "2026-03-22")]   // Mon-Sun
    [InlineData("last-week", "2026-03-09", "2026-03-15")]
    [InlineData("next-week", "2026-03-23", "2026-03-29")]
    [InlineData("this-month", "2026-03-01", "2026-03-31")]
    [InlineData("last-month", "2026-02-01", "2026-02-28")]
    [InlineData("next-month", "2026-04-01", "2026-04-30")]
    [InlineData("this-quarter", "2026-01-01", "2026-03-31")]   // Q1
    [InlineData("last-quarter", "2025-10-01", "2025-12-31")]   // Q4 2025
    [InlineData("this-year", "2026-01-01", "2026-12-31")]
    [InlineData("last-year", "2025-01-01", "2025-12-31")]
    public void Resolve_KnownTerms_ReturnsExpectedRange(string term, string expectedStart, string expectedEnd)
    {
        var range = DateRangeResolver.Resolve(term, ReferenceDate);

        range.Start.Should().Be(DateOnly.Parse(expectedStart));
        range.End.Should().Be(DateOnly.Parse(expectedEnd));
    }

    [Fact]
    public void Resolve_UnknownTerm_ThrowsArgumentException()
    {
        var act = () => DateRangeResolver.Resolve("invalid-term", ReferenceDate);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Unknown date term*");
    }

    [Fact]
    public void Resolve_CaseInsensitive()
    {
        var lower = DateRangeResolver.Resolve("this-month", ReferenceDate);
        var upper = DateRangeResolver.Resolve("THIS-MONTH", ReferenceDate);
        var mixed = DateRangeResolver.Resolve("This-Month", ReferenceDate);

        lower.Should().Be(upper);
        lower.Should().Be(mixed);
    }

    [Fact]
    public void Resolve_TrimsWhitespace()
    {
        var result = DateRangeResolver.Resolve("  today  ", ReferenceDate);

        result.Start.Should().Be(ReferenceDate);
        result.End.Should().Be(ReferenceDate);
    }

    [Fact]
    public void Resolve_NoRelativeDate_UsesToday()
    {
        var result = DateRangeResolver.Resolve("today");

        result.Start.Should().Be(DateOnly.FromDateTime(DateTime.Today));
    }

    [Fact]
    public void Resolve_LastMonth_February_LeapYear()
    {
        // 2024 is a leap year
        var marchInLeapYear = new DateOnly(2024, 3, 15);
        var range = DateRangeResolver.Resolve("last-month", marchInLeapYear);

        range.Start.Should().Be(new DateOnly(2024, 2, 1));
        range.End.Should().Be(new DateOnly(2024, 2, 29)); // Leap year
    }

    [Fact]
    public void Resolve_Monday_ThisWeek_StartsOnMonday()
    {
        var monday = new DateOnly(2026, 3, 16); // Monday
        var range = DateRangeResolver.Resolve("this-week", monday);

        range.Start.Should().Be(monday);
        range.End.Should().Be(new DateOnly(2026, 3, 22)); // Sunday
    }

    [Fact]
    public void Resolve_Sunday_ThisWeek_StartsOnPreviousMonday()
    {
        var sunday = new DateOnly(2026, 3, 22); // Sunday
        var range = DateRangeResolver.Resolve("this-week", sunday);

        range.Start.Should().Be(new DateOnly(2026, 3, 16)); // Previous Monday
        range.End.Should().Be(sunday);
    }
}
