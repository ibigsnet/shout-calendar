using System.Globalization;

namespace ShoutCalendar.Core;

public readonly record struct SyncPassSample(
    int RelayRows,
    int RelayBytes,
    int Released,
    int Added,
    int UploadBytes,
    TimeSpan Elapsed,
    SyncFillReport Report);

/// <summary>One line per fetch pass, for checking that sync stays inside its limits.</summary>
public static class SyncPerf
{
    public const int Keep = 40;

    public static string Pace(SyncPassSample sample)
    {
        if (sample.Report.DroppedCurrent > 0)
            return "behind";
        if (sample.Report.DeferredPast > 0)
            return "pacing past";
        if (sample.Elapsed >= SyncFill.SlowPass)
            return "slow";
        if (sample.RelayRows == 0 && sample.Added == 0)
            return "idle";
        return "keeping up";
    }

    public static string Line(DateTimeOffset at, SyncPassSample sample, SyncLimits limits, int storedBytes)
    {
        var cap = limits.Clamp();
        var ms = Math.Max(0, (int)sample.Elapsed.TotalMilliseconds);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{at.LocalDateTime:HH:mm:ss}  {Pace(sample)}  {ms} ms  relay {sample.RelayRows} ({Kb(sample.RelayBytes)} KB)  applied {sample.Added}  current dropped {sample.Report.DroppedCurrent}  past waiting {sample.Report.DeferredPast}  upload {Kb(sample.UploadBytes)}/{Kb(cap.UploadBytesPerSecond)} KB  download cap {Kb(cap.BytesPerSecond)} KB  items/tick {cap.MaxItemsPerTick}  stored {Kb(storedBytes)}/{Kb(cap.MaxStoredBytes)} KB");
    }

    private static int Kb(int bytes) => Math.Max(0, bytes) / 1000;
}
