using System.Globalization;
using System.Text.RegularExpressions;

namespace ShoutCalendar.Core;

/// <summary>
/// A clock written in a shout, moved into the calendar's time zone when the text names one.
/// PT, ET, CT, and MT are that region's wall time on the civil date. ST is left as written.
/// </summary>
public static class ZoneClock
{
    public readonly record struct Wall(TimeOnly Time, string? Label);

    public readonly record struct Face(DateOnly Date, TimeOnly? Time);

    private static readonly Regex ClockRegex = new(
        @"\b(?:(?<h24>[01]?\d|2[0-3]):(?<m>[0-5]\d)(?:\s*(?<ampm>[ap]\.?m\.?))?|(?<h12>[1-9]|1[0-2])\s*(?<ampm2>[ap]\.?m\.?))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ZoneRegex = new(
        @"^\s*(?<zone>PDT|PST|EDT|EST|CDT|CST|MDT|MST|PT|ET|CT|MT|ST)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<Wall> Walls(string? text)
    {
        var walls = new List<Wall>();
        if (string.IsNullOrWhiteSpace(text))
            return walls;
        foreach (Match match in ClockRegex.Matches(text))
        {
            if (!TryRead(match, out var time))
                continue;
            walls.Add(new Wall(time, LabelAfter(text, match)));
        }

        return walls;
    }

    public static bool Converts(string? label) => SourceId(label) is not null;

    public static bool Labeled(string? text)
    {
        foreach (var wall in Walls(text))
        {
            if (Converts(wall.Label))
                return true;
        }

        return false;
    }

    public static (DateOnly Date, TimeOnly Time) Move(DateOnly civilDate, TimeOnly wall, string? label, TimeZoneInfo calendarZone)
    {
        var sourceId = SourceId(label);
        if (sourceId is null)
            return (civilDate, wall);
        TimeZoneInfo source;
        try
        {
            source = TimeZoneInfo.FindSystemTimeZoneById(sourceId);
        }
        catch (TimeZoneNotFoundException)
        {
            return (civilDate, wall);
        }

        var unspecified = DateTime.SpecifyKind(civilDate.ToDateTime(wall), DateTimeKind.Unspecified);
        if (source.IsInvalidTime(unspecified))
            return (civilDate, wall);
        DateTime utc;
        try
        {
            utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, source);
        }
        catch (ArgumentException)
        {
            return (civilDate, wall);
        }

        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, calendarZone);
        return (DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local));
    }

    /// <summary>The instant to draw and alarm. A zone label in the text wins over a stored clock.</summary>
    public static Face Shown(CalendarEntry entry, TimeZoneInfo? calendarZone)
    {
        var storedDate = entry.Date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        if (calendarZone is null || string.IsNullOrWhiteSpace(entry.EventText))
            return new Face(storedDate, entry.Time);
        foreach (var wall in Walls(entry.EventText))
        {
            if (!Converts(wall.Label))
                continue;
            var moved = Move(storedDate, wall.Time, wall.Label, calendarZone);
            if (AlreadyShown(entry, storedDate, moved))
                return new Face(storedDate, entry.Time ?? moved.Time);
            foreach (var shift in new[] { -1, 1 })
            {
                if (shift < 0 && storedDate == DateOnly.MinValue)
                    continue;
                if (shift > 0 && storedDate == DateOnly.MaxValue)
                    continue;
                var civil = storedDate.AddDays(shift);
                var other = Move(civil, wall.Time, wall.Label, calendarZone);
                if (AlreadyShown(entry, storedDate, other))
                    return new Face(storedDate, entry.Time ?? other.Time);
            }

            return new Face(moved.Date, moved.Time);
        }

        return new Face(storedDate, entry.Time);
    }

    /// <summary>Harvest already stored this converted instant, so the label must not be applied again.</summary>
    private static bool AlreadyShown(CalendarEntry entry, DateOnly storedDate, (DateOnly Date, TimeOnly Time) moved) =>
        entry.Time is TimeOnly stored && moved.Date == storedDate && moved.Time == stored;

    private static string? LabelAfter(string text, Match clock)
    {
        var rest = text[(clock.Index + clock.Length)..];
        var zone = ZoneRegex.Match(rest);
        return zone.Success ? zone.Groups["zone"].Value : null;
    }

    private static string? SourceId(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return null;
        return label.Trim().ToUpperInvariant() switch
        {
            "PT" or "PDT" or "PST" => "America/Los_Angeles",
            "ET" or "EDT" or "EST" => "America/New_York",
            "CT" or "CDT" or "CST" => "America/Chicago",
            "MT" or "MDT" or "MST" => "America/Denver",
            _ => null,
        };
    }

    private static bool TryRead(Match match, out TimeOnly time)
    {
        time = default;
        int hour;
        int minute;
        string? ampm;
        if (match.Groups["h24"].Success)
        {
            hour = int.Parse(match.Groups["h24"].Value, CultureInfo.InvariantCulture);
            minute = int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture);
            ampm = match.Groups["ampm"].Success ? match.Groups["ampm"].Value : null;
        }
        else
        {
            hour = int.Parse(match.Groups["h12"].Value, CultureInfo.InvariantCulture);
            minute = 0;
            ampm = match.Groups["ampm2"].Value;
        }

        if (ampm is not null)
        {
            var marker = ampm.Replace(".", "", StringComparison.Ordinal).ToLowerInvariant();
            if (hour is < 1 or > 12)
                return false;
            if (marker == "am")
                hour = hour == 12 ? 0 : hour;
            else if (marker == "pm")
                hour = hour == 12 ? 12 : hour + 12;
            else
                return false;
        }

        time = new TimeOnly(hour, minute);
        return true;
    }
}
