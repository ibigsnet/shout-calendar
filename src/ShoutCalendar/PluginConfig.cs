using System.Globalization;
using System.Numerics;
using Dalamud.Configuration;
using ShoutCalendar.Core;

namespace ShoutCalendar;

public sealed class PluginConfig : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public List<StoredEvent> Events { get; set; } = new();

    /// <summary>Log lines already copied into <see cref="Events"/>. Not a duplicate policy for live shouts.</summary>
    public List<string> ImportedLogLines { get; set; } = new();

    /// <summary>Null means the install defaults in <see cref="ChatChannels.DefaultIds"/>.</summary>
    public List<int>? WatchedChannels { get; set; }

    public int UnacceptedHoldDays { get; set; } = 1;

    /// <summary>When set, a new line is kept only if two of date, time, and place are present.</summary>
    public bool AggressiveFilter { get; set; } = true;

    public bool AlarmAccepted { get; set; } = true;

    public bool AlarmUnaccepted { get; set; }

    public int AcceptedSound { get; set; } = 1;

    public int UnacceptedSound { get; set; } = 1;

    public int ResetSound { get; set; } = 3;

    public string AcceptedSoundFile { get; set; } = "";

    public string UnacceptedSoundFile { get; set; } = "";

    public string ResetSoundFile { get; set; } = "";

    public bool AlarmResets { get; set; } = true;

    public Vector4 PendingColor { get; set; } = new(0.93f, 0.62f, 0.12f, 0.95f);

    public Vector4 AcceptedColor { get; set; } = new(0.12f, 0.48f, 0.24f, 0.95f);

    public Vector4 TodayColor { get; set; } = new(0.34f, 0.40f, 0.48f, 1f);

    public Vector4 CrystalColor { get; set; } = new(0.18f, 0.52f, 0.86f, 0.95f);

    public Vector4 CactusColor { get; set; } = new(0.55f, 0.78f, 0.22f, 0.95f);

    public Vector4 EventColor { get; set; } = new(0.10f, 0.10f, 0.12f, 0.95f);

    public int AlarmMinutesBefore { get; set; } = 15;

    /// <summary>Null means <see cref="GameSchedule.DefaultIds"/>.</summary>
    public List<string>? EnabledResets { get; set; }

    public string CactpotRegion { get; set; } = "na";

    public IEnumerable<CalendarEntry> ToEntries()
    {
        foreach (var stored in this.Events)
        {
            if (StoredEvent.TryToEntry(stored, out var entry))
                yield return entry;
        }
    }

    public void Add(CalendarEntry entry)
    {
        this.Events.Add(StoredEvent.From(entry));
    }
}

public sealed class StoredEvent
{
    public string Date { get; set; } = "";

    public string Time { get; set; } = "";

    public string? End { get; set; }

    public int? Ward { get; set; }

    public string? Server { get; set; }

    public string Place { get; set; } = "";

    public string EventText { get; set; } = "";

    public string Sender { get; set; } = "";

    public bool Accepted { get; set; }

    public string Id { get; set; } = "";

    public string DetectedAt { get; set; } = "";

    public static StoredEvent From(CalendarEntry entry)
    {
        return new StoredEvent
        {
            Date = entry.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            Time = entry.Time?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "",
            End = entry.End?.ToString("HH:mm", CultureInfo.InvariantCulture),
            Ward = entry.Ward,
            Server = entry.Server,
            Place = entry.Place,
            EventText = entry.EventText,
            Sender = entry.Sender,
            Accepted = entry.Accepted,
            Id = entry.Id,
            DetectedAt = entry.DetectedAt == default
                ? ""
                : entry.DetectedAt.ToString("o", CultureInfo.InvariantCulture),
        };
    }

    public static bool TryToEntry(StoredEvent stored, out CalendarEntry entry)
    {
        entry = null!;
        DateOnly? date = null;
        if (!string.IsNullOrEmpty(stored.Date))
        {
            if (!DateOnly.TryParseExact(stored.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                return false;
            date = parsedDate;
        }
        TimeOnly? time = null;
        if (!string.IsNullOrEmpty(stored.Time))
        {
            if (!TimeOnly.TryParseExact(stored.Time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedTime))
                return false;
            time = parsedTime;
        }
        TimeOnly? end = null;
        if (!string.IsNullOrEmpty(stored.End))
        {
            if (!TimeOnly.TryParseExact(stored.End, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedEnd))
                return false;
            end = parsedEnd;
        }

        entry = new CalendarEntry(
            date,
            time,
            end,
            stored.Ward,
            stored.Server,
            stored.Place,
            stored.EventText,
            stored.Sender,
            stored.Accepted,
            string.IsNullOrEmpty(stored.Id) ? Guid.NewGuid().ToString("N") : stored.Id,
            DateTimeOffset.TryParse(stored.DetectedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var detectedAt)
                ? detectedAt
                : default);
        return true;
    }
}
