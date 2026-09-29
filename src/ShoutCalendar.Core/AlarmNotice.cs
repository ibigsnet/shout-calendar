using System.Globalization;

namespace ShoutCalendar.Core;

/// <summary>The chat line printed when an alarm rings.</summary>
public static class AlarmNotice
{
    public static string Line(CalendarEntry entry, int minutesBefore, string? here = null)
    {
        var title = EventTitle.Readable(EventTitle.Choose(entry.EventText));
        var name = title.Length > 0 ? title : "An event";
        var when = entry.Time?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "";
        var lead = minutesBefore > 0
            ? $"{name} starts in {minutesBefore} minutes"
            : $"{name} is starting";
        if (when.Length > 0)
            lead += " at " + when;
        if (entry.Date is DateOnly day)
            lead += " on " + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var spot = HousingTravel.Find(entry.Place, entry.EventText, entry.Ward, entry.Server);
        var needed = spot?.World ?? "";
        if (needed.Length == 0 && PlayableWorlds.TryNamedWorld(entry.Server, out var named))
            needed = named;

        var parts = new List<string> { "Shout Calendar:" };
        if (PlayableWorlds.TryCanonical(here, out var standing)
            && needed.Length > 0
            && !needed.Equals(standing, StringComparison.OrdinalIgnoreCase))
            parts.Add(Hop(needed, standing));
        if (spot is HousingSpot housing && housing.CityAetheryteId is not null)
        {
            var ward = housing.Ward is int number
                ? $" Select {housing.District} ward {number.ToString(CultureInfo.InvariantCulture)}."
                : "";
            parts.Add($"Teleport: {housing.City} aetheryte.{ward}");
        }

        parts.Add(lead + ".");
        return string.Join(" ", parts);
    }

    private static string Hop(string needed, string current)
    {
        if (DataCenters.SameCenter(needed, current) && DataCenters.TryGroup(needed, out var center))
            return $"Server hop to {needed} first. You are on {current} ({center}). Visit Another World Server from Limsa Lominsa, Gridania, or Ul'dah.";
        var from = DataCenters.TryGroup(current, out var here) ? $"{current} ({here})" : current;
        var to = DataCenters.TryGroup(needed, out var there) ? $"{needed} ({there})" : needed;
        return $"Server hop to {to} first. You are on {from}. Log out and choose Visit Another Data Center.";
    }
}
