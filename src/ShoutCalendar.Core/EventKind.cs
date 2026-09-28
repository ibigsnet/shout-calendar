using System.Numerics;
using System.Text.RegularExpressions;

namespace ShoutCalendar.Core;

/// <summary>A brand named in a shout. The color is that brand's own.</summary>
public readonly record struct EventKind(string Name, Vector4 Color)
{
    public static EventKind? Find(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        Match? first = null;
        EventKind? kind = null;
        void Take(Regex pattern, EventKind candidate)
        {
            var match = pattern.Match(text);
            if (!match.Success)
                return;
            if (first is not null && match.Index >= first.Index)
                return;
            first = match;
            kind = candidate;
        }

        Take(TwitchRegex, Twitch);
        Take(DiscordRegex, Discord);
        Take(YouTubeRegex, YouTube);
        return kind;
    }

    /// <summary>Twitch purple, #9146FF.</summary>
    public static EventKind Twitch { get; } = new("Twitch", new Vector4(0.569f, 0.275f, 1f, 0.95f));

    /// <summary>Discord blurple, #5865F2.</summary>
    public static EventKind Discord { get; } = new("Discord", new Vector4(0.345f, 0.396f, 0.949f, 0.95f));

    /// <summary>YouTube red, #FF0000.</summary>
    public static EventKind YouTube { get; } = new("YouTube", new Vector4(1f, 0f, 0f, 0.95f));

    private static readonly Regex TwitchRegex = new(
        @"\btwitch(?:\.tv|\.com)?\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DiscordRegex = new(
        @"\bdiscord(?:\.gg|\.com|\.app)?\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex YouTubeRegex = new(
        @"\byoutube\.com\b|\byoutu\.be\b|\byoutube\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
}
