namespace ShoutCalendar.Core;

/// <summary>
/// Decides which saved events should ring on this minute.
/// The clock on the entry is compared with the computer's local clock, which is the time shown on the calendar.
/// </summary>
public static class EventAlarm
{
    public const int MinSound = 1;

    public const int MaxSound = 16;

    public readonly record struct Hit(string Id, bool Accepted, bool AtStart = false);

    public static int ClampSound(int sound) => sound is >= MinSound and <= MaxSound ? sound : MinSound;

    public static DateTime MinuteOf(DateTime now) =>
        new(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, now.Kind);

    public static IReadOnlyList<Hit> Due(
        IEnumerable<CalendarEntry> entries,
        DateTime now,
        DateTime? previousMinute,
        bool alarmAccepted,
        bool alarmUnaccepted,
        int minutesBefore,
        TimeZoneInfo? zone = null)
    {
        if (previousMinute is null)
            return [];

        var minute = MinuteOf(now);
        if (previousMinute.Value == minute)
            return [];

        if (minutesBefore < 0)
            minutesBefore = 0;

        var hits = new List<Hit>();
        foreach (var entry in entries)
        {
            if (!Clock(entry, zone, out var day, out var time) || string.IsNullOrEmpty(entry.Id))
                continue;
            if (entry.Accepted)
            {
                if (!alarmAccepted)
                    continue;
            }
            else if (!alarmUnaccepted)
            {
                continue;
            }

            var eventMoment = minute.AddMinutes(minutesBefore);
            if (!OnDay(entry, day, DateOnly.FromDateTime(eventMoment)))
                continue;
            if (eventMoment.Hour != time.Hour || eventMoment.Minute != time.Minute)
                continue;
            hits.Add(new Hit(entry.Id, entry.Accepted));
        }

        return hits;
    }

    public static bool IsStartMinute(CalendarEntry entry, DateTime now, TimeZoneInfo? zone = null)
    {
        if (!Clock(entry, zone, out var day, out var time) || string.IsNullOrEmpty(entry.Id))
            return false;
        var minute = MinuteOf(now);
        if (!OnDay(entry, day, DateOnly.FromDateTime(minute)))
            return false;
        return minute.Hour == time.Hour && minute.Minute == time.Minute;
    }

    /// <summary>The warning time is this minute or already past, so accepting the invite should ring now.</summary>
    public static bool AlreadyDue(CalendarEntry entry, DateTime now, int minutesBefore, TimeZoneInfo? zone = null)
    {
        if (!Clock(entry, zone, out var faced, out var time))
            return false;
        if (minutesBefore < 0)
            minutesBefore = 0;
        var today = DateOnly.FromDateTime(now);
        DateOnly day;
        if (entry.Repeat is not null)
        {
            if (!EventRepeat.FallsOn(entry, today))
                return false;
            day = today;
        }
        else
        {
            day = faced;
        }

        var start = day.ToDateTime(time);
        var grace = Math.Max(minutesBefore, 1);
        return now >= start.AddMinutes(-minutesBefore) && now <= start.AddMinutes(grace);
    }

    private static bool Clock(CalendarEntry entry, TimeZoneInfo? zone, out DateOnly day, out TimeOnly time)
    {
        var face = ZoneClock.Shown(entry, zone);
        day = face.Date;
        if (face.Time is not TimeOnly shown)
        {
            time = default;
            return false;
        }

        time = shown;
        return true;
    }

    private static bool OnDay(CalendarEntry entry, DateOnly faced, DateOnly moment)
    {
        if (entry.Repeat is null)
            return faced == moment;
        return EventRepeat.FallsOn(entry, moment);
    }
}
