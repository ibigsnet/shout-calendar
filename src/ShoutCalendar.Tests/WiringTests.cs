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
        Assert.Contains("Button(\"Today\")", window, StringComparison.Ordinal);
        Assert.Contains("folderDay = todayDate.Day", window, StringComparison.Ordinal);
        Assert.Contains("InputTextWithHint(\"##event-search\", \"Search\"", window, StringComparison.Ordinal);
        Assert.Contains("Delete past local", window, StringComparison.Ordinal);
        Assert.Contains("Delete past sync", window, StringComparison.Ordinal);
        Assert.Contains("Show past local events##show-past-local", window, StringComparison.Ordinal);
        Assert.Contains("Show past sync events##show-past-sync", window, StringComparison.Ordinal);
        Assert.Contains("Show past local events##show-past-local", window, StringComparison.Ordinal);
        Assert.Contains("Show past sync events##show-past-sync", window, StringComparison.Ordinal);
        Assert.Contains("Delete past events##drop-past", window, StringComparison.Ordinal);
        Assert.Contains("this.editingId = null;", window, StringComparison.Ordinal);
        Assert.Contains("OngoingCheck.IsOngoing", window, StringComparison.Ordinal);
        Assert.Contains("month.Cells", window, StringComparison.Ordinal);
        Assert.Contains("Accept", window, StringComparison.Ordinal);
        Assert.Contains("Decline", window, StringComparison.Ordinal);
        Assert.Contains("Delete", window, StringComparison.Ordinal);
        Assert.Contains("Clear local", window, StringComparison.Ordinal);
        Assert.Contains("Clear local accepted", window, StringComparison.Ordinal);
        Assert.Contains("Clear local unaccepted", window, StringComparison.Ordinal);
        Assert.Contains("Clear sync accepted", window, StringComparison.Ordinal);
        Assert.Contains("Clear sync unaccepted", window, StringComparison.Ordinal);
        Assert.Contains("Aggressive filter (2 of date, time, place)", window, StringComparison.Ordinal);
        Assert.Contains("Alarm accepted events", window, StringComparison.Ordinal);
        Assert.Contains("Alarm unaccepted events", window, StringComparison.Ordinal);
        Assert.Contains("BeginTabItem(\"Resets\")", window, StringComparison.Ordinal);
        Assert.Contains("BeginTabItem(\"Settings\")", window, StringComparison.Ordinal);
        Assert.Contains("Faster calendar", window, StringComparison.Ordinal);
        Assert.Contains("Parse debug##parse-debug", window, StringComparison.Ordinal);
        Assert.Contains("Performance log##sync-perf", window, StringComparison.Ordinal);
        Assert.Contains("Off by default.", window, StringComparison.Ordinal);
        Assert.Contains("BeginTabItem(\"Colors\")", window, StringComparison.Ordinal);
        Assert.Contains("###day-folder", window, StringComparison.Ordinal);
        Assert.Contains("Local accepted", window, StringComparison.Ordinal);
        Assert.Contains("Local unaccepted", window, StringComparison.Ordinal);
        Assert.Contains("Sync accepted", window, StringComparison.Ordinal);
        Assert.Contains("Sync unaccepted", window, StringComparison.Ordinal);
        Assert.Contains("Announce in chat", window, StringComparison.Ordinal);
        Assert.Contains("Warn when it starts##alarm-start", window, StringComparison.Ordinal);
        Assert.Contains("PickerHueWheel", window, StringComparison.Ordinal);
        Assert.Contains("Alarm resets", window, StringComparison.Ordinal);
        Assert.Contains("Test accepted", window, StringComparison.Ordinal);
        Assert.Contains("Test unaccepted", window, StringComparison.Ordinal);
        Assert.Contains("it needs two of those three", window, StringComparison.Ordinal);
        Assert.Contains("ImGuiCol.ChildBg", window, StringComparison.Ordinal);
        var schedule = File.ReadAllText(Path.Combine(root, "src", "ShoutCalendar.Core", "GameSchedule.cs"));
        Assert.Contains("Jumbo Cactpot", schedule, StringComparison.Ordinal);
        Assert.Contains("A Nocturne for Heroes", schedule, StringComparison.Ordinal);
        Assert.Contains("200,000 MGP", schedule, StringComparison.Ordinal);
        Assert.Contains("PlayChatSoundEffect", plugin, StringComparison.Ordinal);
        Assert.Contains("!entry.Accepted", window, StringComparison.Ordinal);
        Assert.Contains("AddRectFilled", window, StringComparison.Ordinal);
        var pendingStart = window.IndexOf("private void DrawSharedRow(", StringComparison.Ordinal);
        var pendingEnd = window.IndexOf("private void DrawSyncDetail(", pendingStart, StringComparison.Ordinal);
        Assert.True(pendingStart >= 0 && pendingEnd > pendingStart);
        var pendingBody = window[pendingStart..pendingEnd];
        Assert.Contains("this.session.SyncPendingColor", pendingBody, StringComparison.Ordinal);
        Assert.Contains("AddRectFilled", pendingBody, StringComparison.Ordinal);
        Assert.Contains("SetScrollHereY(0.5f)", window, StringComparison.Ordinal);
        Assert.Contains("BeginCombo(\"##year\"", window, StringComparison.Ordinal);
        Assert.Contains("BeginCombo(\"##month\"", window, StringComparison.Ordinal);
        Assert.Contains("Sync: Out of date. Update Shout Calendar and Shout Calendar Sync.", window, StringComparison.Ordinal);
        Assert.Contains("Update Shout Calendar and Shout Calendar Sync so shared invites stay current.", window, StringComparison.Ordinal);
        Assert.Contains("BeginTabItem(\"Sync\")", window, StringComparison.Ordinal);
        Assert.Contains("DismissOpen", window, StringComparison.Ordinal);
        Assert.Contains("You currently have the calendars for the following servers open:", File.ReadAllText(Path.Combine(root, "src", "ShoutCalendar.Core", "ClearPrompt.cs")), StringComparison.Ordinal);
        Assert.DoesNotContain("sync-tabs", window, StringComparison.Ordinal);
        Assert.Contains("Informedaholic: accept every invite you see", window, StringComparison.Ordinal);
        Assert.Contains("Remember this choice##link-settings", window, StringComparison.Ordinal);
        Assert.Contains("Remember this choice##link-popup", window, StringComparison.Ordinal);
        Assert.Contains("Open this link in your browser?", window, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginPopupModal(\"Make a macro", window, StringComparison.Ordinal);
        Assert.Contains("new MacroHelperWindow", plugin, StringComparison.Ordinal);
        Assert.Contains("this.window.IsOpen = false", plugin, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginPopupModal", File.ReadAllText(Path.Combine(root, "src", "ShoutCalendar", "MacroHelperWindow.cs")), StringComparison.Ordinal);
        Assert.Contains("GitHubFeedback.ErrorPrompt", window, StringComparison.Ordinal);
        Assert.Contains("new Vector2(-1f, 0f)", window, StringComparison.Ordinal);
        Assert.DoesNotContain("TextWrapped(this.pendingLink", window, StringComparison.Ordinal);
        Assert.Contains("Informedaholic: accept every shared invite", window, StringComparison.Ordinal);
        Assert.Contains("Download (Mb/s) (Speed cap)", window, StringComparison.Ordinal);
        Assert.Contains("Upload (Mb/s) (Speed cap)", window, StringComparison.Ordinal);
        Assert.Contains("upload starts at 1 Mb/s", window, StringComparison.Ordinal);
        Assert.DoesNotContain("Speed (Mb/s)", window, StringComparison.Ordinal);
        Assert.Contains("Country mirror", window, StringComparison.Ordinal);
        Assert.Contains("RelayReach.Online", window, StringComparison.Ordinal);
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
