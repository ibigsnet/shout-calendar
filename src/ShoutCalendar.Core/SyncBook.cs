using System.Text.Json;

namespace ShoutCalendar.Core;

public sealed class SyncSnapshot
{
    public ShareSettings Settings { get; set; } = new();

    public SyncLimits Limits { get; set; } = new();

    public List<string> CheckedWorlds { get; set; } = new();

    public string Selected { get; set; } = "";

    public string LastViewed { get; set; } = "";

    public List<string> ViewedWorlds { get; set; } = new();

    public bool ShowSync { get; set; } = true;

    public string RelayHost { get; set; } = "";

    public int RelayPort { get; set; }

    public string RelayChoice { get; set; } = SyncRelays.PublicLabel;

    public int HoldOffSeconds { get; set; } = 10;

    public bool Informedaholic { get; set; }

    public bool MirrorRelay { get; set; }

    public List<SyncAnnouncement> Events { get; set; } = new();

    public List<string> DismissedKeys { get; set; } = new();

    public string BookId { get; set; } = "";
}

/// <summary>Sync state kept apart from the local invite log. Detach drops remote rows and these settings.</summary>
public sealed class SyncBook
{
    public SyncBook(string currentWorld)
    {
        this.Worlds = new WorldCalendar(currentWorld);
        this.BookId = Guid.NewGuid().ToString("N");
    }

    public string BookId { get; private set; }

    public List<SyncAnnouncement> Events { get; } = new();

    public List<string> DismissedKeys { get; } = new();

    public ShareSettings Settings { get; private set; } = new();

    public WorldCalendar Worlds { get; }

    public SyncLimits Limits { get; set; } = new();

    public bool SettingsStored { get; private set; } = true;

    public bool ShowSync { get; set; } = true;

    public string RelayHost { get; set; } = "";

    public int RelayPort { get; set; }

    public string RelayChoice { get; set; } = SyncRelays.PublicLabel;

    public int HoldOffSeconds { get; set; } = 10;

    /// <summary>When set, a shared invite is accepted instead of waiting on the pending list.</summary>
    public bool Informedaholic { get; set; }

    /// <summary>When set, a custom relay is a nearer copy. If it does not answer, sync uses the public relay.</summary>
    public bool MirrorRelay { get; set; }

    /// <summary>Last relay check. Not stored. Detach clears it.</summary>
    public string RelayStatus { get; set; } = "";

    public int StoredBytes => this.Events.Sum(item => item.PayloadBytes);

    public void ForceShare()
    {
        this.Settings.Shout.Contribute = true;
        this.Settings.Yell.Contribute = true;
        this.Settings.Shout.Receive = true;
        this.Settings.Yell.Receive = true;
        this.Settings.ShareUnaccepted = true;
        this.Settings.ShareAccepted = true;
        this.Settings.ShareNoteUpdates = true;
        if (this.RelayChoice != SyncRelays.CustomLabel)
        {
            this.RelayChoice = SyncRelays.PublicLabel;
            this.RelayHost = SyncRelays.PublicHost;
            this.RelayPort = SyncRelays.PublicPort;
        }
    }

    public void Detach()
    {
        this.Events.RemoveAll(item => item.FromSync && !item.HarvestedLocally);
        foreach (var item in this.Events)
            item.FromSync = false;
        this.Settings = new ShareSettings();
        this.SettingsStored = false;
        this.ShowSync = true;
        this.RelayHost = "";
        this.RelayPort = 0;
        this.RelayChoice = SyncRelays.PublicLabel;
        this.HoldOffSeconds = 10;
        this.Informedaholic = false;
        this.MirrorRelay = false;
        this.RelayStatus = "";
        this.Limits = new SyncLimits();
        this.Worlds.ClearExtras();
    }

    public bool SetCategory(string id, string? label)
    {
        var item = this.Events.FirstOrDefault(row => row.Id == id);
        if (item is null)
            return false;
        return EventCategories.TrySet(item, label);
    }

    public bool Dismiss(string id)
    {
        var item = this.Events.FirstOrDefault(row => row.Id == id);
        if (item is null)
            return false;
        this.RememberDismissed(item);
        this.Events.Remove(item);
        return true;
    }

    public int DismissPast(DateTime now, TimeZoneInfo? zone = null) =>
        this.DismissMatching(item => PastEvents.Ended(item, now, zone));

    public int DismissOpen(ClearTarget target)
    {
        var open = new HashSet<string>(this.Worlds.Viewing(), StringComparer.OrdinalIgnoreCase);
        return this.DismissMatching(item => open.Contains(item.World) && target switch
        {
            ClearTarget.SyncAccepted => item.Accepted,
            ClearTarget.SyncUnaccepted => item.IsSyncPending,
            _ => false,
        });
    }

    public int DismissMatching(Func<SyncAnnouncement, bool> match)
    {
        var gone = this.Events.Where(match).ToList();
        foreach (var item in gone)
        {
            this.RememberDismissed(item);
            this.Events.Remove(item);
        }

        return gone.Count;
    }

    private void RememberDismissed(SyncAnnouncement item)
    {
        var key = string.IsNullOrEmpty(item.ContentKey) ? SyncMerge.Key(item) : item.ContentKey;
        if (!this.DismissedKeys.Contains(key))
            this.DismissedKeys.Add(key);
    }

    public bool DeclineRemote(string id)
    {
        var item = this.Events.FirstOrDefault(row => row.Id == id);
        if (item is null)
            return false;
        item.Declined = true;
        item.Accepted = false;
        return true;
    }

    public void AcceptSame(CalendarEntry entry, string heardOn)
    {
        var world = ShareWorld.Choose(heardOn, entry.SpeakerWorld, entry.Server);
        var text = $"{entry.EventText} {entry.Place}";
        foreach (var item in this.Events)
        {
            if (!EventIdentity.SameShout(item.World, item.Text, world, text))
                continue;
            item.Declined = false;
            item.Accepted = true;
        }
    }

    public void ApplyTombstones(IEnumerable<string> keys)
    {
        var gone = new HashSet<string>(keys.Where(key => !string.IsNullOrWhiteSpace(key)), StringComparer.OrdinalIgnoreCase);
        if (gone.Count == 0)
            return;
        this.Events.RemoveAll(item => gone.Contains(item.ContentKey) || gone.Contains(SyncMerge.Key(item)));
        foreach (var key in gone)
        {
            if (!this.DismissedKeys.Contains(key))
                this.DismissedKeys.Add(key);
        }
    }

    public bool AcceptRemote(string id)
    {
        var item = this.Events.FirstOrDefault(row => row.Id == id && row.FromSync && !row.HarvestedLocally);
        if (item is null || item.Accepted)
            return false;
        item.Accepted = true;
        return true;
    }

    public int AcceptAllRemote()
    {
        var count = 0;
        foreach (var item in this.Events)
        {
            if (!item.IsSyncPending)
                continue;
            item.Accepted = true;
            count++;
        }

        return count;
    }

    public int Ingest(byte[]? proof, IReadOnlyList<SyncAnnouncement> incoming, int openConnections, PlaceCatalog? places = null)
    {
        if (!SyncGate.AllowRead(proof))
            return -1;
        if (openConnections > this.Limits.Clamp().MaxConnections)
            return 0;

        this.ApplyTombstones(incoming.Where(item => item.Id.StartsWith("gone:", StringComparison.Ordinal)).Select(item => item.ContentKey));
        var shareable = incoming
            .Where(item => !item.Id.StartsWith("gone:", StringComparison.Ordinal))
            .Where(item => SharePolicy.ShouldReceive(item.Channel, this.Settings))
            .Where(item => PlayableWorlds.TryCanonical(item.World, out _))
            .Where(item => ShoutHarvest.IsSharedEvent(item.Text, item.Channel, DateTimeOffset.UtcNow, places))
            .Where(item => !this.AlreadyHeld(item))
            .Where(item => !this.DismissedKeys.Contains(SyncMerge.Key(item)))
            .ToArray();
        var admitted = SyncBudget.Admit(
            shareable,
            item => item.PayloadBytes,
            this.Limits,
            openConnections,
            bytesAlreadyThisSecond: 0,
            storedBytes: this.StoredBytes,
            memoryBytes: this.StoredBytes);
        var added = 0;
        foreach (var item in admitted)
        {
            if (!PlayableWorlds.TryCanonical(item.World, out var world))
                continue;
            if (!this.Worlds.Fetched().Contains(world, StringComparer.Ordinal))
                continue;
            item.World = world;
            item.FromSync = true;
            item.HarvestedLocally = false;
            item.Accepted = false;
            if (SyncMerge.Apply(this.Events, item) == SyncMergeResult.Duplicate)
                continue;
            added++;
        }

        if (this.Informedaholic)
            this.AcceptAllRemote();
        return added;
    }

    private bool AlreadyHeld(SyncAnnouncement item)
    {
        var key = SyncMerge.Key(item);
        return this.Events.Any(row => SyncMerge.Key(row) == key && item.Revision <= row.Revision);
    }

    public string ToJson()
    {
        var snapshot = new SyncSnapshot
        {
            Settings = this.Settings,
            Limits = this.Limits,
            CheckedWorlds = this.Worlds.Extras().ToList(),
            Selected = this.Worlds.Selected,
            LastViewed = this.Worlds.LastPicked,
            ViewedWorlds = this.Worlds.Viewing().ToList(),
            ShowSync = this.ShowSync,
            RelayHost = this.RelayHost,
            RelayPort = this.RelayPort,
            RelayChoice = this.RelayChoice,
            HoldOffSeconds = this.HoldOffSeconds,
            Informedaholic = this.Informedaholic,
            MirrorRelay = this.MirrorRelay,
            Events = this.Events.ToList(),
            DismissedKeys = this.DismissedKeys.ToList(),
            BookId = this.BookId,
        };
        return JsonSerializer.Serialize(snapshot, SnapshotJson.Options);
    }

    public bool ApplyJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return false;
        SyncSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<SyncSnapshot>(json, SnapshotJson.Options);
        }
        catch (JsonException)
        {
            return false;
        }

        if (snapshot is null)
            return false;
        this.Settings = snapshot.Settings ?? new ShareSettings();
        this.Settings.Shout ??= new ChannelModes();
        this.Settings.Yell ??= new ChannelModes();
        this.Limits = (snapshot.Limits ?? new SyncLimits()).Clamp();
        this.ShowSync = snapshot.ShowSync;
        this.RelayHost = snapshot.RelayHost ?? "";
        this.RelayPort = snapshot.RelayPort < 1 ? 0 : snapshot.RelayPort;
        this.RelayChoice = string.IsNullOrWhiteSpace(snapshot.RelayChoice) ? SyncRelays.PublicLabel : snapshot.RelayChoice;
        this.HoldOffSeconds = snapshot.HoldOffSeconds < 0 ? 0 : snapshot.HoldOffSeconds;
        this.Informedaholic = snapshot.Informedaholic;
        this.MirrorRelay = snapshot.MirrorRelay;
        if (this.Limits.BytesPerSecond == 65_536)
            this.Limits.BytesPerSecond = 1_000_000;
        if (this.Limits.MaxStoredBytes == 1_048_576)
            this.Limits.MaxStoredBytes = 32_000_000;
        if (this.Limits.MaxMemoryBytes == 2_097_152)
            this.Limits.MaxMemoryBytes = 64_000_000;
        this.Worlds.ClearExtras();
        foreach (var world in snapshot.CheckedWorlds ?? [])
            this.Worlds.SetChecked(world, true);
        if (!string.IsNullOrWhiteSpace(snapshot.LastViewed))
            this.Worlds.RememberPick(snapshot.LastViewed);
        else if (!string.IsNullOrWhiteSpace(snapshot.Selected))
            this.Worlds.RememberPick(snapshot.Selected);
        if (snapshot.ViewedWorlds is { Count: > 0 })
            this.Worlds.UseView(snapshot.ViewedWorlds);
        else if (!string.IsNullOrWhiteSpace(snapshot.Selected))
            this.Worlds.Select(snapshot.Selected);
        this.Events.Clear();
        foreach (var item in snapshot.Events ?? [])
        {
            if (string.IsNullOrWhiteSpace(item.Id))
                continue;
            this.Events.Add(item);
        }

        this.DismissedKeys.Clear();
        foreach (var key in snapshot.DismissedKeys ?? [])
        {
            if (!string.IsNullOrWhiteSpace(key) && !this.DismissedKeys.Contains(key))
                this.DismissedKeys.Add(key);
        }

        if (!string.IsNullOrWhiteSpace(snapshot.BookId))
            this.BookId = snapshot.BookId;
        this.SettingsStored = true;
        return true;
    }
}
