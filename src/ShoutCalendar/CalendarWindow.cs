using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using ShoutCalendar.Core;

namespace ShoutCalendar;

public sealed class CalendarWindow : Window
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
    private string syncLimitTipKey = "";
    private string? selectedId;
    private string? editingId;
    private string editNote = "";
    private string editDate = "";
    private string editTime = "";
    private string editPlace = "";
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
    private DateOnly weekStart;
    private DateOnly followedDay = DateOnly.FromDateTime(DateTime.Now);

    private readonly Dictionary<DateOnly, float> spanOriginY = new();
    private IReadOnlyDictionary<DateOnly, int> spanSlots = new Dictionary<DateOnly, int>();
    private SyncAnnouncement[] syncFrame = [];
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
        this.save = save;
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

        this.BeginDraw();
        this.UseTextScale();
        this.DrawTopBar();
        this.DrawHistory();
        ImGui.SameLine();
        this.DrawCalendar();
        this.DrawDayFolder();
        this.DrawClearPrompt();
        this.DrawLinkPrompt();
        this.dialogs.Draw();
    }

    private void DrawHistory()
    {
        ImGui.BeginChild("shout-history", new Vector2(340, 0), true);
        this.UseTextScale();
        if (this.session.Log.ExpireUnaccepted(DateTimeOffset.UtcNow, this.session.UnacceptedHoldDays) > 0)
            this.save();

        if (ImGui.BeginTabBar("shout-tabs"))
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

            if (ImGui.BeginTabItem("Colors"))
            {
                this.DrawColors();
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
        var light = this.session.LightCalendar;
        if (ImGui.Checkbox("Lighter calendar##light-calendar", ref light))
        {
            this.session.LightCalendar = light;
            this.ApplyWindowSize();
            this.save();
        }

        ImGui.TextWrapped("Skips a window per day and extra text measuring. Days and invites still open.");
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
        ImGui.Separator();
        ImGui.TextUnformatted("Feedback");
        var version = PluginVersion();
        if (ImGui.Button("Suggest a feature##feedback-feature", new Vector2(-1f, 0f)))
            this.AskFeedback(GitHubFeedback.FeatureUrl(version), GitHubFeedback.FeaturePrompt(version));
        if (ImGui.Button("Report an error##feedback-error", new Vector2(-1f, 0f)))
            this.AskFeedback(GitHubFeedback.ErrorUrl(version), GitHubFeedback.ErrorPrompt(version));
        ImGui.TextWrapped("Opens a GitHub issue with a short form already filled in. Edit it, then submit. A GitHub account is required.");
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
        var showAll = this.session.ShowAllServers;
        if (ImGui.Checkbox("Show all servers##pending-all-servers", ref showAll))
        {
            this.session.ShowAllServers = showAll;
            this.save();
        }

        ImGui.SameLine();
        var newest = this.session.NewestFirst;
        if (ImGui.Checkbox("Newest first##pending-newest", ref newest))
        {
            this.session.NewestFirst = newest;
            this.save();
        }
        if (PlayableWorlds.TryCanonical(this.StandingWorld(), out var only))
        {
            ImGui.TextDisabled(this.session.ShowAllServers
                ? $"Pending invites follow the open calendars, and only servers turned on in Sync. You are on {only}."
                : $"Pending invites are limited to {only}. Show all servers adds the other open calendars turned on in Sync.");
        }
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

        var pending = this.session.Log.Entries.Where(entry => !entry.Accepted && this.IncludeLocal(entry)).ToList();
        if (this.session.NewestFirst)
            pending.Reverse();
        foreach (var entry in pending)
            this.DrawPendingRow(entry);

        this.DrawSharedPending();
    }

    private void HidePending(bool hidden)
    {
        var changed = this.session.Log.SetPendingHidden(hidden) > 0;
        if (SyncGate.Panel is SyncBook book && book.HidePending(hidden) > 0)
            changed = true;
        if (changed)
            this.save();
    }

    private void DrawSharedPending()
    {
        if (SyncGate.Panel is not SyncBook book)
            return;

        if (!this.session.ShowSyncUnaccepted && !this.session.ShowHidden)
            return;
        var rows = this.syncFrame
            .Where(item => item.Accepted ? this.OnViewedWorld(book, item) : this.PendingOnThisCalendar(book, item))
            .Where(item => item.IsSyncPending || (this.session.ShowHidden && item.Hidden && !item.Declined && !item.Accepted))
            .Where(item => item.Accepted || this.ShowsPendingWorld(item.Text, item.World))
            .ToList();
        if (this.session.NewestFirst)
            rows.Reverse();
        if (rows.Count == 0 && book.Informedaholic)
            return;
        this.DrawSharedBanner(book);
        if (rows.Count == 0)
        {
            ImGui.TextDisabled("No shared invites are waiting.");
            return;
        }

        foreach (var item in rows)
            this.DrawSharedRow(item);
    }

    private void DrawSharedBanner(SyncBook book)
    {
        var label = "Shared on " + string.Join(", ", book.Worlds.Viewing());
        var width = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        var size = ImGui.CalcTextSize(label, false, MathF.Max(1f, width - 8f));
        var pos = ImGui.GetCursorScreenPos();
        var height = size.Y + 8f;
        ImGui.GetWindowDrawList().AddRectFilled(
            pos,
            pos + new Vector2(width, height),
            ImGui.ColorConvertFloat4ToU32(this.session.SharedBarColor));
        ImGui.SetCursorScreenPos(pos + new Vector2(4f, 4f));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, 1f));
        ImGui.PushTextWrapPos(pos.X + width - 4f);
        ImGui.TextWrapped(label);
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();
        ImGui.SetCursorScreenPos(new Vector2(pos.X, pos.Y + height + 2f));
    }

    private void DrawPendingRow(CalendarEntry entry)
    {
        if (!this.MatchesSearch(entry.EventText, entry.Sender, entry.Place, entry.Server, entry.SpeakerWorld))
            return;
        var title = EventTitle.Choose(entry.EventText);
        var who = string.IsNullOrWhiteSpace(entry.Sender) ? "" : entry.Sender;
        var rowLabel = title.Length > 0 ? title : who.Length > 0 ? who : "Invite";
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
        var title = EventTitle.Choose(item.Text);
        var heading = title.Length > 0 ? title : item.World;
        var when = string.IsNullOrWhiteSpace(item.Time) ? "" : item.Time.Trim();
        var day = string.IsNullOrWhiteSpace(item.Date) ? "" : item.Date;
        if (ImGui.Selectable($"{heading}##sync-pick-{item.Id}", this.selectedLine?.SyncId == item.Id))
        {
            TimeOnly? clock = TimeOnly.TryParseExact(when, "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed)
                ? parsed
                : null;
            this.SelectLine(new DayLine(clock, clock is null ? 2 : 1, heading, item.Text, null, null, false, ColorToken: item.ColorToken, SyncId: item.Id));
            if (DateOnly.TryParseExact(day, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var shown))
                this.session.Show(shown);
        }

        var facts = string.Join(" · ", new[] { item.World, day, when }.Where(part => part.Length > 0));
        this.DrawPendingFacts(item.Text, facts);
    }

    private void DrawPendingFacts(string? text, string facts)
    {
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
        var day = entry.Date?.ToString("yyyy-MM-dd") ?? "needs a date";
        if (entry.Repeat is not null)
            day += " · " + entry.Repeat.Label;
        var clock = entry.Time is TimeOnly time
            ? entry.End is TimeOnly end ? $"{time:HH:mm}-{end:HH:mm}" : time.ToString("HH:mm")
            : "";
        var where = !string.IsNullOrWhiteSpace(entry.Place) ? entry.Place : entry.Server ?? "";
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

        ImGui.TextWrapped("Say, shout, yell, incoming tells, free company, free company announcements, and novice network start on.");
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
        ImGui.SameLine();
        if (ImGui.Button(this.weekView ? "Month" : "Week"))
        {
            this.weekView = !this.weekView;
            if (this.weekView)
                this.weekStart = SundayOn(DateOnly.FromDateTime(DateTime.Now));
        }

        ImGui.SameLine();
        var light = this.session.LightCalendar;
        if (ImGui.Checkbox("Lighter##light-calendar-bar", ref light))
        {
            this.session.LightCalendar = light;
            this.ApplyWindowSize();
            this.save();
        }

        if (this.weekView)
        {
            this.DrawWeek();
            ImGui.EndChild();
            return;
        }

        this.FollowToday();
        var month = this.session.CurrentMonth();
        if (ImGui.Button("Previous month"))
            month = this.session.Page(-1);
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
                    month = this.session.CurrentMonth();
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
                    month = this.session.CurrentMonth();
                }

                if (year == todayYear && ImGui.IsWindowAppearing())
                    ImGui.SetScrollHereY(0.5f);
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (ImGui.Button("Next month"))
            month = this.session.Page(1);
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
                month = this.session.CurrentMonth();
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
        for (var index = 0; index < month.Cells.Count; index++)
        {
            var column = index % 7;
            var row = index / 7;
            ImGui.SetCursorScreenPos(grid + new Vector2(column * (side + gap), row * (side + gap)));
            var cell = month.Cells[index];
            this.DrawDay(cell.Date, side, side, false, outside: cell.Day is null);
        }

        this.DrawSchedule(month, grid, side, gap, monthLanes);
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
        draw.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(fill));
        if (eventBorder)
            draw.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(0.85f, 0.72f, 0.28f, 1f)));
        var label = this.session.LightCalendar ? chip : this.Fit(chip, max.X - min.X - 8f);
        draw.PushClipRect(min, max, true);
        this.DrawScaledText(draw, new Vector2(min.X + 4f, min.Y + ((bar - ImGui.GetTextLineHeight()) * 0.5f)), ImGui.ColorConvertFloat4ToU32(ink), label);
        draw.PopClipRect();
        if (ImGui.IsMouseHoveringRect(min, max))
        {
            ImGui.SetTooltip(tip);
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                onClick();
        }
    }

    private readonly record struct OvernightBar(string Key, DateOnly Start, DateOnly End, string Label, string Detail, DayLine Line, Vector4 Fill, Vector4 Ink);

    private List<OvernightBar> OvernightSpans(DateOnly from, DateOnly to)
    {
        var list = new List<OvernightBar>();
        foreach (var entry in this.session.Log.Entries)
        {
            if (!this.IncludeLocal(entry))
                continue;
            var (startDay, startClock, endClock) = Overnight.Shown(entry, TimeZoneInfo.Local);
            if (Overnight.Span(startDay, startClock, endClock) is not { } span)
                continue;
            if (span.End < from || span.Start > to)
                continue;
            var named = EventTitle.Choose(entry.EventText);
            var title = named.Length > 0 ? named : string.IsNullOrWhiteSpace(entry.Place) ? entry.EventText : entry.Place;
            var label = startClock is TimeOnly begin && endClock is TimeOnly stop
                ? $"{begin:HH:mm}–{stop:HH:mm} {title}"
                : title;
            if (!this.MatchesSearch(label, entry.EventText, entry.Sender, entry.Place, entry.Server))
                continue;
            var line = new DayLine(startClock, 0, label, entry.EventText, entry, null, true, OnDay: span.Start);
            var (fill, ink) = this.Ink(line);
            list.Add(new OvernightBar("overnight:" + entry.Id, span.Start, span.End, label, entry.EventText, line, fill, ink));
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
        ResetTone.Cactus => (this.session.CactusColor, new Vector4(0.08f, 0.14f, 0.02f, 1f)),
        ResetTone.Event => (this.session.EventColor, new Vector4(0.96f, 0.96f, 0.96f, 1f)),
        _ => (this.session.CrystalColor, new Vector4(1f, 1f, 1f, 1f)),
    };

    private readonly record struct DayLine(TimeOnly? Time, int Rank, string Title, string Detail, CalendarEntry? Entry, ResetTone? Tone, bool Span, IReadOnlyList<DayLine>? Members = null, string? ColorToken = null, string? SyncId = null, DateOnly? OnDay = null);

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
        this.editingId = entry.Id;
        if (entry.Date is DateOnly day)
            this.session.Show(day);
        this.editNote = entry.EventText;
        this.editDate = entry.Date?.ToString("yyyy-MM-dd") ?? "";
        this.editTime = entry.Time?.ToString("HH:mm") ?? "";
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
        ImGui.SetNextItemWidth(MathF.Max(120f, width - picker - 12f));
        ImGui.InputText($"Time##edit-{entry.Id}", ref this.editTime, 32);
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
        if (!ImGui.Button($"Save##edit-{entry.Id}"))
            return;
        this.session.Log.SetColor(entry.Id, this.editHasColor ? this.editColor : null);
        if (!this.session.Log.Revise(entry.Id, this.editNote, this.editDate, this.editTime, this.editPlace, DateTimeOffset.UtcNow, this.session.Places, this.session.Channels))
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

            if (ImGui.SmallButton($"Delete##detail-{entry.Id}"))
            {
                this.RemoveEntry(entry.Id);
                this.selectedLine = null;
            }

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

    private void DrawPins(string text, string? chip, CalendarEntry? entry = null)
    {
        var housing = HousingTravel.Find(entry?.Place, text, entry?.Ward, entry?.Server);
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
        if (ImGui.Button("Previous week"))
            this.weekStart = this.weekStart.AddDays(-7);
        ImGui.SameLine();
        ImGui.TextUnformatted($"{this.weekStart:MMM d} – {this.weekStart.AddDays(6):MMM d}");
        ImGui.SameLine();
        if (ImGui.Button("Next week"))
            this.weekStart = this.weekStart.AddDays(7);
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (today < this.weekStart || today > this.weekStart.AddDays(6))
        {
            ImGui.SameLine();
            if (ImGui.Button("Today"))
            {
                this.weekStart = SundayOn(today);
                this.session.Show(today);
                if (this.folderDay is not null)
                    this.folderDay = today.Day;
            }
        }

        const float handleH = 16f;
        var room = ImGui.GetContentRegionAvail();
        var line = ImGui.GetTextLineHeightWithSpacing();
        var usable = Math.Max(line * 16f, room.Y - handleH);
        var minDetail = line * 6f;
        var minWeek = line * 8f;
        var share = Math.Clamp(this.session.WeekDetailShare, minDetail / usable, (usable - minWeek) / usable);
        var detailH = usable * share;
        var weekH = usable - detailH;

        ImGui.BeginChild("week-grid", new Vector2(0f, weekH), false);
        this.UseTextScale();
        const float gap = 6f;
        var width = MathF.Max(96f, MathF.Floor((ImGui.GetContentRegionAvail().X - (gap * 6f)) / 7f));
        var header = ImGui.GetCursorScreenPos();
        for (var column = 0; column < 7; column++)
        {
            ImGui.SetCursorScreenPos(header + new Vector2(column * (width + gap), 0));
            ImGui.TextUnformatted(new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" }[column]);
        }

        var grid = header + new Vector2(0, ImGui.GetTextLineHeightWithSpacing());
        var height = Math.Max(line * 6f, ImGui.GetContentRegionAvail().Y - ImGui.GetTextLineHeightWithSpacing() - 4f);
        var weekEnd = this.weekStart.AddDays(6);
        var (weekLanes, weekLaneItems) = this.BuildSpanLanes(this.weekStart, weekEnd, this.weekStart.Year, this.weekStart.Month);
        if (weekEnd.Month != this.weekStart.Month || weekEnd.Year != this.weekStart.Year)
        {
            var extra = this.BuildSpanLanes(this.weekStart, weekEnd, weekEnd.Year, weekEnd.Month);
            weekLaneItems = weekLaneItems.Concat(extra.Items).GroupBy(item => item.Key).Select(group => group.First()).ToList();
            weekLanes = GameSchedule.Lanes(weekLaneItems);
        }

        this.spanSlots = GameSchedule.LaneSlotsByDay(weekLanes, weekLaneItems);
        this.spanOriginY.Clear();
        for (var column = 0; column < 7; column++)
        {
            ImGui.SetCursorScreenPos(grid + new Vector2(column * (width + gap), 0));
            this.DrawDay(this.weekStart.AddDays(column), width, height, false, scrollChips: true);
        }

        this.DrawWeekSpans(grid, width, gap, weekLanes);
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
        draw.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(fill));
        const string grip = "...";
        var gripSize = ImGui.CalcTextSize(grip);
        draw.AddText(
            new Vector2(min.X + ((width - gripSize.X) * 0.5f), min.Y + ((16f - gripSize.Y) * 0.5f)),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.9f, 0.9f, 0.9f, 1f)),
            grip);
    }

    private void DrawWeekSpans(Vector2 grid, float width, float gap, IReadOnlyDictionary<string, int> lanes)
    {
        var weekEnd = this.weekStart.AddDays(6);
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
            if (GameSchedule.WeekSegment(this.weekStart, mark.StartDate, mark.EndDate) is not SpanSegment segment)
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
            if (GameSchedule.WeekSegment(this.weekStart, item.Start, item.End) is not SpanSegment segment)
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
        this.syncFrame = this.FrameForDraw(SyncGate.Panel);
        var scope = this.GlanceScope();
        if (!string.Equals(scope, this.glanceKey, StringComparison.Ordinal))
        {
            this.glances.Clear();
            this.marksFrame.Clear();
            this.glanceKey = scope;
        }

        this.ApplyWindowSize();
    }

    private string GlanceScope()
    {
        var book = SyncGate.Panel;
        var viewed = book is null ? "" : string.Join(',', book.Worlds.Viewing());
        var enabled = book is null ? "" : string.Join(',', book.Worlds.Selectable());
        var resets = string.Join(',', this.VisibleResets().Order(StringComparer.Ordinal));
        return string.Join(
            '|',
            this.session.Log.Revision,
            SyncStamp(this.syncFrame),
            this.eventSearch.Trim(),
            this.session.ShowLocalAccepted,
            this.session.ShowLocalUnaccepted,
            this.session.ShowHidden,
            this.session.ShowSyncAccepted,
            this.session.ShowSyncUnaccepted,
            this.session.ShowAllServers,
            this.session.ShowResets,
            this.session.CactpotRegion,
            resets,
            this.session.CurrentWorld,
            viewed,
            enabled,
            this.session.Year,
            this.session.Month);
    }

    private static int SyncStamp(IReadOnlyList<SyncAnnouncement> rows)
    {
        var hash = new HashCode();
        hash.Add(rows.Count);
        foreach (var row in rows)
        {
            hash.Add(row.Id);
            hash.Add(row.Revision);
            hash.Add(row.Accepted);
            hash.Add(row.Hidden);
            hash.Add(row.Declined);
            hash.Add(row.Date);
            hash.Add(row.Text);
        }

        return hash.ToHashCode();
    }

    private void ApplyWindowSize()
    {
        var min = this.session.LightCalendar ? new Vector2(780f, 520f) : new Vector2(1180f, 820f);
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
        ImGui.SetCursorScreenPos(origin + new Vector2(4f, 4f));
        if (outside)
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        if (ImGui.SmallButton($"{shown.Day}##open-{shown:yyyy-MM-dd}"))
            this.OpenDay(shown);
        if (outside)
            ImGui.PopStyleColor();

        var step = this.SpanRowStep();
        this.spanOriginY[shown] = ImGui.GetCursorScreenPos().Y;
        var lines = this.CachedGlance(shown);
        var slots = showSpanChips ? 0 : (this.spanSlots.TryGetValue(shown, out var reserved) ? reserved : 0);
        var y = this.spanOriginY[shown] + (slots * step);
        var bottom = origin.Y + height - 4f;
        var textWidth = MathF.Max(8f, width - 12f);
        var shownCount = 0;
        var total = 0;
        foreach (var line in lines)
        {
            if (!showSpanChips && line.Span)
                continue;
            total++;
            if (y + step > bottom)
                continue;
            this.DrawFlatChip(draw, origin.X + 4f, y, textWidth, step - 2f, line);
            y += step;
            shownCount++;
        }

        var hidden = total - shownCount;
        if (hidden > 0)
        {
            ImGui.SetCursorScreenPos(new Vector2(origin.X + 4f, MathF.Min(y, bottom - ImGui.GetFrameHeight())));
            if (ImGui.SmallButton($"+{hidden}##more-{shown:yyyy-MM-dd}"))
                this.OpenDay(shown);
        }
    }

    private void DrawFlatChip(ImDrawListPtr draw, float x, float y, float width, float height, DayLine line)
    {
        var (fill, ink) = this.Ink(line);
        var min = new Vector2(x, y);
        var max = min + new Vector2(width, height);
        draw.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(fill));
        var when = this.ClockLabel(line);
        draw.PushClipRect(min, max, true);
        this.DrawScaledText(draw, new Vector2(min.X + 3f, min.Y + 1f), ImGui.ColorConvertFloat4ToU32(ink), when + line.Title);
        draw.PopClipRect();
        if (!ImGui.IsMouseHoveringRect(min, max))
            return;
        ImGui.SetTooltip($"{when}{line.Title}\n{line.Detail}");
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
        var chips = lines.Where(line => showSpanChips || !line.Span).ToList();
        var slots = showSpanChips ? 0 : (this.spanSlots.TryGetValue(shown, out var reserved) ? reserved : 0);
        var spacing = ImGui.GetStyle().ItemSpacing;
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(spacing.X, 0f));
        if (slots > 0)
            ImGui.Dummy(new Vector2(1f, slots * step));
        var reserve = scrollChips ? 0f : ImGui.GetFrameHeightWithSpacing() + 4f;
        var bottom = ImGui.GetWindowContentRegionMax().Y - reserve;
        var shownCount = 0;
        foreach (var line in chips)
        {
            if (!scrollChips && ImGui.GetCursorPosY() + step > bottom)
                break;
            this.DrawColorBlock(line);
            shownCount++;
        }

        ImGui.PopStyleVar();

        var hidden = chips.Count - shownCount;
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
        draw.AddRectFilled(pos, max, ImGui.ColorConvertFloat4ToU32(fill));
        var when = this.ClockLabel(line);
        var label = this.Fit(when + line.Title, width - 8f);
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

        ImGui.TextWrapped("Earlier times are listed first. Edit and Delete work for accepted events here.");
        foreach (var line in this.LinesFor(date))
        {
            ImGui.Separator();
            var (fill, ink) = this.Ink(line);
            var pos = ImGui.GetCursorScreenPos();
            var width = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
            var height = ImGui.GetTextLineHeightWithSpacing();
            ImGui.GetWindowDrawList().AddRectFilled(pos, pos + new Vector2(width, height), ImGui.ColorConvertFloat4ToU32(fill));
            ImGui.PushStyleColor(ImGuiCol.Text, ink);
            ImGui.TextUnformatted(line.Time is TimeOnly time ? $"{time:HH:mm}  {line.Title}" : line.Title);
            ImGui.PopStyleColor();
            if (!string.IsNullOrWhiteSpace(line.Detail))
                ImGui.TextWrapped(line.Detail);
            if (line.SyncId is not null)
            {
                this.DrawSyncDetail(line);
                continue;
            }

            if (line.Entry is null)
            {
                this.DrawPins(line.Detail, line.Title);
                continue;
            }

            if (line.Entry is not CalendarEntry entry)
                continue;
            this.DrawInviteGlance(entry);
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

            if (ImGui.SmallButton($"Delete##folder-{entry.Id}"))
                this.RemoveEntry(entry.Id);
            if (this.editingId == entry.Id)
                this.DrawEdit(entry);
        }

        ImGui.End();
    }

    private List<DayLine> LinesFor(DateOnly date)
    {
        var lines = new List<DayLine>();
        foreach (var entry in this.session.Log.Entries)
        {
            if (!this.IncludeLocal(entry))
                continue;
            var labeled = ZoneClock.Labeled(entry.EventText);
            var (startDay, startClock, endClock) = Overnight.Shown(entry, TimeZoneInfo.Local);
            var overnight = Overnight.Is(startClock, endClock);
            if (entry.Repeat is null)
            {
                if (!Overnight.Covers(date, startDay, startClock, endClock))
                    continue;
            }
            else if (!EventRepeat.FallsOn(entry, date)
                && !(overnight && EventRepeat.FallsOn(entry, date.AddDays(-1))))
            {
                continue;
            }

            var named = EventTitle.Choose(entry.EventText);
            var title = named.Length > 0 ? named : string.IsNullOrWhiteSpace(entry.Place) ? entry.EventText : entry.Place;
            if (overnight && startClock is TimeOnly begin && endClock is TimeOnly stop)
                title = $"{begin:HH:mm}–{stop:HH:mm} {title}";
            var clock = date == startDay ? startClock : endClock;
            lines.Add(new DayLine(clock, overnight ? 0 : clock is null ? 2 : 1, title, entry.EventText, entry, null, overnight, OnDay: date));
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

        lines.Sort(static (left, right) =>
        {
            var rank = left.Rank.CompareTo(right.Rank);
            if (rank != 0)
                return rank;
            if (left.Time is TimeOnly leftTime && right.Time is TimeOnly rightTime)
            {
                var clock = leftTime.CompareTo(rightTime);
                if (clock != 0)
                    return clock;
            }

            return string.Compare(left.Title, right.Title, StringComparison.OrdinalIgnoreCase);
        });
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

        glance.Sort(static (left, right) =>
        {
            var rank = left.Rank.CompareTo(right.Rank);
            if (rank != 0)
                return rank;
            if (left.Time is TimeOnly leftTime && right.Time is TimeOnly rightTime)
            {
                var clock = leftTime.CompareTo(rightTime);
                if (clock != 0)
                    return clock;
            }

            return string.Compare(left.Title, right.Title, StringComparison.OrdinalIgnoreCase);
        });
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
            ink = custom.X + custom.Y + custom.Z > 1.8f ? new Vector4(0.12f, 0.08f, 0.02f, 1f) : new Vector4(1f, 1f, 1f, 1f);
        }
        else if (line.ColorToken == "sync-pending")
        {
            fill = this.session.SyncPendingColor;
            ink = new Vector4(1f, 1f, 1f, 1f);
        }
        else if (line.Tone is ResetTone tone)
        {
            return this.Tone(tone);
        }
        else if (line.Entry is { Accepted: true } || line.ColorToken == "accepted")
        {
            fill = kind is EventKind branded ? this.KindFill(branded) : this.session.AcceptedColor;
            ink = new Vector4(1f, 1f, 1f, 1f);
        }
        else
        {
            fill = this.session.PendingColor;
            ink = new Vector4(0.12f, 0.08f, 0.02f, 1f);
        }

        var accepted = line.Entry is { Accepted: true } || line.ColorToken == "accepted";
        if (accepted && this.Ended(line))
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
        if (line.ColorToken != "accepted")
            return false;
        return PastTone.Ended(line.OnDay, line.Time, null, DateTime.Now);
    }

    private void DrawColors()
    {
        ImGui.TextWrapped("Each swatch opens a hue wheel.");
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

    private void DrawColor(string label, Vector4 color, Action<Vector4> set)
    {
        if (!ImGui.ColorEdit4($"{label}##color-{label}", ref color, ImGuiColorEditFlags.PickerHueWheel | ImGuiColorEditFlags.AlphaBar))
            return;
        set(color);
        this.save();
    }

    private void DrawEvent(CalendarEntry entry, DateTimeOffset now)
    {
        var title = EventTitle.Choose(entry.EventText);
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
        var ink = entry.Accepted
            ? new Vector4(1f, 1f, 1f, 1f)
            : new Vector4(0.12f, 0.08f, 0.02f, 1f);
        draw.AddRectFilled(pos, max, ImGui.ColorConvertFloat4ToU32(fill));
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
        if (ImGui.SmallButton($"Delete##day-{entry.Id}"))
            this.RemoveEntry(entry.Id);
        if (this.editingId == entry.Id)
            this.DrawEdit(entry);
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

    private void DrawClockFix(string id, string date, string time, Action<string, string> apply)
    {
        var missing = string.IsNullOrWhiteSpace(time) || time is "date only";
        if (!missing && this.fixId != id)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton($"Set time##fix-{id}"))
            {
                this.fixId = id;
                this.fixDate = date;
                this.fixTime = time;
            }

            return;
        }

        if (this.fixId != id)
        {
            this.fixId = id;
            this.fixDate = date;
            this.fixTime = missing ? "" : time;
        }

        ImGui.SetNextItemWidth(160f);
        ImGui.InputText($"Date##fix-date-{id}", ref this.fixDate, 16);
        ImGui.SetNextItemWidth(200f);
        ImGui.InputText($"Time##fix-time-{id}", ref this.fixTime, 32);
        if (!ImGui.Button($"Save##fix-save-{id}"))
            return;
        apply(this.fixDate, this.fixTime);
        this.fixId = "";
    }

    private static string SyncWhen(SyncAnnouncement item)
    {
        if (!string.IsNullOrWhiteSpace(item.Time))
            return item.Time.Trim();
        var walls = ZoneClock.Walls(item.Text);
        if (walls.Count == 0)
            return "";
        return walls.Count > 1
            ? $"{walls[0].Time:HH:mm}-{walls[1].Time:HH:mm}"
            : walls[0].Time.ToString("HH:mm");
    }

    private static string WhenText(CalendarEntry entry)
    {
        var shown = ZoneClock.ShownRange(entry, TimeZoneInfo.Local);
        if (shown.Start is not TimeOnly time)
            return "date only";
        return shown.End is TimeOnly end ? $"{time:HH:mm}-{end:HH:mm}" : time.ToString("HH:mm");
    }

    private string ClockLabel(DayLine line)
    {
        if (line.Entry is CalendarEntry entry)
        {
            var shown = ZoneClock.ShownRange(entry, TimeZoneInfo.Local);
            if (shown.Start is TimeOnly start)
                return shown.End is TimeOnly end ? $"{start:HH:mm}-{end:HH:mm} " : $"{start:HH:mm} ";
        }

        return line.Time is TimeOnly time
            ? line.Entry?.End is TimeOnly storedEnd ? $"{time:HH:mm}-{storedEnd:HH:mm} " : $"{time:HH:mm} "
            : "";
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
        if (attached)
            this.DrawSyncPerformance(book!);
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
        ImGui.Dummy(new Vector2(1f, ImGui.GetFrameHeightWithSpacing()));
    }

    private static bool IsSyncLimited(string status) =>
        status.StartsWith("Sync limited", StringComparison.Ordinal)
        || status.StartsWith("Limited", StringComparison.Ordinal);

    private void DrawSyncPerformance(SyncBook book)
    {
        var status = string.IsNullOrWhiteSpace(book.SyncStatus) ? "Idle" : book.SyncStatus.Trim();
        var color = IsSyncLimited(status)
            ? new Vector4(0.95f, 0.55f, 0.22f, 1f)
            : status.StartsWith("Batching", StringComparison.Ordinal) || status.Contains("past waiting", StringComparison.Ordinal)
                ? new Vector4(0.90f, 0.78f, 0.35f, 1f)
                : status.StartsWith("Syncing", StringComparison.Ordinal)
                    || status.StartsWith("Re-sync", StringComparison.Ordinal)
                    || status.StartsWith("Waiting", StringComparison.Ordinal)
                    ? new Vector4(0.45f, 0.72f, 0.95f, 1f)
                    : new Vector4(0.65f, 0.65f, 0.65f, 1f);
        var body = status.StartsWith("Sync ", StringComparison.Ordinal) ? status["Sync ".Length..] : status;
        ImGui.TextColored(color, "Sync: " + body);
    }

    private void DrawDropPast()
    {
        var drop = this.session.DropPastEvents;
        if (ImGui.Checkbox("Delete past events##drop-past", ref drop))
        {
            this.session.DropPastEvents = drop;
            this.save();
        }

        ImGui.TextWrapped("After an event's end time, remove it from this computer. A shout with no end time stays until that day is over.");
    }

    private bool MatchesSearch(params string?[] parts)
    {
        var query = this.eventSearch.Trim();
        if (query.Length == 0)
            return true;
        foreach (var part in parts)
        {
            if (!string.IsNullOrEmpty(part) && part.Contains(query, StringComparison.OrdinalIgnoreCase))
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
        var lines = NoteLayout.Lines(text);
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
        if (!string.IsNullOrWhiteSpace(book.SyncStatus))
        {
            ImGui.SameLine();
            ImGui.TextUnformatted(book.SyncStatus);
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
            ImGui.TextWrapped("Update Shout Calendar Sync to use this relay.");

        if (book.RelayChoice == SyncRelays.CustomLabel)
        {
            var host = book.RelayHost;
            if (ImGui.InputText("Relay address", ref host, 200))
                book.RelayHost = host.Trim();
            var port = book.RelayPort;
            if (ImGui.InputInt("Relay port", ref port))
                book.RelayPort = port < 1 ? 0 : port;
            var mirror = book.MirrorRelay;
            if (ImGui.Checkbox("Country mirror##sync-mirror", ref mirror))
                book.MirrorRelay = mirror;
            ImGui.TextWrapped("A private group stays on this relay. A country mirror is tried first, and sync uses the public relay when the mirror does not answer.");
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Limits");
        var hold = book.HoldOffSeconds;
        book.HoldOffSeconds = this.Limit("Wait after login (seconds)", hold);
        ImGui.TextWrapped("After Sync loads, wait this long before the relay backlog is applied. Sync now skips the wait and pulls the next batch. It does not bring back invites you cleared.");
        book.Limits.MaxConnections = this.Limit("Connections", book.Limits.MaxConnections);
        book.Limits.BytesPerSecond = this.MegabitsPerSecond(book.Limits.BytesPerSecond, "Download (Mb/s)");
        book.Limits.UploadBytesPerSecond = this.MegabitsPerSecond(book.Limits.UploadBytesPerSecond, "Upload (Mb/s)");
        ImGui.TextWrapped("Download is how much shared data to keep each pass. Upload is how much of your shouts to send each pass. Passes are about ten seconds apart, and upload starts at 1 Mb/s.");
        book.Limits.MaxStoredBytes = this.Megabytes(book.Limits.MaxStoredBytes, "Stored size (MB)");
        book.Limits.MaxItemsPerTick = this.Limit("Items per tick", book.Limits.MaxItemsPerTick);
        book.Limits.MaxMemoryBytes = this.Megabytes(book.Limits.MaxMemoryBytes, "Memory (MB)");
        this.MaybeTipSyncLimits(book);

        if (ImGui.CollapsingHeader("Testing##sync-testing"))
        {
            if (ImGui.Button("Re-sync from relay##sync-resync"))
                this.requestSyncResync?.Invoke();
            ImGui.TextWrapped("Forgets local dismissals, then pulls the relay again. Current invites are applied first, then past ones, under the same Limits. Heavier than Sync now. Both run in the background.");
            var debug = book.DebugPerf;
            if (ImGui.Checkbox("Performance log##sync-perf", ref debug))
                book.DebugPerf = debug;
            ImGui.TextWrapped("Off by default. Each fetch pass records whether sync is keeping up, and the counts next to your limits.");
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

    private void DrawSyncDetail(DayLine line)
    {
        var book = SyncGate.Panel;
        var item = book?.CopyEvents().FirstOrDefault(row => row.Id == line.SyncId);
        if (item is null)
        {
            this.selectedLine = null;
            return;
        }

        this.DrawInviteGlanceText(item.Text, item.Date, SyncWhen(item), ListedWorld(item), "");
        this.DrawClockFix(
            "sync-" + item.Id,
            item.Date,
            SyncWhen(item),
            (date, time) => book!.SetClock(item.Id, date, time));
        ImGui.Separator();
        ImGui.TextDisabled("Message");
        this.DrawNote(item.Text, EventTitle.Choose(item.Text));
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

        if (ImGui.SmallButton($"Delete##sync-detail-{item.Id}") && book!.Dismiss(item.Id))
            this.selectedLine = null;
    }

    private void DrawInviteGlance(CalendarEntry entry)
    {
        var title = EventTitle.Choose(entry.EventText);
        if (title.Length > 0)
            ImGui.TextUnformatted(title);
        this.DrawKind(entry.EventText);

        this.FactLine("Date", this.DateLabel(entry));
        this.FactLine("Time", WhenText(entry));
        if (this.editingId != entry.Id)
        {
            this.DrawClockFix(
                "local-" + entry.Id,
                entry.Date?.ToString("yyyy-MM-dd") ?? "",
                WhenText(entry),
                (date, time) =>
                {
                    if (this.session.Log.Revise(entry.Id, entry.EventText, date, time, entry.Place, DateTimeOffset.UtcNow, this.session.Places, this.session.Channels))
                        this.save();
                });
        }
        var spot = HousingTravel.Find(entry.Place, entry.EventText, entry.Ward, entry.Server);
        this.FactLine("Place", spot is HousingSpot housing ? housing.Label : string.IsNullOrWhiteSpace(entry.Place) ? "none found" : entry.Place);
        this.DrawPins(entry.Place + "\n" + entry.EventText, null, entry);
        this.DrawFrom(entry.Sender, entry.SpeakerWorld, entry.Server, EventTitle.Choose(entry.EventText), entry.Id);
        var namedWorld = ServerNames.TryAdvertised(entry.EventText, out var advertised) ? advertised : entry.Server;
        if (!string.IsNullOrWhiteSpace(namedWorld))
            this.FactLine("World", namedWorld);
        if (LinkFinder.Find(entry.EventText).Count > 0)
        {
            ImGui.TextDisabled("Links");
            this.DrawLinks(entry.EventText);
        }
    }

    private void DrawInviteGlanceText(string text, string date, string time, string world, string sender)
    {
        var title = EventTitle.Choose(text);
        if (title.Length > 0)
            ImGui.TextUnformatted(title);
        this.DrawKind(text);

        this.FactLine("Date", string.IsNullOrWhiteSpace(date) ? "needs a date" : date);
        this.FactLine("Time", string.IsNullOrWhiteSpace(time) ? "date only" : time);
        var spot = HousingTravel.Find(null, text, null, world);
        this.FactLine("Place", spot is HousingSpot housing ? housing.Label : "none found");
        this.DrawPins(text, null);
        this.DrawFrom(sender, "", world, EventTitle.Choose(text), sender ?? "");
        if (!string.IsNullOrWhiteSpace(world))
            this.FactLine("World", world);
        if (LinkFinder.Find(text).Count > 0)
        {
            ImGui.TextDisabled("Links");
            this.DrawLinks(text);
        }
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

    private bool IncludeLocal(CalendarEntry entry)
    {
        if (entry.Hidden && !this.session.ShowHidden)
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

        if (!entry.Accepted && !entry.Manual && !this.ShowsPendingWorld(entry.EventText, entry.Server))
            return false;
        if (SyncGate.Panel is not SyncBook book)
            return true;
        return book.Worlds.ShowsLocal(entry);
    }

    private SyncAnnouncement[] FrameForDraw(SyncBook? book)
    {
        var all = book?.CopyEvents() ?? [];
        if (book is null || all.Length == 0)
            return all;
        var standing = this.StandingWorld();
        var showAll = this.session.ShowAllServers;
        var kept = new List<SyncAnnouncement>(all.Length);
        foreach (var item in all)
        {
            if (item.Declined)
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

            if (book.Worlds.DrawnPending(world, showAll, standing))
                kept.Add(item);
        }

        return kept.ToArray();
    }

    private bool ShowsPendingWorld(string? text, string? storedWorld)
    {
        var standing = this.StandingWorld();
        if (SyncGate.Panel is not SyncBook book)
            return this.session.ShowAllServers || ServerNames.ForServer(text, storedWorld, standing);
        if (PlayableWorlds.TryNamedWorld(storedWorld, out var stored) && !book.Worlds.SharesPending(stored, true, standing))
            return false;
        var world = NamedWorld(text, storedWorld);
        if (world.Length == 0)
            return ServerNames.ForServer(text, storedWorld, standing);
        return book.Worlds.DrawnPending(world, this.session.ShowAllServers, standing);
    }

    private static string NamedWorld(string? text, string? storedWorld)
    {
        if (ServerNames.TryAdvertised(text, out var named))
            return named;
        return PlayableWorlds.TryNamedWorld(storedWorld, out var stored) ? stored : "";
    }

    private bool PendingOnThisCalendar(SyncBook book, SyncAnnouncement item)
    {
        var standing = this.StandingWorld();
        if (PlayableWorlds.TryCanonical(item.World, out var listed) && !book.Worlds.SharesPending(listed, true, standing))
            return false;
        var world = NamedWorld(item.Text, item.World);
        if (world.Length == 0)
            return ServerNames.ForServer(item.Text, item.World, standing);
        return book.Worlds.DrawnPending(world, this.session.ShowAllServers, standing);
    }

    private void AddSyncLines(DateOnly date, List<DayLine> lines)
    {
        var book = SyncGate.Panel;
        if (book is null)
            return;
        var standing = this.StandingWorld();
        foreach (var item in this.syncFrame)
        {
            if (!item.FromSync || item.Declined)
                continue;
            if (PlayableWorlds.TryCanonical(item.World, out var world) && !book.Worlds.SharesPending(world, true, standing))
                continue;
            if (item.Hidden && !this.session.ShowHidden)
                continue;
            if (item.Accepted)
            {
                if (!this.session.ShowSyncAccepted || !this.OnViewedWorld(book, item))
                    continue;
            }
            else if (!this.session.ShowSyncUnaccepted || !this.ShowsPendingWorld(item.Text, item.World) || !this.PendingOnThisCalendar(book, item))
            {
                continue;
            }

            var modes = item.Channel == SharePolicy.YellChannel ? book.Settings.Yell : book.Settings.Shout;
            if (!item.Accepted && !modes.Add)
                continue;
            if (!DateOnly.TryParseExact(item.Date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var day))
                continue;
            var walls = ZoneClock.Walls(item.Text);
            TimeOnly? time = TimeOnly.TryParseExact(item.Time, "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var clock)
                ? clock
                : walls.Count > 0 ? walls[0].Time : null;
            TimeOnly? end = walls.Count >= 2 ? walls[1].Time : null;
            var overnight = Overnight.Is(time, end);
            if (!Overnight.Covers(date, day, time, end))
                continue;
            var named = EventTitle.Choose(item.Text);
            var title = named.Length > 0 ? named : item.Text;
            if (overnight && time is TimeOnly begin && end is TimeOnly stop)
                title = $"{begin:HH:mm}–{stop:HH:mm} {title}";
            var clockShown = date == day ? time : end;
            lines.Add(new DayLine(clockShown, overnight ? 0 : clockShown is null ? 2 : 1, title, item.Text, null, null, overnight, null, item.ColorToken, item.Id, date));
        }
    }
}
