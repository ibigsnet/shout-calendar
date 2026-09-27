namespace ShoutCalendar.Core;

/// <summary>
/// Character-data folders for the current user. No machine name and no scan of other accounts.
/// </summary>
public static class ChatLogPaths
{
    public const string GameFolderName = "FINAL FANTASY XIV - A Realm Reborn";

    public static IReadOnlyList<string> CandidateRoots(
        IEnumerable<string>? arguments,
        string? documents,
        string? home,
        string? userProfile)
    {
        var roots = new List<string>();
        var userPath = ReadUserPath(arguments);
        if (!string.IsNullOrWhiteSpace(userPath))
            roots.Add(userPath);

        if (!string.IsNullOrWhiteSpace(documents))
            roots.Add(Path.Combine(documents, "My Games", GameFolderName));

        AddXlcore(roots, home);
        AddXlcore(roots, userProfile);
        return roots;
    }

    public static string? ReadUserPath(IEnumerable<string>? arguments)
    {
        if (arguments is null)
            return null;

        foreach (var argument in arguments)
        {
            if (string.IsNullOrEmpty(argument))
                continue;
            const string key = "UserPath=";
            var at = argument.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
                continue;
            var value = argument[(at + key.Length)..].Trim().Trim('"');
            if (value.Length > 0)
                return value;
        }

        return null;
    }

    private static void AddXlcore(List<string> roots, string? home)
    {
        if (string.IsNullOrWhiteSpace(home))
            return;
        roots.Add(Path.Combine(home, ".xlcore", "ffxivConfig"));
    }
}
