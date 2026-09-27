namespace ShoutCalendar.Core;

/// <summary>
/// Detected shouts. A new shout is pending until <see cref="Accept"/>. Nothing is accepted in bulk.
/// </summary>
public sealed class CalendarLog
{
    private readonly List<CalendarEntry> entries = new();

    public IReadOnlyList<CalendarEntry> Entries => this.entries;

    public bool Add(CalendarEntry? entry)
    {
        if (entry is null)
            return false;
        if (string.IsNullOrEmpty(entry.Id))
            entry = entry with { Id = Guid.NewGuid().ToString("N"), Accepted = false };
        this.entries.Add(Stamp(entry));
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

        TimeOnly? time = parsed?.Time;
        if (TimeOnly.TryParseExact(timeText.Trim(), "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var typedTime))
            time = typedTime;
        else if (string.IsNullOrWhiteSpace(timeText))
            time = parsed?.Time;

        var place = string.IsNullOrWhiteSpace(placeText) ? parsed?.Place ?? "" : placeText.Trim();
        var text = string.IsNullOrWhiteSpace(note) ? current.EventText : note.Trim();
        this.entries[index] = current with
        {
            EventText = text,
            Date = date,
            Time = time,
            End = string.IsNullOrWhiteSpace(timeText) ? parsed?.End : null,
            Place = place,
            Ward = parsed?.Ward ?? current.Ward,
            Server = parsed?.Server ?? current.Server,
        };
        return true;
    }

    public bool Remove(string id)
    {
        var index = this.entries.FindIndex(entry => entry.Id == id);
        if (index < 0)
            return false;
        this.entries.RemoveAt(index);
        return true;
    }

    public bool Accept(string id)
    {
        var index = this.entries.FindIndex(entry => entry.Id == id);
        if (index < 0 || this.entries[index].Accepted)
            return false;
        this.entries[index] = this.entries[index] with { Accepted = true };
        return true;
    }

    public int ExpireUnaccepted(DateTimeOffset now, int holdDays)
    {
        if (holdDays < 1)
            holdDays = 1;
        var cutoff = now - TimeSpan.FromDays(holdDays);
        return this.entries.RemoveAll(entry => !entry.Accepted && entry.DetectedAt < cutoff);
    }

    public void Clear()
    {
        this.entries.Clear();
    }

    public int ClearAccepted() => this.entries.RemoveAll(entry => entry.Accepted);

    public int ClearUnaccepted() => this.entries.RemoveAll(entry => !entry.Accepted);

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
    }
}
