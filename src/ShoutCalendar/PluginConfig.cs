using System.Globalization;
using System.Numerics;
using Dalamud.Configuration;
using ShoutCalendar.Core;

namespace ShoutCalendar;

public sealed class PluginConfig : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public List<StoredEvent> Events { get; set; } = new();

    /// <summary>Shared invite ids pinned on this computer. Not sent to the relay.</summary>
    public List<string> PinnedSync { get; set; } = new();

    /// <summary>Log lines already copied into <see cref="Events"/>. Not a duplicate policy for live shouts.</summary>
    public List<string> ImportedLogLines { get; set; } = new();

    /// <summary>Null means the install defaults in <see cref="ChatChannels.DefaultIds"/>.</summary>
    public List<int>? WatchedChannels { get; set; }

    /// <summary>Null means listening is on. False pauses harvest and keeps <see cref="PausedChannels"/>.</summary>
    public bool? Listening { get; set; }

    public List<int>? PausedChannels { get; set; }

    public int UnacceptedHoldDays { get; set; } = 1;

    /// <summary>When set, a new line is kept only if two of date, time, and place are present.</summary>
    public bool AggressiveFilter { get; set; } = true;

    public bool Informedaholic { get; set; }

    public bool RememberLinkChoice { get; set; }

    public bool OpenRememberedLinks { get; set; }

    public bool AlarmAccepted { get; set; } = true;

    /// <summary>Null means chat announcements stay on.</summary>
    public bool? AlarmChat { get; set; }

    /// <summary>When set, events leave this computer after their end time.</summary>
    public bool DropPastEvents { get; set; }

    /// <summary>Past local invites stay off the calendar until this is on.</summary>
    public bool ShowPastLocal { get; set; }

    /// <summary>Past shared invites stay off the calendar until this is on.</summary>
    public bool ShowPastSync { get; set; }

    /// <summary>Scale for text in the calendar window. 1 is the normal size.</summary>
    public float TextScale { get; set; } = 1f;

    public bool AlarmUnaccepted { get; set; }

    public int AcceptedSound { get; set; } = 1;

    public int UnacceptedSound { get; set; } = 1;

    public int ResetSound { get; set; } = 3;

    public string AcceptedSoundFile { get; set; } = "";

    public string UnacceptedSoundFile { get; set; } = "";

    public string ResetSoundFile { get; set; } = "";

    public bool AlarmResets { get; set; } = true;

    /// <summary>Null keeps the usual shade. 0 is flat. 2 is usual. 3 is the strongest.</summary>
    public float? ShadeStrength { get; set; }

    public List<string> InkFlips { get; set; } = new();

    public Vector4 PendingColor { get; set; } = new(0.93f, 0.62f, 0.12f, 0.95f);

    public Vector4 AcceptedColor { get; set; } = new(0.12f, 0.48f, 0.24f, 0.95f);

    public Vector4 TodayColor { get; set; } = new(1f, 1f, 1f, 0.19f);

    public Vector4 OutsideColor { get; set; } = new(0.22f, 0.22f, 0.24f, 0.427f);

    public Vector4 CrystalColor { get; set; } = new(0.18f, 0.52f, 0.86f, 0.95f);

    public Vector4 CactusColor { get; set; } = new(0.55f, 0.78f, 0.22f, 0.95f);

    public Vector4 EventColor { get; set; } = new(0.144f, 0f, 1f, 0.64f);

    public int AlarmMinutesBefore { get; set; } = 15;

    /// <summary>When set, an alarm also rings on the minute the event starts.</summary>
    public bool AlarmAtStart { get; set; } = true;

    public bool? ShowLocal { get; set; }

    public bool? ShowLocalAccepted { get; set; }

    public bool? ShowLocalUnaccepted { get; set; }

    public bool? ShowSyncAccepted { get; set; }

    public bool? ShowSyncUnaccepted { get; set; }

    public bool? ShowHidden { get; set; }

    public bool NewestFirst { get; set; }

    public float WeekDetailShare { get; set; } = 0.28f;

    public bool ShowLocalHere { get; set; }

    public bool ShowAllServers { get; set; }
    public PendingScope? PendingScope { get; set; }

    public bool? PauseInPvp { get; set; }

    public bool? ShowResets { get; set; }

    public bool LightCalendar { get; set; }

    /// <summary>Unset means the faster calendar stays on.</summary>
    public bool? FastCalendar { get; set; }

    public CalendarAppearance? Appearance { get; set; }

    public bool ParseDebug { get; set; }

    public bool ParseDebugSound { get; set; }

    public int ParseDebugSoundEffect { get; set; } = 2;

    public string ParseDebugSoundFile { get; set; } = "";

    public bool? WeekView { get; set; }

    public Vector4 SyncPendingColor { get; set; } = new(0.63f, 0.28f, 0.72f, 0.95f);

    public Vector4 SharedBarColor { get; set; } = new(0.95f, 0.05f, 0.05f, 1f);

    public Vector4 TwitchColor { get; set; } = new(0.569f, 0.275f, 1f, 0.95f);

    public Vector4 DiscordColor { get; set; } = new(0.345f, 0.396f, 0.949f, 0.95f);

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

    public DateTimeOffset? StartUtc { get; set; }
    public DateTimeOffset? EndUtc { get; set; }
    public string SourceTimeZone { get; set; } = "";
    public int Revision { get; set; } = 1;
    public string[] ExcludedDates { get; set; } = [];
    public string? RepeatUntil { get; set; }
    public bool SeriesDeleted { get; set; }

    public int? Ward { get; set; }

    public string? Server { get; set; }

    public string Place { get; set; } = "";

    public string EventText { get; set; } = "";

    public string Sender { get; set; } = "";

    public bool Accepted { get; set; }

    public string Id { get; set; } = "";

    public string DetectedAt { get; set; } = "";

    public string? Repeat { get; set; }

    public int Channel { get; set; }

    public bool NoteUpdated { get; set; }

    public bool Manual { get; set; }

    public bool Hidden { get; set; }

    public bool Pinned { get; set; }

    public string SpeakerWorld { get; set; } = "";

    public float? ColorR { get; set; }

    public float? ColorG { get; set; }

    public float? ColorB { get; set; }

    public float? ColorA { get; set; }

    public static StoredEvent From(CalendarEntry entry)
    {
        return new StoredEvent
        {
            Date = entry.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            Time = entry.Time?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "",
            End = entry.End?.ToString("HH:mm", CultureInfo.InvariantCulture),
            StartUtc = entry.StartUtc,
            EndUtc = entry.EndUtc,
            SourceTimeZone = entry.SourceTimeZone,
            Revision = entry.Revision,
            ExcludedDates = (entry.ExcludedDates ?? []).Select(day => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ToArray(),
            RepeatUntil = entry.RepeatUntil?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), SeriesDeleted = entry.SeriesDeleted,
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
            Repeat = entry.Repeat?.Store(),
            Channel = entry.Channel,
            NoteUpdated = entry.NoteUpdated,
            Manual = entry.Manual,
            Hidden = entry.Hidden,
            Pinned = entry.Pinned,
            SpeakerWorld = entry.SpeakerWorld,
            ColorR = entry.Color?.X,
            ColorG = entry.Color?.Y,
            ColorB = entry.Color?.Z,
            ColorA = entry.Color?.W,
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
                : default,
            EventRepeat.Parse(stored.Repeat),
            stored.Channel,
            stored.NoteUpdated,
            stored.Manual,
            stored.SpeakerWorld,
            stored.ColorA is null
                ? null
                : new Vector4(stored.ColorR ?? 0f, stored.ColorG ?? 0f, stored.ColorB ?? 0f, stored.ColorA.Value),
            stored.Hidden,
            stored.Pinned,
            stored.StartUtc,
            stored.EndUtc,
            stored.SourceTimeZone ?? "",
            Math.Clamp(stored.Revision, 1, int.MaxValue - 1),
            stored.ExcludedDates is { Length: > 0 } ? stored.ExcludedDates
                .Select(text => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? (DateOnly?)day : null)
                .Where(day => day.HasValue).Select(day => day!.Value).Distinct().ToArray() : null,
            DateOnly.TryParseExact(stored.RepeatUntil, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var until) ? until : null,
            stored.SeriesDeleted);
        return true;
    }
}
