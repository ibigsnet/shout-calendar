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
    private readonly Action<string, float, float, bool, string?> openPin;
    private readonly Action<HousingSpot> openHousing;
    private readonly FileDialogManager dialogs;
    private string? selectedId;
    private string? editingId;
    private string editNote = "";
    private string editDate = "";
    private string editTime = "";
    private string editPlace = "";
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

    public CalendarWindow(
        CalendarSession session,
        ClearPrompt prompt,
        Action save,
        Action<int, string?> previewSound,
        FileDialogManager dialogs,
        Action<string, float, float, bool, string?> openPin,
        Action<HousingSpot> openHousing)
        : base("FFXIV Shout Calendar")
    {
        this.session = session;
        this.prompt = prompt;
        this.save = save;
        this.previewSound = previewSound;
        this.dialogs = dialogs;
        this.openPin = openPin;
        this.openHousing = openHousing;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(1180, 820),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
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
    }

    private void DrawPending()
    {
        var rowRight = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        var continued = false;
        this.DrawWrappingButton("Clear local", rowRight, ref continued, () => this.prompt.Ask(ClearTarget.All));
        this.DrawWrappingButton("Clear local accepted", rowRight, ref continued, () => this.prompt.Ask(ClearTarget.Accepted));
        this.DrawWrappingButton("Clear local unaccepted", rowRight, ref continued, () => this.prompt.Ask(ClearTarget.Unaccepted));
        this.DrawWrappingButton("Delete past local", rowRight, ref continued, () => this.prompt.Ask(ClearTarget.LocalPast));
        this.DrawWrappingButton("Add event", rowRight, ref continued, () => this.adding = true);
        ImGui.Separator();
        this.DrawAddEvent();

        foreach (var entry in this.session.Log.Entries.Where(entry => !entry.Accepted && this.IncludeLocal(entry)).ToList())
        {
            if (!this.MatchesSearch(entry.EventText, entry.Sender, entry.Place, entry.Server, entry.SpeakerWorld))
                continue;
            ImGui.Separator();
            var title = EventTitle.Choose(entry.EventText);
            var who = string.IsNullOrWhiteSpace(entry.Sender) ? "" : entry.Sender;
            var rowLabel = title.Length > 0 ? title : who.Length > 0 ? who : "Invite";
            if (ImGui.Selectable($"{rowLabel}##{entry.Id}", this.selectedId == entry.Id))
            {
                this.selectedId = entry.Id;
                if (entry.Date is DateOnly selectedDay)
                    this.session.Show(selectedDay);
            }

            this.DrawKind(entry.EventText);
            ImGui.TextDisabled(this.InviteFacts(entry));
            if (!string.IsNullOrWhiteSpace(entry.Place))
                ImGui.TextWrapped(entry.Place);
            this.DrawNote(entry.EventText, title);
            this.DrawLinks(entry.EventText);
            if (ImGui.Button($"Edit##{entry.Id}"))
            {
                var named = EventTitle.Choose(entry.EventText);
                var lineTitle = named.Length > 0 ? named : string.IsNullOrWhiteSpace(entry.Place) ? entry.EventText : entry.Place;
                this.SelectLine(new DayLine(entry.Time, entry.Time is null ? 2 : 1, lineTitle, entry.EventText, entry, null, false));
                this.BeginEdit(entry);
            }
            ImGui.SameLine();
            if (ImGui.Button($"Accept##{entry.Id}") && this.session.Log.Accept(entry.Id))
            {
                if (SyncGate.Panel is SyncBook synced)
                    synced.AcceptSame(entry, synced.Worlds.Home);
                if (entry.Date is DateOnly acceptedDay)
                    this.session.Show(acceptedDay);
                this.save();
            }

            ImGui.SameLine();
            if (ImGui.Button($"Decline##{entry.Id}"))
                this.RemoveEntry(entry.Id);
            ImGui.SameLine();
            if (ImGui.Button($"Delete##{entry.Id}"))
                this.RemoveEntry(entry.Id);
        }

        this.DrawSharedPending();
    }

    private void DrawSharedPending()
    {
        if (SyncGate.Panel is not SyncBook book)
            return;

        if (!this.session.ShowSyncUnaccepted)
            return;
        var rows = book.Events
            .Where(item => item.IsSyncPending && book.Worlds.IsViewing(item.World))
            .ToList();
        if (rows.Count == 0 && book.Informedaholic)
            return;
        ImGui.Separator();
        ImGui.TextWrapped("Shared on " + string.Join(", ", book.Worlds.Viewing()));
        if (rows.Count == 0)
        {
            ImGui.TextDisabled("No shared invites are waiting.");
            return;
        }

        foreach (var item in rows)
            this.DrawSharedRow(book, item);
    }

    private void DrawSharedRow(SyncBook book, SyncAnnouncement item)
    {
        if (!this.MatchesSearch(item.Text, item.World, item.Date, item.Time))
            return;
        ImGui.Separator();
        var title = EventTitle.Choose(item.Text);
        var heading = title.Length > 0 ? title : item.World;
        var width = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        var size = ImGui.CalcTextSize(heading, false, width);
        var pos = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddRectFilled(
            pos,
            pos + new Vector2(width, size.Y),
            ImGui.ColorConvertFloat4ToU32(this.session.SyncPendingColor));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 1f, 1f, 1f));
        ImGui.TextWrapped(heading);
        ImGui.PopStyleColor();
        var when = string.IsNullOrWhiteSpace(item.Time) ? "" : item.Time;
        var day = string.IsNullOrWhiteSpace(item.Date) ? "" : item.Date;
        var facts = string.Join(" · ", new[] { item.World, day, when }.Where(part => part.Length > 0));
        this.DrawKind(item.Text);
        if (facts.Length > 0)
            ImGui.TextDisabled(facts);
        this.DrawNote(item.Text, title);
        this.DrawLinks(item.Text);
        if (ImGui.SmallButton($"Accept##sync-accept-{item.Id}") && book.AcceptRemote(item.Id))
            this.save();
        ImGui.SameLine();
        if (ImGui.SmallButton($"Save local##sync-save-{item.Id}"))
            this.SaveLocal(book, item);
        ImGui.SameLine();
        if (ImGui.SmallButton($"Decline##sync-decline-{item.Id}"))
            book.DeclineRemote(item.Id);
        ImGui.SameLine();
        if (ImGui.SmallButton($"Delete##sync-row-{item.Id}"))
            book.Dismiss(item.Id);
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
        ImGui.TextWrapped("On: it needs two of those three, so a place name by itself is skipped.");
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
        this.linkRememberDraft = false;
    }

    private void DrawLinkPrompt()
    {
        if (this.pendingLink is not null)
            ImGui.OpenPopup("Open this link?##shout-link");

        ImGui.SetNextWindowSize(new Vector2(440f, 200f), ImGuiCond.Always);
        var open = this.pendingLink is not null;
        if (!ImGui.BeginPopupModal("Open this link?##shout-link", ref open))
        {
            if (!open)
                this.pendingLink = null;
            return;
        }

        ImGui.PushTextWrapPos(ImGui.GetCursorPos().X + 400f);
        ImGui.TextWrapped("Open this link in your browser?");
        ImGui.TextWrapped(this.pendingLink ?? "");
        ImGui.PopTextWrapPos();
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

    private void FinishLink(bool open)
    {
        if (this.linkRememberDraft)
        {
            this.session.RememberLinkChoice = true;
            this.session.OpenRememberedLinks = open;
            this.save();
        }

        var url = this.pendingLink;
        this.pendingLink = null;
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

        if (this.weekView)
        {
            this.DrawWeek();
            ImGui.EndChild();
            return;
        }

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

        const float gap = 6f;
        var side = MathF.Max(120f, MathF.Floor((ImGui.GetContentRegionAvail().X - (gap * 6f)) / 7f));
        var header = ImGui.GetCursorScreenPos();
        for (var column = 0; column < 7; column++)
        {
            ImGui.SetCursorScreenPos(header + new Vector2(column * (side + gap), 0));
            ImGui.TextUnformatted(new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" }[column]);
        }

        var grid = header + new Vector2(0, ImGui.GetTextLineHeightWithSpacing());
        var rows = (month.Cells.Count + 6) / 7;
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < month.Cells.Count; index++)
        {
            var column = index % 7;
            var row = index / 7;
            ImGui.SetCursorScreenPos(grid + new Vector2(column * (side + gap), row * (side + gap)));
            var cell = month.Cells[index];
            var cellDate = cell.Day is int dayNumber
                ? new DateOnly(month.Year, month.Month, dayNumber)
                : (DateOnly?)null;
            this.DrawDay(cellDate, side, side, false);
        }

        this.DrawSchedule(month, grid, side, gap);
        ImGui.SetCursorScreenPos(grid + new Vector2(0, rows * (side + gap)));
        ImGui.Dummy(new Vector2(1f, 1f));
        this.DrawSelectedDetail();
        this.DrawUndated(now);
        ImGui.EndChild();
    }

    private void DrawSchedule(CalendarMonth month, Vector2 grid, float side, float gap)
    {
        var marks = GameSchedule.InMonth(
            month.Year,
            month.Month,
            TimeZoneInfo.Local,
            this.session.CactpotRegion,
            this.VisibleResets());
        if (marks.Count == 0)
            return;

        var spans = marks.Where(mark => mark.StartDate != mark.EndDate).ToList();
        if (spans.Count == 0)
            return;

        var lanes = GameSchedule.Lanes(spans);
        var draw = ImGui.GetWindowDrawList();
        var style = ImGui.GetStyle();
        var top = style.WindowPadding.Y + ImGui.GetFrameHeight() + style.ItemSpacing.Y;
        var bar = this.GlanceBar() - 2f;
        foreach (var mark in spans)
        {
            if (!this.MatchesSearch(mark.Chip, mark.Name, mark.Detail))
                continue;
            var lane = lanes[mark.Key];
            var (fill, ink) = this.Tone(mark.Tone);
            var when = $"{mark.LocalStart:ddd d MMM HH:mm} – {mark.LocalEnd:ddd d MMM HH:mm}";
            foreach (var segment in GameSchedule.Segments(month, mark.StartDate, mark.EndDate))
            {
                var x1 = grid.X + (segment.FirstColumn * (side + gap)) + 4f;
                var x2 = grid.X + (segment.LastColumn * (side + gap)) + side - 4f;
                var y1 = grid.Y + (segment.Row * (side + gap)) + top + (lane * this.GlanceBar());
                var min = new Vector2(x1, y1);
                var max = new Vector2(x2, y1 + bar);
                draw.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(fill));
                if (mark.Tone == ResetTone.Event)
                    draw.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(0.85f, 0.72f, 0.28f, 1f)));
                var label = this.Fit(mark.Chip, max.X - min.X - 8f);
                draw.PushClipRect(min, max, true);
                this.DrawScaledText(draw, new Vector2(min.X + 4f, min.Y + ((bar - ImGui.GetTextLineHeight()) * 0.5f)), ImGui.ColorConvertFloat4ToU32(ink), label);
                draw.PopClipRect();
                if (ImGui.IsMouseHoveringRect(min, max))
                {
                    ImGui.SetTooltip($"{mark.Name}\n{when}\n{mark.Detail}");
                    if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                        this.SelectLine(this.LineFor(mark));
                }
            }
        }
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
        var count = text.Length;
        while (count > 1 && ImGui.CalcTextSize(text[..count]).X > budget)
            count--;
        return text[..Math.Max(1, count)] + ellipsis;
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
        ImGui.InputText($"Time##edit-{entry.Id}", ref this.editTime, 8);
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
            ImGui.TextUnformatted(entry.Accepted ? "Accepted" : "Pending");
            ImGui.PopStyleColor();
            _ = fill;
            this.DrawNote(entry.EventText, EventTitle.Choose(entry.EventText));
            this.DrawLinks(entry.EventText);
            if (this.editingId == entry.Id)
            {
                this.DrawEdit(entry);
            }
            else
            {
                if (ImGui.SmallButton($"Edit##detail-{entry.Id}"))
                    this.BeginEdit(entry);
                ImGui.SameLine();
            }

            if (ImGui.SmallButton($"Delete##detail-{entry.Id}"))
            {
                this.RemoveEntry(entry.Id);
                this.selectedLine = null;
            }

            this.DrawPins(entry.Place + "\n" + entry.EventText, null, entry);
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
            this.openPin(pin.PlaceName, pin.X, pin.Y, pin.HasMap, pin.Quest);
            if (pin.HasMap)
                shown.Add((pin.X, pin.Y));
        }

        var place = this.session.Places.Match(text).FirstOrDefault() ?? "";
        foreach (var mention in MapMentions.Read(text))
        {
            if (shown.Any(pin => Math.Abs(pin.X - mention.X) < 0.05f && Math.Abs(pin.Y - mention.Y) < 0.05f))
                continue;
            var caption = string.IsNullOrEmpty(place)
                ? $"Flag ({mention.X:0.0}, {mention.Y:0.0}) on your current map"
                : $"{place} ({mention.X:0.0}, {mention.Y:0.0})";
            if (ImGui.SmallButton($"{caption}##spot-{mention.X}-{mention.Y}"))
                this.openPin(place, mention.X, mention.Y, true, null);
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

        const float gap = 6f;
        var width = MathF.Max(120f, MathF.Floor((ImGui.GetContentRegionAvail().X - (gap * 6f)) / 7f));
        var header = ImGui.GetCursorScreenPos();
        for (var column = 0; column < 7; column++)
        {
            ImGui.SetCursorScreenPos(header + new Vector2(column * (width + gap), 0));
            ImGui.TextUnformatted(new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" }[column]);
        }

        var grid = header + new Vector2(0, ImGui.GetTextLineHeightWithSpacing());
        var height = MathF.Max(360f, ImGui.GetContentRegionAvail().Y - ImGui.GetTextLineHeightWithSpacing() - 8f);
        for (var column = 0; column < 7; column++)
        {
            ImGui.SetCursorScreenPos(grid + new Vector2(column * (width + gap), 0));
            this.DrawDay(this.weekStart.AddDays(column), width, height, false);
        }

        this.DrawWeekSpans(grid, width, gap);
        ImGui.SetCursorScreenPos(grid + new Vector2(0, height + gap));
        ImGui.Dummy(new Vector2(1f, 1f));
        this.DrawSelectedDetail();
        this.DrawUndated(DateTimeOffset.UtcNow);
    }

    private void DrawWeekSpans(Vector2 grid, float width, float gap)
    {
        var weekEnd = this.weekStart.AddDays(6);
        var marks = new List<ScheduleOccurrence>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Take(int year, int month)
        {
            foreach (var mark in GameSchedule.InMonth(year, month, TimeZoneInfo.Local, this.session.CactpotRegion, this.VisibleResets()))
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
        if (marks.Count == 0)
            return;

        var lanes = GameSchedule.Lanes(marks);
        var draw = ImGui.GetWindowDrawList();
        var style = ImGui.GetStyle();
        var top = style.WindowPadding.Y + ImGui.GetFrameHeight() + style.ItemSpacing.Y;
        var bar = this.GlanceBar() - 2f;
        foreach (var mark in marks)
        {
            if (GameSchedule.WeekSegment(this.weekStart, mark.StartDate, mark.EndDate) is not SpanSegment segment)
                continue;
            var lane = lanes[mark.Key];
            var (fill, ink) = this.Tone(mark.Tone);
            var when = $"{mark.LocalStart:ddd d MMM HH:mm} – {mark.LocalEnd:ddd d MMM HH:mm}";
            var x1 = grid.X + (segment.FirstColumn * (width + gap)) + 4f;
            var x2 = grid.X + (segment.LastColumn * (width + gap)) + width - 4f;
            var y1 = grid.Y + top + (lane * this.GlanceBar());
            var min = new Vector2(x1, y1);
            var max = new Vector2(x2, y1 + bar);
            draw.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(fill));
            if (mark.Tone == ResetTone.Event)
                draw.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(new Vector4(0.85f, 0.72f, 0.28f, 1f)));
            var label = this.Fit(mark.Chip, max.X - min.X - 8f);
            draw.PushClipRect(min, max, true);
            draw.AddText(new Vector2(min.X + 4f, min.Y + ((bar - ImGui.GetTextLineHeight()) * 0.5f)), ImGui.ColorConvertFloat4ToU32(ink), label);
            draw.PopClipRect();
            if (ImGui.IsMouseHoveringRect(min, max))
            {
                ImGui.SetTooltip($"{mark.Name}\n{when}\n{mark.Detail}");
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    this.SelectLine(this.LineFor(mark));
            }
        }
    }

    private void OpenDay(DateOnly date)
    {
        this.session.Show(date);
        this.folderDay = date.Day;
        this.focusFolder = true;
    }

    private static DateOnly SundayOn(DateOnly day) => day.AddDays(-(int)day.DayOfWeek);

    private void DrawDay(DateOnly? date, float width, float height, bool showSpanChips)
    {
        if (date is not DateOnly shown)
        {
            ImGui.Dummy(new Vector2(width, height));
            return;
        }

        var dayId = $"day-{shown:yyyy-MM-dd}";
        var today = DateOnly.FromDateTime(DateTime.Now);
        var isToday = shown == today;
        if (isToday)
            ImGui.PushStyleColor(ImGuiCol.ChildBg, this.session.TodayColor);
        ImGui.BeginChild(dayId, new Vector2(width, height), true);
        this.UseTextScale();
        if (ImGui.SmallButton($"{shown.Day}##open-{shown:yyyy-MM-dd}"))
            this.OpenDay(shown);

        var lines = this.GlanceFor(shown);
        var spanCount = showSpanChips ? 0 : lines.Count(line => line.Span);
        if (spanCount > 0)
            ImGui.Dummy(new Vector2(1f, spanCount * this.GlanceBar()));
        var shownCount = 0;
        foreach (var line in lines.Where(line => showSpanChips || !line.Span))
        {
            if (ImGui.GetCursorPosY() + this.GlanceBar() > height - 6f)
                break;
            this.DrawColorBlock(line);
            shownCount++;
        }

        var hidden = lines.Count(line => showSpanChips || !line.Span) - shownCount;
        if (hidden > 0 && ImGui.SmallButton($"+{hidden}##more-{shown:yyyy-MM-dd}"))
            this.OpenDay(shown);

        ImGui.EndChild();
        if (isToday)
            ImGui.PopStyleColor();
    }

    private void DrawColorBlock(DayLine line)
    {
        var (fill, ink) = this.Ink(line);
        var width = MathF.Max(8f, ImGui.GetContentRegionAvail().X);
        var height = this.GlanceBar() - 2f;
        var pos = ImGui.GetCursorScreenPos();
        var max = pos + new Vector2(width, height);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(pos, max, ImGui.ColorConvertFloat4ToU32(fill));
        var when = this.ClockLabel(line);
        var label = this.Fit(when + line.Title, width - 8f);
        draw.PushClipRect(pos, max, true);
        this.DrawScaledText(draw, new Vector2(pos.X + 4f, pos.Y + ((height - ImGui.GetTextLineHeight()) * 0.5f)), ImGui.ColorConvertFloat4ToU32(ink), label);
        draw.PopClipRect();
        ImGui.Dummy(new Vector2(width, this.GlanceBar()));
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
            if (ImGui.SmallButton($"Edit##folder-{entry.Id}"))
                this.BeginEdit(entry);
            ImGui.SameLine();
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
            var shown = ZoneClock.ShownRange(entry, TimeZoneInfo.Local);
            if (labeled && entry.Repeat is null)
            {
                if (shown.Date != date)
                    continue;
            }
            else if (!EventRepeat.FallsOn(entry, date))
            {
                continue;
            }

            var named = EventTitle.Choose(entry.EventText);
            var title = named.Length > 0 ? named : string.IsNullOrWhiteSpace(entry.Place) ? entry.EventText : entry.Place;
            var clock = labeled ? shown.Start : entry.Time;
            lines.Add(new DayLine(clock, clock is null ? 2 : 1, title, entry.EventText, entry, null, false));
        }

        this.AddSyncLines(date, lines);

        foreach (var mark in GameSchedule.InMonth(date.Year, date.Month, TimeZoneInfo.Local, this.session.CactpotRegion, this.VisibleResets()))
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
        else if (kind is EventKind branded)
        {
            fill = this.KindFill(branded);
            ink = new Vector4(1f, 1f, 1f, 1f);
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
            fill = this.session.AcceptedColor;
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
        var fill = entry.Color ?? (kind is EventKind branded ? this.KindFill(branded) : entry.Accepted ? this.session.AcceptedColor : this.session.PendingColor);
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
        var who = string.IsNullOrWhiteSpace(entry.Sender) ? "" : entry.Sender;
        return string.Join(" · ", new[] { day, WhenText(entry), who }.Where(part => part.Length > 0));
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

        ImGui.NewLine();
        ImGui.Dummy(new Vector2(1f, ImGui.GetFrameHeightWithSpacing()));
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
        book.HoldOffSeconds = this.Limit("Hold off (seconds)", hold);
        book.Limits.MaxConnections = this.Limit("Connections", book.Limits.MaxConnections);
        book.Limits.BytesPerSecond = this.MegabitsPerSecond(book.Limits.BytesPerSecond);
        book.Limits.MaxStoredBytes = this.Megabytes(book.Limits.MaxStoredBytes, "Stored size (MB)");
        book.Limits.MaxItemsPerTick = this.Limit("Items per tick", book.Limits.MaxItemsPerTick);
        book.Limits.MaxMemoryBytes = this.Megabytes(book.Limits.MaxMemoryBytes, "Memory (MB)");

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
        if (status == RelayReach.Offline)
            return new Vector4(0.86f, 0.36f, 0.32f, 1f);
        if (status == RelayReach.OutOfDate)
            return new Vector4(0.95f, 0.55f, 0.22f, 1f);
        if (status == RelayReach.ViaPublicRelay)
            return new Vector4(0.45f, 0.72f, 0.95f, 1f);
        return new Vector4(0.90f, 0.78f, 0.35f, 1f);
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

    private int MegabitsPerSecond(int bytesPerSecond)
    {
        var megabits = Math.Max(1, (int)((long)bytesPerSecond * 8 / 1_000_000));
        ImGui.SetNextItemWidth(140f);
        if (!ImGui.InputInt("Speed (Mb/s)##sync-mbps", ref megabits))
            return bytesPerSecond;
        if (megabits < 1)
            megabits = 1;
        return megabits * 125_000;
    }

    private void DrawSyncDetail(DayLine line)
    {
        var book = SyncGate.Panel;
        var item = book?.Events.FirstOrDefault(row => row.Id == line.SyncId);
        if (item is null)
        {
            this.selectedLine = null;
            return;
        }

        ImGui.TextWrapped(item.Text);
        this.DrawLinks(item.Text);
        this.DrawPins(item.Text, null);
        if (item.IsSyncPending && ImGui.SmallButton($"Accept##sync-detail-{item.Id}"))
            book!.AcceptRemote(item.Id);
        if (item.IsSyncPending)
            ImGui.SameLine();
        if (ImGui.SmallButton($"Delete##sync-detail-{item.Id}") && book!.Dismiss(item.Id))
            this.selectedLine = null;
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
        if (entry.Accepted)
        {
            if (!this.session.ShowLocalAccepted)
                return false;
        }
        else if (!this.session.ShowLocalUnaccepted)
        {
            return false;
        }

        if (SyncGate.Panel is not SyncBook book)
            return true;
        return book.Worlds.ShowsLocal(entry);
    }

    private void AddSyncLines(DateOnly date, List<DayLine> lines)
    {
        var book = SyncGate.Panel;
        if (book is null)
            return;
        foreach (var item in book.Worlds.Visible(book.Events))
        {
            if (!item.FromSync || item.Declined)
                continue;
            if (item.Accepted)
            {
                if (!this.session.ShowSyncAccepted)
                    continue;
            }
            else if (!this.session.ShowSyncUnaccepted)
            {
                continue;
            }

            var modes = item.Channel == SharePolicy.YellChannel ? book.Settings.Yell : book.Settings.Shout;
            if (!item.Accepted && !modes.Add)
                continue;
            if (!DateOnly.TryParseExact(item.Date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var day) || day != date)
                continue;
            TimeOnly? time = TimeOnly.TryParseExact(item.Time, "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var clock)
                ? clock
                : null;
            var named = EventTitle.Choose(item.Text);
            var title = named.Length > 0 ? named : item.Text;
            lines.Add(new DayLine(time, time is null ? 2 : 1, title, item.Text, null, null, false, null, item.ColorToken, item.Id, day));
        }
    }

    }
