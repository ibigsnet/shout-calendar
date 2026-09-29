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
        var same = this.entries.FindIndex(row =>
            EventIdentity.SameRepost(row, entry)
            || EventIdentity.SameEntry(row, entry)
            || EventIdentity.SameSpeaker(row, entry, when));
        if (same >= 0)
        {
            this.entries[same] = Stamp(Merge(this.entries[same], entry));
            this.Touch();
            return true;
        }

        if (string.IsNullOrEmpty(entry.Id))
            entry = entry with { Id = Guid.NewGuid().ToString("N"), Accepted = false };
        this.entries.Add(Stamp(entry));
        this.Touch();
        return true;
    }

    /// <summary>Collapse reposts already stored. The kept row gains a missing clock, place, or the fuller shout.</summary>
    public int FoldReposts()
    {
        var removed = 0;
        for (var i = 0; i < this.entries.Count; i++)
        {
            for (var j = i + 1; j < this.entries.Count;)
            {
                if (!EventIdentity.SameRepost(this.entries[i], this.entries[j]))
                {
                    j++;
                    continue;
                }

                this.entries[i] = Merge(this.entries[i], this.entries[j]);
                this.entries.RemoveAt(j);
                removed++;
            }
        }

        if (removed > 0)
            this.Touch();
        return removed;
    }

    /// <summary>Fill a local invite from the shared copy of the same night. Does not accept it.</summary>
    public int Absorb(IEnumerable<SyncAnnouncement> shared)
    {
        var changed = 0;
        foreach (var item in shared)
        {
            if (string.IsNullOrWhiteSpace(item.Text))
                continue;
            var incoming = FromShared(item);
            var index = this.entries.FindIndex(row => EventIdentity.SameRepost(row, incoming));
            if (index < 0)
                continue;
            var merged = Merge(this.entries[index], incoming);
            if (merged == this.entries[index])
                continue;
            this.entries[index] = merged;
            changed++;
        }

        if (changed > 0)
            this.Touch();
        return changed;
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
                Place = PreferPlace(current.Place, next.Place),
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

    private static CalendarEntry Merge(CalendarEntry current, CalendarEntry incoming)
    {
        var text = current.EventText ?? "";
        var time = current.Time;
        var end = current.End;
        if (!current.NoteUpdated)
        {
            var currentNow = ZoneClock.TryNowUntil(current.EventText, out _);
            var incomingNow = ZoneClock.TryNowUntil(incoming.EventText, out _);
            if (!incomingNow && incoming.Time is not null && ZoneClock.Walls(incoming.EventText).Count > 0)
            {
                time = incoming.Time;
                end = incoming.End ?? (currentNow ? null : end);
            }
            else
            {
                time ??= incoming.Time;
                end ??= incoming.End;
            }

            if (!incomingNow && (currentNow || (incoming.EventText?.Length ?? 0) > text.Length))
                text = incoming.EventText ?? text;

            if (end is null)
            {
                var walls = ZoneClock.Walls(text);
                if (walls.Count >= 2)
                    end = walls[1].Time;
            }
        }

        return current with
        {
            EventText = text,
            Date = current.Date ?? incoming.Date,
            Time = time,
            End = end,
            Ward = current.Ward ?? incoming.Ward,
            Server = string.IsNullOrWhiteSpace(current.Server) ? incoming.Server ?? "" : current.Server,
            Place = string.IsNullOrWhiteSpace(incoming.Place) ? current.Place : PreferPlace(current.Place, incoming.Place),
            Sender = string.IsNullOrWhiteSpace(incoming.Sender) ? current.Sender : incoming.Sender,
            SpeakerWorld = string.IsNullOrWhiteSpace(incoming.SpeakerWorld) ? current.SpeakerWorld : incoming.SpeakerWorld,
            Accepted = current.Accepted || incoming.Accepted,
            Pinned = current.Pinned || incoming.Pinned,
            Channel = current.Channel != 0 ? current.Channel : incoming.Channel,
            DetectedAt = current.DetectedAt == default ? incoming.DetectedAt : current.DetectedAt,
        };
    }

    private static CalendarEntry FromShared(SyncAnnouncement item)
    {
        DateOnly? date = DateOnly.TryParseExact(item.Date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var day)
            ? day
            : null;
        TimeOnly? time = TimeOnly.TryParseExact(item.Time, "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var clock)
            ? clock
            : null;
        var walls = ZoneClock.Walls(item.Text);
        if (time is null && walls.Count > 0)
            time = walls[0].Time;
        TimeOnly? end = walls.Count >= 2 ? walls[1].Time : null;
        return new CalendarEntry(date, time, end, NumberFrom(item.Text), item.World, "", item.Text ?? "", "", false, item.Id, default);
    }

    private static int? NumberFrom(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var match = System.Text.RegularExpressions.Regex.Match(text, @"\b(?:ward\s*|[Ww])(\d{1,2})\b");
        return match.Success && int.TryParse(match.Groups[1].Value, out var number) ? number : null;
    }

    private static string PreferPlace(string current, string next)
    {
        if (string.IsNullOrWhiteSpace(current))
            return next ?? "";
        if (string.IsNullOrWhiteSpace(next))
        {
            if (current.Contains("ward", StringComparison.OrdinalIgnoreCase) || current.Contains("plot", StringComparison.OrdinalIgnoreCase))
                return current;
            return "";
        }

        var nextDistrict = HousingTravel.DistrictName(next);
        var currentDistrict = HousingTravel.DistrictName(current);
        if (nextDistrict is not null && !nextDistrict.Equals(currentDistrict, StringComparison.OrdinalIgnoreCase))
            return next;
        return next.Length >= current.Length ? next : current;
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

    public bool SetPinned(string id, bool pinned)
    {
        var index = this.entries.FindIndex(entry => entry.Id == id);
        if (index < 0 || this.entries[index].Pinned == pinned)
            return false;
        this.entries[index] = this.entries[index] with { Pinned = pinned };
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
