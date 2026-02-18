namespace xafhangfire.Jobs;

public readonly record struct DateRange(DateOnly Start, DateOnly End);

public static class DateRangeResolver
{
    public static DateRange Resolve(string term, DateOnly? relativeTo = null)
    {
        var today = relativeTo ?? DateOnly.FromDateTime(DateTime.Today);

        return term.ToLowerInvariant().Trim() switch
        {
            "today" => new DateRange(today, today),
            "yesterday" => new DateRange(today.AddDays(-1), today.AddDays(-1)),
            "this-week" => WeekRange(today, 0),
            "last-week" => WeekRange(today, -1),
            "next-week" => WeekRange(today, 1),
            "this-month" => MonthRange(today, 0),
            "last-month" => MonthRange(today, -1),
            "next-month" => MonthRange(today, 1),
            "this-quarter" => QuarterRange(today, 0),
            "last-quarter" => QuarterRange(today, -1),
            "this-year" => new DateRange(
                new DateOnly(today.Year, 1, 1),
                new DateOnly(today.Year, 12, 31)),
            "last-year" => new DateRange(
                new DateOnly(today.Year - 1, 1, 1),
                new DateOnly(today.Year - 1, 12, 31)),
            _ => throw new ArgumentException($"Unknown date term: '{term}'", nameof(term))
        };
    }

    private static DateRange WeekRange(DateOnly today, int weekOffset)
    {
        var diff = (int)today.DayOfWeek - (int)DayOfWeek.Monday;
        if (diff < 0) diff += 7;
        var monday = today.AddDays(-diff + weekOffset * 7);
        return new DateRange(monday, monday.AddDays(6));
    }

    private static DateRange MonthRange(DateOnly today, int monthOffset)
    {
        var first = new DateOnly(today.Year, today.Month, 1).AddMonths(monthOffset);
        var last = first.AddMonths(1).AddDays(-1);
        return new DateRange(first, last);
    }

    private static DateRange QuarterRange(DateOnly today, int quarterOffset)
    {
        var currentQuarter = (today.Month - 1) / 3;
        var targetQuarter = currentQuarter + quarterOffset;
        var targetYear = today.Year + (targetQuarter < 0 ? -1 : targetQuarter / 4);
        targetQuarter = ((targetQuarter % 4) + 4) % 4;
        var firstMonth = targetQuarter * 3 + 1;
        var first = new DateOnly(targetYear, firstMonth, 1);
        var last = first.AddMonths(3).AddDays(-1);
        return new DateRange(first, last);
    }
}
