using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using ShoutCalendar.Core;

namespace ShoutCalendar;

public sealed partial class CalendarWindow : Window
{
    private readonly CalendarSession session;
    private readonly ClearPrompt prompt;
    private readonly Action save;
    private readonly Action<int, string?> previewSound;
    private readonly Action<string, float, float, bool, string?, string?> openPin;
    private readonly Action<HousingSpot> openHousing;
    private readonly Action? requestSyncNow;
    private readonly Action? requestSyncResync;
    private readonly Action? openMacroHelper;
    private readonly FileDialogManager dialogs;
    private DateTimeOffset? syncLimitTipAt;
    private readonly SyncStatusBuffer syncStatusBuffer = new();
    private string syncLimitTipKey = "";
    private string? selectedId;
    private string? editingId;
    private string editNote = "";
    private string editDate = "";
    private string editTime = "";
    private string editPlace = "";
    private ClockInputErrors editErrors;
    private ClockInputErrors fixErrors;
    private string fixId = "";
    private string fixDate = "";
    private string fixTime = "";
    private Vector4 editColor;
    private bool editHasColor;
    private string holdDaysText = "";
    private bool holdDaysReady;
    private string minutesBeforeText = "";
    private bool minutesBeforeReady;
    private int? folderDay;
    private bool focusFolder;
    private DayLine? selectedLine;
    private string? pendingLink;
    private string pendingLinkCaption = "";
    private string tellNotice = "";
    private string tellNoticeId = "";
    private bool linkRememberDraft;
    private bool adding;
    private string addNote = "";
    private string addDate = "";
    private string addTime = "";
    private string addPlace = "";
    private string addNotice = "";
    private string eventSearch = "";
    private bool weekView;
    private bool weekChosen;
    private DateOnly weekStart;
    private DateOnly followedDay = DateOnly.FromDateTime(DateTime.Now);

    private readonly Dictionary<DateOnly, float> spanOriginY = new();
    private IReadOnlyDictionary<DateOnly, int> spanSlots = new Dictionary<DateOnly, int>();
    private SyncAnnouncement[] syncFrame = [];
    private SyncAnnouncement[] framed = [];
    private string frameKey = "";
    private string glanceKey = "";
    private readonly Dictionary<DateOnly, List<DayLine>> glances = new();
    private readonly Dictionary<(int Year, int Month), IReadOnlyList<ScheduleOccurrence>> marksFrame = new();
    private Vector2 appliedMin;

    public CalendarWindow(
        CalendarSession session,
        ClearPrompt prompt,
        Action save,
        Action<int, string?> previewSound,
        FileDialogManager dialogs,
        Action<string, float, float, bool, string?, string?> openPin,
        Action<HousingSpot> openHousing,
        Action? requestSyncNow = null,
        Action? openMacroHelper = null,
        Action? requestSyncResync = null)
        : base("FFXIV Shout Calendar")
    {
        this.session = session;
        this.prompt = prompt;
        this.save = () => { this.nextSnapshot = 0; this.glanceKey = ""; save(); };
        this.previewSound = previewSound;
        this.dialogs = dialogs;
        this.openPin = openPin;
        this.openHousing = openHousing;
        this.requestSyncNow = requestSyncNow;
        this.requestSyncResync = requestSyncResync;
        this.openMacroHelper = openMacroHelper;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(1180, 820),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
        if (this.session.PauseInPvp && Plugin.ClientState.IsPvPExcludingDen)
        {
            ImGui.TextUnformatted("Paused during PvP.");
            return;
        }

        var drawStart = System.Diagnostics.Stopwatch.GetTimestamp();
        this.UpdateCompactMode();
        this.BeginDraw();
        this.UseTextScale();
        this.DrawTopBar();
        if (!this.CompactMode || this.compactSidebar)
        {
            this.DrawHistory();
            ImGui.SameLine();
        }
        this.DrawCalendar();
        this.DrawDayFolder();
        this.DrawClearPrompt();
        this.DrawLinkPrompt();
        this.dialogs.Draw();
        if (this.appearanceChanged) { this.appearanceChanged = false; this.save(); }
        var drawMs = System.Diagnostics.Stopwatch.GetElapsedTime(drawStart).TotalMilliseconds;
        this.drawAverageMs = this.drawAverageMs == 0 ? drawMs : this.drawAverageMs * 0.95 + drawMs * 0.05;
        this.drawPeakMs = Math.Max(this.drawPeakMs, drawMs);
    }

    private void DrawHistory()
    {
        ImGui.BeginChild("shout-history", new Vector2(this.CompactMode ? 290 : 340, 0), true);
        this.UseTextScale();
        if (Environment.TickCount64 >= this.nextExpiry)
        {
            this.nextExpiry = Environment.TickCount64 + 1000;
            if (this.session.Log.ExpireUnaccepted(DateTimeOffset.UtcNow, this.session.UnacceptedHoldDays) > 0) this.save();
        }

        if (ImGui.BeginTabBar("shout-tabs", ImGuiTabBarFlags.FittingPolicyScroll))
        {
            if (ImGui.BeginTabItem("Pending"))
            {
                this.DrawPending();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Settings"))
            {
                this.DrawSettings();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Resets"))
            {
                this.DrawResets();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Appearance"))
            {
                this.DrawAppearance();
                ImGui.EndTabItem();
            }

            if (SyncGate.Panel is SyncBook && ImGui.BeginTabItem("Sync"))
            {
                this.DrawSyncSettings();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        ImGui.EndChild();
    }

    private void DrawSettings()
    {
        var pausePvp = this.session.PauseInPvp;
        if (ImGui.Checkbox("Pause during PvP##pause-pvp", ref pausePvp))
        {
            this.session.PauseInPvp = pausePvp;
            this.save();
        }

        ImGui.TextWrapped("Chat, alarms, and sync wait until you leave the match. The Wolves' Den still listens.");
        var listening = this.session.Listening;
        if (ImGui.Checkbox("Listen to chat", ref listening))
        {
            this.session.SetListening(listening);
            this.save();
        }

        this.DrawHoldDays();
        this.DrawDropPast();
        this.DrawAggressiveFilter();
        this.DrawInformedaholic();
        this.DrawLinkChoice();
        this.DrawChannelOptions();
        this.DrawAlarms();
        ImGui.Separator();
        if (ImGui.Button("Make a macro##macro-help", new Vector2(-1f, 0f)))
            this.openMacroHelper?.Invoke();
        ImGui.TextWrapped("Opens a guide you can keep beside User Macros. The calendar steps aside until you close that guide.");
        this.DrawPerformance();
        ImGui.Separator();
        ImGui.TextUnformatted("Feedback");
        var version = PluginVersion();
        if (ImGui.Button("Suggest a feature##feedback-feature", new Vector2(-1f, 0f)))
            this.AskFeedback(GitHubFeedback.FeatureUrl(version), GitHubFeedback.FeaturePrompt(version));
        if (ImGui.Button("Report an error##feedback-error", new Vector2(-1f, 0f)))
            this.AskFeedback(GitHubFeedback.ErrorUrl(version), GitHubFeedback.ErrorPrompt(version));
        ImGui.TextWrapped("Opens a GitHub issue with a short form already filled in. Edit it, then submit. A GitHub account is required.");
    }

    private void DrawPerformance()
    {
        ImGui.SetNextItemOpen(true, ImGuiCond.Once);
        if (!ImGui.CollapsingHeader("Performance##calendar-performance"))
            return;
        ImGui.TextWrapped("Grid style, compact mode and draw timing are in Appearance.");
        var parseDebug = this.session.ParseDebug;
        if (ImGui.Checkbox("Parse debug##parse-debug", ref parseDebug))
        {
            this.session.ParseDebug = parseDebug;
            this.save();
        }

        ImGui.TextWrapped("Prints Shout Calendar: [Debug] in chat when a shout is kept. No line means that message was not parsed.");
        this.DrawAlarmChoice(
            "Debug sound##parse-debug-sound",
            this.session.ParseDebugSound,
            value => this.session.ParseDebugSound = value,
            "Debug <se.#>##parse-debug-se",
            this.session.ParseDebugSoundEffect,
            sound => this.session.ParseDebugSoundEffect = sound,
            this.session.ParseDebugSoundFile,
            file => this.session.ParseDebugSoundFile = file,
            "parse-debug");
    }

    private void AskFeedback(string url, string caption)
    {
        if (!LinkFinder.IsHttp(url))
            return;
        if (this.session.RememberLinkChoice && this.session.OpenRememberedLinks)
        {
            LaunchLink(url);
            return;
        }

        this.pendingLink = url;
        this.pendingLinkCaption = caption;
        this.linkRememberDraft = false;
    }

    private static string PluginVersion()
    {
        var version = typeof(CalendarWindow).Assembly.GetName().Version;
        if (version is null)
            return "";
        var text = version.ToString();
        return text.EndsWith(".0", StringComparison.Ordinal) ? text[..^2] : text;
    }

    private void DrawPending()
    {
        if (ImGui.Button("Hide all##pending-hide-all"))
            this.HidePending(true);
        ImGui.SameLine();
        if (ImGui.Button("Unhide all##pending-unhide-all"))
            this.HidePending(false);
        if (SyncGate.Panel is not null)
        {
            if (this.AppearanceChoice("Pending from", (int)this.session.PendingScope,
                ["Current world", "Open calendars", "All synced worlds"], out var scope))
            {
                this.session.PendingScope = (PendingScope)scope;
                this.session.ShowAllServers = scope != 0;
            }
            ImGui.TextWrapped("Uses the advertised destination when known. Sync > Servers controls downloads; the calendar selector controls the grid.");
        }
        var newest = this.session.NewestFirst;
        if (ImGui.Checkbox("Newest first##pending-newest", ref newest))
        { this.session.NewestFirst = newest; this.save(); }
        if (this.session.NewestFirst)
            ImGui.TextDisabled("Newest first keeps moving the rows while shouts are still coming in.");

        var rowRight = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        var continued = false;
        this.DrawWrappingButton("Clear local", rowRight, ref continued, () => this.prompt.Ask(ClearTarget.All));
        this.DrawWrappingButton("Clear local accepted", rowRight, ref continued, () => this.prompt.Ask(ClearTarget.Accepted));
        this.DrawWrappingButton("Clear local unaccepted", rowRight, ref continued, () => this.prompt.Ask(ClearTarget.Unaccepted));
        this.DrawWrappingButton("Delete past local", rowRight, ref continued, () => this.prompt.Ask(ClearTarget.LocalPast));
        this.DrawWrappingButton("Add event", rowRight, ref continued, () => this.adding = true);
        ImGui.Separator();
        this.DrawAddEvent();

        var pending = this.session.Log.Entries.Where(entry => !entry.Accepted && this.IncludeLocal(entry, pendingList: true)).ToList();
        if (this.session.NewestFirst)
            pending.Reverse();
        if (this.session.Appearance.Lists == InviteListStyle.Tree)
            this.DrawPendingTree(pending, this.SharedPendingRows());
        else this.DrawPendingPages(pending, this.SharedPendingRows());
    }

    private void HidePending(bool hidden)
    {
        var changed = this.session.Log.SetPendingHidden(hidden) > 0;
        if (SyncGate.Panel is SyncBook book && book.HidePending(hidden) > 0)
            changed = true;
        if (changed)
            this.save();
    }

    private IEnumerable<SyncAnnouncement> SharedPendingRows()
    {
        if (SyncGate.Panel is not SyncBook book || (!this.session.ShowSyncUnaccepted && !this.session.ShowHidden)) return [];
        var rows = this.syncFrame
            .Where(item => item.Accepted ? this.OnViewedWorld(book, item) : this.PendingOnThisCalendar(book, item))
            .Where(item => item.IsSyncPending || (this.session.ShowHidden && item.Hidden && !item.Declined && !item.Accepted))
            .Where(item => item.Accepted || this.ShowsPendingWorld(item.Text, item.World));
        return this.session.NewestFirst ? rows.Reverse() : rows;
    }

    private void DrawSharedBanner(SyncBook book)
    {
        var label = DataCenters.SharedOn(book.Worlds.Viewing()).Replace("Shared on", "Viewing calendars:", StringComparison.Ordinal) + " · details";
        var width = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        var size = ImGui.CalcTextSize(label, false, MathF.Max(1f, width - 8f));
        var pos = ImGui.GetCursorScreenPos();
        var height = size.Y + 8f;
        ImGui.GetWindowDrawList().AddRectFilled(
            pos,
            pos + new Vector2(width, height),
            ImGui.ColorConvertFloat4ToU32(this.session.SharedBarColor));
        ImGui.SetCursorScreenPos(pos + new Vector2(4f, 4f));
        ImGui.PushStyleColor(ImGuiCol.Text, this.session.Appearance.Ink("Shared bar", this.session.SharedBarColor));
        ImGui.PushTextWrapPos(pos.X + width - 4f);
        ImGui.TextWrapped(label);
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();
        if (ImGui.IsMouseHoveringRect(pos, pos + new Vector2(width, height)))
        {
            ImGui.SetTooltip("Selected calendars. Click for event counts by destination.");
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) this.serverDetails = !this.serverDetails;
        }
        ImGui.SetCursorScreenPos(new Vector2(pos.X, pos.Y + height + 2f));
        if (this.serverDetails) ImGui.TextWrapped(this.VisibleServerCounts());
    }

    private void DrawPendingRow(CalendarEntry entry)
    {
        if (!this.MatchesSearch(entry.EventText, entry.Sender, entry.Place, entry.Server, entry.SpeakerWorld))
            return;
        var title = this.TitleOf(entry.EventText);
        var who = string.IsNullOrWhiteSpace(entry.Sender) ? "" : entry.Sender;
        var rowLabel = (LiveInvite.IsRecent(entry.EventText, entry.DetectedAt, DateTimeOffset.UtcNow) ? "Now · " : "") + (title.Length > 0 ? title : who.Length > 0 ? who : "Invite");
        if (ImGui.Selectable($"{rowLabel}##{entry.Id}", this.selectedId == entry.Id))
        {
            this.selectedId = entry.Id;
            this.SelectLine(new DayLine(entry.Time, entry.Time is null ? 2 : 1, rowLabel, entry.EventText, entry, null, false));
            if (entry.Date is DateOnly selectedDay)
                this.session.Show(selectedDay);
        }

        this.DrawPendingFacts(entry.EventText, PendingFacts(entry));
    }

    private void DrawSharedRow(SyncAnnouncement item)
    {
        if (!this.MatchesSearch(item.Text, item.World, item.Date, item.Time))
            return;
        var title = item.Title.Length > 0 ? item.Title : this.TitleOf(item.Text);
        var heading = title.Length > 0 ? title : item.World;
        var shownClock = ZoneClock.ShownRange(SyncClock.Entry(item), TimeZoneInfo.Local);
        var when = shownClock.Start?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "";
        var day = string.IsNullOrWhiteSpace(item.Date) ? "" : shownClock.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (ImGui.Selectable($"{heading}##sync-pick-{item.Id}", this.selectedLine?.SyncId == item.Id))
        {
            TimeOnly? clock = TimeOnly.TryParseExact(when, "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed)
                ? parsed
                : null;
            this.SelectLine(new DayLine(clock, clock is null ? 2 : 1, heading, item.Text, null, null, false, ColorToken: item.ColorToken, SyncId: item.Id));
            if (DateOnly.TryParseExact(day, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var shown))
                this.session.Show(shown);
        }

        var summary = this.SharedEntry(item);
        var facts = PendingFacts(summary);
        this.DrawPendingFacts(item.Text, facts);
    }

    private void DrawPendingFacts(string? text, string facts)
    {
        var preview = InviteSummary.Preview(text);
        if (preview.Length > 0 && preview != InviteSummary.Title(text)) ImGui.TextWrapped(preview);
        if (EventKind.Find(text) is EventKind kind)
        {
            ImGui.TextColored(this.KindFill(kind), kind.Name);
            ImGui.SameLine();
        }

        if (facts.Length > 0)
            ImGui.TextDisabled(facts);
    }

    private static string PendingFacts(CalendarEntry entry)
    {
        var shown = ZoneClock.ShownRange(entry, TimeZoneInfo.Local);
        var day = entry.Date is null ? "needs a date" : shown.Date.ToString("yyyy-MM-dd");
        if (entry.Repeat is not null)
            day += " · " + entry.Repeat.Label;
        var clock = shown.Start is TimeOnly time
            ? shown.End is TimeOnly end ? $"{time:HH:mm}-{end:HH:mm}" : time.ToString("HH:mm")
            : "";
        var spot = HousingTravel.FindVenue(entry.Place, entry.EventText, entry.Ward, entry.Server, entry.SpeakerWorld);
        var where = spot is HousingSpot found
            ? found.Label
            : !string.IsNullOrWhiteSpace(entry.Place) ? entry.Place : entry.Server ?? "";
        if (where.Length > 42)
            where = where[..41] + "…";
        return string.Join(" · ", new[] { day, clock, where }.Where(part => part.Length > 0));
    }

    private void SaveLocal(SyncBook book, SyncAnnouncement item)
    {
        var copy = LocalCopy.From(item, this.session.Places, DateTimeOffset.Now);
        if (!this.session.Log.Add(copy))
            return;
        book.AcceptRemote(item.Id);
        this.save();
    }

    private void DrawResets()
    {
        var showResets = this.session.ShowResets;
        if (ImGui.Checkbox("Show resets", ref showResets))
        {
            this.session.ShowResets = showResets;
            this.save();
        }

        ImGui.TextWrapped("Crystal blue is a reset. Cactus green is the Cactpot. The dark bar is a limited event. Hover a row for the details.");
        this.DrawResetGroup("Jumbo Cactpot", GameSchedule.GroupCactpot, true, true);
        this.DrawResetGroup("Weekly", GameSchedule.GroupWeekly, true, false);
        this.DrawResetGroup("Daily", GameSchedule.GroupDaily, false, false);
        this.DrawResetGroup("Grand Company", GameSchedule.GroupGrand, false, false);
        this.DrawResetGroup("Limited", GameSchedule.GroupEvent, true, false);
    }

    private void DrawResetGroup(string title, string group, bool open, bool regionPicker)
    {
        ImGui.SetNextItemOpen(open, ImGuiCond.FirstUseEver);
        if (!ImGui.CollapsingHeader($"{title}##reset-group-{group}"))
            return;

        if (regionPicker)
            this.DrawCactpotRegion();

        var zone = TimeZoneInfo.Local;
        var now = DateTimeOffset.UtcNow;
        foreach (var item in GameSchedule.Items.Where(item => item.Group == group))
        {
            var enabled = this.session.Resets.Contains(item.Id);
            if (ImGui.Checkbox($"{item.Name}##reset-{item.Id}", ref enabled))
            {
                this.session.SetReset(item.Id, enabled);
                this.save();
            }

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(item.Detail);
            ImGui.SameLine();
            ImGui.TextDisabled(GameSchedule.NextLine(item.Id, now, zone, this.session.CactpotRegion));
        }
    }

    private void DrawCactpotRegion()
    {
        var region = GameSchedule.NormalizeRegion(this.session.CactpotRegion);
        if (!ImGui.BeginCombo("Data centers##cactpot-region", GameSchedule.RegionLabel(region)))
            return;
        foreach (var choice in new[] { GameSchedule.RegionNa, GameSchedule.RegionEu, GameSchedule.RegionJp, GameSchedule.RegionOc })
        {
            if (!ImGui.Selectable($"{GameSchedule.RegionLabel(choice)}##region-{choice}", choice == region))
                continue;
            this.session.CactpotRegion = choice;
            this.save();
        }

        ImGui.EndCombo();
    }

    private void DrawWrappingButton(string label, float rowRight, ref bool continued, Action onClick)
    {
        var style = ImGui.GetStyle();
        var width = ImGui.CalcTextSize(label).X + (style.FramePadding.X * 2f);
        if (continued)
        {
            var nextRight = ImGui.GetItemRectMax().X + style.ItemSpacing.X + width;
            if (nextRight <= rowRight)
                ImGui.SameLine();
        }

        if (ImGui.Button(label))
            onClick();
        continued = true;
    }

    private void DrawHoldDays()
    {
        if (!this.holdDaysReady)
        {
            this.holdDaysText = this.session.UnacceptedHoldDays.ToString();
            this.holdDaysReady = true;
        }

        ImGui.SetNextItemWidth(48f);
        ImGui.InputText("Hold unaccepted for (days)##hold", ref this.holdDaysText, 4);
        if (!ImGui.IsItemDeactivatedAfterEdit())
            return;
        if (!int.TryParse(this.holdDaysText.Trim(), out var days) || days < 1)
        {
            this.holdDaysText = this.session.UnacceptedHoldDays.ToString();
            return;
        }

        if (days == this.session.UnacceptedHoldDays)
            return;
        this.session.UnacceptedHoldDays = days;
        this.save();
    }

    private void DrawAggressiveFilter()
    {
        var aggressive = this.session.AggressiveFilter;
        if (ImGui.Checkbox("Aggressive filter (2 of date, time, place)##aggressive", ref aggressive))
        {
            this.session.AggressiveFilter = aggressive;
            this.save();
        }

        ImGui.TextWrapped("Off: a line is kept when it has a date, a time, or a place.");
        ImGui.TextWrapped("On: it needs two of those three, and at least one must be a written date, a clear clock, or a housing spot. A world name with tonight or now is not enough.");
    }

    private void DrawInformedaholic()
    {
        var on = this.session.Informedaholic;
        if (ImGui.Checkbox("Informedaholic: accept every invite you see##local-auto", ref on))
        {
            this.session.Informedaholic = on;
            if (on)
                this.session.Log.AcceptPending();
            this.save();
        }

        ImGui.TextWrapped("Kept invites are accepted for you, so they skip the pending list. The filter still decides which lines are invites.");
    }

    private void DrawLinkChoice()
    {
        var remember = this.session.RememberLinkChoice;
        if (ImGui.Checkbox("Remember this choice##link-settings", ref remember))
        {
            this.session.RememberLinkChoice = remember;
            if (remember)
                this.session.OpenRememberedLinks = true;
            else
                this.session.OpenRememberedLinks = false;
            this.save();
        }

        if (!remember)
            ImGui.TextWrapped("Each link asks before it opens.");
        else if (this.session.OpenRememberedLinks)
            ImGui.TextWrapped("Links open without asking.");
        else
            ImGui.TextWrapped("Links stay closed without asking.");
    }

    private void DrawLinks(string? text)
    {
        var links = LinkFinder.Find(text);
        if (links.Count == 0)
            return;
        var rowRight = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        var continued = false;
        foreach (var link in links)
        {
            var label = link.Label.Length > 48 ? link.Label[..45] + "…" : link.Label;
            this.DrawWrappingButton(label, rowRight, ref continued, () => this.AskLink(link.Url));
        }
    }

    private void AskLink(string url)
    {
        if (!LinkFinder.IsHttp(url))
            return;
        if (this.session.RememberLinkChoice)
        {
            if (this.session.OpenRememberedLinks)
                LaunchLink(url);
            return;
        }

        this.pendingLink = url;
        this.pendingLinkCaption = "";
        this.linkRememberDraft = false;
    }

    private void DrawLinkPrompt()
    {
        if (this.pendingLink is not null)
            ImGui.OpenPopup("Open this link?##shout-link");

        ImGui.SetNextWindowSize(new Vector2(440f, 240f), ImGuiCond.Always);
        var open = this.pendingLink is not null;
        if (!ImGui.BeginPopupModal("Open this link?##shout-link", ref open))
        {
            if (!open)
                this.ClearLinkPrompt();
            return;
        }

        this.UseTextScale();
        ImGui.TextWrapped("Open this link in your browser?");
        var footer = (ImGui.GetFrameHeightWithSpacing() * 2f) + 4f;
        var bodyHeight = MathF.Max(ImGui.GetTextLineHeightWithSpacing() * 2f, ImGui.GetContentRegionAvail().Y - footer);
        ImGui.BeginChild("##link-body", new Vector2(-1f, bodyHeight), false);
        this.UseTextScale();
        var columns = Math.Max(12, (int)(ImGui.GetContentRegionAvail().X / Math.Max(1f, ImGui.CalcTextSize("n").X)));
        var shown = this.pendingLinkCaption.Length > 0
            ? this.pendingLinkCaption
            : PromptLayout.Wrap(this.pendingLink ?? "", columns);
        ImGui.TextWrapped(shown);
        ImGui.EndChild();
        ImGui.Checkbox("Remember this choice##link-popup", ref this.linkRememberDraft);
        if (ImGui.Button("Open##link-open"))
        {
            this.FinishLink(true);
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel##link-cancel"))
        {
            this.FinishLink(false);
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void ClearLinkPrompt()
    {
        this.pendingLink = null;
        this.pendingLinkCaption = "";
    }

    private void FinishLink(bool open)
    {
        if (this.linkRememberDraft)
        {
            this.session.RememberLinkChoice = true;
            this.session.OpenRememberedLinks = open;
            this.save();
        }

        var url = this.pendingLink;
        this.ClearLinkPrompt();
        if (open && url is not null)
            LaunchLink(url);
    }

    private static void LaunchLink(string url)
    {
        if (!LinkFinder.IsHttp(url))
            return;
        Util.OpenLink(url);
    }

    private void DrawAlarms()
    {
        ImGui.SetNextItemOpen(false, ImGuiCond.FirstUseEver);
        if (!ImGui.CollapsingHeader("Alarms##alarms"))
            return;

        var chat = this.session.AlarmChat;
        if (ImGui.Checkbox("Announce in chat##alarm-chat", ref chat))
        {
            this.session.AlarmChat = chat;
            this.save();
        }

        this.DrawMinutesBefore();
        this.DrawAlarmChoice(
            "Alarm accepted events##alarm-accepted",
            this.session.AlarmAccepted,
            value => this.session.AlarmAccepted = value,
            "Accepted <se.#>##accepted-se",
            this.session.AcceptedSound,
            sound => this.session.AcceptedSound = sound,
            this.session.AcceptedSoundFile,
            file => this.session.AcceptedSoundFile = file,
            "accepted");
        this.DrawAlarmChoice(
            "Alarm unaccepted events##alarm-pending",
            this.session.AlarmUnaccepted,
            value => this.session.AlarmUnaccepted = value,
            "Unaccepted <se.#>##pending-se",
            this.session.UnacceptedSound,
            sound => this.session.UnacceptedSound = sound,
            this.session.UnacceptedSoundFile,
            file => this.session.UnacceptedSoundFile = file,
            "pending");
        this.DrawAlarmChoice(
            "Alarm resets##alarm-resets",
            this.session.AlarmResets,
            value => this.session.AlarmResets = value,
            "Reset <se.#>##reset-se",
            this.session.ResetSound,
            sound => this.session.ResetSound = sound,
            this.session.ResetSoundFile,
            file => this.session.ResetSoundFile = file,
            "resets");
        ImGui.TextWrapped("A WAV file plays instead of the chat sound. Leave the file empty to use <se.#>.");
    }

    private void DrawAlarmChoice(
        string checkbox,
        bool enabled,
        Action<bool> setEnabled,
        string soundLabel,
        int sound,
        Action<int> setSound,
        string file,
        Action<string> setFile,
        string id)
    {
        if (ImGui.Checkbox(checkbox, ref enabled))
        {
            setEnabled(enabled);
            this.save();
        }

        this.DrawSoundChoice(soundLabel, sound, setSound);
        var testLabel = id switch
        {
            "accepted" => "Test accepted",
            "pending" => "Test unaccepted",
            "parse-debug" => "Test debug",
            _ => "Test resets",
        };
        if (ImGui.Button($"{testLabel}##{id}-se"))
            this.previewSound(sound, file);

        var path = file ?? "";
        ImGui.SetNextItemWidth(220f);
        if (ImGui.InputText($"File##{id}-file", ref path, 260))
        {
            setFile(path);
            this.save();
        }

        ImGui.SameLine();
        if (ImGui.Button($"Browse##{id}-file"))
        {
            this.dialogs.OpenFileDialog(
                "Alarm sound",
                "WAV{.wav}",
                (ok, chosen) =>
                {
                    if (!ok || string.IsNullOrWhiteSpace(chosen))
                        return;
                    setFile(chosen);
                    this.save();
                });
        }
    }

    private void DrawSoundChoice(string label, int current, Action<int> setSound)
    {
        var shown = $"<se.{EventAlarm.ClampSound(current)}>";
        if (!ImGui.BeginCombo(label, shown))
            return;

        for (var sound = EventAlarm.MinSound; sound <= EventAlarm.MaxSound; sound++)
        {
            var choice = $"<se.{sound}>";
            if (ImGui.Selectable($"{choice}##{label}-{sound}", sound == EventAlarm.ClampSound(current)))
            {
                setSound(sound);
                this.save();
            }
        }

        ImGui.EndCombo();
    }

    private void DrawMinutesBefore()
    {
        if (!this.minutesBeforeReady)
        {
            this.minutesBeforeText = this.session.AlarmMinutesBefore.ToString();
            this.minutesBeforeReady = true;
        }

        ImGui.SetNextItemWidth(48f);
        ImGui.InputText("Minutes before##alarm-lead", ref this.minutesBeforeText, 4);
        var edited = ImGui.IsItemDeactivatedAfterEdit();
        ImGui.SameLine();
        var atStart = this.session.AlarmAtStart;
        if (ImGui.Checkbox("Warn when it starts##alarm-start", ref atStart))
        {
            this.session.AlarmAtStart = atStart;
            this.save();
        }

        if (!edited)
            return;
        if (!int.TryParse(this.minutesBeforeText.Trim(), out var minutes) || minutes < 0)
        {
            this.minutesBeforeText = this.session.AlarmMinutesBefore.ToString();
            return;
        }

        if (minutes == this.session.AlarmMinutesBefore)
            return;
        this.session.AlarmMinutesBefore = minutes;
        this.save();
    }

    private void DrawChannelOptions()
    {
        ImGui.SetNextItemOpen(false, ImGuiCond.FirstUseEver);
        if (!ImGui.CollapsingHeader("Chats##listen"))
            return;

        ImGui.TextWrapped("Say, shout, yell, incoming tells, free company, free company announcements, and novice network start on. An unchecked chat is not added, and invites from that chat stay off the calendar.");
        if (!this.session.Listening)
            ImGui.BeginDisabled();
        foreach (var option in ChatChannels.All)
        {
            var enabled = this.session.ChannelOn(option.Channel);
            if (!ImGui.Checkbox($"{option.Label}##chat-{option.Channel}", ref enabled))
                continue;
            this.session.SetChannel(option.Channel, enabled);
            this.save();
        }

        if (!this.session.Listening)
            ImGui.EndDisabled();
    }

    private void ApplySyncWeek()
    {
        if (!this.weekChosen && this.session.WeekView is bool chosen)
        {
            this.weekChosen = true;
            this.weekView = chosen;
            if (chosen)
                this.weekStart = SundayOn(DateOnly.FromDateTime(DateTime.Now));
        }

        if (this.weekChosen || this.weekView || !Plugin.SyncEnabled())
            return;
        this.weekView = true;
        this.weekStart = SundayOn(DateOnly.FromDateTime(DateTime.Now));
    }

    private void DrawCalendar()
    {
        ImGui.BeginChild("shout-month");
        this.UseTextScale();
        ImGui.SetNextItemWidth(160f);
        var textScale = this.session.TextScale;
        if (ImGui.SliderFloat("##text-scale", ref textScale, 0.85f, 2f, "%.2f"))
            this.session.TextScale = textScale;
        var scaleEdited = ImGui.IsItemDeactivatedAfterEdit();
        ImGui.SameLine();
        ImGui.SetNextItemWidth(64f);
        if (ImGui.InputFloat("Text##text-size", ref textScale, 0f, 0f, "%.2f"))
            this.session.TextScale = Math.Clamp(textScale, 0.85f, 2f);
        if (scaleEdited || ImGui.IsItemDeactivatedAfterEdit())
            this.save();
        ImGui.SetNextItemWidth(260f);
        ImGui.InputTextWithHint("##event-search", "Search", ref this.eventSearch, 200);
        if (!this.CompactMode) this.ApplySyncWeek();
        ImGui.SameLine();
        if (!this.CompactMode && ImGui.Button(this.weekView ? "Month" : "Week"))
        {
            this.weekView = !this.weekView;
            this.weekChosen = true;
            this.session.WeekView = this.weekView;
            this.save();
            if (this.weekView)
                this.weekStart = SundayOn(DateOnly.FromDateTime(DateTime.Now));
        }

        if (this.weekView || this.CompactMode)
        {
            this.DrawWeek();
            ImGui.EndChild();
            return;
        }

        this.FollowToday();
        var month = this.GridMonth();
        if (ImGui.Button("Previous month"))
        { this.session.Show(new DateOnly(month.Year, month.Month, 1).AddMonths(-1)); month = this.GridMonth(); }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(128f);
        if (ImGui.BeginCombo("##month", new DateOnly(2000, month.Month, 1).ToString("MMMM", System.Globalization.CultureInfo.InvariantCulture)))
        {
            for (var choice = 1; choice <= 12; choice++)
            {
                var label = new DateOnly(2000, choice, 1).ToString("MMMM", System.Globalization.CultureInfo.InvariantCulture);
                if (ImGui.Selectable($"{label}##month-{choice}", choice == month.Month))
                {
                    this.session.Show(new DateOnly(month.Year, choice, 1));
                    month = this.GridMonth();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        var todayYear = DateTime.Now.Year;
        var years = CalendarPicker.Years(todayYear, month.Year);
        ImGui.SetNextItemWidth(88f);
        if (ImGui.BeginCombo("##year", month.Year.ToString(System.Globalization.CultureInfo.InvariantCulture)))
        {
            foreach (var year in years)
            {
                if (ImGui.Selectable($"{year}##year-{year}", year == month.Year))
                {
                    this.session.Show(new DateOnly(year, month.Month, 1));
                    month = this.GridMonth();
                }

                if (year == todayYear && ImGui.IsWindowAppearing())
                    ImGui.SetScrollHereY(0.5f);
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (ImGui.Button("Next month"))
        { this.session.Show(new DateOnly(month.Year, month.Month, 1).AddMonths(1)); month = this.GridMonth(); }
        var today = DateTime.Now;
        if (month.Year != today.Year || month.Month != today.Month)
        {
            ImGui.SameLine();
            if (ImGui.Button("Today"))
            {
                var todayDate = DateOnly.FromDateTime(today);
                this.session.Show(todayDate);
                if (this.folderDay is not null)
                    this.folderDay = todayDate.Day;
                month = this.GridMonth();
            }
        }

        const float splitGap = 8f;
        const float gap = 6f;
        var room = ImGui.GetContentRegionAvail();
        var line = ImGui.GetTextLineHeightWithSpacing();
        var rows = (month.Cells.Count + 6) / 7;
        var headerH = line;
        var byWidth = MathF.Max(80f, MathF.Floor((room.X - (gap * 6f)) / 7f));
        var naturalGrid = headerH + (rows * byWidth) + (Math.Max(0, rows - 1) * gap);
        var minDetail = line * 9f;
        var maxGrid = Math.Max(line * 10f, room.Y - minDetail - splitGap);
        var gridH = Math.Clamp(naturalGrid, line * 10f, maxGrid);
        var detailH = Math.Max(minDetail, room.Y - gridH - splitGap);

        ImGui.BeginChild("month-grid", new Vector2(0f, gridH), false);
        this.UseTextScale();
        var byHeight = MathF.Floor((ImGui.GetContentRegionAvail().Y - headerH - (gap * Math.Max(0, rows - 1))) / Math.Max(1, rows));
        var side = MathF.Max(80f, MathF.Min(byWidth, byHeight));
        var header = ImGui.GetCursorScreenPos();
        for (var column = 0; column < 7; column++)
        {
            ImGui.SetCursorScreenPos(header + new Vector2(column * (side + gap), 0));
            ImGui.TextUnformatted(new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" }[column]);
        }

        var grid = header + new Vector2(0, headerH);
        var monthStart = month.Cells.Count > 0 && month.Cells[0].Date is DateOnly firstDay
            ? firstDay
            : new DateOnly(month.Year, month.Month, 1);
        var monthEnd = month.Cells.Count > 0 && month.Cells[^1].Date is DateOnly lastDay
            ? lastDay
            : new DateOnly(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month));
        var (monthLanes, monthLaneItems) = this.BuildSpanLanes(monthStart, monthEnd, month.Year, month.Month);
        if (monthEnd.Month != monthStart.Month || monthEnd.Year != monthStart.Year)
        {
            var extra = this.BuildSpanLanes(monthStart, monthEnd, monthEnd.Year, monthEnd.Month);
            monthLaneItems = monthLaneItems.Concat(extra.Items).GroupBy(item => item.Key).Select(group => group.First()).ToList();
            monthLanes = GameSchedule.Lanes(monthLaneItems);
        }
        this.spanSlots = GameSchedule.LaneSlotsByDay(monthLanes, monthLaneItems);
        this.spanOriginY.Clear();
        this.nightAnchors.Clear();
        for (var index = 0; index < month.Cells.Count; index++)
        {
            var column = index % 7;
            var row = index / 7;
            ImGui.SetCursorScreenPos(grid + new Vector2(column * (side + gap), row * (side + gap)));
            var cell = month.Cells[index];
            this.DrawDay(cell.Date, side, side, false, outside: cell.Day is null);
        }

        this.DrawSchedule(month, grid, side, gap, monthLanes);
        this.DrawNightLinks();
        ImGui.EndChild();

        ImGui.BeginChild("month-detail", new Vector2(0f, detailH), true);
        this.UseTextScale();
        this.DrawSelectedDetail();
        this.DrawUndated(DateTimeOffset.UtcNow);
        ImGui.EndChild();
        ImGui.EndChild();
    }

    private (IReadOnlyDictionary<string, int> Lanes, List<(string Key, DateOnly Start, DateOnly End)> Items) BuildSpanLanes(
        DateOnly from,
        DateOnly to,
        int year,
        int month)
    {
        var marks = this.Marks(year, month)
            .Where(mark => mark.StartDate != mark.EndDate && mark.EndDate >= from && mark.StartDate <= to)
            .Where(mark => this.MatchesSearch(mark.Chip, mark.Name, mark.Detail))
            .ToList();
        var overnight = this.OvernightSpans(from, to);
        var items = marks
            .Select(mark => (mark.Key, mark.StartDate, mark.EndDate))
            .Concat(overnight.Select(item => (item.Key, item.Start, item.End)))
            .ToList();
        return (GameSchedule.Lanes(items), items);
    }

    private void DrawSchedule(
        CalendarMonth month,
        Vector2 grid,
        float side,
        float gap,
        IReadOnlyDictionary<string, int> lanes)
    {
        var marks = this.Marks(month.Year, month.Month);
        var scheduleSpans = marks.Where(mark => mark.StartDate != mark.EndDate).ToList();
        var overnight = this.OvernightSpans(
            month.Cells.Count > 0 && month.Cells[0].Date is DateOnly from ? from : new DateOnly(month.Year, month.Month, 1),
            month.Cells.Count > 0 && month.Cells[^1].Date is DateOnly to ? to : new DateOnly(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month)));
        if (scheduleSpans.Count == 0 && overnight.Count == 0)
            return;

        var draw = ImGui.GetWindowDrawList();
        var pad = ImGui.GetStyle().WindowPadding.X;
        var step = this.SpanRowStep();
        var bar = step - 2f;
        foreach (var mark in scheduleSpans)
        {
            if (!this.MatchesSearch(mark.Chip, mark.Name, mark.Detail))
                continue;
            if (!lanes.TryGetValue(mark.Key, out var lane))
                continue;
            var (fill, ink) = this.Tone(mark.Tone);
            var when = $"{mark.LocalStart:ddd d MMM HH:mm} – {mark.LocalEnd:ddd d MMM HH:mm}";
            foreach (var segment in GameSchedule.Segments(month, mark.StartDate, mark.EndDate))
            {
                var origin = this.SpanOriginForMonth(month, segment, grid, side, gap);
                this.PaintSpanBar(draw, grid, side, gap, pad, origin, bar, step, segment, lane, fill, ink, mark.Chip, mark.Tone == ResetTone.Event, $"{mark.Name}\n{when}\n{mark.Detail}", () => this.SelectLine(this.LineFor(mark)));
            }
        }

        foreach (var item in overnight)
        {
            if (!lanes.TryGetValue(item.Key, out var lane))
                continue;
            foreach (var segment in GameSchedule.Segments(month, item.Start, item.End))
            {
                var origin = this.SpanOriginForMonth(month, segment, grid, side, gap);
                this.PaintSpanBar(draw, grid, side, gap, pad, origin, bar, step, segment, lane, item.Fill, item.Ink, item.Label, false, item.Detail, () => this.SelectLine(item.Line));
            }
        }
    }

    private float SpanOriginForMonth(CalendarMonth month, SpanSegment segment, Vector2 grid, float side, float gap)
    {
        var index = (segment.Row * 7) + segment.FirstColumn;
        if (index >= 0 && index < month.Cells.Count && month.Cells[index].Date is DateOnly date
            && this.spanOriginY.TryGetValue(date, out var origin))
            return origin;

        var style = ImGui.GetStyle();
        return grid.Y + (segment.Row * (side + gap)) + style.WindowPadding.Y + ImGui.GetFrameHeight() + style.ItemSpacing.Y;
    }

    private void PaintSpanBar(
        ImDrawListPtr draw,
        Vector2 grid,
        float side,
        float gap,
        float pad,
        float originY,
        float bar,
        float step,
        SpanSegment segment,
        int lane,
        Vector4 fill,
        Vector4 ink,
        string chip,
        bool eventBorder,
        string tip,
        Action onClick)
    {
        var x1 = grid.X + (segment.FirstColumn * (side + gap)) + pad;
        var x2 = grid.X + (segment.LastColumn * (side + gap)) + side - pad;
        var y1 = originY + (lane * step);
        var min = new Vector2(x1, y1);
        var max = new Vector2(x2, y1 + bar);
        this.PaintEventFill(draw, min, max, fill);
        if (eventBorder)
            draw.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(0.85f, 0.72f, 0.28f, 1f)));
        var label = this.session.LightCalendar ? chip : this.Fit(chip, max.X - min.X - 8f);
        draw.PushClipRect(min, max, true);
        this.DrawScaledText(draw, new Vector2(min.X + 4f, min.Y + ((bar - ImGui.GetTextLineHeight()) * 0.5f)), ImGui.ColorConvertFloat4ToU32(ink), label);
        draw.PopClipRect();
        if (ImGui.IsMouseHoveringRect(min, max))
        {
            ImGui.SetTooltip(this.session.LightCalendar ? chip : tip);
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                onClick();
        }
    }

    private readonly record struct OvernightBar(string Key, DateOnly Start, DateOnly End, string Label, string Detail, DayLine Line, Vector4 Fill, Vector4 Ink);

    private List<OvernightBar> OvernightSpans(DateOnly from, DateOnly to)
    {
        if (this.session.Appearance.Overnight != OvernightStyle.Bars) return [];
        if (this.overnightCache.TryGetValue((from, to), out var cached)) return cached;
        var list = new List<OvernightBar>();
        this.overnightCache[(from, to)] = list;
        foreach (var entry in this.session.Log.Entries)
        {
            if (!this.IncludeLocal(entry))
                continue;
            var named = EventTitle.Readable(EventTitle.Choose(entry.EventText));
            var title = (LiveInvite.IsRecent(entry.EventText, entry.DetectedAt, DateTimeOffset.UtcNow) ? "Now · " : "") + (named.Length > 0 ? named : string.IsNullOrWhiteSpace(entry.Place) ? entry.EventText : entry.Place);
            if (!this.MatchesSearch(title, entry.EventText, entry.Sender, entry.Place, entry.Server))
                continue;
            foreach (var span in Overnight.Spans(entry, from, to, TimeZoneInfo.Local))
            {
                var label = $"{span.StartClock:HH:mm}–{span.EndClock:HH:mm} {title}";
                var line = new DayLine(span.StartClock, 0, title, entry.EventText, span.Entry, null, true,
                    OnDay: span.Start, EndAt: span.EndClock, Pinned: entry.Pinned);
                var (fill, ink) = this.Ink(line);
                var key = $"overnight:{span.StartClock:HHmm}:local:{entry.Id}:{span.Start:yyyy-MM-dd}";
                list.Add(new OvernightBar(key, span.Start, span.End, label, entry.EventText, line, fill, ink));
            }
        }

        foreach (var item in this.syncFrame)
        {
            if (!this.IncludeSync(item))
                continue;
            var named = EventTitle.Readable((item.Title.Length > 0 ? item.Title : EventTitle.Choose(item.Text)));
            var title = (LiveInvite.IsRecent(item.Text, item.ObservedAt, DateTimeOffset.UtcNow) ? "Now · " : "") + (named.Length > 0 ? named : item.Text);
            if (!this.MatchesSearch(title, item.Text, item.World))
                continue;
            foreach (var span in Overnight.Spans(this.SharedEntry(item), from, to, TimeZoneInfo.Local))
            {
                var label = $"{span.StartClock:HH:mm}–{span.EndClock:HH:mm} {title}";
                var line = new DayLine(span.StartClock, 0, title, item.Text, null, null, true, null,
                    item.ColorToken, item.Id, span.Start, span.EndClock, this.session.SyncPinned(item.Id));
                var (fill, ink) = this.Ink(line);
                var key = $"overnight:{span.StartClock:HHmm}:sync:{item.Id}:{span.Start:yyyy-MM-dd}";
                list.Add(new OvernightBar(key, span.Start, span.End, label, item.Text, line, fill, ink));
            }
        }

        return list;
    }

    private void UseTextScale()
    {
        var scale = this.session.TextScale;
        if (scale is < 0.85f or > 2f || float.IsNaN(scale))
            scale = 1f;
        ImGui.SetWindowFontScale(scale);
    }

    private void DrawScaledText(ImDrawListPtr draw, Vector2 pos, uint color, string text)
    {
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), pos, color, text);
    }

    private float GlanceBar() => ImGui.GetTextLineHeight() + 6f;

    /// <summary>Shared row height for multi-day span lanes and single-day chips.</summary>
    private float SpanRowStep() => this.GlanceBar();

    private string Fit(string text, float width)
    {
        if (string.IsNullOrEmpty(text) || width <= 0f)
            return "";
        if (ImGui.CalcTextSize(text).X <= width)
            return text;
        const string ellipsis = "…";
        var budget = width - ImGui.CalcTextSize(ellipsis).X;
        if (budget <= 0f)
            return "";
        var low = 1;
        var high = text.Length;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (ImGui.CalcTextSize(text[..mid]).X <= budget)
                low = mid;
            else
                high = mid - 1;
        }

        return text[..low] + ellipsis;
    }

    private (Vector4 Fill, Vector4 Ink) Tone(ResetTone tone) => tone switch
    {
        ResetTone.Cactus => (this.session.CactusColor, this.session.Appearance.Ink("Cactpot", this.session.CactusColor)),
        ResetTone.Event => (this.session.EventColor, this.session.Appearance.Ink("Limited event", this.session.EventColor)),
        _ => (this.session.CrystalColor, this.session.Appearance.Ink("Reset", this.session.CrystalColor)),
    };

    private readonly record struct DayLine(TimeOnly? Time, int Rank, string Title, string Detail, CalendarEntry? Entry, ResetTone? Tone, bool Span, IReadOnlyList<DayLine>? Members = null, string? ColorToken = null, string? SyncId = null, DateOnly? OnDay = null, TimeOnly? EndAt = null, bool Pinned = false);

    private void DrawUndated(DateTimeOffset now)
    {
        var undated = this.session.Log.Entries.Where(entry => entry.Date is null && this.IncludeLocal(entry)).ToList();
        if (undated.Count == 0)
            return;
        ImGui.Separator();
        ImGui.TextUnformatted("Needs a date");
        foreach (var entry in undated)
        {
            if (!this.MatchesSearch(entry.EventText, entry.Sender, entry.Place, entry.Server, entry.SpeakerWorld))
                continue;
            this.DrawEvent(entry, now);
        }
    }

    private void BeginEdit(CalendarEntry entry)
    {
        entry = this.session.Log.Entries.FirstOrDefault(saved => saved.Id == entry.Id) ?? entry;
        this.editingId = entry.Id;
        var clock = ClockInput.FromEntry(entry, TimeZoneInfo.Local);
        if (clock.Date is DateOnly day)
            this.session.Show(day);
        this.editErrors = default;
        this.editNote = entry.EventText;
        this.editDate = clock.DateText;
        this.editTime = clock.TimeText;
        this.editPlace = entry.Place;
        this.editHasColor = entry.Color is not null;
        this.editColor = entry.Color ?? (entry.Accepted ? this.session.AcceptedColor : this.session.PendingColor);
    }

    private void DrawEdit(CalendarEntry entry)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var picker = 168f;
        ImGui.BeginGroup();
        ImGui.SetNextItemWidth(MathF.Max(120f, width - picker - 12f));
        ImGui.InputTextMultiline($"Note##edit-{entry.Id}", ref this.editNote, 4000, new Vector2(MathF.Max(120f, width - picker - 12f), 72f));
        ImGui.SetNextItemWidth(MathF.Max(120f, width - picker - 12f));
        ImGui.InputText($"Date##edit-{entry.Id}", ref this.editDate, 16);
        FieldError(this.editErrors.Date);
        ImGui.SetNextItemWidth(MathF.Max(120f, width - picker - 12f));
        ImGui.InputText($"Time##edit-{entry.Id}", ref this.editTime, 32);
        FieldError(this.editErrors.Time);
        ImGui.SetNextItemWidth(MathF.Max(120f, width - picker - 12f));
        ImGui.InputText($"Location##edit-{entry.Id}", ref this.editPlace, 200);
        ImGui.EndGroup();
        ImGui.SameLine();
        ImGui.BeginGroup();
        var color = this.editColor;
        if (ImGui.ColorEdit4($"##tint-{entry.Id}", ref color, ImGuiColorEditFlags.PickerHueWheel | ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar))
        {
            this.editColor = color;
            this.editHasColor = true;
        }

        if (ImGui.SmallButton($"Default##tint-{entry.Id}"))
            this.editHasColor = false;
        ImGui.EndGroup();
        ImGui.TextDisabled("Time: 21:00 or 21:00-23:00. Empty Date or Time removes it.");
        FieldError(this.editErrors.Message);
        if (!ImGui.Button($"Save##edit-{entry.Id}"))
            return;
        if (!this.session.Log.TryRevise(entry.Id, this.editNote, this.editDate, this.editTime, this.editPlace,
            this.editHasColor ? this.editColor : null, DateTimeOffset.UtcNow, this.session.Places, this.session.Channels, out this.editErrors))
            return;
        if (!entry.Accepted)
            this.session.Log.Accept(entry.Id);
        var updated = this.session.Log.Entries.First(item => item.Id == entry.Id);
        if (updated.Date is DateOnly moved)
        {
            this.session.Show(moved);
            if (this.folderDay is not null)
                this.folderDay = moved.Day;
        }

        if (this.selectedLine is DayLine selected && selected.Entry?.Id == updated.Id)
            this.selectedLine = selected with { Entry = updated, Detail = updated.EventText };
        this.editingId = null;
        this.save();
    }

    private void SelectLine(DayLine line)
    {
        this.selectedLine = line;
        if (line.Entry is not CalendarEntry entry || (this.editingId is not null && this.editingId != entry.Id))
            this.editingId = null;
    }

    private void DrawSelectedDetail()
    {
        if (this.selectedLine is not DayLine line)
            return;

        ImGui.Separator();
        if (line.SyncId is not null)
        {
            this.DrawSyncDetail(line);
            return;
        }

        if (line.Entry is CalendarEntry snapshot)
        {
            var entry = this.session.Log.Entries.FirstOrDefault(item => item.Id == snapshot.Id);
            if (entry is null)
            {
                this.selectedLine = null;
                return;
            }

            var (fill, ink) = this.Ink(line with { Entry = entry });
            ImGui.PushStyleColor(ImGuiCol.Text, ink);
            ImGui.TextUnformatted(entry.Accepted ? "Accepted" : entry.Hidden ? "Hidden" : "Pending");
            ImGui.PopStyleColor();
            _ = fill;
            this.DrawInviteGlance(entry);
            ImGui.Separator();
            ImGui.TextDisabled("Message");
            this.DrawNote(entry.EventText, EventTitle.Choose(entry.EventText));
            if (this.editingId == entry.Id)
            {
                this.DrawEdit(entry);
            }
            else
            {
                if (!entry.Accepted)
                {
                    if (ImGui.SmallButton($"Accept##detail-{entry.Id}") && this.session.Log.Accept(entry.Id))
                    {
                        if (SyncGate.Panel is SyncBook synced)
                            synced.AcceptSame(entry, synced.Worlds.Home);
                        if (entry.Date is DateOnly acceptedDay)
                            this.session.Show(acceptedDay);
                        this.save();
                    }

                    ImGui.SameLine();
                    if (entry.Hidden)
                    {
                        if (ImGui.SmallButton($"Unhide##detail-{entry.Id}") && this.session.Log.SetHidden(entry.Id, false))
                            this.save();
                    }
                    else if (ImGui.SmallButton($"Hide##detail-{entry.Id}") && this.session.Log.SetHidden(entry.Id, true))
                    {
                        this.save();
                    }

                    ImGui.SameLine();
                }

                if (ImGui.SmallButton($"Edit##detail-{entry.Id}"))
                    this.BeginEdit(entry);
                ImGui.SameLine();
            }

            if (ImGui.SmallButton($"{(entry.Pinned ? "Unpin" : "Pin")}##pin-{entry.Id}") && this.session.Log.SetPinned(entry.Id, !entry.Pinned))
                this.save();
            ImGui.SameLine();
            this.DrawDelete(entry, snapshot.Date, $"detail-{entry.Id}");

            return;
        }

        var members = line.Members is { Count: > 0 } ? line.Members : [line];
        var clock = line.Time is TimeOnly time ? $"{time:HH:mm}  " : "";
        ImGui.TextUnformatted(clock + line.Title);
        foreach (var member in members)
        {
            ImGui.Separator();
            var (resetFill, resetInk) = this.Ink(member);
            ImGui.PushStyleColor(ImGuiCol.Text, resetInk);
            ImGui.TextUnformatted(member.Title);
            ImGui.PopStyleColor();
            _ = resetFill;
            if (!string.IsNullOrWhiteSpace(member.Detail))
                ImGui.TextWrapped(member.Detail);
            this.DrawPins(member.Detail, member.Title);
        }

        ImGui.TextUnformatted("This reset time is fixed.");
    }

    private void DrawPins(string text, string? chip, CalendarEntry? entry = null, string? heardOn = null)
    {
        var housing = HousingTravel.FindVenue(entry?.Place, text, entry?.Ward, entry?.Server ?? heardOn, entry?.SpeakerWorld, heardOn);
        if (housing is HousingSpot spot && ImGui.SmallButton($"{spot.City} aetheryte##housing-{spot.Label}"))
            this.openHousing(spot);

        var shown = new List<(float X, float Y)>();
        foreach (var pin in GameSchedule.Guide(chip))
        {
            var caption = pin.HasMap
                ? $"{pin.Label} ({pin.X:0.0}, {pin.Y:0.0})"
                : pin.Quest ?? pin.Label;
            if (pin.HasMap && pin.Quest is not null)
                caption = $"{pin.Quest} — {caption}";
            if (!ImGui.SmallButton($"{caption}##pin-{caption}"))
                continue;
            this.openPin(pin.PlaceName, pin.X, pin.Y, pin.HasMap, pin.Quest, null);
            if (pin.HasMap)
                shown.Add((pin.X, pin.Y));
        }

        var catalogPlace = this.session.Places.Match(text).FirstOrDefault() ?? "";
        foreach (var mention in MapMentions.Read(text))
        {
            if (shown.Any(pin => Math.Abs(pin.X - mention.X) < 0.05f && Math.Abs(pin.Y - mention.Y) < 0.05f))
                continue;
            var place = string.IsNullOrEmpty(mention.Place) ? catalogPlace : mention.Place;
            var caption = string.IsNullOrEmpty(place)
                ? $"Flag ({mention.X:0.0}, {mention.Y:0.0}) on your current map"
                : $"{place} ({mention.X:0.0}, {mention.Y:0.0})";
            if (ImGui.SmallButton($"{caption}##spot-{mention.X}-{mention.Y}"))
                this.openPin(place, mention.X, mention.Y, true, null, entry?.Server);
        }
    }

    private DayLine LineFor(ScheduleOccurrence mark)
    {
        var time = TimeOnly.FromDateTime(mark.LocalStart);
        return new DayLine(time, 0, mark.Chip, mark.Name + ". " + mark.Detail, null, mark.Tone, mark.StartDate != mark.EndDate);
    }

    private void DrawWeek()
    {
        var days = this.VisibleDays;
        if (ImGui.Button(this.CompactMode ? "Previous 3 days" : "Previous week"))
            this.weekStart = this.weekStart.AddDays(-days);
        ImGui.SameLine();
        ImGui.TextUnformatted($"{this.weekStart:MMM d} – {this.weekStart.AddDays(days - 1):MMM d}");
        ImGui.SameLine();
        if (ImGui.Button(this.CompactMode ? "Next 3 days" : "Next week"))
            this.weekStart = this.weekStart.AddDays(days);
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (today < this.weekStart || today > this.weekStart.AddDays(days - 1))
        {
            ImGui.SameLine();
            if (ImGui.Button("Today"))
            {
                this.weekStart = this.CompactMode ? today.AddDays(-1) : SundayOn(today);
                this.session.Show(today);
                if (this.folderDay is not null)
                    this.folderDay = today.Day;
            }
        }

        const float handleH = 16f;
        var room = ImGui.GetContentRegionAvail();
        var line = ImGui.GetTextLineHeightWithSpacing();
        var usable = Math.Max(line * (this.CompactMode ? 8f : 16f), room.Y - handleH);
        var minDetail = line * (this.CompactMode ? 3f : 6f);
        var minWeek = line * (this.CompactMode ? 4f : 8f);
        var share = Math.Clamp(this.session.WeekDetailShare, minDetail / usable, (usable - minWeek) / usable);
        var detailH = usable * share;
        var weekH = usable - detailH;

        ImGui.BeginChild("week-grid", new Vector2(0f, weekH), false);
        this.UseTextScale();
        const float gap = 6f;
        var width = MathF.Max(96f, MathF.Floor((ImGui.GetContentRegionAvail().X - (gap * (days - 1))) / days));
        var header = ImGui.GetCursorScreenPos();
        for (var column = 0; column < days; column++)
        {
            ImGui.SetCursorScreenPos(header + new Vector2(column * (width + gap), 0));
            ImGui.TextUnformatted(this.weekStart.AddDays(column).ToString("ddd"));
        }

        var grid = header + new Vector2(0, ImGui.GetTextLineHeightWithSpacing());
        var height = Math.Max(line * 6f, ImGui.GetContentRegionAvail().Y - ImGui.GetTextLineHeightWithSpacing() - 4f);
        var weekEnd = this.weekStart.AddDays(days - 1);
        var (weekLanes, weekLaneItems) = this.BuildSpanLanes(this.weekStart, weekEnd, this.weekStart.Year, this.weekStart.Month);
        if (weekEnd.Month != this.weekStart.Month || weekEnd.Year != this.weekStart.Year)
        {
            var extra = this.BuildSpanLanes(this.weekStart, weekEnd, weekEnd.Year, weekEnd.Month);
            weekLaneItems = weekLaneItems.Concat(extra.Items).GroupBy(item => item.Key).Select(group => group.First()).ToList();
            weekLanes = GameSchedule.Lanes(weekLaneItems);
        }

        this.spanSlots = GameSchedule.LaneSlotsByDay(weekLanes, weekLaneItems);
        this.spanOriginY.Clear();
        this.nightAnchors.Clear();
        for (var column = 0; column < days; column++)
        {
            ImGui.SetCursorScreenPos(grid + new Vector2(column * (width + gap), 0));
            this.DrawDay(this.weekStart.AddDays(column), width, height, false, scrollChips: true);
        }

        this.DrawWeekSpans(grid, width, gap, weekLanes);
        this.DrawNightLinks();
        ImGui.EndChild();

        this.DrawWeekSplit(usable, minDetail, minWeek);
        ImGui.BeginChild("week-detail", new Vector2(0f, detailH), true);
        this.UseTextScale();
        this.DrawSelectedDetail();
        this.DrawUndated(DateTimeOffset.UtcNow);
        ImGui.EndChild();
    }

    private void DrawWeekSplit(float usable, float minDetail, float minWeek)
    {
        var width = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        ImGui.InvisibleButton("##week-split", new Vector2(width, 16f));
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var active = ImGui.IsItemActive();
        var hovered = ImGui.IsItemHovered();
        if (hovered || active)
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNs);
        if (active)
        {
            var next = this.session.WeekDetailShare - (ImGui.GetIO().MouseDelta.Y / usable);
            this.session.WeekDetailShare = Math.Clamp(next, minDetail / usable, (usable - minWeek) / usable);
        }

        if (ImGui.IsItemDeactivated())
            this.save();

        var draw = ImGui.GetWindowDrawList();
        var fill = active
            ? new Vector4(0.45f, 0.62f, 0.95f, 1f)
            : hovered
                ? new Vector4(0.32f, 0.36f, 0.42f, 1f)
                : new Vector4(0.22f, 0.24f, 0.28f, 1f);
        this.PaintEventFill(draw, min, max, fill);
        const string grip = "...";
        var gripSize = ImGui.CalcTextSize(grip);
        draw.AddText(
            new Vector2(min.X + ((width - gripSize.X) * 0.5f), min.Y + ((16f - gripSize.Y) * 0.5f)),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.9f, 0.9f, 0.9f, 1f)),
            grip);
    }

    private void DrawWeekSpans(Vector2 grid, float width, float gap, IReadOnlyDictionary<string, int> lanes)
    {
        var weekEnd = this.weekStart.AddDays(this.VisibleDays - 1);
        var marks = new List<ScheduleOccurrence>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Take(int year, int month)
        {
            foreach (var mark in this.Marks(year, month))
            {
                if (mark.StartDate == mark.EndDate || mark.EndDate < this.weekStart || mark.StartDate > weekEnd)
                    continue;
                if (!seen.Add(mark.Key))
                    continue;
                if (!this.MatchesSearch(mark.Chip, mark.Name, mark.Detail))
                    continue;
                marks.Add(mark);
            }
        }

        Take(this.weekStart.Year, this.weekStart.Month);
        if (weekEnd.Year != this.weekStart.Year || weekEnd.Month != this.weekStart.Month)
            Take(weekEnd.Year, weekEnd.Month);
        var overnight = this.OvernightSpans(this.weekStart, weekEnd);
        if (marks.Count == 0 && overnight.Count == 0)
            return;

        var draw = ImGui.GetWindowDrawList();
        var pad = ImGui.GetStyle().WindowPadding.X;
        var step = this.SpanRowStep();
        var bar = step - 2f;
        var fallback = this.spanOriginY.TryGetValue(this.weekStart, out var shared)
            ? shared
            : grid.Y + ImGui.GetStyle().WindowPadding.Y + ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y;
        foreach (var mark in marks)
        {
            if (this.ViewSegment(mark.StartDate, mark.EndDate) is not SpanSegment segment)
                continue;
            if (!lanes.TryGetValue(mark.Key, out var lane))
                continue;
            var (fill, ink) = this.Tone(mark.Tone);
            var when = $"{mark.LocalStart:ddd d MMM HH:mm} – {mark.LocalEnd:ddd d MMM HH:mm}";
            var originDate = this.weekStart.AddDays(segment.FirstColumn);
            var origin = this.spanOriginY.TryGetValue(originDate, out var y) ? y : fallback;
            this.PaintSpanBar(
                draw,
                grid,
                width,
                gap,
                pad,
                origin,
                bar,
                step,
                segment,
                lane,
                fill,
                ink,
                mark.Chip,
                mark.Tone == ResetTone.Event,
                $"{mark.Name}\n{when}\n{mark.Detail}",
                () => this.SelectLine(this.LineFor(mark)));
        }

        foreach (var item in overnight)
        {
            if (this.ViewSegment(item.Start, item.End) is not SpanSegment segment)
                continue;
            if (!lanes.TryGetValue(item.Key, out var lane))
                continue;
            var originDate = this.weekStart.AddDays(segment.FirstColumn);
            var origin = this.spanOriginY.TryGetValue(originDate, out var y) ? y : fallback;
            this.PaintSpanBar(draw, grid, width, gap, pad, origin, bar, step, segment, lane, item.Fill, item.Ink, item.Label, false, item.Detail, () => this.SelectLine(item.Line));
        }
    }

    private void OpenDay(DateOnly date)
    {
        this.session.Show(date);
        this.folderDay = date.Day;
        this.focusFolder = true;
    }

    private static DateOnly SundayOn(DateOnly day) => day.AddDays(-(int)day.DayOfWeek);

    private void FollowToday()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (today == this.followedDay)
            return;
        var wasOpenMonth = this.session.Year == this.followedDay.Year && this.session.Month == this.followedDay.Month;
        this.followedDay = today;
        if (!wasOpenMonth || (this.session.Year == today.Year && this.session.Month == today.Month))
            return;
        this.session.Show(today);
        if (this.folderDay is not null)
            this.folderDay = today.Day;
    }

    private void BeginDraw()
    {
        var bookId = SyncGate.Panel?.BookId ?? "";
        if (Environment.TickCount64 < this.nextSnapshot && this.sampledBook == bookId && this.sampledLocalRevision == this.session.Log.Revision)
        { this.ApplyWindowSize(); return; }
        this.nextSnapshot = Environment.TickCount64 + 250;
        this.sampledBook = bookId;
        this.sampledLocalRevision = this.session.Log.Revision;
        this.syncFrame = this.FrameForDraw(SyncGate.Panel);
        var scope = this.GlanceScope();
        if (!string.Equals(scope, this.glanceKey, StringComparison.Ordinal))
        {
            this.glances.Clear();
            this.marksFrame.Clear();
            this.overnightCache.Clear();
            this.sharedEntryCache.Clear();
            this.titleCache.Clear();
            this.nestedCache.Clear();
            this.glanceKey = scope;
        }

        this.ApplyWindowSize();
    }

    private string ChannelScope() =>
        string.Join(',', ChatChannels.All.Where(option => this.session.ChannelOn(option.Channel)).Select(option => option.Channel));

    private string GlanceScope()
    {
        var book = SyncGate.Panel;
        var viewed = book is null ? "" : string.Join(',', book.Worlds.Viewing());
        var enabled = book is null ? "" : string.Join(',', book.Worlds.Selectable());
        var resets = string.Join(',', this.VisibleResets().Order(StringComparer.Ordinal));
        return string.Join(
            '|',
            this.session.Log.Revision,
            string.Join(',', this.session.PinnedSync.Order(StringComparer.Ordinal)),
            SyncDisplay.Stamp(this.syncFrame),
            this.eventSearch.Trim(),
            this.session.ShowLocalAccepted,
            this.session.ShowLocalUnaccepted,
            this.session.ShowHidden,
            this.session.ShowSyncAccepted,
            this.session.ShowSyncUnaccepted,
            this.session.PendingScope,
            this.session.ShowResets,
            this.session.ShowPastLocal,
            this.session.ShowPastSync,
            this.ChannelScope(),
            DateTime.Now.ToString("yyyyMMddHHmm", System.Globalization.CultureInfo.InvariantCulture),
            this.session.CactpotRegion,
            resets,
            this.session.CurrentWorld,
            viewed,
            enabled,
            this.session.Year,
            this.session.Month,
            this.weekStart, this.CompactMode, this.session.Appearance.Layout, this.session.Appearance.Overnight);
    }

    private void ApplyWindowSize()
    {
        var min = this.CompactMode ? new Vector2(620f, 380f) : this.session.LightCalendar ? new Vector2(780f, 520f) : new Vector2(1180f, 820f);
        if (this.appliedMin == min)
            return;
        this.appliedMin = min;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = min,
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    private IReadOnlyList<ScheduleOccurrence> Marks(int year, int month)
    {
        if (this.marksFrame.TryGetValue((year, month), out var found))
            return found;
        var list = GameSchedule.InMonth(year, month, TimeZoneInfo.Local, this.session.CactpotRegion, this.VisibleResets());
        this.marksFrame[(year, month)] = list;
        return list;
    }

    private List<DayLine> CachedGlance(DateOnly date)
    {
        if (this.glances.TryGetValue(date, out var found))
            return found;
        var built = this.GlanceFor(date);
        this.glances[date] = built;
        return built;
    }

    private void DrawDayFlat(DateOnly shown, float width, float height, bool showSpanChips, bool outside)
    {
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var isToday = shown == today;
        var bg = isToday
            ? this.session.TodayColor
            : outside
                ? this.session.OutsideColor
                : new Vector4(0f, 0f, 0f, 0.18f);
        draw.AddRectFilled(origin, origin + new Vector2(width, height), ImGui.ColorConvertFloat4ToU32(bg));
        var lineHeight = ImGui.GetTextLineHeight();
        var dayPos = origin + new Vector2(6f, 4f);
        var dayColor = outside || isToday ? ImGui.ColorConvertFloat4ToU32(this.session.Appearance.Ink(outside ? "Other month" : "Today", bg)) : ImGui.GetColorU32(ImGuiCol.Text);
        draw.AddText(dayPos, dayColor, shown.Day.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var dayMax = dayPos + new Vector2(lineHeight * 2f, lineHeight);
        if (ImGui.IsMouseHoveringRect(dayPos, dayMax) && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            this.OpenDay(shown);

        var step = this.SpanRowStep();
        this.spanOriginY[shown] = dayPos.Y + lineHeight + 4f;
        var lines = this.CachedGlance(shown);
        var slots = showSpanChips ? 0 : (this.spanSlots.TryGetValue(shown, out var reserved) ? reserved : 0);
        var y = this.spanOriginY[shown] + (slots * step);
        var bottom = origin.Y + height - 4f;
        var textWidth = MathF.Max(8f, width - 12f);
        var overflowY = bottom - lineHeight;
        if (lines.Count(line => this.ShowChip(line, showSpanChips)) * step > bottom - y) bottom = overflowY - 2f;
        var hidden = 0;
        if (this.session.Appearance.Layout == CalendarLayout.Nested)
            hidden = this.DrawNestedDay(shown, showSpanChips, origin.X + 4f, ref y, bottom, textWidth, step);
        else
        {
            var shownCount = 0;
            var total = 0;
            foreach (var line in lines)
            {
                if (!this.ShowChip(line, showSpanChips))
                    continue;
                total++;
                if (y + step > bottom)
                    continue;
                this.DrawFlatChip(draw, origin.X + 4f, y, textWidth, step - 3f, line);
                y += step;
                shownCount++;
            }

            hidden = total - shownCount;
        }
        if (hidden > 0)
        {
            var morePos = new Vector2(origin.X + 4f, MathF.Max(origin.Y + lineHeight, MathF.Min(y + 2f, overflowY)));
            draw.AddText(morePos, ImGui.GetColorU32(ImGuiCol.TextDisabled), $"+{hidden}");
            var moreMax = morePos + new Vector2(lineHeight * 2f, lineHeight);
            if (ImGui.IsMouseHoveringRect(morePos, moreMax) && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                this.OpenDay(shown);
        }

        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(new Vector2(width, height));
    }

    private void DrawFlatChip(ImDrawListPtr draw, float x, float y, float width, float height, DayLine line)
    {
        var (fill, ink) = this.Ink(line);
        var min = new Vector2(x, y);
        var max = min + new Vector2(width, height);
        this.PaintEventFill(draw, min, max, fill);
        this.NoteNightAnchor(line, min, max, fill);
        var when = this.ClockLabel(line);
        draw.PushClipRect(min, max, true);
        this.DrawScaledText(draw, new Vector2(min.X + 3f, min.Y + 1f), ImGui.ColorConvertFloat4ToU32(ink), PinMark(line) + when + line.Title);
        draw.PopClipRect();
        if (!ImGui.IsMouseHoveringRect(min, max))
            return;
        ImGui.SetTooltip(this.session.LightCalendar ? $"{when}{line.Title}" : $"{when}{line.Title}\n{line.Detail}");
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            this.SelectLine(line);
    }

    private void DrawDay(DateOnly? date, float width, float height, bool showSpanChips, bool scrollChips = false, bool outside = false)
    {
        if (date is not DateOnly shown)
        {
            ImGui.Dummy(new Vector2(width, height));
            return;
        }

        if (this.session.LightCalendar)
        {
            this.DrawDayFlat(shown, width, height, showSpanChips, outside);
            return;
        }

        var dayId = $"day-{shown:yyyy-MM-dd}";
        var today = DateOnly.FromDateTime(DateTime.Now);
        var isToday = shown == today;
        if (isToday)
            ImGui.PushStyleColor(ImGuiCol.ChildBg, this.session.TodayColor);
        else if (outside)
            ImGui.PushStyleColor(ImGuiCol.ChildBg, this.session.OutsideColor);
        var flags = scrollChips ? ImGuiWindowFlags.AlwaysVerticalScrollbar : ImGuiWindowFlags.None;
        ImGui.BeginChild(dayId, new Vector2(width, height), true, flags);
        this.UseTextScale();
        if (outside)
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        if (ImGui.SmallButton($"{shown.Day}##open-{shown:yyyy-MM-dd}"))
            this.OpenDay(shown);
        if (outside)
            ImGui.PopStyleColor();

        var step = this.SpanRowStep();
        this.spanOriginY[shown] = ImGui.GetCursorScreenPos().Y;
        var lines = this.CachedGlance(shown);
        var chips = lines.Where(line => this.ShowChip(line, showSpanChips)).ToList();
        var slots = showSpanChips ? 0 : (this.spanSlots.TryGetValue(shown, out var reserved) ? reserved : 0);
        var spacing = ImGui.GetStyle().ItemSpacing;
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(spacing.X, 1f));
        if (slots > 0)
            ImGui.Dummy(new Vector2(1f, slots * step));
        var reserve = scrollChips ? 0f : ImGui.GetFrameHeightWithSpacing() + 4f;
        var bottom = ImGui.GetWindowContentRegionMax().Y - reserve;
        var hidden = 0;
        if (this.session.Appearance.Layout == CalendarLayout.Nested)
        {
            var origin = ImGui.GetCursorScreenPos();
            var y = origin.Y;
            var end = scrollChips ? y + chips.Count * step : y + Math.Max(0, bottom - ImGui.GetCursorPosY());
            hidden = this.DrawNestedDay(shown, showSpanChips, origin.X, ref y, end, ImGui.GetContentRegionAvail().X, step);
            ImGui.Dummy(new Vector2(1, Math.Max(1, y - origin.Y)));
        }
        else
        {
            var shownCount = 0;
            foreach (var line in chips)
            {
                if (!scrollChips && ImGui.GetCursorPosY() + step > bottom)
                    break;
                this.DrawColorBlock(line);
                shownCount++;
            }

            hidden = chips.Count - shownCount;
        }
        ImGui.PopStyleVar();
        if (hidden > 0 && ImGui.SmallButton($"+{hidden}##more-{shown:yyyy-MM-dd}"))
            this.OpenDay(shown);

        ImGui.EndChild();
        if (isToday || outside)
            ImGui.PopStyleColor();
    }

    private void DrawColorBlock(DayLine line)
    {
        var (fill, ink) = this.Ink(line);
        var width = MathF.Max(8f, ImGui.GetContentRegionAvail().X);
        var step = this.SpanRowStep();
        var height = step - 2f;
        var pos = ImGui.GetCursorScreenPos();
        var max = pos + new Vector2(width, height);
        var draw = ImGui.GetWindowDrawList();
        this.PaintEventFill(draw, pos, max, fill);
        this.NoteNightAnchor(line, pos, max, fill);
        var when = this.ClockLabel(line);
        var label = this.Fit(PinMark(line) + when + line.Title, width - 8f);
        draw.PushClipRect(pos, max, true);
        this.DrawScaledText(draw, new Vector2(pos.X + 4f, pos.Y + ((height - ImGui.GetTextLineHeight()) * 0.5f)), ImGui.ColorConvertFloat4ToU32(ink), label);
        draw.PopClipRect();
        ImGui.Dummy(new Vector2(width, step));
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip($"{when}{line.Title}\n{line.Detail}");
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                this.SelectLine(line);
        }
    }

    private void DrawDayFolder()
    {
        if (this.folderDay is not int day)
            return;
        var days = DateTime.DaysInMonth(this.session.Year, this.session.Month);
        if (day < 1 || day > days)
        {
            this.folderDay = null;
            return;
        }

        var open = true;
        var date = new DateOnly(this.session.Year, this.session.Month, day);
        ImGui.SetNextWindowSize(new Vector2(520, 560), ImGuiCond.FirstUseEver);
        if (this.focusFolder)
        {
            ImGui.SetNextWindowCollapsed(false, ImGuiCond.Always);
            ImGui.SetNextWindowFocus();
        }

        if (!ImGui.Begin($"{date:dddd d MMMM}###day-folder", ref open))
        {
            ImGui.End();
            return;
        }

        this.focusFolder = false;
        this.UseTextScale();

        if (!open)
            this.folderDay = null;

        foreach (var line in this.CachedGlance(date))
            this.DrawFolderRow(line);

        ImGui.End();
    }

    private void DrawFolderRow(DayLine line)
    {
        var when = this.ClockLabel(line);
        var id = line.SyncId ?? line.Entry?.Id ?? $"{when}{line.Title}";
        var expanded = ImGui.CollapsingHeader($"{PinMark(line)}{when}{line.Title}##folder-{id}");
        if (!expanded)
        {
            var facts = this.session.Appearance.Lists == InviteListStyle.Tree ? "" : this.FolderFacts(line);
            if (facts.Length > 0)
                ImGui.TextDisabled(facts);
            return;
        }

        if (line.SyncId is not null)
        {
            this.DrawSyncDetail(line, foldMessage: true);
            this.DrawMessageFold(id, line.Detail);
            return;
        }

        if (line.Members is { Count: > 1 })
        {
            foreach (var member in line.Members)
            {
                ImGui.TextUnformatted(member.Title);
                if (!string.IsNullOrWhiteSpace(member.Detail))
                    ImGui.TextWrapped(member.Detail);
            }

            return;
        }

        if (line.Entry is not CalendarEntry entry)
        {
            this.DrawPins(line.Detail, line.Title);
            return;
        }

        this.DrawInviteGlance(entry);
        this.DrawMessageFold(id, entry.EventText);
        if (ImGui.SmallButton($"Edit##folder-{entry.Id}"))
            this.BeginEdit(entry);
        ImGui.SameLine();
        if (!entry.Accepted)
        {
            if (ImGui.SmallButton($"Accept##folder-{entry.Id}") && this.session.Log.Accept(entry.Id))
            {
                if (SyncGate.Panel is SyncBook synced)
                    synced.AcceptSame(entry, synced.Worlds.Home);
                this.save();
            }

            ImGui.SameLine();
            if (entry.Hidden)
            {
                if (ImGui.SmallButton($"Unhide##folder-{entry.Id}") && this.session.Log.SetHidden(entry.Id, false))
                    this.save();
            }
            else if (ImGui.SmallButton($"Hide##folder-{entry.Id}") && this.session.Log.SetHidden(entry.Id, true))
            {
                this.save();
            }

            ImGui.SameLine();
        }

        if (ImGui.SmallButton($"{(entry.Pinned ? "Unpin" : "Pin")}##folder-pin-{entry.Id}") && this.session.Log.SetPinned(entry.Id, !entry.Pinned))
            this.save();
        ImGui.SameLine();
        this.DrawDelete(entry, entry.Date, $"folder-{entry.Id}");
        if (this.editingId == entry.Id)
            this.DrawEdit(entry);
    }

    private string FolderFacts(DayLine line)
    {
        if (line.Entry is CalendarEntry entry)
        {
            var spot = HousingTravel.FindVenue(entry.Place, entry.EventText, entry.Ward, entry.Server, entry.SpeakerWorld);
            if (spot is HousingSpot housing)
                return housing.Label;
            var (name, world) = SenderName.TellTarget(entry.Sender, entry.SpeakerWorld);
            var from = name.Length == 0 ? "" : world.Length > 0 ? $"{name} @ {world}" : name;
            return string.Join(" · ", new[] { entry.Place, from }.Where(part => !string.IsNullOrWhiteSpace(part)));
        }

        if (line.SyncId is null)
            return "";
        var shared = HousingTravel.FindVenue(null, line.Detail, null, null, null);
        return shared is HousingSpot found ? found.Label : "";
    }

    private List<DayLine> LinesFor(DateOnly date)
    {
        var lines = new List<DayLine>();
        foreach (var entry in this.session.Log.Entries)
        {
            if (!this.IncludeLocal(entry))
                continue;
            var occurrence = SyncClock.OnDate(entry, date)
                ?? (date > DateOnly.MinValue ? SyncClock.OnDate(entry, date.AddDays(-1)) : null);
            if (occurrence is null) continue;
            var (startDay, startClock, endClock) = Overnight.Shown(occurrence, TimeZoneInfo.Local);
            var overnight = Overnight.Is(startClock, endClock);
            if (!Overnight.Covers(date, startDay, startClock, endClock)) continue;

            var named = EventTitle.Readable(EventTitle.Choose(entry.EventText));
            var title = (LiveInvite.IsRecent(entry.EventText, entry.DetectedAt, DateTimeOffset.UtcNow) ? "Now · " : "") + (named.Length > 0 ? named : string.IsNullOrWhiteSpace(entry.Place) ? entry.EventText : entry.Place);
            var clock = date == startDay ? startClock : overnight ? TimeOnly.MinValue : endClock;
            lines.Add(new DayLine(clock, overnight ? 0 : clock is null ? 2 : 1, title, entry.EventText, occurrence, null, overnight, OnDay: date, EndAt: endClock, Pinned: entry.Pinned));
        }

        this.AddSyncLines(date, lines);

        foreach (var mark in this.Marks(date.Year, date.Month))
        {
            if (date < mark.StartDate || date > mark.EndDate)
                continue;
            TimeOnly? time = null;
            if (mark.StartDate == mark.EndDate || date == mark.StartDate)
                time = TimeOnly.FromDateTime(mark.LocalStart);
            else if (date == mark.EndDate)
                time = TimeOnly.FromDateTime(mark.LocalEnd);
            var span = mark.StartDate != mark.EndDate;
            var title = time is TimeOnly ? mark.Chip : mark.Chip;
            lines.Add(new DayLine(time, span ? 0 : 1, title, mark.Name + ". " + mark.Detail, null, mark.Tone, span));
        }

        if (this.eventSearch.Trim().Length > 0)
        {
            lines.RemoveAll(line => !this.MatchesSearch(
                line.Title,
                line.Detail,
                line.Entry?.EventText,
                line.Entry?.Sender,
                line.Entry?.Place,
                line.Entry?.Server,
                line.Entry?.SpeakerWorld));
        }

        lines.Sort(static (left, right) => DaySort.Compare(left.Pinned, left.Rank, left.Time, left.Title, right.Pinned, right.Rank, right.Time, right.Title));
        return lines;
    }

    private List<DayLine> GlanceFor(DateOnly date)
    {
        var lines = this.LinesFor(date);
        var glance = new List<DayLine>();
        glance.AddRange(lines.Where(line => line.Entry is not null || line.Span || line.SyncId is not null));
        foreach (var group in lines.Where(line => line.Entry is null && line.SyncId is null && !line.Span).GroupBy(line => line.Time))
        {
            var items = group.OrderBy(line => line.Title, StringComparer.OrdinalIgnoreCase).ToList();
            if (items.Count == 1)
            {
                glance.Add(items[0]);
                continue;
            }

            glance.Add(new DayLine(
                group.Key,
                1,
                string.Join(", ", items.Select(line => line.Title)),
                "",
                null,
                items[0].Tone,
                false,
                items));
        }

        glance.Sort(static (left, right) => DaySort.Compare(left.Pinned, left.Rank, left.Time, left.Title, right.Pinned, right.Rank, right.Time, right.Title));
        return glance;
    }

    private (Vector4 Fill, Vector4 Ink) Ink(DayLine line)
    {
        Vector4 fill;
        Vector4 ink;
        var kind = EventKind.Find(line.Entry?.EventText ?? line.Detail);
        if (line.Entry?.Color is Vector4 custom)
        {
            fill = custom;
            ink = AutomaticInk(custom);
        }
        else if (line.ColorToken == "sync-pending")
        {
            fill = this.session.SyncPendingColor;
            ink = this.session.Appearance.Ink("Sync pending", fill);
        }
        else if (line.Tone is ResetTone tone)
        {
            return this.Tone(tone);
        }
        else if (line.Entry is { Accepted: true } || line.ColorToken == "accepted")
        {
            fill = kind is EventKind branded ? this.KindFill(branded) : this.session.AcceptedColor;
            ink = this.session.Appearance.Ink(kind?.Name ?? "Accepted", fill);
        }
        else
        {
            fill = this.session.PendingColor;
            ink = this.session.Appearance.Ink("Pending", fill);
        }

        var accepted = line.Entry is { Accepted: true } || line.ColorToken == "accepted";
        if (this.Ended(line) && (accepted || line.ColorToken == "sync-pending"))
            fill = PastTone.Grey(fill);
        return (fill, ink);
    }

    private bool Ended(DayLine line)
    {
        if (line.Entry is CalendarEntry entry)
        {
            var range = ZoneClock.ShownRange(entry, TimeZoneInfo.Local);
            return PastTone.Ended(range.Date, range.Start, range.End, DateTime.Now);
        }
        return PastTone.Ended(line.OnDay, line.Time, line.EndAt, DateTime.Now);
    }

    private void DrawColors()
    {
        ImGui.TextWrapped("Select a swatch to edit its color and text contrast. Presets preserve your palette and contrast choices.");
        ImGui.Separator();
        ImGui.TextDisabled("Calendar");
        this.DrawColor("Pending", this.session.PendingColor, color => this.session.PendingColor = color);
        this.DrawColor("Accepted", this.session.AcceptedColor, color => this.session.AcceptedColor = color);
        this.DrawColor("Today", this.session.TodayColor, color => this.session.TodayColor = color);
        this.DrawColor("Other month", this.session.OutsideColor, color => this.session.OutsideColor = color);
        ImGui.Separator();
        ImGui.TextDisabled("Resets");
        this.DrawColor("Reset", this.session.CrystalColor, color => this.session.CrystalColor = color);
        this.DrawColor("Cactpot", this.session.CactusColor, color => this.session.CactusColor = color);
        this.DrawColor("Limited event", this.session.EventColor, color => this.session.EventColor = color);
        if (SyncGate.Panel is not null)
        {
            ImGui.Separator();
            ImGui.TextDisabled("Sync");
            this.DrawColor("Sync pending", this.session.SyncPendingColor, color => this.session.SyncPendingColor = color);
            this.DrawColor("Shared bar", this.session.SharedBarColor, color => this.session.SharedBarColor = color);
        }

        ImGui.Separator();
        ImGui.TextDisabled("Detected");
        this.DrawColor("Twitch", this.session.TwitchColor, color => this.session.TwitchColor = color);
        this.DrawColor("Discord", this.session.DiscordColor, color => this.session.DiscordColor = color);
    }

    private static Vector4 AutomaticInk(Vector4 color) =>
        color.X + color.Y + color.Z > 1.8f ? new Vector4(0.12f, 0.08f, 0.02f, 1f) : new Vector4(1f, 1f, 1f, 1f);

    private void DrawColor(string label, Vector4 color, Action<Vector4> set)
    {
        ImGui.PushID("color-" + label);
        if (ImGui.ColorButton("##swatch", color, ImGuiColorEditFlags.AlphaPreviewHalf, new Vector2(28, 20))) ImGui.OpenPopup("Edit color");
        ImGui.SameLine();
        if (ImGui.Selectable(label, false)) ImGui.OpenPopup("Edit color");
        if (ImGui.BeginPopup("Edit color"))
        {
            ImGui.TextUnformatted(label);
            ImGui.SetNextItemWidth(220);
            if (ImGui.ColorPicker4("##picker", ref color, ImGuiColorEditFlags.PickerHueWheel | ImGuiColorEditFlags.AlphaBar))
            { set(color); this.save(); }
            var mode = this.session.Appearance.Text.GetValueOrDefault(label);
            if (this.AppearanceChoice("Text contrast", (int)mode, ["Auto", "Light", "Dark"], out var selected))
                this.session.Appearance.Text[label] = (TextContrast)selected;
            ImGui.TextColored(this.session.Appearance.Ink(label, color), "Sample text · 21:00");
            ImGui.EndPopup();
        }
        ImGui.PopID();
    }

    private void DrawEvent(CalendarEntry entry, DateTimeOffset now)
    {
        var title = this.TitleOf(entry.EventText);
        var who = string.IsNullOrWhiteSpace(entry.Sender) ? "" : entry.Sender;
        var head = title.Length > 0 ? title : who.Length > 0 ? who : "Invite";
        var label = OngoingCheck.IsOngoing(entry, now)
            ? $"ongoing · {head}"
            : head;
        var facts = this.InviteFacts(entry);
        var place = entry.Place ?? "";
        var wrap = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        var textHeight = ImGui.CalcTextSize(label, false, wrap).Y
            + ImGui.CalcTextSize(facts, false, wrap).Y
            + (place.Length > 0 ? ImGui.CalcTextSize(place, false, wrap).Y : 0f);
        var pos = ImGui.GetCursorScreenPos();
        var max = pos + new Vector2(wrap, textHeight + 4f);
        var draw = ImGui.GetWindowDrawList();
        var kind = EventKind.Find(entry.EventText);
        var fill = entry.Color
            ?? (entry.Accepted && kind is EventKind branded
                ? this.KindFill(branded)
                : entry.Accepted ? this.session.AcceptedColor : this.session.PendingColor);
        var span = ZoneClock.ShownRange(entry, TimeZoneInfo.Local);
        if (entry.Accepted && PastTone.Ended(span.Date, span.Start, span.End, now.LocalDateTime))
            fill = PastTone.Grey(fill);
        var ink = entry.Color is Vector4 chosen
            ? AutomaticInk(chosen)
            : entry.Accepted
                ? this.session.Appearance.Ink(kind?.Name ?? "Accepted", fill)
                : this.session.Appearance.Ink("Pending", fill);
        this.PaintEventFill(draw, pos, max, fill);
        ImGui.PushStyleColor(ImGuiCol.Text, ink);
        ImGui.TextWrapped(label);
        ImGui.TextDisabled(facts);
        if (place.Length > 0)
            ImGui.TextWrapped(place);
        ImGui.PopStyleColor();
        this.DrawNote(entry.EventText, title);
        if (ImGui.SmallButton($"Edit##day-{entry.Id}"))
            this.BeginEdit(entry);
        ImGui.SameLine();
        this.DrawDelete(entry, entry.Date, $"day-{entry.Id}");
        if (this.editingId == entry.Id)
            this.DrawEdit(entry);
    }

    private void DrawDelete(CalendarEntry entry, DateOnly? sourceDay, string key, SyncBook? book = null)
    {
        var popup = "Delete occurrence##" + key;
        var day = sourceDay ?? entry.Date;
        if (ImGui.SmallButton("Delete##" + key))
        {
            if (entry.Repeat is null)
            {
                if (book is null) this.RemoveEntry(entry.Id); else book.Dismiss(entry.Id);
                this.selectedLine = null;
            }
            else if (ImGui.GetIO().KeyShift && day is DateOnly selectedDay)
                Delete(DeleteScope.Occurrence, selectedDay);
            else if (ImGui.GetIO().KeyCtrl)
                Delete(DeleteScope.Series, day ?? DateOnly.FromDateTime(DateTime.Today));
            else ImGui.OpenPopup(popup);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Shift + click: delete this occurrence immediately.\nCtrl + click: delete the entire series immediately.\nClick: choose what to delete.");
        if (!ImGui.BeginPopup(popup)) return;
        ImGui.TextUnformatted("Delete from your calendar");
        if (day is DateOnly selected)
        {
            if (ImGui.Selectable($"This occurrence ({selected:yyyy-MM-dd})")) Delete(DeleteScope.Occurrence, selected);
            if (ImGui.Selectable("This and future occurrences")) Delete(DeleteScope.Following, selected);
        }
        if (ImGui.Selectable("Entire series")) Delete(DeleteScope.Series, day ?? DateOnly.FromDateTime(DateTime.Today));
        ImGui.EndPopup();

        void Delete(DeleteScope scope, DateOnly date)
        {
            if (book is null) this.session.Log.Delete(entry.Id, date, scope);
            else book.Delete(entry.Id, date, scope);
            this.selectedLine = null;
            this.save();
        }
    }

    private void RemoveEntry(string id)
    {
        if (!this.session.Log.Remove(id))
            return;
        if (this.editingId == id)
            this.editingId = null;
        this.save();
    }

    private string InviteFacts(CalendarEntry entry)
    {
        var day = entry.Date?.ToString("yyyy-MM-dd") ?? "needs a date";
        if (entry.Repeat is not null)
            day += " · " + entry.Repeat.Label;
        var (whoName, whoWorld) = SenderName.TellTarget(entry.Sender, entry.SpeakerWorld);
        var who = whoName.Length == 0 ? "" : whoWorld.Length > 0 ? $"{whoName} @ {whoWorld}" : whoName;
        return string.Join(" · ", new[] { day, WhenText(entry), who }.Where(part => part.Length > 0));
    }

    private void DrawClockFix(string id, string date, string time, Func<string, string, ClockInputErrors> apply)
    {
        var missing = string.IsNullOrWhiteSpace(time) || time is "date only";
        if (!missing && this.fixId != id)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton($"Set time##fix-{id}"))
            {
                this.fixId = id;
                this.fixErrors = default;
                this.fixDate = date;
                this.fixTime = time;
            }

            return;
        }

        if (this.fixId != id)
        {
            this.fixId = id;
            this.fixErrors = default;
            this.fixDate = date;
            this.fixTime = missing ? "" : time;
        }

        ImGui.SetNextItemWidth(160f);
        ImGui.InputText($"Date##fix-date-{id}", ref this.fixDate, 16);
        FieldError(this.fixErrors.Date);
        ImGui.SetNextItemWidth(200f);
        ImGui.InputText($"Time##fix-time-{id}", ref this.fixTime, 32);
        FieldError(this.fixErrors.Time);
        FieldError(this.fixErrors.Message);
        if (!ImGui.Button($"Save##fix-save-{id}"))
            return;
        this.fixErrors = apply(this.fixDate, this.fixTime);
        if (this.fixErrors.Any) return;
        this.fixId = "";
        this.save();
    }

    private static void FieldError(string? message)
    {
        if (string.IsNullOrEmpty(message)) return;
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.55f, 0.35f, 1f));
        ImGui.TextWrapped(message);
        ImGui.PopStyleColor();
    }

    private static string SyncDay(SyncAnnouncement item) => string.IsNullOrWhiteSpace(item.Date) ? "" :
        ZoneClock.Shown(SyncClock.Entry(item), TimeZoneInfo.Local).Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string SyncWhen(SyncAnnouncement item)
    {
        return WhenText(SyncClock.Entry(item));
    }

    private static string WhenText(CalendarEntry entry)
    {
        var shown = ZoneClock.ShownRange(entry, TimeZoneInfo.Local);
        if (shown.Start is not TimeOnly time)
            return "date only";
        return shown.End is TimeOnly end ? $"{time:HH:mm}-{end:HH:mm}" : time.ToString("HH:mm");
    }

    private static string PinMark(DayLine line) => line.Pinned ? "▲ " : "";

    private string ClockLabel(DayLine line)
    {
        if (line.Entry is CalendarEntry entry)
        {
            var shown = ZoneClock.ShownRange(entry, TimeZoneInfo.Local);
            var startDay = shown.Date;
            if (line.OnDay is DateOnly day && startDay is DateOnly first)
                return DaySort.SliceLabel(day, first, shown.Start ?? entry.Time, shown.End ?? entry.End);
            return DaySort.RangeLabel(shown.Start ?? entry.Time, shown.End ?? entry.End);
        }

        if (line.Span && line.Time == TimeOnly.MinValue && line.EndAt is TimeOnly tail)
            return DaySort.RangeLabel(TimeOnly.MinValue, tail);
        return DaySort.RangeLabel(line.Time, line.EndAt);
    }

    private Vector4 KindFill(EventKind kind) => kind.Name switch
    {
        "Twitch" => this.session.TwitchColor,
        "Discord" => this.session.DiscordColor,
        _ => kind.Color,
    };

    private void DrawClearPrompt()
    {
        if (this.prompt.IsOpen)
            ImGui.OpenPopup("Clear shouts?");

        ImGui.SetNextWindowSize(new Vector2(440f, 240f), ImGuiCond.Always);
        var open = this.prompt.IsOpen;
        if (!ImGui.BeginPopupModal("Clear shouts?", ref open))
        {
            if (!open && this.prompt.IsOpen)
                this.prompt.AnswerNo();
            return;
        }

        IReadOnlyList<string> servers = SyncGate.Panel is SyncBook openBook ? openBook.Worlds.Viewing() : [];
        var home = SyncGate.Panel is SyncBook homeBook ? homeBook.Worlds.Home : "";
        var question = this.prompt.Question(servers, home);
        ImGui.PushTextWrapPos(ImGui.GetCursorPos().X + 400f);
        ImGui.TextWrapped(question);
        ImGui.PopTextWrapPos();
        if (ImGui.Button("Yes"))
        {
            var target = this.prompt.Target;
            this.prompt.AnswerYes(this.session.Log);
            if (SyncGate.Panel is SyncBook cleared && target is ClearTarget.SyncAccepted or ClearTarget.SyncUnaccepted)
                cleared.DismissOpen(target);
            if (SyncGate.Panel is SyncBook pastBook && target is ClearTarget.SyncPast)
                pastBook.DismissPast(DateTime.Now);

            this.save();
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("No"))
        {
            this.prompt.AnswerNo();
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void DrawTopBar()
    {
        var book = SyncGate.Panel;
        var attached = book is not null;
        var status = this.syncStatusBuffer.Update(book, Environment.TickCount64);
        if (status is not null)
            this.DrawSyncPerformance(status);
        if (this.CompactMode)
        {
            if (ImGui.SmallButton(this.compactSidebar ? "Hide panels" : "Panels / settings")) this.compactSidebar = !this.compactSidebar;
            ImGui.SameLine();
            ImGui.TextDisabled("3 days · low-height view");
            ImGui.SameLine();
            if (ImGui.SmallButton("View filters")) ImGui.OpenPopup("Compact view filters");
            if (ImGui.BeginPopup("Compact view filters"))
            {
                this.DrawViewToggle("Local accepted", this.session.ShowLocalAccepted, value => this.session.ShowLocalAccepted = value); ImGui.NewLine();
                this.DrawViewToggle("Local unaccepted", this.session.ShowLocalUnaccepted, value => this.session.ShowLocalUnaccepted = value); ImGui.NewLine();
                this.DrawViewToggle("Resets", this.session.ShowResets, value => this.session.ShowResets = value); ImGui.NewLine();
                if (attached)
                {
                    this.DrawViewToggle("Sync accepted", this.session.ShowSyncAccepted, value => this.session.ShowSyncAccepted = value); ImGui.NewLine();
                    this.DrawViewToggle("Sync unaccepted", this.session.ShowSyncUnaccepted, value => this.session.ShowSyncUnaccepted = value); ImGui.NewLine();
                }
                this.DrawViewToggle("Show hidden", this.session.ShowHidden, value => this.session.ShowHidden = value); ImGui.NewLine();
                if (book is not null) this.DrawViewTree(book);
                ImGui.EndPopup();
            }
            if (book is not null && this.session.Appearance.ShowServerSummary) this.DrawSharedBanner(book);
            return;
        }
        var showResets = this.session.ShowResets;
        var style = ImGui.GetStyle();
        var regionMax = ImGui.GetWindowContentRegionMax().X;
        var y = ImGui.GetCursorPosY();
        var labels = new List<string> { "Local accepted", "Local unaccepted", "Resets" };
        if (attached)
        {
            labels.Add("Sync accepted");
            labels.Add("Sync unaccepted");
        }

        float Box(string label) => ImGui.CalcTextSize(label).X + ImGui.GetFrameHeight() + (style.FramePadding.X * 2f) + style.ItemInnerSpacing.X;
        var toggleWidth = labels.Sum(Box) + (style.ItemSpacing.X * (labels.Count - 1));
        var rightEdge = attached ? regionMax - 292f : regionMax;
        var toggleX = MathF.Max(0f, (rightEdge - toggleWidth) / 2f);

        if (!attached)
        {
            const string localCalendar = "Local Calendar";
            ImGui.SetCursorPos(new Vector2(regionMax - ImGui.CalcTextSize(localCalendar).X, y));
            ImGui.TextUnformatted(localCalendar);
        }
        else
        {
            const float comboWidth = 280f;
            ImGui.SetCursorPos(new Vector2(regionMax - comboWidth, y));
            ImGui.SetNextItemWidth(comboWidth);
            var openNames = string.Join(", ", book!.Worlds.Viewing());
            ImGui.SetNextWindowSize(new Vector2(420f, 520f), ImGuiCond.Appearing);
            if (ImGui.BeginCombo("##server-select", openNames, ImGuiComboFlags.HeightLargest))
            {
                this.DrawViewTree(book);
                ImGui.EndCombo();
            }
        }

        ImGui.SetCursorPos(new Vector2(toggleX, y));
        this.DrawViewToggle("Local accepted", this.session.ShowLocalAccepted, value => this.session.ShowLocalAccepted = value);
        this.DrawViewToggle("Local unaccepted", this.session.ShowLocalUnaccepted, value => this.session.ShowLocalUnaccepted = value);
        this.DrawViewToggle("Resets", showResets, value => this.session.ShowResets = value);
        if (attached)
        {
            this.DrawViewToggle("Sync accepted", this.session.ShowSyncAccepted, value => this.session.ShowSyncAccepted = value);
            this.DrawViewToggle("Sync unaccepted", this.session.ShowSyncUnaccepted, value => this.session.ShowSyncUnaccepted = value);
        }

        this.DrawViewToggle("Show hidden", this.session.ShowHidden, value => this.session.ShowHidden = value);

        ImGui.NewLine();
        if (!this.CompactMode) ImGui.Dummy(new Vector2(1f, 4f));
        if (book is not null && this.session.Appearance.ShowServerSummary) this.DrawSharedBanner(book);
    }

    private static bool IsSyncLimited(string status) =>
        status.StartsWith("Sync limited", StringComparison.Ordinal)
        || status.StartsWith("Limited", StringComparison.Ordinal);

    private void DrawSyncPerformance(SyncStatusView status)
    {
        if (status.RelayStatus == RelayReach.OutOfDate)
        {
            ImGui.TextColored(new Vector4(0.95f, 0.55f, 0.22f, 1f), "Sync: Out of date. Update Shout Calendar and Shout Calendar Sync.");
            return;
        }
        var now = DateTimeOffset.UtcNow;
        var progress = status.Progress;
        var color = progress?.IsStale(now) == true ? new Vector4(0.8f, 0.72f, 0.48f, 1)
            : progress?.Phase switch
            {
                SyncPhase.Current => new Vector4(0.48f, 0.72f, 0.53f, 1),
                SyncPhase.CatchingUp or SyncPhase.Waiting => new Vector4(0.82f, 0.73f, 0.44f, 1),
                SyncPhase.Retry or SyncPhase.Upgrade => new Vector4(0.90f, 0.51f, 0.40f, 1),
                _ => new Vector4(0.53f, 0.70f, 0.85f, 1),
            };
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextWrapped("Sync: " + (progress?.Summary(now) ?? "Connecting · waiting for first check"));
        ImGui.PopStyleColor();
        if (progress is not null)
        {
            ImGui.TextDisabled(progress.LastSuccessSummary(now));
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"{progress.Detail}\nDownloaded {progress.DownloadBytes / 1000.0:F1} KB · uploaded {progress.UploadBytes / 1000.0:F1} KB\nApplied counts new invitations and updates. Zero changes is normal when nothing changed.\nHidden/past filters and personal deletions can keep fetched invitations off your calendar. Use Sync > Restore deleted shared invites to undo deletions.");
        }
    }

    private void DrawDropPast()
    {
        var showLocal = this.session.ShowPastLocal;
        if (ImGui.Checkbox("Show past local events##show-past-local", ref showLocal))
        {
            this.session.ShowPastLocal = showLocal;
            this.save();
        }

        if (SyncGate.Panel is not null)
        {
            var showSync = this.session.ShowPastSync;
            if (ImGui.Checkbox("Show past sync events##show-past-sync", ref showSync))
            {
                this.session.ShowPastSync = showSync;
                this.save();
            }
        }

        var drop = this.session.DropPastEvents;
        if (ImGui.Checkbox("Delete past events##drop-past", ref drop))
        {
            this.session.DropPastEvents = drop;
            this.save();
        }

        ImGui.TextWrapped("Past events stay off the calendar until show past is on. Delete past events removes them from this computer after the end time. A shout with no end time stays until that day is over.");
    }

    private bool MatchesSearch(params string?[] parts)
    {
        var query = EventTitle.SearchKey(this.eventSearch);
        if (query.Length == 0)
            return true;
        foreach (var part in parts)
        {
            if (!string.IsNullOrEmpty(part) && EventTitle.SearchKey(part).Contains(query, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private void DrawKind(string? text)
    {
        if (EventKind.Find(text) is not EventKind kind)
            return;
        ImGui.TextColored(this.KindFill(kind), kind.Name);
    }

    private void DrawNote(string? text, string? title)
    {
        var lines = NoteLayout.Lines(EventTitle.Readable(text));
        if (lines.Count == 0)
            return;
        if (lines.Count == 1 && string.Equals(lines[0], title, StringComparison.Ordinal))
            return;
        foreach (var line in lines)
        {
            if (string.Equals(line, title, StringComparison.Ordinal))
                continue;
            ImGui.TextWrapped(line);
        }
    }

    private void DrawViewToggle(string label, bool value, Action<bool> set)
    {
        if (ImGui.Checkbox($"{label}##view-{label}", ref value))
        {
            set(value);
            this.save();
        }

        ImGui.SameLine();
    }

    private void DrawSyncSettings()
    {
        var book = SyncGate.Panel;
        if (book is null)
            return;

        ImGui.TextWrapped("Public Shout and Yell this calendar keeps are shared. Other chats stay on this computer. Accept adds a shared invite to your calendar. A shout that names a world is shared on that world. Otherwise it is shared on the speaker's home world. Shout and Yell are heard only on the world you are standing on.");
        var syncRight = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        var syncContinued = false;
        this.DrawWrappingButton("Clear sync accepted", syncRight, ref syncContinued, () => this.prompt.Ask(ClearTarget.SyncAccepted));
        this.DrawWrappingButton("Clear sync unaccepted", syncRight, ref syncContinued, () => this.prompt.Ask(ClearTarget.SyncUnaccepted));
        this.DrawWrappingButton("Delete past sync", syncRight, ref syncContinued, () => this.prompt.Ask(ClearTarget.SyncPast));
        if (ImGui.Button("Sync now##sync-now"))
            this.requestSyncNow?.Invoke();
        var displayedStatus = this.syncStatusBuffer.Current?.Status;
        if (!string.IsNullOrWhiteSpace(displayedStatus))
        {
            ImGui.SameLine();
            ImGui.TextUnformatted(displayedStatus);
        }

        var autoAccept = book.Informedaholic;
        if (ImGui.Checkbox("Informedaholic: accept every shared invite##sync-auto", ref autoAccept))
        {
            book.Informedaholic = autoAccept;
            if (autoAccept)
                book.AcceptAllRemote();
        }

        ImGui.TextWrapped("Shared shouts and yells are accepted for you. They still have to be events.");
        var relay = book.RelayChoice == SyncRelays.CustomLabel ? SyncRelays.CustomLabel : SyncRelays.PublicLabel;
        ImGui.SetNextItemWidth(180f);
        if (ImGui.BeginCombo("Relay", relay))
        {
            if (ImGui.Selectable(SyncRelays.PublicLabel, relay == SyncRelays.PublicLabel))
            {
                book.RelayChoice = SyncRelays.PublicLabel;
                book.RelayHost = SyncRelays.PublicHost;
                book.RelayPort = SyncRelays.PublicPort;
            }

            if (ImGui.Selectable(SyncRelays.CustomLabel, relay == SyncRelays.CustomLabel))
            {
                book.RelayChoice = SyncRelays.CustomLabel;
                if (SyncRelays.IsPublic(book.RelayHost, book.RelayPort))
                {
                    book.RelayHost = "";
                    book.RelayPort = 0;
                }
            }

            ImGui.EndCombo();
        }

        if (!string.IsNullOrEmpty(book.RelayStatus))
        {
            ImGui.SameLine();
            ImGui.TextColored(RelayInk(book.RelayStatus), book.RelayStatus);
        }

        if (book.RelayStatus == RelayReach.OutOfDate)
            ImGui.TextWrapped("Update Shout Calendar and Shout Calendar Sync so shared invites stay current.");

        if (!SyncRelays.PublicConfigured && (book.RelayChoice == SyncRelays.PublicLabel || book.BackupRelayChoice == SyncRelays.PublicLabel))
            ImGui.TextWrapped("Public relay is not configured in this build. Install a configured release, or choose your own primary and backup relays.");

        if (book.RelayChoice == SyncRelays.CustomLabel)
        {
            var host = book.RelayHost;
            if (ImGui.InputText("Relay address", ref host, 200))
                book.RelayHost = host.Trim();
            var port = book.RelayPort;
            if (ImGui.InputInt("Relay port", ref port))
                book.RelayPort = port < 1 ? 0 : port;
            ImGui.TextWrapped("Your custom relay is tried first. Public relay is the default backup. Choose Off below to keep a private group on its own relay.");
        }

        if (ImGui.CollapsingHeader("Backup relay · " + book.BackupRelayChoice + "##backup-relay"))
        {
            ImGui.SetNextItemWidth(180f);
            if (ImGui.BeginCombo("Backup", book.BackupRelayChoice))
            {
                foreach (var choice in new[] { SyncRelays.PublicLabel, SyncRelays.CustomLabel, SyncRelays.OffLabel })
                    if (ImGui.Selectable(choice, choice == book.BackupRelayChoice)) book.BackupRelayChoice = choice;
                ImGui.EndCombo();
            }
            if (book.BackupRelayChoice == SyncRelays.CustomLabel)
            {
                var backup = book.BackupRelayHost;
                if (ImGui.InputText("Backup address", ref backup, 200)) book.BackupRelayHost = backup.Trim();
                var port = book.BackupRelayPort;
                if (ImGui.InputInt("Backup port", ref port)) book.BackupRelayPort = Math.Clamp(port, 1, 65535);
            }
            if (book.Backup is not null && book.DistinctBackup is null)
                ImGui.TextWrapped("Primary and backup are the same relay; only one connection is used.");
            ImGui.TextWrapped("On an outage, eligible Shout/Yell invitations are sent to the backup. Public fallback makes those invitations available on the public relay. Choose Off to disable fallback.");
            ImGui.TextWrapped("Requires a separately operated relay with replicated data. Saved invitations remain available offline. A backup cannot recover events it never received.");
        }
        ImGui.Separator();
        if (ImGui.Button("Refresh details##sync-resync")) this.requestSyncResync?.Invoke();
        ImGui.TextWrapped("Checks again for new information. Your accepted, hidden, declined and deleted choices are preserved.");
        if (ImGui.Button("Restore deleted shared invites##sync-restore"))
        {
            book.PrepareDebugResync();
            this.session.DropPastEvents = false;
            this.session.ShowPastSync = true;
            this.save();
            this.requestSyncResync?.Invoke();
        }
        ImGui.TextWrapped("Restore clears shared deletion choices, shows past shared events and turns off automatic past deletion. It can only recover invitations the relay still retains. It does not restore local-only events or override hidden/declined choices.");
        ImGui.Separator();
        ImGui.TextUnformatted("Limits");
        var hold = book.HoldOffSeconds;
        book.HoldOffSeconds = this.Limit("Wait after login (seconds)", hold);
        ImGui.TextWrapped("After Sync loads, wait this long before the relay backlog is applied. Sync now skips the wait and pulls the next batch. It does not bring back invites you cleared.");
        book.Limits.MaxConnections = this.Limit("Connections", book.Limits.MaxConnections);
        book.Limits.BytesPerSecond = this.MegabitsPerSecond(book.Limits.BytesPerSecond, "Download (Mb/s) (Speed cap)");
        book.Limits.UploadBytesPerSecond = this.MegabitsPerSecond(book.Limits.UploadBytesPerSecond, "Upload (Mb/s) (Speed cap)");
        ImGui.TextWrapped("Download and upload pace shared data with a one-second burst. Passes are about ten seconds apart; upload starts at 1 Mb/s and larger backlogs fill over several passes. The buffer budget limits serialized shared data, not all game memory.");
        book.Limits.MaxStoredBytes = this.Megabytes(book.Limits.MaxStoredBytes, "Stored size (MB)");
        book.Limits.MaxItemsPerTick = this.Limit("Invites per sync pass", book.Limits.MaxItemsPerTick);
        ImGui.TextWrapped("This limits new or updated invites applied each pass, not the size of the relay catalog download.");
        book.Limits.MaxMemoryBytes = this.Megabytes(book.Limits.MaxMemoryBytes, "Buffer budget (MB)");
        this.MaybeTipSyncLimits(book);

        if (ImGui.CollapsingHeader("Diagnostics##sync-testing"))
        {
            var debug = book.DebugPerf;
            if (ImGui.Checkbox("Performance log##sync-perf", ref debug))
                book.DebugPerf = debug;
            ImGui.TextWrapped("Off by default. Each pass records catalog invites for your selected worlds, downloaded bytes, applied changes, and invites waiting for later passes. An unchanged cached catalog can download 0 bytes.");
            ImGui.TextWrapped("Stored locally, including in the Dalamud log when enabled. These samples contain counts, timing and transfer sizes, not invitation text. Nothing is sent to an assistant. Zero downloaded bytes can mean an unchanged cached catalog.");
            var perf = book.CopyPerf();
            if (perf.Length > 0)
            {
                if (ImGui.Button("Copy performance log##sync-perf-copy"))
                    ImGui.SetClipboardText(string.Join('\n', perf));
                ImGui.SameLine();
                if (ImGui.Button("Clear##sync-perf-clear"))
                    book.ClearPerf();
                var logHeight = MathF.Min(180f, 8f + (perf.Length * ImGui.GetTextLineHeightWithSpacing()));
                ImGui.BeginChild("sync-perf-log", new Vector2(0f, logHeight), true);
                for (var i = perf.Length - 1; i >= 0; i--)
                    ImGui.TextWrapped(perf[i]);
                ImGui.EndChild();
            }
        }

        ImGui.SetNextItemOpen(true, ImGuiCond.FirstUseEver);
        if (!ImGui.CollapsingHeader("Servers"))
            return;
        ImGui.TextDisabled($"You are on {book.Worlds.Here}. Checked worlds are synced.");
        var height = MathF.Max(120f, ImGui.GetContentRegionAvail().Y - 4f);
        ImGui.BeginChild("sync-worlds", new Vector2(0, height), true);
        this.UseTextScale();
        this.DrawServerTree(book, "sync-world");

        ImGui.EndChild();
    }

    private void DrawServerTree(SyncBook book, string scope)
    {
        foreach (var group in DataCenters.All)
        {
            var centerOn = book.Worlds.DataCenterChecked(group.Name);
            if (ImGui.Checkbox($"##dc-{scope}-{group.Name}", ref centerOn))
                book.Worlds.SetDataCenter(group.Name, centerOn);
            ImGui.SameLine();
            if (group.Worlds.Any(world => world.Equals(book.Worlds.Here, StringComparison.OrdinalIgnoreCase)))
                ImGui.SetNextItemOpen(true, ImGuiCond.Appearing);
            var open = ImGui.TreeNode($"{group.Name}##dc-node-{scope}-{group.Name}");
            if (!open)
                continue;
            ImGui.Indent();
            foreach (var world in group.Worlds)
                this.DrawWorldCheck(book, world, scope);
            ImGui.Unindent();
            ImGui.TreePop();
        }
    }

    private void DrawViewTree(SyncBook book)
    {
        foreach (var group in DataCenters.All)
        {
            var anySynced = group.Worlds.Any(book.Worlds.IsChecked);
            if (!anySynced)
                continue;
            var centerOn = book.Worlds.DataCenterViewed(group.Name);
            if (ImGui.Checkbox($"##view-dc-{group.Name}", ref centerOn))
                book.Worlds.SetViewDataCenter(group.Name, centerOn);
            ImGui.SameLine();
            if (group.Worlds.Any(world => world.Equals(book.Worlds.Here, StringComparison.OrdinalIgnoreCase)))
                ImGui.SetNextItemOpen(true, ImGuiCond.Appearing);
            var open = ImGui.TreeNode($"{group.Name}##view-dc-node-{group.Name}");
            if (!open)
                continue;
            ImGui.Indent();
            foreach (var world in group.Worlds)
            {
                if (!book.Worlds.IsChecked(world))
                    continue;
                var on = book.Worlds.IsViewing(world);
                if (ImGui.Checkbox($"{world}##view-{world}", ref on))
                    book.Worlds.SetViewing(world, on);
            }

            ImGui.Unindent();
            ImGui.TreePop();
        }
    }

    private void DrawWorldCheck(SyncBook book, string world, string scope)
    {
        var home = world == book.Worlds.Home;
        var on = book.Worlds.IsChecked(world);
        if (home)
            ImGui.BeginDisabled();
        if (ImGui.Checkbox($"{world}##{scope}-{world}", ref on) && !home)
            book.Worlds.SetChecked(world, on);
        if (home)
            ImGui.EndDisabled();
    }

    private static Vector4 RelayInk(string status)
    {
        if (status == RelayReach.Online)
            return new Vector4(0.45f, 0.82f, 0.45f, 1f);
        if (status == RelayReach.Degraded)
            return new Vector4(0.95f, 0.55f, 0.22f, 1f);
        if (status == RelayReach.Offline)
            return new Vector4(0.86f, 0.36f, 0.32f, 1f);
        if (status == RelayReach.OutOfDate)
            return new Vector4(0.95f, 0.55f, 0.22f, 1f);
        if (status == RelayReach.ViaPublicRelay)
            return new Vector4(0.45f, 0.72f, 0.95f, 1f);
        return new Vector4(0.90f, 0.78f, 0.35f, 1f);
    }

    private void MaybeTipSyncLimits(SyncBook book)
    {
        var status = book.SyncStatus ?? "";
        if (!IsSyncLimited(status))
            return;
        var key = $"{status}|{book.Limits.MaxConnections}|{book.Limits.BytesPerSecond}|{book.Limits.UploadBytesPerSecond}|{book.Limits.MaxItemsPerTick}|{book.Limits.MaxStoredBytes}|{book.Limits.MaxMemoryBytes}";
        var now = DateTimeOffset.UtcNow;
        if (key == this.syncLimitTipKey
            && this.syncLimitTipAt is DateTimeOffset last
            && now - last < TimeSpan.FromMinutes(5))
            return;
        this.syncLimitTipKey = key;
        this.syncLimitTipAt = now;
        Plugin.ChatGui.Print(SyncFill.ChatTip());
    }

    private int Limit(string label, int value)
    {
        ImGui.SetNextItemWidth(140f);
        if (!ImGui.InputInt($"{label}##sync-cap-{label}", ref value))
            return value;
        return value < 0 ? 0 : value;
    }

    private int Megabytes(int bytes, string label)
    {
        var megabytes = Math.Max(1, bytes / 1_000_000);
        ImGui.SetNextItemWidth(140f);
        if (!ImGui.InputInt($"{label}##sync-mb-{label}", ref megabytes))
            return bytes;
        if (megabytes < 1)
            megabytes = 1;
        return megabytes * 1_000_000;
    }

    private int MegabitsPerSecond(int bytesPerSecond, string label)
    {
        var megabits = Math.Max(1, (int)((long)bytesPerSecond * 8 / 1_000_000));
        ImGui.SetNextItemWidth(140f);
        if (!ImGui.InputInt($"{label}##sync-mbps-{label}", ref megabits))
            return bytesPerSecond;
        if (megabits < 1)
            megabits = 1;
        return megabits * 125_000;
    }

    private string StandingWorld()
    {
        if (PlayableWorlds.TryCanonical(this.session.CurrentWorld, out var current))
            return current;
        if (SyncGate.Panel is SyncBook book && PlayableWorlds.TryCanonical(book.Worlds.Here, out var here))
            return here;
        return "";
    }

    private static string ListedWorld(SyncAnnouncement item) =>
        ServerNames.TryAdvertised(item.Text, out var named) ? named : item.World;

    private bool OnViewedWorld(SyncBook book, SyncAnnouncement item) =>
        book.Worlds.IsViewing(ListedWorld(item));

    private void DrawMessageFold(string id, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        if (!ImGui.CollapsingHeader($"Message##folder-msg-{id}"))
            return;
        this.DrawNote(text, EventTitle.Choose(text));
    }

    private void DrawSyncDetail(DayLine line, bool foldMessage = false)
    {
        var book = SyncGate.Panel;
        var item = book?.Events.FirstOrDefault(row => row.Id == line.SyncId);
        if (item is null)
        {
            this.selectedLine = null;
            return;
        }

        this.DrawInviteGlanceText(item.Text, SyncDay(item), SyncWhen(item), ListedWorld(item), item.Place);
        this.DrawClockFix(
            "sync-" + item.Id,
            SyncDay(item),
            SyncWhen(item),
            (date, time) =>
            {
                book!.SetClock(item.Id, date, time, out var errors);
                return errors;
            });
        ImGui.Separator();
        if (!foldMessage)
        {
            ImGui.TextDisabled("Message");
            this.DrawNote(item.Text, (item.Title.Length > 0 ? item.Title : EventTitle.Choose(item.Text)));
        }
        if ((item.IsSyncPending || item.Hidden) && ImGui.SmallButton($"Accept##sync-detail-{item.Id}"))
            book!.AcceptRemote(item.Id);
        if (item.IsSyncPending)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton($"Save local##sync-detail-save-{item.Id}"))
                this.SaveLocal(book!, item);
        }

        if (item.IsSyncPending || item.Hidden)
            ImGui.SameLine();
        if (item.Hidden)
        {
            if (ImGui.SmallButton($"Unhide##sync-detail-{item.Id}") && book!.HideRemote(item.Id, false))
                this.save();
            ImGui.SameLine();
        }
        else if (item.IsSyncPending)
        {
            if (ImGui.SmallButton($"Hide##sync-detail-{item.Id}") && book!.HideRemote(item.Id, true))
                this.save();
            ImGui.SameLine();
            if (ImGui.SmallButton($"Decline##sync-detail-{item.Id}"))
                book!.DeclineRemote(item.Id);
            ImGui.SameLine();
        }

        if (ImGui.SmallButton($"{(this.session.SyncPinned(item.Id) ? "Unpin" : "Pin")}##sync-pin-{item.Id}")
            && this.session.SetSyncPinned(item.Id, !this.session.SyncPinned(item.Id)))
        {
            this.save();
        }

        ImGui.SameLine();
        var original = SyncClock.Entry(item);
        var occurrence = line.OnDay is DateOnly shown ? SyncClock.OnDate(original, shown) : original;
        this.DrawDelete(original, occurrence?.Date, $"sync-detail-{item.Id}", book);
    }

    private void DrawInviteGlance(CalendarEntry entry)
    {
        var title = EventTitle.Readable(EventTitle.Choose(entry.EventText));
        if (title.Length > 0)
            ImGui.TextUnformatted(title);
        this.DrawKind(entry.EventText);

        if (LiveInvite.IsRecent(entry.EventText, entry.DetectedAt, DateTimeOffset.UtcNow))
            ImGui.TextWrapped("Assembling now · heard " + entry.DetectedAt.LocalDateTime.ToString("HH:mm") + ". No end time was advertised.");
        this.FactLine("Date", this.DateLabel(entry));
        this.FactLine("Time", WhenText(entry));
        if (this.editingId != entry.Id)
        {
            var original = this.session.Log.Entries.FirstOrDefault(saved => saved.Id == entry.Id) ?? entry;
            var clock = ClockInput.FromEntry(original, TimeZoneInfo.Local);
            this.DrawClockFix(
                "local-" + entry.Id,
                clock.DateText,
                clock.TimeText,
                (date, time) =>
                {
                    this.session.Log.TryRevise(original.Id, original.EventText, date, time, original.Place, original.Color,
                        DateTimeOffset.UtcNow, this.session.Places, this.session.Channels, out var errors);
                    return errors;
                });
        }
        var spot = HousingTravel.FindVenue(entry.Place, entry.EventText, entry.Ward, entry.Server, entry.SpeakerWorld);
        var world = spot?.World;
        if (string.IsNullOrWhiteSpace(world))
            world = ShareWorld.Choose("", entry.SpeakerWorld, entry.Server, entry.EventText);
        this.DrawVenue(world, spot, string.IsNullOrWhiteSpace(entry.Place) ? "none found" : entry.Place);
        this.DrawPins(entry.Place + "\n" + entry.EventText, null, entry);
        this.DrawFrom(entry.Sender, entry.SpeakerWorld, entry.Server, EventTitle.Choose(entry.EventText), entry.Id);
        if (LinkFinder.Find(entry.EventText).Count > 0)
        {
            ImGui.TextDisabled("Links");
            this.DrawLinks(entry.EventText);
        }
    }

    private void DrawInviteGlanceText(string text, string date, string time, string world, string sender)
    {
        var title = EventTitle.Readable(EventTitle.Choose(text));
        if (title.Length > 0)
            ImGui.TextUnformatted(title);
        this.DrawKind(text);

        this.FactLine("Date", string.IsNullOrWhiteSpace(date) ? "needs a date" : date);
        this.FactLine("Time", string.IsNullOrWhiteSpace(time) ? "date only" : time);
        var spot = HousingTravel.FindVenue(null, text, null, world, null, world);
        var shownWorld = spot?.World;
        if (string.IsNullOrWhiteSpace(shownWorld))
            shownWorld = world;
        this.DrawVenue(shownWorld, spot, "none found");
        this.DrawPins(text, null, null, world);
        this.DrawFrom(sender, "", world, EventTitle.Choose(text), sender ?? "");
        if (LinkFinder.Find(text).Count > 0)
        {
            ImGui.TextDisabled("Links");
            this.DrawLinks(text);
        }
    }

    private void DrawVenue(string? world, HousingSpot? spot, string fallbackPlace)
    {
        var named = "";
        if (PlayableWorlds.TryNamedWorld(world, out var playable))
            named = playable;
        else if (!string.IsNullOrWhiteSpace(world))
            named = world.Trim();
        if (named.Length > 0)
            this.FactLine("World", named);
        var place = spot is HousingSpot housing ? PlaceOnly(housing) : fallbackPlace;
        this.FactLine("Place", string.IsNullOrWhiteSpace(place) ? "none found" : place);
    }

    private static string PlaceOnly(HousingSpot housing)
    {
        var label = housing.District;
        if (housing.Ward is int ward)
            label += " ward " + ward.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (housing.Plot is int plot)
            label += " plot " + plot.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return label;
    }

    private void DrawFrom(string? sender, string? speakerWorld, string? eventWorld, string? title, string id)
    {
        var (name, world) = SenderName.TellTarget(sender, speakerWorld);
        if (world.Length == 0 && PlayableWorlds.TryNamedWorld(eventWorld, out var named))
            world = named;
        var shown = name.Length == 0 ? "" : world.Length > 0 ? $"{name} @ {world}" : name;
        if (shown.Length == 0)
        {
            this.FactLine("From", "");
            return;
        }

        ImGui.TextDisabled("From");
        ImGui.SameLine();
        if (ImGui.SmallButton($"{shown}##tell-{id}"))
        {
            var filled = Plugin.AskTell(TellDraft.Command(sender, speakerWorld, eventWorld, title));
            this.tellNoticeId = id;
            this.tellNotice = filled
                ? "The tell is in the chat box. Press Enter to send."
                : "Tell copied. Paste it into chat.";
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Put a tell in the chat box. Press Enter to send.");
        if (!string.IsNullOrEmpty(this.tellNotice) && this.tellNoticeId == id)
            ImGui.TextWrapped(this.tellNotice);
    }

    private void FactLine(string name, string value)
    {
        ImGui.TextDisabled(name);
        ImGui.SameLine();
        if (string.IsNullOrWhiteSpace(value) || value is "none found" or "needs a date" or "date only")
            ImGui.TextDisabled(value);
        else
            ImGui.TextWrapped(value);
    }

    private string DateLabel(CalendarEntry entry)
    {
        if (entry.Date is not DateOnly day)
            return "needs a date";
        var label = day.ToString("dddd, MMM d, yyyy");
        if (entry.Repeat is not null)
            label += " · " + entry.Repeat.Label;
        return label;
    }

    private void DrawAddEvent()
    {
        if (!this.adding)
            return;
        ImGui.TextWrapped("This event stays on this computer. A shout or yell is what shares.");
        ImGui.InputTextMultiline("Details##add-note", ref this.addNote, 4000, new Vector2(-1f, 90f));
        if (ImGui.SmallButton("Read details##add-read"))
            this.ReadAddedDetails();
        ImGui.InputText("Date##add-date", ref this.addDate, 16);
        ImGui.InputText("Time##add-time", ref this.addTime, 8);
        ImGui.InputText("Location##add-place", ref this.addPlace, 200);
        if (this.addNotice.Length > 0)
            ImGui.TextWrapped(this.addNotice);
        if (ImGui.SmallButton("Save##add-save") && this.SaveAdded())
            this.adding = false;
        ImGui.SameLine();
        if (ImGui.SmallButton("Cancel##add-cancel"))
            this.adding = false;
        ImGui.Separator();
    }

    private void ReadAddedDetails()
    {
        var parsed = ShoutHarvest.TryHarvest(
            this.addNote,
            ShoutHarvest.ShoutChannel,
            DateTimeOffset.Now,
            this.session.Places,
            this.session.Channels,
            this.session.HousingHint,
            aggressive: false,
            zone: this.session.Zone);
        if (parsed is null)
            return;
        if (parsed.Date is DateOnly day)
            this.addDate = day.ToString("yyyy-MM-dd");
        if (parsed.Time is TimeOnly time)
            this.addTime = time.ToString("HH:mm");
        if (!string.IsNullOrWhiteSpace(parsed.Place))
            this.addPlace = parsed.Place;
    }

    private void FillBlankDetails()
    {
        var parsed = ShoutHarvest.TryHarvest(
            this.addNote,
            ShoutHarvest.ShoutChannel,
            DateTimeOffset.Now,
            this.session.Places,
            this.session.Channels,
            this.session.HousingHint,
            aggressive: false,
            zone: this.session.Zone);
        if (parsed is null)
            return;
        if (this.addDate.Trim().Length == 0 && parsed.Date is DateOnly day)
            this.addDate = day.ToString("yyyy-MM-dd");
        if (this.addTime.Trim().Length == 0 && parsed.Time is TimeOnly time)
            this.addTime = time.ToString("HH:mm");
        if (this.addPlace.Trim().Length == 0 && !string.IsNullOrWhiteSpace(parsed.Place))
            this.addPlace = parsed.Place;
    }

    private bool SaveAdded()
    {
        this.addNotice = "";
        var note = this.addNote.Trim();
        var place = this.addPlace.Trim();
        var dateText = this.addDate.Trim();
        var timeText = this.addTime.Trim();
        if (note.Length == 0 && place.Length == 0 && dateText.Length == 0 && timeText.Length == 0)
        {
            this.addNotice = "Paste the details, or type a date and a time.";
            return false;
        }

        if (dateText.Length == 0 && timeText.Length == 0)
            this.FillBlankDetails();
        dateText = this.addDate.Trim();
        timeText = this.addTime.Trim();
        place = this.addPlace.Trim();
        if (dateText.Length == 0 && timeText.Length == 0)
        {
            this.addNotice = "No date or time was found. Type them, or put them in the details.";
            return false;
        }

        DateOnly? date = null;
        if (dateText.Length > 0)
        {
            if (!DateOnly.TryParseExact(dateText, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var typedDate))
            {
                this.addNotice = "Date uses yyyy-MM-dd.";
                return false;
            }

            date = typedDate;
        }

        TimeOnly? time = null;
        if (timeText.Length > 0)
        {
            if (!TimeOnly.TryParseExact(timeText, "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var typedTime))
            {
                this.addNotice = "Time uses HH:mm.";
                return false;
            }

            time = typedTime;
        }

        var parsed = ShoutHarvest.TryHarvest(
            note,
            ShoutHarvest.ShoutChannel,
            DateTimeOffset.Now,
            this.session.Places,
            this.session.Channels,
            this.session.HousingHint,
            aggressive: false,
            zone: this.session.Zone);
        var entry = new CalendarEntry(
            date,
            time,
            timeText.Length == 0 ? parsed?.End : null,
            parsed?.Ward,
            parsed?.Server,
            place.Length > 0 ? place : parsed?.Place ?? "",
            note.Length > 0 ? note : parsed?.EventText ?? place,
            "",
            true,
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            parsed?.Repeat,
            0,
            false,
            true);
        if (!this.session.Log.Add(entry))
            return false;
        if (date is DateOnly shown)
            this.session.Show(shown);
        this.addNote = "";
        this.addDate = "";
        this.addTime = "";
        this.addPlace = "";
        this.addNotice = "";
        this.save();
        return true;
    }

    private IReadOnlySet<string> VisibleResets() =>
        this.session.ShowResets ? this.session.Resets : EmptyResets;

    private static readonly HashSet<string> EmptyResets = new();

    private bool IncludeLocal(CalendarEntry entry, bool pendingList = false)
    {
        if (entry.SeriesDeleted) return false;
        if (entry.Hidden && !this.session.ShowHidden)
            return false;
        if (!this.session.ShowsChannel(entry.Channel))
            return false;
        if (!this.session.ShowPastLocal && PastEvents.Ended(entry, DateTime.Now))
            return false;
        if (entry.Accepted)
        {
            if (!this.session.ShowLocalAccepted)
                return false;
        }
        else if (!this.session.ShowLocalUnaccepted && !entry.Hidden)
        {
            return false;
        }

        if (SyncGate.Panel is not SyncBook book)
            return !pendingList || this.ShowsPendingWorld(entry.EventText, entry.Server);
        return pendingList ? this.ShowsPendingWorld(entry.EventText, entry.Server) : book.Worlds.ShowsLocal(entry);
    }

    private SyncAnnouncement[] FrameForDraw(SyncBook? book)
    {
        var all = book?.CopyEvents() ?? [];
        if (book is null || all.Length == 0)
            return all;
        var standing = this.StandingWorld();
        var key = string.Join(
            '|',
            this.session.Log.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            SyncDisplay.Stamp(all).ToString(System.Globalization.CultureInfo.InvariantCulture),
            this.session.PendingScope,
            standing,
            this.session.ShowPastSync,
            this.ChannelScope(),
            DateTime.Now.ToString("yyyyMMddHHmm", System.Globalization.CultureInfo.InvariantCulture),
            string.Join(',', book.Worlds.Viewing()), string.Join(',', book.Worlds.Fetched()));
        if (key == this.frameKey)
            return this.framed;
        var kept = new List<SyncAnnouncement>(all.Length);
        foreach (var item in all)
        {
            if (item.SeriesDeleted || item.Declined || this.DuplicateOfLocal(item))
                continue;
            if (!this.session.ShowsChannel(item.Channel == 0 ? ShoutHarvest.ShoutChannel : item.Channel))
                continue;
            if (!this.session.ShowPastSync && PastEvents.Ended(item, DateTime.Now))
                continue;
            var channel = item.Channel == 0 ? ShoutHarvest.ShoutChannel : item.Channel;
            if (!ShoutHarvest.IsSharedEvent(item.Text, channel, DateTimeOffset.Now))
                continue;
            if (!PlayableWorlds.TryCanonical(item.World, out var world))
            {
                kept.Add(item);
                continue;
            }

            if (!book.Worlds.SharesPending(world, true, standing))
                continue;
            if (item.Accepted)
            {
                if (book.Worlds.IsViewing(world))
                    kept.Add(item);
                continue;
            }

            if (book.Worlds.PendingVisible(ListedWorld(item), this.session.PendingScope, standing) || book.Worlds.IsViewing(ListedWorld(item)))
                kept.Add(item);
        }

        this.framed = kept.ToArray();
        this.frameKey = key;
        return this.framed;
    }

    private bool DuplicateOfLocal(SyncAnnouncement item)
    {
        foreach (var entry in this.session.Log.Entries)
        {
            if (EventIdentity.SameRepost(entry, item))
                return true;
        }

        return false;
    }

    private bool ShowsPendingWorld(string? text, string? storedWorld)
    {
        // Pending scope belongs to Sync; a saved scope must not hide locally collected invites while detached.
        if (SyncGate.Panel is not SyncBook book)
            return true;
        return book.Worlds.PendingVisible(NamedWorld(text, storedWorld), this.session.PendingScope, this.StandingWorld());
    }

    private static string NamedWorld(string? text, string? storedWorld)
    {
        if (ServerNames.TryAdvertised(text, out var named))
            return named;
        return PlayableWorlds.TryNamedWorld(storedWorld, out var stored) ? stored : "";
    }

    private bool PendingOnThisCalendar(SyncBook book, SyncAnnouncement item) =>
        book.Worlds.PendingVisible(ListedWorld(item), this.session.PendingScope, this.StandingWorld());

    private bool IncludeSync(SyncAnnouncement item)
    {
        var book = SyncGate.Panel;
        if (book is null || !item.FromSync || item.Declined)
            return false;
        if (PlayableWorlds.TryCanonical(item.World, out var world) && !book.Worlds.SharesPending(world, true, this.StandingWorld()))
            return false;
        if (item.Hidden && !this.session.ShowHidden)
            return false;
        if (item.Accepted)
        {
            if (!this.session.ShowSyncAccepted || !this.OnViewedWorld(book, item))
                return false;
        }
        else if (!this.session.ShowSyncUnaccepted || !book.Worlds.IsViewing(ListedWorld(item)))
        {
            return false;
        }

        var modes = item.Channel == SharePolicy.YellChannel ? book.Settings.Yell : book.Settings.Shout;
        return item.Accepted || modes.Add;
    }

    private void AddSyncLines(DateOnly date, List<DayLine> lines)
    {
        foreach (var item in this.syncFrame)
        {
            if (!this.IncludeSync(item))
                continue;
            var entry = this.SharedEntry(item);
            var occurrence = SyncClock.OnDate(entry, date)
                ?? (date > DateOnly.MinValue ? SyncClock.OnDate(entry, date.AddDays(-1)) : null);
            if (occurrence is null) continue;
            var shown = ZoneClock.ShownRange(occurrence, TimeZoneInfo.Local);
            var day = shown.Date;
            var time = shown.Start;
            var end = shown.End;
            var overnight = Overnight.Is(time, end);
            if (!Overnight.Covers(date, day, time, end)) continue;
            var named = EventTitle.Readable((item.Title.Length > 0 ? item.Title : EventTitle.Choose(item.Text)));
            var title = (LiveInvite.IsRecent(item.Text, item.ObservedAt, DateTimeOffset.UtcNow) ? "Now · " : "") + (named.Length > 0 ? named : item.Text);
            var clockShown = date == day ? time : overnight ? TimeOnly.MinValue : end;
            lines.Add(new DayLine(clockShown, overnight ? 0 : clockShown is null ? 2 : 1, title, item.Text, null, null, overnight, null, item.ColorToken, item.Id, date, end, this.session.SyncPinned(item.Id)));
        }
    }
}
