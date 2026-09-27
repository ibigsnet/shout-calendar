using System.Globalization;
using System.Text.RegularExpressions;

namespace ShoutCalendar.Core;

/// <summary>
/// Decides whether a shout becomes one calendar entry.
/// A watched chat line is kept when it has a clock time or a calendar date, and a place.
/// Clock labels are stored as written. No timezone, Eorzea-time, or duration is applied.
/// </summary>
public static class ShoutHarvest
{
    /// <summary>
    /// On-disk channel byte for shout, from the documented FFXIV chat-line channel list (0x0B).
    /// Dalamud's LogKind sheet uses the same value: <c>XivChatType.Shout = 11</c>.
    /// </summary>
    public const int ShoutChannel = 0x0B;

    /// <summary>Dalamud <c>XivChatType.FreeCompany</c>.</summary>
    public const int FreeCompanyChannel = 24;

    private static readonly Regex ClockRegex = new(
        @"\b(?:(?<h24>[01]?\d|2[0-3]):(?<m>[0-5]\d)(?:\s*(?<ampm>[ap]\.?m\.?))?|(?<h12>[1-9]|1[0-2])\s*(?<ampm2>[ap]\.?m\.?))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex WardRegex = new(
        @"\b(?:ward\s*#?\s*|(?<![A-Za-z])[Ww])(?<n>30|[12][0-9]|[1-9])(?!\d)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PlotShorthandRegex = new(
        @"(?<![A-Za-z])[Pp](?<n>[1-9]|[1-5][0-9]|60)(?!\d)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex WeekdayRegex = new(
        @"\b(?:(?<when>next|this)\s+)?(?<weekday>sunday|monday|tuesday|wednesday|thursday|friday|saturday)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DateRegex = new(
        @"\b(?<m>1[0-2]|0?[1-9])/(?<d>3[01]|[12]\d|0?[1-9])/(?<y>\d{4}|\d{2})\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CoordinateRegex = new(
        @"\b[Xx]\s*[:=]?\s*(?<x>\d{1,2}(?:\.\d+)?)\b\s*[,/]?\s*\b[Yy]\s*[:=]?\s*(?<y>\d{1,2}(?:\.\d+)?)\b|\((?<x2>\d{1,2}\.\d+)\s*,\s*(?<y2>\d{1,2}\.\d+)\)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PlaceWordRegex = new(
        @"\b(?<kind>plot|apartment|room|house|cottage)\s*#?\s*(?<n>\d{1,3})\b|\b(?<sub>subdivision)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static CalendarEntry? TryHarvest(
        string? text,
        int channel,
        DateTimeOffset shoutTimestamp,
        PlaceCatalog? places = null,
        IReadOnlySet<int>? channels = null,
        string? housingHint = null,
        bool aggressive = false)
    {
        if (!IsWatched(channel, channels) || string.IsNullOrWhiteSpace(text))
            return null;

        var clocks = ReadClocks(text);
        var statedDate = ReadDate(text) ?? ReadRelativeDay(text, shoutTimestamp) ?? ReadWeekday(text, shoutTimestamp);

        int? ward = null;
        var wardMatch = WardRegex.Match(text);
        if (wardMatch.Success)
            ward = int.Parse(wardMatch.Groups["n"].Value, CultureInfo.InvariantCulture);

        var servers = ServerNames.Match(text);
        var locations = (places ?? PlaceCatalog.Empty).Match(text);
        var coordinates = ReadCoordinates(text);
        var extras = ReadPlaceWords(text);
        if (!extras.Any(extra => extra.StartsWith("plot ", StringComparison.Ordinal)))
        {
            var plot = PlotShorthandRegex.Match(text);
            if (plot.Success)
                extras.Add($"plot {plot.Groups["n"].Value}");
        }

        var placeParts = new List<string>();
        if (ward is int wardNumber)
            placeParts.Add($"ward {wardNumber.ToString(CultureInfo.InvariantCulture)}");
        placeParts.AddRange(extras);
        if (coordinates is not null)
            placeParts.Add(coordinates);
        placeParts.AddRange(locations);
        placeParts.AddRange(servers);
        if (ward is not null && housingHint is not null && !PlaceAlreadyNamesDistrict(placeParts))
            placeParts.Add(housingHint);

        var hasDate = statedDate is not null;
        var hasTime = clocks.Count > 0;
        var hasPlace = placeParts.Count > 0;
        if (aggressive)
        {
            var signals = (hasDate ? 1 : 0) + (hasTime ? 1 : 0) + (hasPlace ? 1 : 0);
            if (signals < 2)
                return null;
        }
        else if (!hasDate && !hasTime && !hasPlace)
        {
            return null;
        }

        DateOnly? date = statedDate;
        if (date is null && clocks.Count > 0)
            date = DateOnly.FromDateTime(shoutTimestamp.UtcDateTime);
        TimeOnly? end = clocks.Count == 2 ? clocks[1] : null;
        TimeOnly? time = clocks.Count > 0 ? clocks[0] : null;
        var server = servers.Count == 0 ? null : string.Join(", ", servers);

        return new CalendarEntry(
            date,
            time,
            end,
            ward,
            server,
            string.Join(", ", placeParts),
            text.Trim(),
            "",
            false,
            "",
            default);
    }

    public static IReadOnlyList<CalendarEntry> HarvestLog(ReadOnlySpan<byte> log, PlaceCatalog? places = null)
    {
        var found = new List<CalendarEntry>();
        foreach (var line in ChatLogReader.Read(log))
        {
            var when = DateTimeOffset.FromUnixTimeSeconds(line.TimestampUnix);
            var entry = TryHarvest(line.Message, line.Channel, when, places);
            if (entry is not null)
                found.Add(entry);
        }

        return found;
    }

    public static bool IsWatched(int channel, IReadOnlySet<int>? channels = null) => ChatChannels.Allows(channel, channels);

    private static readonly Regex RelativeDayRegex = new(
        @"\b(?<day>tomorrow|tonight|today)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static DateOnly? ReadRelativeDay(string text, DateTimeOffset shoutTimestamp)
    {
        var match = RelativeDayRegex.Match(text);
        if (!match.Success)
            return null;
        var day = DateOnly.FromDateTime(shoutTimestamp.UtcDateTime);
        return match.Groups["day"].Value.Equals("tomorrow", StringComparison.OrdinalIgnoreCase)
            ? day.AddDays(1)
            : day;
    }

    private static DateOnly? ReadWeekday(string text, DateTimeOffset shoutTimestamp)
    {
        var match = WeekdayRegex.Match(text);
        if (!match.Success)
            return null;

        var names = new[]
        {
            "sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday",
        };
        var target = Array.FindIndex(names, name => name.Equals(match.Groups["weekday"].Value, StringComparison.OrdinalIgnoreCase));
        if (target < 0)
            return null;

        var today = DateOnly.FromDateTime(shoutTimestamp.UtcDateTime);
        var delta = (target - (int)today.DayOfWeek + 7) % 7;
        var next = match.Groups["when"].Success
            && match.Groups["when"].Value.Equals("next", StringComparison.OrdinalIgnoreCase);
        if (next && delta == 0)
            delta = 7;
        return today.AddDays(delta);
    }

    private static DateOnly? ReadDate(string text)
    {
        var match = DateRegex.Match(text);
        if (!match.Success)
            return null;
        var month = int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture);
        var day = int.Parse(match.Groups["d"].Value, CultureInfo.InvariantCulture);
        var year = int.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture);
        if (year < 100)
            year += 2000;
        if (month is < 1 or > 12 || day is < 1 or > 31)
            return null;
        try
        {
            return new DateOnly(year, month, day);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static bool PlaceAlreadyNamesDistrict(List<string> placeParts)
    {
        foreach (var part in placeParts)
        {
            if (part.Contains("Mist", StringComparison.OrdinalIgnoreCase)
                || part.Contains("Lavender", StringComparison.OrdinalIgnoreCase)
                || part.Contains("Goblet", StringComparison.OrdinalIgnoreCase)
                || part.Contains("Shirogane", StringComparison.OrdinalIgnoreCase)
                || part.Contains("Empyreum", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string? ReadCoordinates(string text)
    {
        var match = CoordinateRegex.Match(text);
        if (!match.Success)
            return null;
        var x = match.Groups["x"].Success ? match.Groups["x"].Value : match.Groups["x2"].Value;
        var y = match.Groups["y"].Success ? match.Groups["y"].Value : match.Groups["y2"].Value;
        return $"x {x}, y {y}";
    }

    private static List<TimeOnly> ReadClocks(string text)
    {
        var clocks = new List<TimeOnly>();
        foreach (Match match in ClockRegex.Matches(text))
        {
            if (TryReadClock(match, out var time))
                clocks.Add(time);
        }

        return clocks;
    }

    private static bool TryReadClock(Match match, out TimeOnly time)
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

    private static List<string> ReadPlaceWords(string text)
    {
        var extras = new List<string>();
        foreach (Match match in PlaceWordRegex.Matches(text))
        {
            if (match.Groups["sub"].Success)
            {
                extras.Add("subdivision");
                continue;
            }

            var number = int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture);
            if (number <= 0)
                continue;
            extras.Add($"{match.Groups["kind"].Value.ToLowerInvariant()} {number.ToString(CultureInfo.InvariantCulture)}");
        }

        return extras;
    }
}
