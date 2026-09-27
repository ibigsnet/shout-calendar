namespace ShoutCalendar.Core;

/// <summary>
/// An entry is ongoing only when the shout stated two ordered clock times and the
/// evaluation instant's UTC minute falls inside that interval on the entry's UTC date.
/// A single clock time is never ongoing. An end that is not after the start is not ongoing.
/// </summary>
public static class OngoingCheck
{
    public static bool IsOngoing(CalendarEntry entry, DateTimeOffset evaluationInstant)
    {
        if (entry.Time is not TimeOnly start || entry.End is not TimeOnly end || end <= start)
            return false;

        var utc = evaluationInstant.UtcDateTime;
        if (entry.Date is not DateOnly date || DateOnly.FromDateTime(utc) != date)
            return false;

        var clock = new TimeOnly(utc.Hour, utc.Minute);
        return clock >= start && clock <= end;
    }
}
