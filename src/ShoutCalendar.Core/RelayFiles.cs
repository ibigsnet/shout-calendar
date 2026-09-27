namespace ShoutCalendar.Core;

/// <summary>The relay event file and the tombstones that must not come back.</summary>
public static class RelayFiles
{
    public static string EventsFile(string storeDirectory) => Path.Combine(storeDirectory, "events.json");

    public static string TombstoneFile(string storeDirectory) => Path.Combine(storeDirectory, "tombstones.txt");

    public static void Load(string storeDirectory, RelayLog log)
    {
        var events = EventsFile(storeDirectory);
        if (File.Exists(events))
            log.Restore(RelayCodec.DecodeList(File.ReadAllBytes(events)));
        var tombstones = TombstoneFile(storeDirectory);
        if (File.Exists(tombstones))
            log.RestoreTombstones(File.ReadAllLines(tombstones));
    }

    public static void Save(string storeDirectory, RelayLog log)
    {
        Directory.CreateDirectory(storeDirectory);
        var events = EventsFile(storeDirectory);
        var temp = events + ".tmp";
        File.WriteAllBytes(temp, RelayCodec.EncodeList(log.Events));
        File.Move(temp, events, true);
        File.WriteAllLines(TombstoneFile(storeDirectory), log.Tombstones);
    }
}
