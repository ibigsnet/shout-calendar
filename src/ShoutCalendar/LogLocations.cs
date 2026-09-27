using ShoutCalendar.Core;

namespace ShoutCalendar;

/// <summary>Character log folders for whoever is running the client.</summary>
internal static class LogLocations
{
    public static IEnumerable<string> LogDirectories()
    {
        var roots = ChatLogPaths.CandidateRoots(
            Environment.GetCommandLineArgs(),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetEnvironmentVariable("HOME"),
            Environment.GetEnvironmentVariable("USERPROFILE"));
        return LogDirectories(roots);
    }

    public static IEnumerable<string> LogDirectories(IEnumerable<string> roots)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                continue;

            IEnumerable<string> characters;
            try
            {
                characters = Directory.GetDirectories(root, "FFXIV_CHR*");
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var character in characters)
            {
                var log = Path.Combine(character, "log");
                if (Directory.Exists(log) && seen.Add(log))
                    yield return log;
            }
        }
    }
}
