namespace ShoutCalendar.Core;

/// <summary>Finite caps. Overflow is dropped. Nothing here means unlimited.</summary>
public sealed class SyncLimits
{
    public int MaxConnections { get; set; } = 4;

    public int BytesPerSecond { get; set; } = 1_000_000;

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
}
