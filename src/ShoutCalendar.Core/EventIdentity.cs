using System.Text.RegularExpressions;

namespace ShoutCalendar.Core;

/// <summary>Whether two shouts are the same event even when the clock or a few words changed.</summary>
public static class EventIdentity
{
    private static readonly Regex ClockRegex = new(
        @"\b(?:[01]?\d|2[0-3]):[0-5]\d\b|\b(?:[1-9]|1[0-2])(?::[0-5]\d)?\s*[ap]\.?m\.?\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex WardRegex = new(
        @"\b(?:ward\s*|[Ww])(\d{1,2})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PlotRegex = new(
        @"\b(?:plot\s*|[Pp])(\d{1,2})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    {
        "tonight", "today", "tomorrow", "crystal", "aether", "primal", "dynamis", "chaos",
        "light", "elemental", "gaia", "mana", "meteor", "materia", "goblet", "mist",
        "shirogane", "empyreum", "lavender", "beds", "ward", "plot", "with", "from", "that",
        "this", "your", "have", "been", "will", "come", "join",
    };

    public static bool SameShout(string? world, string? text, string? otherWorld, string? otherText)
    {
        if (!string.IsNullOrWhiteSpace(world)
            && !string.IsNullOrWhiteSpace(otherWorld)
            && !world.Trim().Equals(otherWorld.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        var leftWard = Number(WardRegex, IconText.Plain(text));
        var rightWard = Number(WardRegex, IconText.Plain(otherText));
        if (leftWard is null || rightWard is null || leftWard != rightWard)
            return false;
        var leftPlot = Number(PlotRegex, IconText.Plain(text));
        var rightPlot = Number(PlotRegex, IconText.Plain(otherText));
        if (leftPlot is null || rightPlot is null || leftPlot != rightPlot)
            return false;
        return Overlap(text, otherText);
    }

    public static bool SameEntry(CalendarEntry left, CalendarEntry right)
    {
        var leftWorld = ShareWorld.Choose("", left.SpeakerWorld, left.Server);
        var rightWorld = ShareWorld.Choose("", right.SpeakerWorld, right.Server);
        var leftText = $"{left.EventText} {left.Place}";
        var rightText = $"{right.EventText} {right.Place}";
        return SameShout(leftWorld, leftText, rightWorld, rightText);
    }

    /// <summary>The same person on shout or yell, within eight hours, is one invite unless the ward or plot changed.</summary>
    public static bool SameSpeaker(CalendarEntry current, CalendarEntry incoming, DateTimeOffset when)
    {
        if (current.Manual || incoming.Manual)
            return false;
        if (string.IsNullOrWhiteSpace(current.Sender) || string.IsNullOrWhiteSpace(incoming.Sender))
            return false;
        if (!current.Sender.Equals(incoming.Sender, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!SimilarChat(current.Channel, incoming.Channel))
            return false;
        var earlier = current.DetectedAt == default ? when : current.DetectedAt;
        var gap = when - earlier;
        if (gap < TimeSpan.Zero || gap > TimeSpan.FromHours(8))
            return false;
        if (current.Ward is int leftWard && incoming.Ward is int rightWard && leftWard != rightWard)
            return false;
        var leftPlot = Number(PlotRegex, $"{current.EventText} {current.Place}");
        var rightPlot = Number(PlotRegex, $"{incoming.EventText} {incoming.Place}");
        if (leftPlot is not null && rightPlot is not null && leftPlot != rightPlot)
            return false;
        var leftWorld = ShareWorld.Choose("", current.SpeakerWorld, current.Server);
        var rightWorld = ShareWorld.Choose("", incoming.SpeakerWorld, incoming.Server);
        return string.IsNullOrWhiteSpace(leftWorld)
            || string.IsNullOrWhiteSpace(rightWorld)
            || leftWorld.Equals(rightWorld, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SimilarChat(int left, int right)
    {
        if (left == right)
            return true;
        return SharePolicy.IsShareable(left) && SharePolicy.IsShareable(right);
    }

    private static int? Number(Regex regex, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var match = regex.Match(text);
        return match.Success && int.TryParse(match.Groups[1].Value, out var number) ? number : null;
    }

    private static bool Overlap(string? leftText, string? rightText)
    {
        var left = Words(leftText);
        var right = Words(rightText);
        if (left.Count == 0 || right.Count == 0)
            return false;
        var shared = left.Intersect(right, StringComparer.OrdinalIgnoreCase).Count();
        var smaller = Math.Min(left.Count, right.Count);
        return shared >= 3 && shared * 2 >= smaller;
    }

    private static HashSet<string> Words(string? text)
    {
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text))
            return words;
        var stripped = ClockRegex.Replace(text, " ");
        foreach (var raw in stripped.Split([' ', '\r', '\n', '\t', ',', '.', '!', '?', '/', '|', ':', ';', '&', '♥', '★', '♪'], StringSplitOptions.RemoveEmptyEntries))
        {
            var word = raw.Trim().ToLowerInvariant();
            if (word.Length < 4 || Skip.Contains(word))
                continue;
            words.Add(word);
        }

        return words;
    }
}
