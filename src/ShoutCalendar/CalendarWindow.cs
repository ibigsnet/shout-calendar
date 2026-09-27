using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using ShoutCalendar.Core;

namespace ShoutCalendar;

public sealed class CalendarWindow : Window
{
    private readonly CalendarSession session;
    private readonly ClearPrompt prompt;
    private readonly Action save;
    private string? selectedId;
    private string? editingId;
    private string editNote = "";
    private string editDate = "";
    private string editTime = "";
    private string editPlace = "";
    private string holdDaysText = "";
    private bool holdDaysReady;

    public CalendarWindow(CalendarSession session, ClearPrompt prompt, Action save)
        : base("Shout Calendar (provisional)")
    {
        this.session = session;
        this.prompt = prompt;
        this.save = save;
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
        this.DrawClearPrompt();
    }

    private void DrawHistory()
    {
        ImGui.BeginChild("shout-history", new Vector2(340, 0), true);
        if (this.session.Log.ExpireUnaccepted(DateTimeOffset.UtcNow, this.session.UnacceptedHoldDays) > 0)
            this.save();

        ImGui.TextUnformatted("Pending");
        this.DrawHoldDays();
        this.DrawAggressiveFilter();
        var rowRight = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        var continued = false;
        this.DrawWrappingButton("Clear all", rowRight, ref continued, () => this.prompt.Ask(ClearTarget.All));
        this.DrawWrappingButton("Clear accepted", rowRight, ref continued, () => this.prompt.Ask(ClearTarget.Accepted));
        this.DrawWrappingButton("Clear unaccepted", rowRight, ref continued, () => this.prompt.Ask(ClearTarget.Unaccepted));
        this.DrawChannelOptions();

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
            ImGui.TextUnformatted($"{dayText} {WhenText(entry)}");
            if (!string.IsNullOrWhiteSpace(entry.Place))
                ImGui.TextWrapped(entry.Place);
            ImGui.TextWrapped(entry.EventText);
            if (ImGui.Button($"Edit##{entry.Id}"))
                this.BeginEdit(entry);
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

        ImGui.EndChild();
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
        if (!ImGui.Checkbox("Aggressive filter (2 of date, time, place)##aggressive", ref aggressive))
            return;
        this.session.AggressiveFilter = aggressive;
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
            this.DrawDay(month.Cells[index], side, now);
        }

        ImGui.SetCursorScreenPos(grid + new Vector2(0, rows * (side + gap)));
        ImGui.Dummy(new Vector2(1f, 1f));
        this.DrawUndated(now);
        ImGui.EndChild();
    }

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
        if (this.session.Log.Revise(entry.Id, this.editNote, this.editDate, this.editTime, this.editPlace, DateTimeOffset.UtcNow, this.session.Places, this.session.Channels))
        {
            var updated = this.session.Log.Entries.First(item => item.Id == entry.Id);
            if (updated.Date is DateOnly moved)
                this.session.Show(moved);
            this.save();
        }

        this.editingId = null;
    }

    private void DrawDay(MonthCell cell, float side, DateTimeOffset now)
    {
        if (cell.Day is not int day)
        {
            ImGui.Dummy(new Vector2(side, side));
            return;
        }

        var dayId = $"day-{this.session.Year}-{this.session.Month}-{day}";
        ImGui.BeginChild(dayId, new Vector2(side, side), true);
        ImGui.TextUnformatted(day.ToString());
        foreach (var entry in cell.Entries)
            this.DrawEvent(entry, now);
        ImGui.EndChild();
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
        var fill = entry.Accepted
            ? new Vector4(0.12f, 0.48f, 0.24f, 0.95f)
            : new Vector4(0.93f, 0.62f, 0.12f, 0.95f);
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
