namespace ShoutCalendar.Core;

/// <summary>
/// Decides which saved events should ring on this minute.
/// The clock on the entry is compared with the computer's local clock, which is the time shown on the calendar.
/// </summary>
public static class EventAlarm
{
    public const int MinSound = 1;

    public const int MaxSound = 16;

    public readonly record struct Hit(string Id, bool Accepted);

    public static int ClampSound(int sound) => sound is >= MinSound and <= MaxSound ? sound : MinSound;

    public static DateTime MinuteOf(DateTime now) =>
        new(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, now.Kind);

    public static IReadOnlyList<Hit> Due(
        IEnumerable<CalendarEntry> entries,
        DateTime now,
        DateTime? previousMinute,
        bool alarmAccepted,
        bool alarmUnaccepted,
        int minutesBefore)
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
            if (entry.Time is not TimeOnly time || string.IsNullOrEmpty(entry.Id))
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
            if (!EventRepeat.FallsOn(entry, DateOnly.FromDateTime(eventMoment)))
                continue;
            if (eventMoment.Hour != time.Hour || eventMoment.Minute != time.Minute)
                continue;
            hits.Add(new Hit(entry.Id, entry.Accepted));
        }

        return hits;
    }
}
