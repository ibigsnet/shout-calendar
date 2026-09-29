namespace ShoutCalendar.Core;

/// <summary>Finite caps. Overflow is dropped. Nothing here means unlimited.</summary>
public sealed class SyncLimits
{
    public int MaxConnections { get; set; } = 4;

    /// <summary>Shared invites kept on one pass. Default is about 8 Mb/s.</summary>
    public int BytesPerSecond { get; set; } = 1_000_000;

    /// <summary>Own shouts sent on one pass. Default is about 1 Mb/s.</summary>
    public int UploadBytesPerSecond { get; set; } = 125_000;

    public int MaxStoredBytes { get; set; } = 32_000_000;

    public int MaxItemsPerTick { get; set; } = 64;

    public int MaxMemoryBytes { get; set; } = 64_000_000;

    public SyncLimits Clamp()
    {
        static int AtLeastOne(int value) => value < 1 ? 1 : value;
        return new SyncLimits
        {
            MaxConnections = AtLeastOne(this.MaxConnections),
            BytesPerSecond = AtLeastOne(this.BytesPerSecond),
            UploadBytesPerSecond = AtLeastOne(this.UploadBytesPerSecond < 1 ? 125_000 : this.UploadBytesPerSecond),
            MaxStoredBytes = AtLeastOne(this.MaxStoredBytes),
            MaxItemsPerTick = AtLeastOne(this.MaxItemsPerTick),
            MaxMemoryBytes = AtLeastOne(this.MaxMemoryBytes),
        };
    }
}

public static class SyncBudget
{
    public static IReadOnlyList<T> Admit<T>(
        IReadOnlyList<T> incoming,
        Func<T, int> bytesOf,
        SyncLimits limits,
        int openConnections,
        int bytesAlreadyThisSecond,
        int storedBytes,
        int memoryBytes)
    {
        var cap = limits.Clamp();
        if (openConnections > cap.MaxConnections)
            return Array.Empty<T>();

        var taken = new List<T>();
        var rate = Math.Max(0, bytesAlreadyThisSecond);
        var stored = Math.Max(0, storedBytes);
        var memory = Math.Max(0, memoryBytes);
        foreach (var item in incoming)
        {
            if (taken.Count >= cap.MaxItemsPerTick)
                break;
            var size = Math.Max(1, bytesOf(item));
            if (rate + size > cap.BytesPerSecond)
                break;
            if (stored + size > cap.MaxStoredBytes)
                break;
            if (memory + size > cap.MaxMemoryBytes)
                break;
            taken.Add(item);
            rate += size;
            stored += size;
            memory += size;
        }

        return taken;
    }

    /// <summary>
    /// Shouts to send on this pass. Already stored revisions are skipped and do not spend the upload budget.
    /// One pass sends at most <see cref="SyncLimits.UploadBytesPerSecond"/>.
    /// </summary>
    public static IReadOnlyList<SyncAnnouncement> SelectUploads(
        IEnumerable<SyncAnnouncement> outbound,
        SyncLimits limits,
        IReadOnlyDictionary<string, int>? alreadySent = null)
    {
        var left = limits.Clamp().UploadBytesPerSecond;
        var seen = alreadySent is null
            ? new Dictionary<string, int>(StringComparer.Ordinal)
            : new Dictionary<string, int>(alreadySent, StringComparer.Ordinal);
        var chosen = new List<SyncAnnouncement>();
        foreach (var item in outbound)
        {
            if (item is null)
                continue;
            var key = SyncMerge.Key(item);
            var revision = item.Revision < 1 ? 1 : item.Revision;
            if (seen.TryGetValue(key, out var sent) && revision <= sent)
                continue;
            var size = Math.Max(1, item.PayloadBytes);
            if (size > left)
                continue;
            chosen.Add(item);
            seen[key] = revision;
            left -= size;
        }

        return chosen;
    }
}
