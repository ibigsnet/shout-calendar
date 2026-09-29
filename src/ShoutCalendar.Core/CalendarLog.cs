using System.Numerics;

namespace ShoutCalendar.Core;

/// <summary>
/// Detected shouts. A new shout is pending until <see cref="Accept"/>. <see cref="AcceptPending"/> accepts the ones already waiting.
/// </summary>
public sealed class CalendarLog
{
    private readonly List<CalendarEntry> entries = new();

    public IReadOnlyList<CalendarEntry> Entries => this.entries;

    /// <summary>Changes when an invite is added, edited, or removed.</summary>
    public int Revision { get; private set; }

    private void Touch() => this.Revision++;

    public bool Add(CalendarEntry? entry)
    {
        if (entry is null)
            return false;
        var when = entry.DetectedAt == default ? DateTimeOffset.UtcNow : entry.DetectedAt;
        var same = this.entries.FindIndex(row => EventIdentity.SameEntry(row, entry) || EventIdentity.SameSpeaker(row, entry, when));
        if (same >= 0)
        {
            var current = this.entries[same];
            this.entries[same] = Stamp(entry with
            {
                Id = current.Id,
                Accepted = current.Accepted || entry.Accepted,
                Sender = string.IsNullOrWhiteSpace(entry.Sender) ? current.Sender : entry.Sender,
                SpeakerWorld = string.IsNullOrWhiteSpace(entry.SpeakerWorld) ? current.SpeakerWorld : entry.SpeakerWorld,
                Manual = current.Manual,
                DetectedAt = current.DetectedAt == default ? entry.DetectedAt : current.DetectedAt,
            });
            this.Touch();
            return true;
        }

        if (string.IsNullOrEmpty(entry.Id))
            entry = entry with { Id = Guid.NewGuid().ToString("N"), Accepted = false };
        this.entries.Add(Stamp(entry));
        this.Touch();
        return true;
    }

    public int Reharvest(Func<CalendarEntry, CalendarEntry?> read)
    {
        var changed = 0;
        for (var i = 0; i < this.entries.Count; i++)
        {
            var current = this.entries[i];
            if (current.Manual || current.NoteUpdated || string.IsNullOrWhiteSpace(current.EventText))
                continue;
            var next = read(current);
            if (next is null)
                continue;
            var refreshClocks = ZoneClock.TryNowUntil(current.EventText, out _);
            var updated = current with
            {
                Date = refreshClocks ? next.Date ?? current.Date : current.Date ?? next.Date,
                Time = refreshClocks ? next.Time ?? current.Time : current.Time ?? next.Time,
                End = refreshClocks ? next.End ?? current.End : current.End ?? next.End,
                Ward = current.Ward ?? next.Ward,
                Server = string.IsNullOrWhiteSpace(current.Server) ? next.Server : current.Server,
                Place = next.Place.Length > current.Place.Length ? next.Place : current.Place,
            };
            if (updated == current)
                continue;
            this.entries[i] = updated;
            changed++;
        }

        if (changed > 0)
            this.Touch();
        return changed;
    }

    public bool Rewrite(string id, CalendarEntry incoming)
    {
        var index = this.entries.FindIndex(entry => entry.Id == id);
        if (index < 0)
            return false;
        var current = this.entries[index];
        this.entries[index] = Stamp(incoming with
        {
            Id = current.Id,
            Accepted = current.Accepted,
            Sender = string.IsNullOrWhiteSpace(incoming.Sender) ? current.Sender : incoming.Sender,
            NoteUpdated = current.NoteUpdated,
        });
        this.Touch();
        return true;
    }

    public bool Revise(string id, string note, string dateText, string timeText, string placeText, DateTimeOffset heardAt, PlaceCatalog places, IReadOnlySet<int> channels)
    {
        var index = this.entries.FindIndex(entry => entry.Id == id);
        if (index < 0)
            return false;

        var current = this.entries[index];
        var parsed = ShoutHarvest.TryHarvest(note, ShoutHarvest.ShoutChannel, heardAt, places, channels);
        DateOnly? date = parsed?.Date;
        if (DateOnly.TryParseExact(dateText.Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var typedDate))
            date = typedDate;
        else if (string.IsNullOrWhiteSpace(dateText))
            date = parsed?.Date;

        TimeOnly? time = current.Time;
        TimeOnly? end = current.End;
        var typedClock = timeText.Trim();
        if (typedClock.Length == 0)
        {
            time = parsed?.Time ?? current.Time;
            end = parsed?.End ?? current.End;
        }
        else if (ZoneClock.TryTyped(typedClock, out var start, out var typedEnd))
        {
            time = start;
            end = typedEnd;
        }

        var place = string.IsNullOrWhiteSpace(placeText) ? parsed?.Place ?? "" : placeText.Trim();
        var text = string.IsNullOrWhiteSpace(note) ? current.EventText : note.Trim();
        this.entries[index] = current with
        {
            EventText = text,
            Date = date,
            Time = time,
            End = end,
            Place = place,
            Ward = parsed?.Ward ?? current.Ward,
            Server = parsed?.Server ?? current.Server,
            NoteUpdated = current.NoteUpdated || !string.Equals(text, current.EventText, StringComparison.Ordinal),
        };
        this.Touch();
        return true;
    }

    public bool SetColor(string id, Vector4? color)
    {
        var index = this.entries.FindIndex(entry => entry.Id == id);
        if (index < 0)
            return false;
        this.entries[index] = this.entries[index] with { Color = color };
        this.Touch();
        return true;
    }

    public bool Remove(string id)
    {
        var index = this.entries.FindIndex(entry => entry.Id == id);
        if (index < 0)
            return false;
        this.entries.RemoveAt(index);
        this.Touch();
        return true;
    }

    public bool Accept(string id)
    {
        var index = this.entries.FindIndex(entry => entry.Id == id);
        if (index < 0 || this.entries[index].Accepted)
            return false;
        this.entries[index] = this.entries[index] with { Accepted = true, Hidden = false };
        this.Touch();
        return true;
    }

    public bool SetHidden(string id, bool hidden)
    {
        var index = this.entries.FindIndex(entry => entry.Id == id);
        if (index < 0 || this.entries[index].Hidden == hidden)
            return false;
        this.entries[index] = this.entries[index] with { Hidden = hidden };
        this.Touch();
        return true;
    }

    /// <summary>Park or restore every invite that is still waiting. Accepted rows stay as they are.</summary>
    public int SetPendingHidden(bool hidden)
    {
        var count = 0;
        for (var i = 0; i < this.entries.Count; i++)
        {
            if (this.entries[i].Accepted || this.entries[i].Hidden == hidden)
                continue;
            this.entries[i] = this.entries[i] with { Hidden = hidden };
            count++;
        }

        if (count > 0)
            this.Touch();
        return count;
    }

    public int AcceptPending()
    {
        var count = 0;
        for (var i = 0; i < this.entries.Count; i++)
        {
            if (this.entries[i].Accepted)
                continue;
            this.entries[i] = this.entries[i] with { Accepted = true };
            count++;
        }

        if (count > 0)
            this.Touch();
        return count;
    }

    public int ExpireUnaccepted(DateTimeOffset now, int holdDays)
    {
        if (holdDays < 1)
            holdDays = 1;
        var cutoff = now - TimeSpan.FromDays(holdDays);
        var removed = this.entries.RemoveAll(entry => !entry.Accepted && entry.DetectedAt < cutoff);
        if (removed > 0)
            this.Touch();
        return removed;
    }

    public void Clear()
    {
        if (this.entries.Count == 0)
            return;
        this.entries.Clear();
        this.Touch();
    }

    public int ClearAccepted()
    {
        var removed = this.entries.RemoveAll(entry => entry.Accepted);
        if (removed > 0)
            this.Touch();
        return removed;
    }

    public int ClearUnaccepted()
    {
        var removed = this.entries.RemoveAll(entry => !entry.Accepted);
        if (removed > 0)
            this.Touch();
        return removed;
    }

    public int ClearPast(DateTime now, TimeZoneInfo? zone = null)
    {
        var removed = this.entries.RemoveAll(entry => PastEvents.Ended(entry, now, zone));
        if (removed > 0)
            this.Touch();
        return removed;
    }

    private static CalendarEntry Stamp(CalendarEntry entry)
    {
        return entry.DetectedAt == default
            ? entry with { DetectedAt = DateTimeOffset.UtcNow }
            : entry;
    }

    public void Restore(IEnumerable<CalendarEntry> saved)
    {
        foreach (var entry in saved)
        {
            var stored = string.IsNullOrEmpty(entry.Id)
            ? entry with { Id = Guid.NewGuid().ToString("N") }
            : entry;
        this.entries.Add(Stamp(stored));
        }

        this.Touch();
    }
}
