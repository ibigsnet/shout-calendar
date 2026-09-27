using System.Globalization;
using System.Text.RegularExpressions;

namespace ShoutCalendar.Core;

/// <summary>Map coordinates written in a shout or a schedule note.</summary>
public static partial class MapMentions
{
    public readonly record struct Spot(float X, float Y);

    private static readonly Regex Pair = new(
        @"(?:\(\s*|\bx\s+)(?<x>\d{1,2}(?:\.\d+)?)\s*(?:,\s*y|,|\s+y)\s*(?<y>\d{1,2}(?:\.\d+)?)\)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<Spot> Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var found = new List<Spot>();
        foreach (Match match in Pair.Matches(text))
        {
            if (!float.TryParse(match.Groups["x"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
                continue;
            if (!float.TryParse(match.Groups["y"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                continue;
            if (x is < 0 or > 50 || y is < 0 or > 50)
                continue;
            if (found.Any(spot => Math.Abs(spot.X - x) < 0.05f && Math.Abs(spot.Y - y) < 0.05f))
                continue;
            found.Add(new Spot(x, y));
        }

        return found;
    }
}
