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
        var byRank = rank.CompareTo(otherRank);
        if (byRank != 0)
            return byRank;
        if (time is TimeOnly left && otherTime is TimeOnly right)
        {
            var clock = left.CompareTo(right);
            if (clock != 0)
                return clock;
        }

        return string.Compare(title, otherTitle, StringComparison.OrdinalIgnoreCase);
    }
}
