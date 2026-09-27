using System.Numerics;

namespace ShoutCalendar.Core;

/// <summary>
/// Month shown by the calendar window. The window draws <see cref="CurrentMonth"/>
/// and moves it with <see cref="Page"/>.
/// </summary>
public sealed class CalendarSession
{
    public CalendarSession(DateOnly displayedDay)
    {
        this.Year = displayedDay.Year;
        this.Month = displayedDay.Month;
    }

    public CalendarLog Log { get; } = new();

    private readonly ChatBurst burst = new();

    public PlaceCatalog Places { get; set; } = PlaceCatalog.Empty;

    public HashSet<int> Channels { get; } = new(ChatChannels.DefaultIds);

    public string? HousingHint { get; set; }

    /// <summary>Zone used when a shout names PT, ET, CT, or MT. Unset leaves those clocks as written.</summary>
    public TimeZoneInfo? Zone { get; set; }

    public int UnacceptedHoldDays { get; set; } = 1;

    /// <summary>When set, a line is kept only if two of date, time, and place are present.</summary>
    public bool AggressiveFilter { get; set; } = true;

    /// <summary>When set, a kept invite is accepted instead of waiting on the pending list.</summary>
    public bool Informedaholic { get; set; }

    /// <summary>When set, link clicks use <see cref="OpenRememberedLinks"/> and skip the prompt.</summary>
    public bool RememberLinkChoice { get; set; }

    /// <summary>The remembered prompt answer. Used only while <see cref="RememberLinkChoice"/> is set.</summary>
    public bool OpenRememberedLinks { get; set; }

    public bool AlarmAccepted { get; set; } = true;

    public bool AlarmUnaccepted { get; set; }

    public int AcceptedSound { get; set; } = EventAlarm.MinSound;

    public int UnacceptedSound { get; set; } = EventAlarm.MinSound;

    public int ResetSound { get; set; } = 3;

    public string AcceptedSoundFile { get; set; } = "";

    public string UnacceptedSoundFile { get; set; } = "";

    public string ResetSoundFile { get; set; } = "";

    public bool AlarmResets { get; set; } = true;

    public Vector4 PendingColor { get; set; } = new(0.93f, 0.62f, 0.12f, 0.95f);

    public Vector4 AcceptedColor { get; set; } = new(0.12f, 0.48f, 0.24f, 0.95f);

    public Vector4 TodayColor { get; set; } = new(0.183f, 0.183f, 0.183f, 1f);

    public Vector4 CrystalColor { get; set; } = new(0.18f, 0.52f, 0.86f, 0.95f);

    public Vector4 CactusColor { get; set; } = new(0.55f, 0.78f, 0.22f, 0.95f);

    public Vector4 EventColor { get; set; } = new(0f, 0f, 1f, 0.64f);

    public int AlarmMinutesBefore { get; set; } = 15;

    public bool ShowLocal { get; set; } = true;

    public bool ShowResets { get; set; } = true;

    public Vector4 SyncPendingColor { get; set; } = new(0.45f, 0.28f, 0.72f, 0.95f);

    public HashSet<string> Resets { get; } = new(GameSchedule.DefaultIds);

    public string CactpotRegion { get; set; } = GameSchedule.RegionNa;

    public void UseResets(IEnumerable<string>? saved)
    {
        this.Resets.Clear();
        foreach (var id in GameSchedule.MergeSaved(saved))
        {
            if (GameSchedule.IsKnown(id))
                this.Resets.Add(id);
        }
    }

    public void SetReset(string id, bool enabled)
    {
        if (!GameSchedule.IsKnown(id))
            return;
        if (enabled)
            this.Resets.Add(id);
        else
            this.Resets.Remove(id);
    }

    public void UseChannels(IEnumerable<int>? saved)
    {
        this.Channels.Clear();
        foreach (var channel in saved ?? ChatChannels.DefaultIds)
        {
            if (ChatChannels.IsKnown(channel))
                this.Channels.Add(channel);
        }
    }

    public void SetChannel(int channel, bool enabled)
    {
        if (!ChatChannels.IsKnown(channel))
            return;
        if (enabled)
            this.Channels.Add(channel);
        else
            this.Channels.Remove(channel);
    }

    public int Year { get; private set; }

    public int Month { get; private set; }

    public CalendarMonth CurrentMonth() => CalendarMonth.Create(this.Year, this.Month, this.Log.Entries);

    public CalendarMonth Page(int monthDelta)
    {
        var next = this.CurrentMonth().Page(monthDelta);
        this.Year = next.Year;
        this.Month = next.Month;
        return next;
    }

    public void Show(DateOnly day)
    {
        this.Year = day.Year;
        this.Month = day.Month;
    }

    public bool TryAddShout(string? text, int channel, DateTimeOffset shoutTimestamp, string? sender = null, string? speakerWorld = null)
    {
        var combined = this.burst.Push(sender, channel, shoutTimestamp, text, out var replaceId);
        var detected = ShoutHarvest.TryHarvest(
            combined,
            channel,
            shoutTimestamp,
            this.Places,
            this.Channels,
            this.HousingHint,
            this.AggressiveFilter,
            this.Zone);
        if (detected is null)
            return false;
        detected = detected with
        {
            Sender = sender?.Trim() ?? "",
            SpeakerWorld = speakerWorld?.Trim() ?? "",
        };
        if (replaceId is not null && this.Log.Rewrite(replaceId, detected))
        {
            this.burst.Remember(replaceId);
            return true;
        }

        if (!this.Log.Add(detected))
            return false;
        var id = this.Log.Entries[^1].Id;
        this.burst.Remember(id);
        if (this.Informedaholic)
            this.Log.Accept(id);
        return true;
    }
}
