using System.Text.RegularExpressions;

namespace ShoutCalendar.Core;

/// <summary>A housing spot named in an invite. The district is what a map and a teleport can use.</summary>
public readonly record struct HousingSpot(string District, int? Ward, int? Plot, string? World)
{
    public string Label
    {
        get
        {
            var label = this.District;
            if (this.Ward is int ward)
                label += " ward " + ward.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (this.Plot is int plot)
                label += " plot " + plot.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(this.World))
                label += " on " + this.World;
            return label;
        }
    }
}

public static class HousingTravel
{
    private static readonly (string Token, string Name)[] Districts =
    [
        ("Goblet", "The Goblet"),
        ("Lavender", "The Lavender Beds"),
        ("Shirogane", "Shirogane"),
        ("Empyreum", "Empyreum"),
        ("Mist", "Mist"),
    ];

    private static readonly Regex PlotRegex = new(
        @"\bplot\s+(\d{1,2})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static HousingSpot? Find(string? place, string? text, int? ward, string? server)
    {
        var blob = $"{place} {text}";
        string? district = null;
        foreach (var (token, name) in Districts)
        {
            if (!blob.Contains(token, StringComparison.OrdinalIgnoreCase))
                continue;
            district = name;
            break;
        }

        if (district is null)
            return null;

        int? plot = null;
        var plotMatch = PlotRegex.Match(blob);
        if (plotMatch.Success && int.TryParse(plotMatch.Groups[1].Value, out var plotNumber))
            plot = plotNumber;

        string? world = null;
        if (PlayableWorlds.TryNamedWorld(server, out var named))
            world = named;
        else
        {
            foreach (var candidate in ServerNames.Match(blob))
            {
                if (!PlayableWorlds.TryCanonical(candidate, out var playable))
                    continue;
                world = playable;
                break;
            }
        }

        return new HousingSpot(district, ward, plot, world);
    }
}
