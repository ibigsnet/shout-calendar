using System.Text.RegularExpressions;

namespace ShoutCalendar.Core;

/// <summary>Web and Discord addresses written inside an invite.</summary>
public static partial class LinkFinder
{
    public readonly record struct Found(string Label, string Url);

    private static readonly Regex UrlRegex = new(
        @"https?://[^\s<>""']+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DiscordRegex = new(
        @"(?:https?://)?(?:discord\.gg|discord(?:app)?\.com/invite)/([A-Za-z0-9-]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<Found> Find(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var ranked = new List<(int At, string Url)>();
        foreach (Match match in UrlRegex.Matches(text))
            ranked.Add((match.Index, TrimTail(match.Value)));
        foreach (Match match in DiscordRegex.Matches(text))
        {
            var url = "https://discord.gg/" + match.Groups[1].Value;
            if (ranked.Any(hit => hit.Url.Contains(match.Groups[1].Value, StringComparison.OrdinalIgnoreCase)))
                continue;
            ranked.Add((match.Index, url));
        }

        var found = new List<Found>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hit in ranked.OrderBy(hit => hit.At))
            Add(found, seen, hit.Url);
        return found;
    }

    public static bool IsHttp(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }

    private static void Add(List<Found> found, HashSet<string> seen, string url)
    {
        if (!IsHttp(url) || !seen.Add(url))
            return;
        var label = url;
        if (label.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            label = label["https://".Length..];
        else if (label.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            label = label["http://".Length..];
        found.Add(new Found(label, url));
    }

    private static string TrimTail(string value) =>
        value.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '>', '"', '\'');
}
