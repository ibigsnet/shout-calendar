namespace ShoutCalendar.Tests;

public class WiringTests
{
    [Fact]
    public void WindowDrawsTheMonthModelAndACommandOpensIt()
    {
        var root = RepoRoot();
        var plugin = File.ReadAllText(Path.Combine(root, "src", "ShoutCalendar", "Plugin.cs"));
        var window = File.ReadAllText(Path.Combine(root, "src", "ShoutCalendar", "CalendarWindow.cs"));

        Assert.Contains("CalendarCommand.Open", plugin, StringComparison.Ordinal);
        Assert.Contains("AddHandler", plugin, StringComparison.Ordinal);
        Assert.Contains("OpenMainUi", plugin, StringComparison.Ordinal);
        Assert.Contains("OpenConfigUi", plugin, StringComparison.Ordinal);
        Assert.Contains("this.window.Toggle()", plugin, StringComparison.Ordinal);
        Assert.Contains("this.session.CurrentMonth()", window, StringComparison.Ordinal);
        Assert.Contains("this.session.Page(", window, StringComparison.Ordinal);
        Assert.Contains("OngoingCheck.IsOngoing", window, StringComparison.Ordinal);
        Assert.Contains("month.Cells", window, StringComparison.Ordinal);
        Assert.Contains("Accept", window, StringComparison.Ordinal);
        Assert.Contains("Decline", window, StringComparison.Ordinal);
        Assert.Contains("Delete", window, StringComparison.Ordinal);
        Assert.Contains("Clear all", window, StringComparison.Ordinal);
        Assert.Contains("Clear accepted", window, StringComparison.Ordinal);
        Assert.Contains("Clear unaccepted", window, StringComparison.Ordinal);
        Assert.Contains("Aggressive filter (2 of date, time, place)", window, StringComparison.Ordinal);
        Assert.Contains("Alarm accepted events", window, StringComparison.Ordinal);
        Assert.Contains("Alarm unaccepted events", window, StringComparison.Ordinal);
        Assert.Contains("PlayChatSoundEffect", plugin, StringComparison.Ordinal);
        Assert.Contains("!entry.Accepted", window, StringComparison.Ordinal);
        Assert.Contains("AddRectFilled", window, StringComparison.Ordinal);
    }

    [Fact]
    public void QuestionsRecordAsksTheUndecidedBehavior()
    {
        var questions = File.ReadAllText(Path.Combine(RepoRoot(), "QUESTIONS.md"));
        Assert.Contains("display name and author", questions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GitHub", questions, StringComparison.Ordinal);
        Assert.Contains("official Dalamud", questions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AI model", questions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ST", questions, StringComparison.Ordinal);
        Assert.Contains("ET", questions, StringComparison.Ordinal);
        Assert.Contains("PT", questions, StringComparison.Ordinal);
        Assert.Contains("Eorzea", questions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tonight", questions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("in 20 minutes", questions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("one-time shout stays ongoing", questions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("repeated shout", questions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("duplicate", questions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("beyond ward numbers and server names", questions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Channels besides shout", questions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("this client only", questions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DevMode", questions, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "ShoutCalendar", "Plugin.cs")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not find the plugin project from the test output directory.");
    }
}
