using System.Globalization;

namespace ShoutCalendar.Core;

/// <summary>A shared invite kept on this computer. It is not shared again.</summary>
public static class LocalCopy
{
    public static CalendarEntry From(SyncAnnouncement item, PlaceCatalog? places, DateTimeOffset now)
    {
        var channel = item.Channel is SharePolicy.YellChannel or ShoutHarvest.ShoutChannel
            ? item.Channel
            : ShoutHarvest.ShoutChannel;
        var parsed = ShoutHarvest.TryHarvest(item.Text, channel, now, places, aggressive: false);
        DateOnly? date = parsed?.Date;
        if (DateOnly.TryParseExact(item.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var stated))
            date = stated;
        TimeOnly? time = parsed?.Time;
        if (TimeOnly.TryParseExact(item.Time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var clock))
            time = clock;
        var server = string.IsNullOrWhiteSpace(parsed?.Server) ? item.World : parsed!.Server;
        return new CalendarEntry(
            date,
            time,
            parsed?.End,
            parsed?.Ward,
            server,
            parsed?.Place ?? "",
            item.Text ?? "",
            "",
            true,
            "local-" + (string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id),
            now,
            parsed?.Repeat,
            0,
            false,
            true,
            item.World ?? "");
    }
}
