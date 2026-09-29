using System.Text;

namespace ShoutCalendar.Core;

/// <summary>Opens a GitHub issue with the title and body already filled in.</summary>
public static class GitHubFeedback
{
    public const string Repo = "https://github.com/ibigsnet/shout-calendar";

    public static string FeatureUrl(string? version) =>
        IssueUrl("Feature request: ", FeatureBody(version));

    public static string ErrorUrl(string? version) =>
        IssueUrl("Error report: ", ErrorBody(version));

    public static string IssueUrl(string title, string body)
    {
        var builder = new StringBuilder(Repo);
        builder.Append("/issues/new?title=");
        builder.Append(Uri.EscapeDataString(title));
        builder.Append("&body=");
        builder.Append(Uri.EscapeDataString(body));
        return builder.ToString();
    }

    private static string FeatureBody(string? version) =>
        $"""
        ### Idea

        <!-- What should Shout Calendar do? -->

        ### Why it helps


        ### Plugin
        {PluginLine(version)}
        """;

    private static string ErrorBody(string? version) =>
        $"""
        ### What happened


        ### What you expected


        ### Steps
        1.
        2.

        ### Plugin
        {PluginLine(version)}

        Leave out character names, Discord invites, and log lines that name other players.
        """;

    private static string PluginLine(string? version)
    {
        var trimmed = (version ?? "").Trim();
        return trimmed.Length == 0 ? "Shout Calendar" : "Shout Calendar " + trimmed;
    }
}
