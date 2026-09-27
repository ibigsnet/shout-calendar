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
        return ChatLogPaths.LogDirectories(roots);
    }
}
