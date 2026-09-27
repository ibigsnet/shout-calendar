namespace ShoutCalendar.Core;

/// <summary>How a game schedule row is painted. Colors stay in the window.</summary>
public enum ResetTone
{
    Crystal,
    Cactus,
    Event,
}

public sealed record ScheduleItem(string Id, string Name, string Chip, string Detail, ResetTone Tone, bool DefaultOn);

public sealed record ScheduleOccurrence(
    string Id,
    string Name,
    string Chip,
    string Detail,
    DateTime LocalStart,
    DateTime LocalEnd,
    ResetTone Tone)
{
    public string Key => this.Id + ":" + this.LocalStart.ToString("yyyyMMddHHmm");

    public DateOnly StartDate => DateOnly.FromDateTime(this.LocalStart);

    public DateOnly EndDate => DateOnly.FromDateTime(this.LocalEnd);
}

public readonly record struct SpanSegment(int Row, int FirstColumn, int LastColumn);

/// <summary>
/// Fixed FFXIV reset clocks and the limited events worth putting on the month.
/// Clocks are the official UTC instants. The calendar shows them in the computer's local time.
/// </summary>
public static class GameSchedule
{
    public const string Daily = "daily-reset";
    public const string Weekly = "weekly-reset";
    public const string GrandCompany = "grand-company";
    public const string Cactpot = "jumbo-cactpot";
    public const string Nocturne = "nocturne-2026";

    public const string RegionNa = "na";
    public const string RegionEu = "eu";
    public const string RegionJp = "jp";
    public const string RegionOc = "oc";

    private static readonly DateTimeOffset NocturneStart = new(2026, 9, 24, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NocturneEnd = new(2026, 10, 13, 14, 59, 0, TimeSpan.Zero);

    public static IReadOnlyList<ScheduleItem> Items { get; } =
    [
        new(
            Cactpot,
            "Jumbo Cactpot",
            "Cactpot",
            "Weekly Gold Saucer drawing. Up to three tickets. The early bird bonus lasts one hour after the draw. The clock follows the data-center region below.",
            ResetTone.Cactus,
            true),
        new(
            Weekly,
            "Weekly reset",
            "Weekly",
            "Tuesday 08:00 UTC. Raid lockouts, tomestone cap, challenge log, custom deliveries, Wondrous Tails, Fashion Report, squadron priority missions, and masked carnivale.",
            ResetTone.Crystal,
            true),
        new(
            Daily,
            "Daily reset",
            "Daily",
            "15:00 UTC every day. Duty roulette bonuses, allied society quests, and daily hunt bills.",
            ResetTone.Crystal,
            false),
        new(
            GrandCompany,
            "Grand Company",
            "GC",
            "20:00 UTC every day. Grand Company supply and provisioning, Rowena collectables, and squadron training.",
            ResetTone.Crystal,
            false),
        new(
            Nocturne,
            "A Nocturne for Heroes",
            "Nocturne",
            "Final Fantasy XV return. The Regalia Type-G, the four-seat black car, is 200,000 MGP from the Ironworks Vendor in the Gold Saucer after the quests. Kipih Jakkya starts it in Ul'dah, Steps of Nald (8.5, 9.7). Level 50 and the quest The Ultimate Weapon. 24 Sep 2026 08:00 UTC through 13 Oct 2026 14:59 UTC.",
            ResetTone.Event,
            true),
    ];

    public static IReadOnlyList<string> DefaultIds { get; } = Items.Where(item => item.DefaultOn).Select(item => item.Id).ToArray();

    public static bool IsKnown(string? id) => Items.Any(item => item.Id == id);

    public static string NormalizeRegion(string? region) => region switch
    {
        RegionEu or RegionJp or RegionOc => region,
        _ => RegionNa,
    };

    public static string RegionLabel(string region) => NormalizeRegion(region) switch
    {
        RegionEu => "Europe (Chaos, Light)",
        RegionJp => "Japan (Elemental, Gaia, Mana, Meteor)",
        RegionOc => "Oceania (Materia)",
        _ => "North America (Crystal, Aether, Primal, Dynamis)",
    };

    public static IReadOnlyList<ScheduleOccurrence> InMonth(
        int year,
        int month,
        TimeZoneInfo zone,
        string? region,
        IReadOnlySet<string> enabled)
    {
        var found = new List<ScheduleOccurrence>();
        var chosen = NormalizeRegion(region);
        foreach (var item in Items)
        {
            if (!enabled.Contains(item.Id))
                continue;
            if (item.Id == Nocturne)
            {
                var occurrence = Campaign(item, NocturneStart, NocturneEnd, zone, year, month);
                if (occurrence is not null)
                    found.Add(occurrence);
                continue;
            }

            foreach (var instant in Instants(item.Id, chosen, year, month))
            {
                var local = TimeZoneInfo.ConvertTime(instant, zone).DateTime;
                if (local.Year != year || local.Month != month)
                    continue;
                found.Add(new ScheduleOccurrence(item.Id, item.Name, item.Chip, item.Detail, local, local, item.Tone));
            }
        }

        return found;
    }

    public static string NextLine(string id, DateTimeOffset now, TimeZoneInfo zone, string? region)
    {
        var item = Items.First(candidate => candidate.Id == id);
        if (id == Nocturne)
        {
            var start = TimeZoneInfo.ConvertTime(NocturneStart, zone);
            var end = TimeZoneInfo.ConvertTime(NocturneEnd, zone);
            return $"{start:ddd d MMM HH:mm} – {end:ddd d MMM HH:mm}";
        }

        var following = now.UtcDateTime.AddMonths(1);
        var upcoming = Instants(id, NormalizeRegion(region), now.Year, now.Month)
            .Concat(Instants(id, NormalizeRegion(region), following.Year, following.Month))
            .Where(instant => instant >= now.AddMinutes(-1))
            .Order()
            .Cast<DateTimeOffset?>()
            .FirstOrDefault();
        if (upcoming is null)
            return item.Detail;
        var local = TimeZoneInfo.ConvertTime(upcoming.Value, zone);
        return $"{local:ddd d MMM HH:mm}";
    }

    public static IReadOnlyList<SpanSegment> Segments(CalendarMonth month, DateOnly start, DateOnly end)
    {
        var segments = new List<SpanSegment>();
        int? row = null;
        var first = 0;
        var last = 0;
        for (var index = 0; index < month.Cells.Count; index++)
        {
            if (month.Cells[index].Day is not int day)
                continue;
            var date = new DateOnly(month.Year, month.Month, day);
            if (date < start || date > end)
                continue;
            var column = index % 7;
            var thisRow = index / 7;
            if (row is null || thisRow != row || column != last + 1)
            {
                if (row is int open)
                    segments.Add(new SpanSegment(open, first, last));
                row = thisRow;
                first = column;
                last = column;
            }
            else
            {
                last = column;
            }
        }

        if (row is int close)
            segments.Add(new SpanSegment(close, first, last));
        return segments;
    }

    public static IReadOnlyDictionary<string, int> Lanes(IReadOnlyList<ScheduleOccurrence> items)
    {
        var lanes = new Dictionary<string, int>();
        var ends = new List<DateOnly>();
        foreach (var item in items.OrderBy(item => item.StartDate).ThenBy(item => item.EndDate).ThenBy(item => item.Key))
        {
            var lane = ends.FindIndex(end => end < item.StartDate);
            if (lane < 0)
            {
                lane = ends.Count;
                ends.Add(item.EndDate);
            }
            else
            {
                ends[lane] = item.EndDate;
            }

            lanes[item.Key] = lane;
        }

        return lanes;
    }

    private static ScheduleOccurrence? Campaign(
        ScheduleItem item,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        TimeZoneInfo zone,
        int year,
        int month)
    {
        var start = TimeZoneInfo.ConvertTime(startUtc, zone).DateTime;
        var end = TimeZoneInfo.ConvertTime(endUtc, zone).DateTime;
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        if (DateOnly.FromDateTime(end) < monthStart || DateOnly.FromDateTime(start) > monthEnd)
            return null;
        return new ScheduleOccurrence(item.Id, item.Name, item.Chip, item.Detail, start, end, item.Tone);
    }

    private static IEnumerable<DateTimeOffset> Instants(string id, string region, int year, int month)
    {
        var first = new DateOnly(year, month, 1).AddDays(-2);
        var last = new DateOnly(year, month, DateTime.DaysInMonth(year, month)).AddDays(2);
        for (var date = first; date <= last; date = date.AddDays(1))
        {
            if (id == Daily)
                yield return Utc(date, 15, 0);
            else if (id == GrandCompany)
                yield return Utc(date, 20, 0);
            else if (id == Weekly && date.DayOfWeek == DayOfWeek.Tuesday)
                yield return Utc(date, 8, 0);
            else if (id == Cactpot && MatchesCactpot(date, region))
                yield return Utc(date, CactpotHour(region), 0);
        }
    }

    private static bool MatchesCactpot(DateOnly utcDate, string region) =>
        utcDate.DayOfWeek == (NormalizeRegion(region) == RegionNa ? DayOfWeek.Sunday : DayOfWeek.Saturday);

    private static int CactpotHour(string region) => NormalizeRegion(region) switch
    {
        RegionEu => 19,
        RegionJp => 12,
        RegionOc => 9,
        _ => 2,
    };

    private static DateTimeOffset Utc(DateOnly date, int hour, int minute) =>
        new(date.Year, date.Month, date.Day, hour, minute, 0, TimeSpan.Zero);
}
