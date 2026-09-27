using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ShoutCalendar.Core;

public sealed class SyncAnnouncement
{
    public string Id { get; set; } = "";

    public string World { get; set; } = "";

    public int Channel { get; set; }

    public string Text { get; set; } = "";

    public bool FromSync { get; set; }

    public bool HarvestedLocally { get; set; }

    public bool Accepted { get; set; }

    public bool NoteUpdated { get; set; }

    public string Category { get; set; } = "";

    public string Date { get; set; } = "";

    public string Time { get; set; } = "";

    public int Revision { get; set; }

    public string ContentKey { get; set; } = "";

    public bool IsSyncPending => this.FromSync && !this.HarvestedLocally && !this.Accepted;

    public string ColorToken => this.IsSyncPending ? "sync-pending" : this.Accepted ? "accepted" : "pending";

    public int PayloadBytes => RelayCodec.Encode(this).Length;

    public static SyncAnnouncement FromLocal(CalendarEntry entry, string homeWorld)
    {
        var world = homeWorld;
        if (PlayableWorlds.TryNamedWorld(entry.Server, out var named))
            world = named;

        return new SyncAnnouncement
        {
            Id = entry.Id,
            World = world,
            Channel = entry.Channel,
            Text = entry.EventText,
            HarvestedLocally = true,
            Accepted = entry.Accepted,
            NoteUpdated = entry.NoteUpdated,
            Date = entry.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            Time = entry.Time?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "",
            Revision = entry.NoteUpdated ? 2 : 1,
        };
    }
}

public static class EventCategories
{
    public static readonly IReadOnlyList<string> BuiltIn =
    [
        "bard show",
        "summoner show",
        "dance club",
        "gambling event",
    ];

    public static bool TrySet(SyncAnnouncement announcement, string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return false;
        announcement.Category = label.Trim();
        return true;
    }
}

public static class RelayProtocol
{
    public const string Submit = "SUBMIT";

    public const string Fetch = "FETCH";

    public const string Stored = "stored";

    public const string Refused = "refused";

    public const string Denied = "denied";

    public const string Dropped = "dropped";

    public const string Ok = "ok";

    public const string Online = "online";

    public const string Upgrade = "upgrade";

    /// <summary>Sent as X-Sync-Protocol. A newer relay answers upgrade.</summary>
    public const int Version = 1;
}

public static class RelayCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static byte[] Encode(SyncAnnouncement item) =>
        JsonSerializer.SerializeToUtf8Bytes(Wire.From(item), Options);

    public static byte[] EncodeList(IEnumerable<SyncAnnouncement> items) =>
        JsonSerializer.SerializeToUtf8Bytes(items.Select(Wire.From).ToArray(), Options);

    public static SyncAnnouncement? Decode(ReadOnlySpan<byte> payload)
    {
        try
        {
            var wire = JsonSerializer.Deserialize<Wire>(payload, Options);
            return wire?.ToAnnouncement();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static IReadOnlyList<SyncAnnouncement> DecodeList(ReadOnlySpan<byte> payload)
    {
        try
        {
            var wires = JsonSerializer.Deserialize<Wire[]>(payload, Options) ?? [];
            return wires.Select(wire => wire.ToAnnouncement()).ToArray();
        }
        catch (JsonException)
        {
            return Array.Empty<SyncAnnouncement>();
        }
    }

    public static byte[] Frame(string verb, byte[] signature, byte[] body)
    {
        var header = Encoding.ASCII.GetBytes($"{verb}\n{Convert.ToBase64String(signature)}\n{body.Length}\n");
        var framed = new byte[header.Length + body.Length];
        header.CopyTo(framed, 0);
        body.CopyTo(framed, header.Length);
        return framed;
    }

    private sealed class Wire
    {
        public string Id { get; set; } = "";

        public string World { get; set; } = "";

        public int Channel { get; set; }

        public string Text { get; set; } = "";

        public bool Accepted { get; set; }

        public bool NoteUpdated { get; set; }

        public bool HarvestedLocally { get; set; }

        public bool FromSync { get; set; }

        public string Category { get; set; } = "";

        public string Date { get; set; } = "";

        public string Time { get; set; } = "";

        public int Revision { get; set; }

        public string ContentKey { get; set; } = "";

        public static Wire From(SyncAnnouncement item) => new()
        {
            Id = item.Id,
            World = item.World,
            Channel = item.Channel,
            Text = item.Text,
            Accepted = item.Accepted,
            NoteUpdated = item.NoteUpdated,
            HarvestedLocally = item.HarvestedLocally,
            FromSync = item.FromSync,
            Category = item.Category,
            Date = item.Date,
            Time = item.Time,
            Revision = item.Revision,
            ContentKey = item.ContentKey,
        };

        public SyncAnnouncement ToAnnouncement() => new()
        {
            Id = this.Id,
            World = this.World,
            Channel = this.Channel,
            Text = this.Text,
            Accepted = this.Accepted,
            NoteUpdated = this.NoteUpdated,
            HarvestedLocally = this.HarvestedLocally,
            FromSync = this.FromSync,
            Category = this.Category,
            Date = this.Date,
            Time = this.Time,
            Revision = this.Revision,
            ContentKey = this.ContentKey,
        };
    }
}
