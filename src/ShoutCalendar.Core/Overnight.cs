namespace ShoutCalendar.Core;

/// <summary>
/// A clock that ends before the start continues onto the next civil day.
/// An end of exactly 00:00 stays on the start day. 00:01 and later still span.
/// The stored date is the start day unless the shout named another date.
/// </summary>
public static class Overnight
{
    public static bool ContinuesNextDay(TimeOnly start, TimeOnly end) =>
        end != TimeOnly.MinValue && end <= start;

    public static bool Is(TimeOnly? start, TimeOnly? end) =>
        start is TimeOnly begin && end is TimeOnly stop && ContinuesNextDay(begin, stop);

    public static bool Covers(DateOnly day, DateOnly startDay, TimeOnly? start, TimeOnly? end)
    {
        if (!Is(start, end))
            return day == startDay;
        return day == startDay || day == startDay.AddDays(1);
    }

    public static (DateOnly Start, DateOnly End)? Span(DateOnly startDay, TimeOnly? start, TimeOnly? end)
    {
        if (!Is(start, end))
            return null;
        return (startDay, startDay.AddDays(1));
    }

    public static (DateOnly Start, TimeOnly? StartClock, TimeOnly? EndClock) Shown(CalendarEntry entry, TimeZoneInfo? zone)
    {
        var shown = ZoneClock.ShownRange(entry, zone);
        var startClock = shown.Start ?? entry.Time;
        var endClock = shown.End ?? entry.End;
        var startDay = entry.Date ?? shown.Date;
        return (startDay, startClock, endClock);
    }
}
