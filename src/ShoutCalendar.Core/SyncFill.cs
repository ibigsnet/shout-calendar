namespace ShoutCalendar.Core;

/// <summary>Orders relay rows so current and upcoming invites win the budget before past ones.</summary>
public static class SyncFill
{
    /// <summary>Passes slower than this may tip the player to ease Limits.</summary>
    public static readonly TimeSpan SlowPass = TimeSpan.FromSeconds(2);

    public static IReadOnlyList<SyncAnnouncement> Prioritize(
        IEnumerable<SyncAnnouncement> incoming,
        DateTime now,
        TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        return incoming
            .OrderBy(item => PastEvents.Ended(item, now, zone) ? 1 : 0)
            .ThenBy(item => item.Date ?? "")
            .ThenBy(item => item.Time ?? "")
            .ThenBy(item => item.Id ?? "", StringComparer.Ordinal)
            .ToList();
    }

    public static SyncFillReport Measure(
        IReadOnlyList<SyncAnnouncement> fetched,
        IReadOnlyList<SyncAnnouncement> admitted,
        DateTime now,
        TimeSpan elapsed,
        TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var fetchedCurrent = fetched.Count(item => !PastEvents.Ended(item, now, zone));
        var fetchedPast = fetched.Count - fetchedCurrent;
        var admittedSet = new HashSet<string>(admitted.Select(SyncMerge.Key), StringComparer.Ordinal);
        var admittedCurrent = admitted.Count(item => !PastEvents.Ended(item, now, zone));
        var admittedPast = admitted.Count - admittedCurrent;
        var droppedCurrent = Math.Max(0, fetchedCurrent - admittedCurrent);
        var deferredPast = Math.Max(0, fetchedPast - admittedPast);
        var droppedByCap = Math.Max(0, fetched.Count - admitted.Count);
        return new SyncFillReport(
            fetched.Count,
            admitted.Count,
            admittedCurrent,
            admittedPast,
            deferredPast,
            droppedByCap,
            droppedCurrent,
            elapsed >= SlowPass);
    }

    public static string? StatusTip(SyncFillReport report)
    {
        if (report.DroppedCurrent > 0)
            return "Sync limited — raise Limits or lower Items per tick.";
        return null;
    }

    public static string ChatTip() =>
        "Shout Calendar: sync is limited. Raise Connections, Mb/s, Items per tick, or Stored/Memory under Sync Limits.";
}

public readonly record struct SyncFillReport(
    int Fetched,
    int Admitted,
    int AdmittedCurrent,
    int AdmittedPast,
    int DeferredPast,
    int DroppedByCap,
    int DroppedCurrent,
    bool Slow);
