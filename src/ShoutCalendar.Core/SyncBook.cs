using System.Text.Json;

namespace ShoutCalendar.Core;

public sealed class SyncSnapshot
{
    public ShareSettings Settings { get; set; } = new();

    public SyncLimits Limits { get; set; } = new();

    public List<string> CheckedWorlds { get; set; } = new();

    public string Selected { get; set; } = "";

    public bool ShowSync { get; set; } = true;

    public string RelayHost { get; set; } = "";

    public int RelayPort { get; set; }

    public string RelayChoice { get; set; } = SyncRelays.PublicLabel;

    public int HoldOffSeconds { get; set; } = 10;

    public List<SyncAnnouncement> Events { get; set; } = new();

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

    public ShareSettings Settings { get; private set; } = new();

    public WorldCalendar Worlds { get; }

    public SyncLimits Limits { get; set; } = new();

    public bool SettingsStored { get; private set; } = true;

    public bool ShowSync { get; set; } = true;

    public string RelayHost { get; set; } = "";

    public int RelayPort { get; set; }

    public string RelayChoice { get; set; } = SyncRelays.PublicLabel;

    public int HoldOffSeconds { get; set; } = 10;

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

    public bool AcceptRemote(string id)
    {
        var item = this.Events.FirstOrDefault(row => row.Id == id && row.FromSync && !row.HarvestedLocally);
        if (item is null || item.Accepted)
            return false;
        item.Accepted = true;
        return true;
    }

    public int Ingest(byte[]? proof, IReadOnlyList<SyncAnnouncement> incoming, int openConnections)
    {
        if (!SyncGate.AllowRead(proof))
            return -1;
        if (openConnections > this.Limits.Clamp().MaxConnections)
            return 0;

        var shareable = incoming
            .Where(item => SharePolicy.ShouldReceive(item.Channel, this.Settings))
            .Where(item => PlayableWorlds.TryCanonical(item.World, out _))
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

        return added;
    }

    public string ToJson()
    {
        var snapshot = new SyncSnapshot
        {
            Settings = this.Settings,
            Limits = this.Limits,
            CheckedWorlds = this.Worlds.Extras().ToList(),
            Selected = this.Worlds.Selected,
            ShowSync = this.ShowSync,
            RelayHost = this.RelayHost,
            RelayPort = this.RelayPort,
            RelayChoice = this.RelayChoice,
            HoldOffSeconds = this.HoldOffSeconds,
            Events = this.Events.ToList(),
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
        this.Worlds.ClearExtras();
        foreach (var world in snapshot.CheckedWorlds ?? [])
            this.Worlds.SetChecked(world, true);
        if (!string.IsNullOrWhiteSpace(snapshot.Selected))
            this.Worlds.Select(snapshot.Selected);
        this.Events.Clear();
        foreach (var item in snapshot.Events ?? [])
        {
            if (string.IsNullOrWhiteSpace(item.Id))
                continue;
            this.Events.Add(item);
        }

        if (!string.IsNullOrWhiteSpace(snapshot.BookId))
            this.BookId = snapshot.BookId;
        this.SettingsStored = true;
        return true;
    }
}
