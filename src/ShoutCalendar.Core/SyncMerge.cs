using System.Globalization;
using System.Net.Http;
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
        PreferBody(current, incoming);
        incoming.Accepted = current.Accepted || incoming.Accepted;
        incoming.Declined = current.Declined;
        incoming.Hidden = current.Hidden;
        if (current.Declined)
            incoming.Accepted = false;
        incoming.ContentKey = SyncMerge.Key(incoming);
        events[index] = incoming;
        return SyncMergeResult.Updated;
    }

    /// <summary>Keep an explicit clock and the fuller shout when a repost is merged.</summary>
    public static void Combine(SyncAnnouncement kept, SyncAnnouncement extra)
    {
        PreferBody(kept, extra);
        kept.Text = extra.Text ?? kept.Text;
        if (!string.IsNullOrEmpty(extra.Date))
            kept.Date = extra.Date;
        if (!string.IsNullOrEmpty(extra.Time))
            kept.Time = extra.Time;
        if (string.IsNullOrEmpty(kept.World))
            kept.World = extra.World;
        kept.Accepted = kept.Accepted || extra.Accepted;
        kept.Declined = kept.Declined || extra.Declined;
        kept.Hidden = kept.Hidden && extra.Hidden;
        if (kept.Declined)
            kept.Accepted = false;
        if (extra.Revision > kept.Revision)
            kept.Revision = extra.Revision;
        kept.ContentKey = Key(kept);
    }

    private static void PreferBody(SyncAnnouncement current, SyncAnnouncement incoming)
    {
        var currentNow = ZoneClock.TryNowUntil(current.Text, out _);
        var incomingNow = ZoneClock.TryNowUntil(incoming.Text, out _);
        if (incomingNow && !currentNow)
        {
            incoming.Text = current.Text ?? "";
            if (!string.IsNullOrEmpty(current.Time))
                incoming.Time = current.Time;
            if (!string.IsNullOrEmpty(current.Date))
                incoming.Date = current.Date;
        }
        else if ((current.Text?.Length ?? 0) > (incoming.Text?.Length ?? 0) && !currentNow)
        {
            incoming.Text = current.Text ?? "";
        }

        if (string.IsNullOrEmpty(incoming.Time))
            incoming.Time = current.Time;
        if (string.IsNullOrEmpty(incoming.Date))
            incoming.Date = current.Date;
        if (string.IsNullOrEmpty(incoming.World))
            incoming.World = current.World;
    }

    private static bool Same(SyncAnnouncement row, SyncAnnouncement incoming)
    {
        if (!string.IsNullOrEmpty(incoming.Id) && row.Id == incoming.Id
            && row.World.Equals(incoming.World, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrEmpty(row.ContentKey) && row.ContentKey == incoming.ContentKey)
            return true;
        if (EventIdentity.SameShout(row.World, row.Text, incoming.World, incoming.Text))
            return true;
        return EventIdentity.SameRepost(row, incoming);
    }
}

/// <summary>Holds a burst of shared invites and releases one batch after the hold-off.</summary>
public sealed class SyncBuffer
{
    private readonly List<Held> waiting = new();
    private readonly HashSet<string> released = new(StringComparer.Ordinal);

    public IReadOnlyList<SyncAnnouncement> Push(DateTimeOffset now, int holdOffSeconds, IEnumerable<SyncAnnouncement> incoming)
    {
        foreach (var item in incoming)
        {
            var key = SyncMerge.Key(item);
            if (this.released.Contains(key) || this.waiting.Any(row => SyncMerge.Key(row.Item) == key))
                continue;
            this.waiting.Add(new Held(now, item));
        }

        var hold = TimeSpan.FromSeconds(Math.Max(0, holdOffSeconds));
        var ready = new List<SyncAnnouncement>();
        this.waiting.RemoveAll(row =>
        {
            if (now - row.Seen < hold)
                return false;
            this.released.Add(SyncMerge.Key(row.Item));
            ready.Add(row.Item);
            return true;
        });
        return ready;
    }

    /// <summary>Forget held and released keys so a debug re-sync can pull the same shouts again.</summary>
    public void Reset()
    {
        this.waiting.Clear();
        this.released.Clear();
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

/// <summary>What the relay dropdown shows beside the selected relay.</summary>
public static class RelayReach
{
    public const string Resolving = "Resolving";

    public const string Online = "Online";

    public const string Degraded = "Degraded";

    public const string Offline = "Offline";

    public const string OutOfDate = "Out of date";

    public const string ViaPublicRelay = "Via public relay";

    public static string Prefer(string primary, bool mirror, bool primaryIsPublic, string publicStatus)
    {
        if (primary == Online || primary == Degraded || primary == OutOfDate)
            return primary;
        if (!mirror || primaryIsPublic)
            return primary;
        if (publicStatus == Online)
            return ViaPublicRelay;
        if (publicStatus == OutOfDate)
            return OutOfDate;
        return Offline;
    }

    public static string Read(string host, int port, bool mirror = false)
    {
        var primary = Probe(host, port);
        if (primary == Online || primary == Degraded || primary == OutOfDate || !mirror || SyncRelays.IsPublic(host, port))
            return primary;
        return Prefer(primary, true, false, Probe(SyncRelays.PublicHost, SyncRelays.PublicPort));
    }

    private static string Probe(string host, int port)
    {
        if (string.IsNullOrWhiteSpace(host) || port < 1)
            return Offline;
        try
        {
            if (port == SyncRelays.PublicPort)
                return ReadHttp($"https://{host}/v1/status");
            using var client = new System.Net.Sockets.TcpClient();
            if (!client.ConnectAsync(host, port).Wait(TimeSpan.FromSeconds(4)) || !client.Connected)
                return Offline;
            return Online;
        }
        catch (Exception)
        {
            return Offline;
        }
    }

    public static string ReadHttp(string url)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("X-Sync-Protocol", RelayProtocol.Version.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("X-Sync-Share-Format", ShareFormat.Current.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("X-Sync-Signature", Convert.ToBase64String(new byte[64]));
        request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes("1"));
        using var response = client.Send(request);
        var status = response.Headers.TryGetValues("X-Sync-Status", out var values) ? values.FirstOrDefault() : "";
        return Describe(status);
    }

    public static string Describe(string? status)
    {
        if (string.Equals(status, RelayProtocol.Upgrade, StringComparison.OrdinalIgnoreCase))
            return OutOfDate;
        if (string.Equals(status, RelayProtocol.Degraded, StringComparison.OrdinalIgnoreCase))
            return Degraded;
        return Online;
    }
}
