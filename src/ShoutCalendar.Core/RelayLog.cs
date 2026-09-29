namespace ShoutCalendar.Core;

/// <summary>
/// Public events held by the relay. A bad signature is denied, a private channel is refused,
/// and a full cap drops the new row without removing what is already stored.
/// </summary>
public sealed class RelayLog
{
    private readonly List<SyncAnnouncement> events = new();
    private readonly HashSet<string> tombstones = new(StringComparer.OrdinalIgnoreCase);

    public SyncLimits Limits { get; set; } = new();

    public int StoredBytes => this.events.Sum(item => item.PayloadBytes);

    public IReadOnlyList<SyncAnnouncement> Events => this.events;

    public IReadOnlyCollection<string> Tombstones => this.tombstones;

    public SyncMergeResult Merge(SyncAnnouncement item) => SyncMerge.Apply(this.events, item);

    public void Restore(IEnumerable<SyncAnnouncement> rows)
    {
        this.events.AddRange(rows);
    }

    public string Accept(byte[] payload, byte[]? signature, int openConnections, int bytesThisSecond, int itemsThisTick)
    {
        if (payload is null || payload.Length == 0 || signature is null || !SyncGate.Verify(payload, signature))
            return RelayProtocol.Denied;

        var item = RelayCodec.Decode(payload);
        if (item is null || !SharePolicy.IsShareable(item.Channel) || !PlayableWorlds.TryCanonical(item.World, out var world))
            return RelayProtocol.Refused;
        if (!ShareFormat.Accepts(item.ShareFormat))
            return RelayProtocol.Refused;

        var cap = this.Limits.Clamp();
        if (itemsThisTick >= cap.MaxItemsPerTick)
            return RelayProtocol.Dropped;

        item.World = world;
        item.FromSync = true;
        item.HarvestedLocally = false;
        item.Accepted = false;
        item.ContentKey = SyncMerge.Key(item);
        if (this.tombstones.Contains(item.ContentKey) || this.tombstones.Contains(item.Id))
            return RelayProtocol.Stored;
        if (this.events.Any(row => SyncMerge.Key(row) == item.ContentKey && item.Revision <= row.Revision))
            return RelayProtocol.Stored;
        var weight = Math.Max(payload.Length, item.PayloadBytes);
        var admitted = SyncBudget.Admit(
            [item],
            _ => weight,
            cap,
            openConnections,
            bytesThisSecond,
            this.StoredBytes,
            this.StoredBytes);
        if (admitted.Count == 0)
            return RelayProtocol.Dropped;

        SyncMerge.Apply(this.events, item);
        return RelayProtocol.Stored;
    }

    public string Read(byte[] payload, byte[]? signature, out IReadOnlyList<SyncAnnouncement> rows)
    {
        rows = Array.Empty<SyncAnnouncement>();
        if (payload is null || payload.Length == 0 || signature is null || !SyncGate.Verify(payload, signature))
            return RelayProtocol.Denied;
        var world = System.Text.Encoding.UTF8.GetString(payload).Trim();
        if (!PlayableWorlds.TryCanonical(world, out var canonical))
            return RelayProtocol.Refused;
        rows = this.events
            .Where(item => item.World.Equals(canonical, StringComparison.OrdinalIgnoreCase) && SharePolicy.IsShareable(item.Channel))
            .Where(item => ShareFormat.Accepts(item.ShareFormat))
            .Where(item => !this.tombstones.Contains(item.ContentKey) && !this.tombstones.Contains(item.Id))
            .ToArray();
        return RelayProtocol.Ok;
    }

    public int PruneLegacy() => this.events.RemoveAll(item => !ShareFormat.Accepts(item.ShareFormat));

    public bool Purge(string idOrKey)
    {
        if (string.IsNullOrWhiteSpace(idOrKey))
            return false;
        var item = this.events.FirstOrDefault(row =>
            row.Id.Equals(idOrKey, StringComparison.OrdinalIgnoreCase)
            || row.ContentKey.Equals(idOrKey, StringComparison.OrdinalIgnoreCase)
            || SyncMerge.Key(row).Equals(idOrKey, StringComparison.OrdinalIgnoreCase));
        var key = item is null ? idOrKey.Trim() : (string.IsNullOrEmpty(item.ContentKey) ? SyncMerge.Key(item) : item.ContentKey);
        this.tombstones.Add(key);
        if (item is not null)
        {
            this.tombstones.Add(item.Id);
            this.events.Remove(item);
        }

        return item is not null;
    }

    public void RestoreTombstones(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            if (!string.IsNullOrWhiteSpace(key))
                this.tombstones.Add(key.Trim());
        }
    }
}
