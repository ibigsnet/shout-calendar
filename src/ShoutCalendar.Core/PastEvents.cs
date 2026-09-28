using System.Globalization;

namespace ShoutCalendar.Core;

/// <summary>
/// An event is past once its end clock has passed.
/// A shout with no end clock stays through that calendar day.
/// A repeating shout stays.
/// </summary>
public static class PastEvents
{
    public static bool Ended(CalendarEntry entry, DateTime now, TimeZoneInfo? zone = null)
    {
        if (entry.Repeat is not null)
            return false;
        zone ??= TimeZoneInfo.Local;
        var shown = ZoneClock.ShownRange(entry, zone);
        var walls = ZoneClock.Walls(entry.EventText);
        var start = shown.Start ?? entry.Time;
        var end = shown.End ?? entry.End;
        if (end is null && walls.Count >= 2)
            end = walls[1].Time;
        if (end is null)
            return PastTone.Ended(shown.Date, null, null, now);
        return PastTone.Ended(shown.Date, start, end, now);
    }

    public static bool Ended(SyncAnnouncement item, DateTime now, TimeZoneInfo? zone = null)
    {
        DateOnly? date = null;
        if (DateOnly.TryParseExact((item.Date ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            date = parsedDate;
        TimeOnly? time = null;
        if (TimeOnly.TryParseExact((item.Time ?? "").Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedTime))
            time = parsedTime;
        var entry = new CalendarEntry(
            date,
            time,
            null,
            null,
            item.World,
            "",
            item.Text,
            "",
            item.Accepted,
            item.Id,
            default);
        return Ended(entry, now, zone);
    }
}
