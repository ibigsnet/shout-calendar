using ShoutCalendar.Core;

namespace ShoutCalendar.Tests;

public class ChatLogPathTests
{
    [Fact]
    public void UsesTheCurrentUsersPathsAndTheGameUserPath()
    {
        const string userPath = @"C:\Users\ada\Documents\My Games\FINAL FANTASY XIV - A Realm Reborn";
        var roots = ChatLogPaths.CandidateRoots(
            ["UserPath=" + userPath],
            @"C:\Users\ada\Documents",
            "/home/ada",
            @"C:\Users\ada");

        Assert.Equal(
            [
                userPath,
                Path.Combine(@"C:\Users\ada\Documents", "My Games", ChatLogPaths.GameFolderName),
                Path.Combine("/home/ada", ".xlcore", "ffxivConfig"),
                Path.Combine(@"C:\Users\ada", ".xlcore", "ffxivConfig"),
            ],
            roots);
        Assert.DoesNotContain(roots, root => root.Contains("rifle", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(roots, root => root.Contains(@"Z:\home", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UserPathWithSpacesStaysOneFolder()
    {
        var path = ChatLogPaths.ReadUserPath(
            ["FINAL FANTASY XIV", "UserPath=Z:\\home\\mina\\.xlcore\\ffxivConfig"]);

        Assert.Equal(@"Z:\home\mina\.xlcore\ffxivConfig", path);
    }

    [Fact]
    public void MissingHintsProduceNoRoots()
    {
        Assert.Empty(ChatLogPaths.CandidateRoots(null, null, "  ", null));
    }
}
