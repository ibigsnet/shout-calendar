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

    /// <summary>When false, no chat is harvested. Turning it back on restores only the chats that were on.</summary>
    public bool Listening { get; private set; } = true;

    private readonly HashSet<int> pausedChannels = new();

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

    /// <summary>When set, a ringing alarm also prints the event in chat.</summary>
    public bool AlarmChat { get; set; } = true;

    /// <summary>When set, an event leaves this computer after its end time.</summary>
    public bool DropPastEvents { get; set; }

    /// <summary>Past local invites stay off the calendar until this is on.</summary>
    public bool ShowPastLocal { get; set; }

    /// <summary>Past shared invites stay off the calendar until this is on.</summary>
    public bool ShowPastSync { get; set; }

    /// <summary>Scale for text in the calendar window. 1 is the normal size.</summary>
    public float TextScale { get; set; } = 1f;

    public bool AlarmUnaccepted { get; set; }

    public int AcceptedSound { get; set; } = EventAlarm.MinSound;

    public int UnacceptedSound { get; set; } = EventAlarm.MinSound;

    public int ResetSound { get; set; } = 3;

    public string AcceptedSoundFile { get; set; } = "";

    public string UnacceptedSoundFile { get; set; } = "";

    public string ResetSoundFile { get; set; } = "";

    public bool AlarmResets { get; set; } = true;

    /// <summary>0 is a flat chip. 2 is a strong shade. 3 is the strongest.</summary>
    public float ShadeStrength { get; set; }

    /// <summary>Color names whose chip text is the opposite of the usual white or black.</summary>
    public HashSet<string> InkFlips { get; } = new(StringComparer.Ordinal);

    public bool InkFlipped(string key) => this.InkFlips.Contains(key);

    public void SetInkFlip(string key, bool flipped)
    {
        if (flipped)
            this.InkFlips.Add(key);
        else
            this.InkFlips.Remove(key);
    }

    public Vector4 PendingColor { get; set; } = new(0.93f, 0.62f, 0.12f, 0.95f);

    public Vector4 AcceptedColor { get; set; } = new(0.12f, 0.48f, 0.24f, 0.95f);

    public Vector4 TodayColor { get; set; } = new(1f, 1f, 1f, 0.19f);

    /// <summary>Days from the previous or next month shown to fill the week.</summary>
    public Vector4 OutsideColor { get; set; } = new(0.22f, 0.22f, 0.24f, 0.427f);

    public Vector4 CrystalColor { get; set; } = new(0.18f, 0.52f, 0.86f, 0.95f);

    public Vector4 CactusColor { get; set; } = new(0.55f, 0.78f, 0.22f, 0.95f);

    public Vector4 EventColor { get; set; } = new(0.144f, 0f, 1f, 0.64f);

    public int AlarmMinutesBefore { get; set; } = 15;

    /// <summary>When set, an alarm also rings on the minute the event starts.</summary>
    public bool AlarmAtStart { get; set; } = true;

    public HashSet<string> PinnedSync { get; } = new(StringComparer.Ordinal);

    public bool SyncPinned(string? id) => !string.IsNullOrEmpty(id) && this.PinnedSync.Contains(id);

    public bool SetSyncPinned(string id, bool pinned) =>
        pinned ? this.PinnedSync.Add(id) : this.PinnedSync.Remove(id);

    public bool ShowLocal { get; set; } = true;

    public bool ShowLocalAccepted { get; set; } = true;

    public bool ShowLocalUnaccepted { get; set; } = true;

    public bool ShowSyncAccepted { get; set; } = true;

    public bool ShowSyncUnaccepted { get; set; } = true;

    /// <summary>When set, parked local and shared invites are drawn again.</summary>
    public bool ShowHidden { get; set; }

    /// <summary>Pending list shows the newest invite first. Default keeps newest at the bottom.</summary>
    public bool NewestFirst { get; set; }

    /// <summary>Share of the week view given to the event panel under the days. 0.28 matches the built-in split.</summary>
    public float WeekDetailShare { get; set; } = 0.28f;

    /// <summary>When set, pending invites that name another world stay on the list and on the calendar.</summary>
    public bool ShowAllServers { get; set; }
    public PendingScope PendingScope { get; set; }

    /// <summary>When set, chat, alarms, and sync wait outside a PvP match. The Wolves' Den still runs.</summary>
    public bool PauseInPvp { get; set; } = true;

    /// <summary>World the client is standing on.</summary>
    public string CurrentWorld { get; set; } = "";

    public bool ShowResets { get; set; } = true;

    /// <summary>Draws the month with fewer windows. On unless the user turns it off.</summary>
    public bool LightCalendar { get; set; } = true;

    public CalendarAppearance Appearance { get; set; } = new();

    /// <summary>Prints a chat line when a shout is kept. Off unless the user turns it on.</summary>
    public bool ParseDebug { get; set; }

    /// <summary>Plays a chat sound with the parse debug line.</summary>
    public bool ParseDebugSound { get; set; }

    public int ParseDebugSoundEffect { get; set; } = 2;

    public string ParseDebugSoundFile { get; set; } = "";

    /// <summary>Null until the user picks Week or Month.</summary>
    public bool? WeekView { get; set; }

    public Vector4 SyncPendingColor { get; set; } = new(0.63f, 0.28f, 0.72f, 0.95f);

    public Vector4 SharedBarColor { get; set; } = new(0.95f, 0.05f, 0.05f, 1f);

    public Vector4 TwitchColor { get; set; } = new(0.569f, 0.275f, 1f, 0.95f);

    public Vector4 DiscordColor { get; set; } = new(0.345f, 0.396f, 0.949f, 0.95f);

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
        var target = this.Listening ? this.Channels : this.pausedChannels;
        if (enabled)
            target.Add(channel);
        else
            target.Remove(channel);
    }

    public bool ChannelOn(int channel) =>
        (this.Listening ? this.Channels : this.pausedChannels).Contains(channel);

    /// <summary>A known chat that is unchecked stays off the calendar. Hand-added invites have no chat.</summary>
    public bool ShowsChannel(int channel) => !ChatChannels.IsKnown(channel) || this.ChannelOn(channel);

    public void SetListening(bool on)
    {
        if (on == this.Listening)
            return;
        if (!on)
        {
            this.pausedChannels.Clear();
            foreach (var channel in this.Channels)
                this.pausedChannels.Add(channel);
            this.Channels.Clear();
            this.Listening = false;
            return;
        }

        this.Channels.Clear();
        foreach (var channel in this.pausedChannels)
            this.Channels.Add(channel);
        this.Listening = true;
    }

    public void RememberPaused(IEnumerable<int>? saved)
    {
        this.pausedChannels.Clear();
        foreach (var channel in saved ?? [])
        {
            if (ChatChannels.IsKnown(channel))
                this.pausedChannels.Add(channel);
        }

        this.Channels.Clear();
        this.Listening = false;
    }

    public IReadOnlyList<int> PausedChannels() => this.pausedChannels.Order().ToArray();

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

    public bool TryAddShout(string? text, int channel, DateTimeOffset shoutTimestamp, string? sender = null, string? speakerWorld = null) =>
        this.KeepShout(text, channel, shoutTimestamp, sender, speakerWorld) is not null;

    public CalendarEntry? KeepShout(string? text, int channel, DateTimeOffset shoutTimestamp, string? sender = null, string? speakerWorld = null)
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
            return null;
        var spoken = speakerWorld?.Trim() ?? "";
        var venue = ShareWorld.Choose(this.CurrentWorld, spoken, detected.Server, combined);
        detected = detected with
        {
            Sender = SenderName.Clean(sender),
            SpeakerWorld = spoken,
            Server = string.IsNullOrWhiteSpace(detected.Server) && venue.Length > 0 ? venue : detected.Server,
        };
        if (replaceId is not null && this.Log.Rewrite(replaceId, detected))
        {
            this.burst.Remember(replaceId);
            return detected;
        }

        if (!this.Log.Add(detected))
            return null;
        var id = this.Log.Entries[^1].Id;
        this.burst.Remember(id);
        if (this.Informedaholic)
            this.Log.Accept(id);
        return detected;
    }
}
