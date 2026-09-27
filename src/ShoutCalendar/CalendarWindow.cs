using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using ShoutCalendar.Core;

namespace ShoutCalendar;

public sealed class CalendarWindow : Window
{
    private readonly CalendarSession session;
    private readonly ClearPrompt prompt;
    private readonly Action save;
    private readonly Action<int, string?> previewSound;
    private readonly Action<string, float, float, bool, string?> openPin;
    private readonly FileDialogManager dialogs;
    private string? selectedId;
    private string? editingId;
    private string editNote = "";
    private string editDate = "";
    private string editTime = "";
    private string editPlace = "";
    private string holdDaysText = "";
    private bool holdDaysReady;
    private string minutesBeforeText = "";
    private bool minutesBeforeReady;
    private int? folderDay;
    private DayLine? selectedLine;

    public CalendarWindow(
        CalendarSession session,
        ClearPrompt prompt,
        Action save,
        Action<int, string?> previewSound,
        FileDialogManager dialogs,
        Action<string, float, float, bool, string?> openPin)
        : base("Shout Calendar (provisional)")
    {
        this.session = session;
        this.prompt = prompt;
        this.save = save;
        this.previewSound = previewSound;
        this.dialogs = dialogs;
        this.openPin = openPin;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(1180, 820),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
        this.DrawHistory();
        ImGui.SameLine();
        this.DrawCalendar();
        this.DrawDayFolder();
        this.DrawClearPrompt();
        this.dialogs.Draw();
    }

    private void DrawHistory()
    {
        ImGui.BeginChild("shout-history", new Vector2(340, 0), true);
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

            ImGui.EndTabBar();
        }

        ImGui.EndChild();
    }

    private void DrawSettings()
    {
        this.DrawHoldDays();
        this.DrawAggressiveFilter();
        this.DrawChannelOptions();
        this.DrawAlarms();
    }

    private void DrawPending()
    {
        var rowRight = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        var continued = false;
        this.DrawWrappingButton("Clear all", rowRight, ref continued, () => this.prompt.Ask(ClearTarget.All));
        this.DrawWrappingButton("Clear accepted", rowRight, ref continued, () => this.prompt.Ask(ClearTarget.Accepted));
        this.DrawWrappingButton("Clear unaccepted", rowRight, ref continued, () => this.prompt.Ask(ClearTarget.Unaccepted));
        ImGui.Separator();
        ImGui.TextWrapped("Pending alerts show below.");

        foreach (var entry in this.session.Log.Entries.Where(entry => !entry.Accepted).ToList())
        {
            ImGui.Separator();
            var who = string.IsNullOrWhiteSpace(entry.Sender) ? "unknown" : entry.Sender;
            if (ImGui.Selectable($"{who}##{entry.Id}", this.selectedId == entry.Id))
            {
                this.selectedId = entry.Id;
                if (entry.Date is DateOnly selectedDay)
                    this.session.Show(selectedDay);
            }

            var dayText = entry.Date?.ToString("yyyy-MM-dd") ?? "needs a date";
            if (entry.Repeat is not null)
                dayText += " · " + entry.Repeat.Label;
            ImGui.TextUnformatted($"{dayText} {WhenText(entry)}");
            if (!string.IsNullOrWhiteSpace(entry.Place))
                ImGui.TextWrapped(entry.Place);
            ImGui.TextWrapped(entry.EventText);
            if (ImGui.Button($"Edit##{entry.Id}"))
            {
                var title = string.IsNullOrWhiteSpace(entry.Place) ? entry.EventText : entry.Place;
                this.SelectLine(new DayLine(entry.Time, entry.Time is null ? 2 : 1, title, entry.EventText, entry, null, false));
            }
            ImGui.SameLine();
            if (ImGui.Button($"Accept##{entry.Id}") && this.session.Log.Accept(entry.Id))
            {
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
    }

    private void DrawResets()
    {
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

        ImGui.TextWrapped("Off: a line is kept when it has a date, a time, or a place. On: it needs two of those three, so a place name by itself is skipped. Today, tonight, tomorrow, and a weekday such as next Tuesday count as a date.");
    }

    private void DrawAlarms()
    {
        ImGui.SetNextItemOpen(false, ImGuiCond.FirstUseEver);
        if (!ImGui.CollapsingHeader("Alarms##alarms"))
            return;

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
        if (!ImGui.IsItemDeactivatedAfterEdit())
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
        foreach (var option in ChatChannels.All)
        {
            var enabled = this.session.Channels.Contains(option.Channel);
            if (!ImGui.Checkbox($"{option.Label}##chat-{option.Channel}", ref enabled))
                continue;
            this.session.SetChannel(option.Channel, enabled);
            this.save();
        }
    }

    private void DrawCalendar()
    {
        ImGui.BeginChild("shout-month");
        var month = this.session.CurrentMonth();
        if (ImGui.Button("Previous month"))
            month = this.session.Page(-1);
        ImGui.SameLine();
        ImGui.TextUnformatted(month.Title);
        ImGui.SameLine();
        if (ImGui.Button("Next month"))
            month = this.session.Page(1);
        var today = DateTime.Now;
        if (month.Year != today.Year || month.Month != today.Month)
        {
            ImGui.SameLine();
            if (ImGui.Button("Today"))
            {
                this.session.Show(DateOnly.FromDateTime(today));
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
            this.DrawDay(month.Cells[index], side);
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
            this.session.Resets);
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

    private readonly record struct DayLine(TimeOnly? Time, int Rank, string Title, string Detail, CalendarEntry? Entry, ResetTone? Tone, bool Span, IReadOnlyList<DayLine>? Members = null);

    private void DrawUndated(DateTimeOffset now)
    {
        var undated = this.session.Log.Entries.Where(entry => entry.Date is null).ToList();
        if (undated.Count == 0)
            return;
        ImGui.Separator();
        ImGui.TextUnformatted("Needs a date");
        foreach (var entry in undated)
            this.DrawEvent(entry, now);
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
    }

    private void DrawEdit(CalendarEntry entry)
    {
        ImGui.InputText($"Note##edit-{entry.Id}", ref this.editNote, 400);
        ImGui.InputText($"Date##edit-{entry.Id}", ref this.editDate, 16);
        ImGui.InputText($"Time##edit-{entry.Id}", ref this.editTime, 8);
        ImGui.InputText($"Location##edit-{entry.Id}", ref this.editPlace, 200);
        if (!ImGui.Button($"Save##edit-{entry.Id}"))
            return;
        if (!this.session.Log.Revise(entry.Id, this.editNote, this.editDate, this.editTime, this.editPlace, DateTimeOffset.UtcNow, this.session.Places, this.session.Channels))
            return;
        if (!entry.Accepted)
            this.session.Log.Accept(entry.Id);
        var updated = this.session.Log.Entries.First(item => item.Id == entry.Id);
        if (updated.Date is DateOnly moved)
            this.session.Show(moved);
        if (this.selectedLine is DayLine selected && selected.Entry?.Id == updated.Id)
            this.selectedLine = selected with { Entry = updated };
        this.BeginEdit(updated);
        this.save();
    }

    private void SelectLine(DayLine line)
    {
        this.selectedLine = line;
        if (line.Entry is CalendarEntry entry)
            this.BeginEdit(entry);
        else
            this.editingId = null;
    }

    private void DrawSelectedDetail()
    {
        if (this.selectedLine is not DayLine line)
            return;

        ImGui.Separator();
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
            this.DrawEdit(entry);
            if (ImGui.SmallButton($"Delete##detail-{entry.Id}"))
            {
                this.RemoveEntry(entry.Id);
                this.selectedLine = null;
            }

            this.DrawPins(entry.Place + "\n" + entry.EventText, null);
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

    private void DrawPins(string text, string? chip)
    {
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
        foreach (var spot in MapMentions.Read(text))
        {
            if (shown.Any(pin => Math.Abs(pin.X - spot.X) < 0.05f && Math.Abs(pin.Y - spot.Y) < 0.05f))
                continue;
            var caption = string.IsNullOrEmpty(place)
                ? $"Flag ({spot.X:0.0}, {spot.Y:0.0}) on your current map"
                : $"{place} ({spot.X:0.0}, {spot.Y:0.0})";
            if (ImGui.SmallButton($"{caption}##spot-{spot.X}-{spot.Y}"))
                this.openPin(place, spot.X, spot.Y, true, null);
        }
    }

    private DayLine LineFor(ScheduleOccurrence mark)
    {
        var time = TimeOnly.FromDateTime(mark.LocalStart);
        return new DayLine(time, 0, mark.Chip, mark.Name + ". " + mark.Detail, null, mark.Tone, mark.StartDate != mark.EndDate);
    }

    private void DrawDay(MonthCell cell, float side)
    {
        if (cell.Day is not int day)
        {
            ImGui.Dummy(new Vector2(side, side));
            return;
        }

        var dayId = $"day-{this.session.Year}-{this.session.Month}-{day}";
        var date = new DateOnly(this.session.Year, this.session.Month, day);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var isToday = date == today;
        if (isToday)
            ImGui.PushStyleColor(ImGuiCol.ChildBg, this.session.TodayColor);
        ImGui.BeginChild(dayId, new Vector2(side, side), true);
        if (ImGui.SmallButton($"{day}##open-{day}"))
            this.folderDay = day;
        var lines = this.GlanceFor(date);
        var spanCount = lines.Count(line => line.Span);
        if (spanCount > 0)
            ImGui.Dummy(new Vector2(1f, spanCount * this.GlanceBar()));
        var shown = 0;
        foreach (var line in lines.Where(line => !line.Span))
        {
            if (ImGui.GetCursorPosY() + this.GlanceBar() > side - 6f)
                break;
            this.DrawColorBlock(line);
            shown++;
        }

        var hidden = lines.Count(line => !line.Span) - shown;
        if (hidden > 0 && ImGui.SmallButton($"+{hidden}##more-{day}"))
            this.folderDay = day;
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
        var when = line.Time is TimeOnly time ? $"{time:HH:mm} " : "";
        var label = this.Fit(when + line.Title, width - 8f);
        draw.PushClipRect(pos, max, true);
        draw.AddText(new Vector2(pos.X + 4f, pos.Y + ((height - ImGui.GetTextLineHeight()) * 0.5f)), ImGui.ColorConvertFloat4ToU32(ink), label);
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
        if (!ImGui.Begin($"{date:dddd d MMMM}###day-folder", ref open))
        {
            ImGui.End();
            return;
        }

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
            if (!EventRepeat.FallsOn(entry, date))
                continue;
            var title = string.IsNullOrWhiteSpace(entry.Place) ? entry.EventText : entry.Place;
            lines.Add(new DayLine(entry.Time, entry.Time is null ? 2 : 1, title, entry.EventText, entry, null, false));
        }

        foreach (var mark in GameSchedule.InMonth(date.Year, date.Month, TimeZoneInfo.Local, this.session.CactpotRegion, this.session.Resets))
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
        glance.AddRange(lines.Where(line => line.Entry is not null || line.Span));
        foreach (var group in lines.Where(line => line.Entry is null && !line.Span).GroupBy(line => line.Time))
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
        if (line.Tone is ResetTone tone)
            return this.Tone(tone);
        if (line.Entry is { Accepted: true })
            return (this.session.AcceptedColor, new Vector4(1f, 1f, 1f, 1f));
        return (this.session.PendingColor, new Vector4(0.12f, 0.08f, 0.02f, 1f));
    }

    private void DrawColors()
    {
        ImGui.TextWrapped("Each swatch opens a hue wheel.");
        this.DrawColor("Pending", this.session.PendingColor, color => this.session.PendingColor = color);
        this.DrawColor("Accepted", this.session.AcceptedColor, color => this.session.AcceptedColor = color);
        this.DrawColor("Today", this.session.TodayColor, color => this.session.TodayColor = color);
        this.DrawColor("Reset", this.session.CrystalColor, color => this.session.CrystalColor = color);
        this.DrawColor("Cactpot", this.session.CactusColor, color => this.session.CactusColor = color);
        this.DrawColor("Limited event", this.session.EventColor, color => this.session.EventColor = color);
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
        var when = WhenText(entry);
        var who = string.IsNullOrWhiteSpace(entry.Sender) ? "" : entry.Sender + " ";
        var label = OngoingCheck.IsOngoing(entry, now)
            ? $"ongoing {when} {who}{entry.Place}"
            : $"{when} {who}{entry.Place}";
        var wrap = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        var textHeight = ImGui.CalcTextSize(label, false, wrap).Y + ImGui.CalcTextSize(entry.EventText, false, wrap).Y;
        var pos = ImGui.GetCursorScreenPos();
        var max = pos + new Vector2(wrap, textHeight + 4f);
        var draw = ImGui.GetWindowDrawList();
        var fill = entry.Accepted ? this.session.AcceptedColor : this.session.PendingColor;
        var ink = entry.Accepted
            ? new Vector4(1f, 1f, 1f, 1f)
            : new Vector4(0.12f, 0.08f, 0.02f, 1f);
        draw.AddRectFilled(pos, max, ImGui.ColorConvertFloat4ToU32(fill));
        ImGui.PushStyleColor(ImGuiCol.Text, ink);
        ImGui.TextWrapped(label);
        ImGui.TextWrapped(entry.EventText);
        ImGui.PopStyleColor();
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

    private static string WhenText(CalendarEntry entry)
    {
        if (entry.Time is not TimeOnly time)
            return "date only";
        return entry.End is TimeOnly end ? $"{time:HH:mm}-{end:HH:mm}" : time.ToString("HH:mm");
    }

    private void DrawClearPrompt()
    {
        if (this.prompt.IsOpen)
            ImGui.OpenPopup("Clear shouts?");

        var open = this.prompt.IsOpen;
        if (!ImGui.BeginPopupModal("Clear shouts?", ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            if (!open && this.prompt.IsOpen)
                this.prompt.AnswerNo();
            return;
        }

        ImGui.TextUnformatted(this.prompt.Question);
        if (ImGui.Button("Yes"))
        {
            this.prompt.AnswerYes(this.session.Log);
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

    }
