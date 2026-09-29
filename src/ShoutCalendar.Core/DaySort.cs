namespace ShoutCalendar.Core;

/// <summary>Order of rows inside one calendar day. Pinned invites stay in time order above the rest.</summary>
public static class DaySort
{
    public static int Compare(
        bool pinned,
        int rank,
        TimeOnly? time,
        string title,
        bool otherPinned,
        int otherRank,
        TimeOnly? otherTime,
        string otherTitle)
    {
        var pin = otherPinned.CompareTo(pinned);
        if (pin != 0)
            return pin;
        var clock = CompareClock(time, otherTime);
        if (clock != 0)
            return clock;
        var byRank = rank.CompareTo(otherRank);
        if (byRank != 0)
            return byRank;
        return string.Compare(title, otherTitle, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The chip clock. The next morning of a night that crossed midnight starts at 00:00.</summary>
    public static string SliceLabel(DateOnly day, DateOnly startDay, TimeOnly? start, TimeOnly? end)
    {
        if (start is TimeOnly begin && end is TimeOnly stop && day != startDay && Overnight.ContinuesNextDay(begin, stop))
            return RangeLabel(TimeOnly.MinValue, stop);
        return RangeLabel(start, end);
    }

    public static string RangeLabel(TimeOnly? start, TimeOnly? end)
    {
        if (start is not TimeOnly begin)
            return "";
        if (end is not TimeOnly stop || stop == begin)
            return $"{begin:HH:mm} ";
        return $"{begin:HH:mm}-{stop:HH:mm} ";
    }

    /// <summary>A clock with no time sorts after every timed row.</summary>
    private static int CompareClock(TimeOnly? time, TimeOnly? other)
    {
        if (time is TimeOnly left && other is TimeOnly right)
            return left.CompareTo(right);
        if (time is null && other is null)
            return 0;
        return time is null ? 1 : -1;
    }
}
