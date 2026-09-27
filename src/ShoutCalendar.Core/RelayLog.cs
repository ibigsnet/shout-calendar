namespace ShoutCalendar.Core;

/// <summary>
/// Public events held by the relay. A bad signature is denied, a private channel is refused,
/// and a full cap drops the new row without removing what is already stored.
/// </summary>
public sealed class RelayLog
{
    private readonly List<SyncAnnouncement> events = new();

    public SyncLimits Limits { get; set; } = new();

    public int StoredBytes => this.events.Sum(item => item.PayloadBytes);

    public IReadOnlyList<SyncAnnouncement> Events => this.events;

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

        var cap = this.Limits.Clamp();
        if (itemsThisTick >= cap.MaxItemsPerTick)
            return RelayProtocol.Dropped;

        item.World = world;
        item.FromSync = true;
        item.HarvestedLocally = false;
        item.Accepted = false;
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

        var index = this.events.FindIndex(row => row.Id == item.Id && row.World.Equals(item.World, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            this.events[index] = item;
        else
            this.events.Add(item);
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
            .ToArray();
        return RelayProtocol.Ok;
    }
}
