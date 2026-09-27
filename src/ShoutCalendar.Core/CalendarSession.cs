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

    public PlaceCatalog Places { get; set; } = PlaceCatalog.Empty;

    public HashSet<int> Channels { get; } = new(ChatChannels.DefaultIds);

    public string? HousingHint { get; set; }

    public int UnacceptedHoldDays { get; set; } = 14;

    /// <summary>When set, a line is kept only if two of date, time, and place are present.</summary>
    public bool AggressiveFilter { get; set; }

    public bool AlarmAccepted { get; set; } = true;

    public bool AlarmUnaccepted { get; set; }

    public int AcceptedSound { get; set; } = EventAlarm.MinSound;

    public int UnacceptedSound { get; set; } = EventAlarm.MinSound;

    public int AlarmMinutesBefore { get; set; }

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

    public bool TryAddShout(string? text, int channel, DateTimeOffset shoutTimestamp, string? sender = null)
    {
        var detected = ShoutHarvest.TryHarvest(
            text,
            channel,
            shoutTimestamp,
            this.Places,
            this.Channels,
            this.HousingHint,
            this.AggressiveFilter);
        if (detected is null)
            return false;
        return this.Log.Add(detected with { Sender = sender?.Trim() ?? "" });
    }
}
