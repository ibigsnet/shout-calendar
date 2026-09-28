using System.Globalization;

namespace ShoutCalendar.Core;

/// <summary>The chat line printed when an alarm rings.</summary>
public static class AlarmNotice
{
    public static string Line(CalendarEntry entry, int minutesBefore)
    {
        var title = EventTitle.Choose(entry.EventText);
        var name = title.Length > 0 ? title : "An event";
        var when = entry.Time?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "";
        var lead = minutesBefore > 0
            ? $"{name} starts in {minutesBefore} minutes"
            : $"{name} is starting";
        if (when.Length > 0)
            lead += " at " + when;
        if (entry.Date is DateOnly day)
            lead += " on " + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var details = new List<string> { "Shout Calendar: " + lead + "." };
        if (!string.IsNullOrWhiteSpace(entry.Place))
            details.Add(entry.Place.Trim());
        else if (!string.IsNullOrWhiteSpace(entry.Server))
            details.Add(entry.Server.Trim());
        if (!string.IsNullOrWhiteSpace(entry.Sender))
            details.Add(entry.Sender.Trim());
        var note = (entry.EventText ?? "").Trim();
        if (note.Length > 0 && !string.Equals(note, title, StringComparison.Ordinal))
            details.Add(note.Length > 360 ? note[..360] : note);
        return string.Join(" ", details);
    }
}
