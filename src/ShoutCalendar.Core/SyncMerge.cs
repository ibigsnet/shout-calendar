using System.Security.Cryptography;
using System.Text;

namespace ShoutCalendar.Core;

public enum SyncMergeResult
{
    New,
    Duplicate,
    Updated,
}

/// <summary>
/// Several people hearing the same public shout are one calendar row.
/// A later edit is a newer revision of that row, not a second invite.
/// </summary>
public static class SyncMerge
{
    public static string Key(SyncAnnouncement item)
    {
        var text = string.Join(' ', (item.Text ?? "").Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        var raw = $"{item.World.Trim().ToLowerInvariant()}|{item.Channel}|{text}|{item.Date}|{item.Time}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    public static SyncMergeResult Apply(List<SyncAnnouncement> events, SyncAnnouncement incoming)
    {
        incoming.ContentKey = Key(incoming);
        var index = events.FindIndex(row => Same(row, incoming));
        if (index < 0)
        {
            if (incoming.Revision < 1)
                incoming.Revision = 1;
            events.Add(incoming);
            return SyncMergeResult.New;
        }

        var current = events[index];
        var sameBody = string.Equals(current.Text, incoming.Text, StringComparison.Ordinal)
            && string.Equals(current.Category, incoming.Category, StringComparison.Ordinal);
        if (incoming.Revision <= current.Revision && sameBody)
            return SyncMergeResult.Duplicate;

        incoming.Revision = Math.Max(current.Revision + 1, incoming.Revision);
        if (!string.IsNullOrEmpty(current.Id))
            incoming.Id = current.Id;
        incoming.ContentKey = current.ContentKey;
        events[index] = incoming;
        return SyncMergeResult.Updated;
    }

    private static bool Same(SyncAnnouncement row, SyncAnnouncement incoming)
    {
        if (!string.IsNullOrEmpty(incoming.Id) && row.Id == incoming.Id
            && row.World.Equals(incoming.World, StringComparison.OrdinalIgnoreCase))
            return true;
        return !string.IsNullOrEmpty(row.ContentKey) && row.ContentKey == incoming.ContentKey;
    }
}

/// <summary>Holds a burst of shared invites and releases one batch after the hold-off.</summary>
public sealed class SyncBuffer
{
    private readonly List<Held> waiting = new();

    public IReadOnlyList<SyncAnnouncement> Push(DateTimeOffset now, int holdOffSeconds, IEnumerable<SyncAnnouncement> incoming)
    {
        foreach (var item in incoming)
        {
            var key = SyncMerge.Key(item);
            if (this.waiting.Any(row => SyncMerge.Key(row.Item) == key))
                continue;
            this.waiting.Add(new Held(now, item));
        }

        var hold = TimeSpan.FromSeconds(Math.Max(0, holdOffSeconds));
        var ready = new List<SyncAnnouncement>();
        this.waiting.RemoveAll(row =>
        {
            if (now - row.Seen < hold)
                return false;
            ready.Add(row.Item);
            return true;
        });
        return ready;
    }

    private readonly record struct Held(DateTimeOffset Seen, SyncAnnouncement Item);
}

public static class SyncRelays
{
    public const string PublicLabel = "Public relay";

    public const string CustomLabel = "Custom";

    public const string PublicHost = "shout.ibigs.us";

    public const int PublicPort = 443;

    public static bool IsPublic(string? host, int port) =>
        port == PublicPort && string.Equals(host, PublicHost, StringComparison.OrdinalIgnoreCase);
}
