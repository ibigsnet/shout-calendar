using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using ShoutCalendar.Core;

namespace ShoutCalendar;

public sealed partial class CalendarWindow
{
    private double drawAverageMs;
    private double drawPeakMs;
    private long nextSnapshot;
    private long nextExpiry;
    private string sampledBook = "";
    private long sampledLocalRevision = -1;
    private CalendarMonth? gridMonth;
    private readonly Dictionary<(DateOnly From, DateOnly To), List<OvernightBar>> overnightCache = new();
    private readonly Dictionary<string, CalendarEntry> sharedEntryCache = new();
    private readonly Dictionary<string, string> titleCache = new();
    private readonly Dictionary<(DateOnly Day, bool Spans), List<NestedLine>> nestedCache = new();
    private readonly List<NightAnchor> nightAnchors = new();
    private bool serverDetails;
    private string serverCountKey = "";
    private string serverCounts = "";
    private readonly record struct NightAnchor(string Id, DateOnly Start, bool Tail, Vector2 Edge, uint Color);
    private readonly record struct NestedLine(DayLine Line, int Depth, int Descendants);

    private string TitleOf(string text)
    {
        if (!this.titleCache.TryGetValue(text, out var title))
            this.titleCache[text] = title = InviteSummary.Title(text);
        return title;
    }

    private CalendarEntry SharedEntry(SyncAnnouncement item)
    {
        if (!this.sharedEntryCache.TryGetValue(item.Id, out var entry))
            this.sharedEntryCache[item.Id] = entry = SyncClock.Entry(item);
        return entry;
    }

    private CalendarMonth GridMonth()
    {
        if (this.gridMonth is null || this.gridMonth.Year != this.session.Year || this.gridMonth.Month != this.session.Month)
            this.gridMonth = CalendarMonth.Create(this.session.Year, this.session.Month, []);
        return this.gridMonth;
    }

    private void DrawAppearance()
    {
        var appearance = this.session.Appearance;
        ImGui.TextWrapped("Start with a look, then adjust it. Your colors, events and calendar choices are preserved.");
        foreach (var preset in new[] { "Minimal", "Classic", "Soft", "Layered" })
        {
            if (ImGui.Button(preset + "##look")) { appearance.Preset(preset); this.save(); }
            if (preset != "Layered") ImGui.SameLine();
        }
        ImGui.TextDisabled("Minimal: separate day chips. Classic: flat with overnight bars.");
        ImGui.TextWrapped("Soft adds a subtle gradient. Layered nests shorter events inside longer events; it does not imply that the events are related.");
        ImGui.Separator();
        if (this.AppearanceChoice("Event layout", (int)appearance.Layout, ["Flat rows", "Nested time ranges"], out var layout))
            appearance.Layout = (CalendarLayout)layout;
        if (this.AppearanceChoice("Overnight", (int)appearance.Overnight, ["Spanning bars", "Separate day chips", "Straight arrows", "Curved arrows"], out var overnight))
            appearance.Overnight = (OvernightStyle)overnight;
        ImGui.TextWrapped("Arrows join visible chips for the same occurrence. If a chip is clipped or on another week, its clock label still shows the continuation. Events ending exactly at midnight stay on their starting day.");
        var shade = appearance.Shade;
        ImGui.SetNextItemWidth(160f);
        if (ImGui.SliderFloat("Shading", ref shade, 0f, 1f, "%.2f")) appearance.Shade = shade;
        if (ImGui.IsItemDeactivatedAfterEdit()) this.save();
        ImGui.TextDisabled("0 is flat. Try 0.15–0.30 for a gentle gradient.");
        if (this.AppearanceChoice("Invite lists", (int)appearance.Lists, ["Server > event > message", "Summaries", "Full listings"], out var lists))
            appearance.Lists = (InviteListStyle)lists;
        ImGui.TextWrapped("The tree starts closed. Open a server, then an event for facts and actions, then Message for its original text. Summary and full listings use pages of 25 invitations. Full listings show their messages and can cost more to draw.");
        var summary = appearance.ShowServerSummary;
        if (ImGui.Checkbox("Server summary bar", ref summary)) { appearance.ShowServerSummary = summary; this.save(); }
        if (this.AppearanceChoice("Low-height helper", (int)appearance.LowHeight, ["Auto (768 px or less)", "Always on", "Off"], out var lowHeight))
            appearance.LowHeight = (LowHeightMode)lowHeight;
        ImGui.TextWrapped("Auto uses the game's UI height. The helper opens yesterday, today and tomorrow, with a collapsible side panel. Normal month/week preference is preserved.");
        var lightweight = this.session.LightCalendar;
        if (ImGui.Checkbox("Lightweight grid", ref lightweight)) { this.session.LightCalendar = lightweight; this.save(); }
        ImGui.TextWrapped("Uses fewer child windows and clipped labels. The full grid supports scrolling within week days. Neither mode changes which events are saved. Try both on a busy week.");
        var timing = appearance.ShowDrawTiming;
        if (ImGui.Checkbox("Show calendar draw timing", ref timing)) { appearance.ShowDrawTiming = timing; this.save(); }
        if (timing)
        {
            ImGui.TextUnformatted($"Draw average {this.drawAverageMs:F2} ms · peak {this.drawPeakMs:F2} ms");
            if (ImGui.SmallButton("Reset peak")) this.drawPeakMs = 0;
            ImGui.TextWrapped("Measures this window's CPU drawing work, including open panels. It is not total frame time or GPU time. Compare the same date range and opened details.");
        }
        ImGui.Separator();
        ImGui.TextUnformatted("Preview");
        var origin = ImGui.GetCursorScreenPos();
        var width = MathF.Max(80, ImGui.GetContentRegionAvail().X);
        var height = ImGui.GetTextLineHeightWithSpacing() + 8;
        var draw = ImGui.GetWindowDrawList();
        this.PaintEventFill(draw, origin, origin + new Vector2(width, height), this.session.AcceptedColor);
        draw.AddText(origin + new Vector2(5, 4), ImGui.ColorConvertFloat4ToU32(appearance.Ink("Accepted", this.session.AcceptedColor)), "21:00–02:00 · Evening gathering");
        ImGui.Dummy(new Vector2(width, height + 3));
        if (ImGui.CollapsingHeader("Colors##appearance-colors")) this.DrawColors();
    }

    private bool AppearanceChoice(string label, int selected, string[] choices, out int result)
    {
        result = selected;
        var changed = false;
        ImGui.SetNextItemWidth(185f);
        if (ImGui.BeginCombo(label, choices[Math.Clamp(selected, 0, choices.Length - 1)]))
        {
            for (var i = 0; i < choices.Length; i++)
                if (ImGui.Selectable(choices[i], i == selected)) { result = i; changed = true; }
            ImGui.EndCombo();
        }
        // Caller assigns before SaveAppearanceChoices runs at the end of this frame.
        if (changed) this.appearanceChanged = true;
        return changed;
    }

    private bool appearanceChanged;
    private bool wasCompact;
    private bool compactSidebar;
    private DateOnly compactToday;
    private DateOnly savedWeekStart;
    private bool CompactMode => this.session.Appearance.Compact(ImGui.GetIO().DisplaySize.Y);
    private int VisibleDays => this.CompactMode ? 3 : 7;

    private void UpdateCompactMode()
    {
        var compact = this.CompactMode;
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (compact && !this.wasCompact)
        {
            this.savedWeekStart = this.weekStart;
            this.weekStart = today.AddDays(-1);
            this.compactToday = today;
            this.Size = new Vector2(MathF.Max(620, MathF.Min(980, ImGui.GetIO().DisplaySize.X - 20)), MathF.Max(380, ImGui.GetIO().DisplaySize.Y - 40));
            this.SizeCondition = ImGuiCond.Always;
        }
        else if (compact && this.compactToday != today)
        {
            if (this.weekStart == this.compactToday.AddDays(-1)) this.weekStart = today.AddDays(-1);
            this.compactToday = today;
        }
        else if (!compact && this.wasCompact) this.weekStart = this.savedWeekStart;
        else if (this.wasCompact == compact) this.SizeCondition = ImGuiCond.FirstUseEver;
        this.wasCompact = compact;
    }

    private SpanSegment? ViewSegment(DateOnly start, DateOnly end)
    {
        var last = this.weekStart.AddDays(this.VisibleDays - 1);
        if (end < this.weekStart || start > last) return null;
        return new SpanSegment(0, Math.Max(0, start.DayNumber - this.weekStart.DayNumber), Math.Min(this.VisibleDays - 1, end.DayNumber - this.weekStart.DayNumber));
    }
    private bool ShowChip(DayLine line, bool showSpans) => showSpans || !line.Span
        || (this.session.Appearance.Overnight != OvernightStyle.Bars && (line.Entry is not null || line.SyncId is not null));

    private void PaintEventFill(ImDrawListPtr draw, Vector2 min, Vector2 max, Vector4 fill)
    {
        if (this.session.Appearance.Shade <= 0) draw.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(fill));
        else
        {
            var top = ImGui.ColorConvertFloat4ToU32(fill);
            var bottom = ImGui.ColorConvertFloat4ToU32(LayerShade.Shadow(fill, this.session.Appearance.Shade));
            draw.AddRectFilledMultiColor(min, max, top, top, bottom, bottom);
        }
    }

    private List<NestedLine> Nested(DateOnly day, bool showSpans)
    {
        if (this.nestedCache.TryGetValue((day, showSpans), out var found)) return found;
        var lines = this.CachedGlance(day).Where(line => this.ShowChip(line, showSpans)).ToArray();
        var parents = DayNest.Parents(lines.Select(line =>
        {
            var start = line.Time ?? TimeOnly.MinValue;
            var end = line.EndAt ?? line.Entry?.End;
            return new DayInterval(start, end ?? start, end is not null && end != start,
                end is TimeOnly stop && stop < start);
        }).ToArray());
        var output = new List<NestedLine>();
        void Add(int index, int depth)
        {
            var at = output.Count;
            output.Add(new(lines[index], depth, 0));
            for (var i = 0; i < lines.Length; i++) if (parents[i] == index) Add(i, depth + 1);
            output[at] = new(lines[index], depth, output.Count - at - 1);
        }
        for (var i = 0; i < lines.Length; i++) if (parents[i] < 0) Add(i, 0);
        this.nestedCache[(day, showSpans)] = output;
        return output;
    }

    private int DrawNestedDay(DateOnly day, bool spans, float x, ref float y, float bottom, float width, float step)
    {
        var rows = this.Nested(day, spans);
        var visible = Math.Max(0, (int)MathF.Min(rows.Count, MathF.Floor((bottom - y) / step)));
        var draw = ImGui.GetWindowDrawList();
        for (var i = 0; i < visible; i++)
        {
            var node = rows[i];
            var indent = MathF.Min(node.Depth * 5, width * 0.35f);
            if (node.Descendants > 0)
            {
                var (fill, _) = this.Ink(node.Line);
                this.PaintEventFill(draw, new(x + indent, y), new(x + width, y + step * Math.Min(node.Descendants + 1, visible - i) - 2), LayerShade.Shadow(fill, 0.25f));
            }
            this.DrawFlatChip(draw, x + indent, y, width - indent, step - 3, node.Line);
            y += step;
        }
        return rows.Count - visible;
    }

    private void NoteNightAnchor(DayLine line, Vector2 min, Vector2 max, Vector4 fill)
    {
        if (this.session.Appearance.Overnight is not (OvernightStyle.Straight or OvernightStyle.Curved)
            || !line.Span || line.OnDay is not DateOnly day || (line.Entry is null && line.SyncId is null)) return;
        if (!ImGui.IsRectVisible(min, max)) return;
        var tail = line.Time == TimeOnly.MinValue;
        var start = tail ? day.AddDays(-1) : day;
        var edge = new Vector2(tail ? min.X : max.X, (min.Y + max.Y) / 2);
        this.nightAnchors.Add(new((line.Entry is null ? "sync:" : "local:") + (line.Entry?.Id ?? line.SyncId), start, tail, edge, ImGui.ColorConvertFloat4ToU32(fill)));
    }

    private void DrawNightLinks()
    {
        var draw = ImGui.GetWindowDrawList();
        foreach (var group in this.nightAnchors.GroupBy(anchor => (anchor.Id, anchor.Start)))
        {
            var start = group.FirstOrDefault(anchor => !anchor.Tail);
            var tail = group.FirstOrDefault(anchor => anchor.Tail);
            if (start.Id is null || tail.Id is null) continue;
            var offset = MathF.Max(18, MathF.Min(55, MathF.Abs(tail.Edge.X - start.Edge.X) / 2));
            var c1 = start.Edge + new Vector2(offset, 0);
            var c2 = tail.Edge - new Vector2(offset, 0);
            var curved = this.session.Appearance.Overnight == OvernightStyle.Curved;
            if (curved) draw.AddBezierCubic(start.Edge, c1, c2, tail.Edge, start.Color, 1.3f);
            else draw.AddLine(start.Edge, tail.Edge, start.Color, 1.3f);
            var dir = tail.Edge - (curved ? c2 : start.Edge);
            if (dir.LengthSquared() < 1) continue;
            dir = Vector2.Normalize(dir);
            var side = new Vector2(-dir.Y, dir.X);
            draw.AddTriangleFilled(tail.Edge, tail.Edge - dir * 6 + side * 3, tail.Edge - dir * 6 - side * 3, start.Color);
        }
    }

    private void DrawPendingTree(IEnumerable<CalendarEntry> local, IEnumerable<SyncAnnouncement> shared)
    {
        var lines = local.Select(entry => (World: entry.Server ?? "Local", Line: new DayLine(entry.Time, 1,
            (LiveInvite.IsRecent(entry.EventText, entry.DetectedAt, DateTimeOffset.UtcNow) ? "Now · " : "") + this.TitleOf(entry.EventText), entry.EventText, entry, null, false, OnDay: entry.Date)))
            .Concat(shared.Select(item => (World: ListedWorld(item), Line: new DayLine(this.SharedEntry(item).Time, 1,
                (LiveInvite.IsRecent(item.Text, item.ObservedAt, DateTimeOffset.UtcNow) ? "Now · " : "") + (item.Title.Length > 0 ? item.Title : this.TitleOf(item.Text)), item.Text, null, null, false, SyncId: item.Id, OnDay: this.SharedEntry(item).Date))));
        foreach (var group in lines.GroupBy(row => row.World).OrderBy(group => group.Key))
        {
            if (!ImGui.TreeNode($"{group.Key} · {group.Count()} invites##pending-world-{group.Key}")) continue;
            foreach (var row in group) this.DrawFolderRow(row.Line);
            ImGui.TreePop();
        }
    }

    private int pendingPage;
    private void DrawPendingPages(IEnumerable<CalendarEntry> local, IEnumerable<SyncAnnouncement> shared)
    {
        var rows = local.Select(entry => (Local: (CalendarEntry?)entry, Shared: (SyncAnnouncement?)null))
            .Concat(shared.Select(item => (Local: (CalendarEntry?)null, Shared: (SyncAnnouncement?)item))).ToArray();
        const int pageSize = 25;
        var pages = Math.Max(1, (rows.Length + pageSize - 1) / pageSize);
        this.pendingPage = Math.Clamp(this.pendingPage, 0, pages - 1);
        if (ImGui.SmallButton("Previous##pending-page")) this.pendingPage = Math.Max(0, this.pendingPage - 1);
        ImGui.SameLine();
        ImGui.TextUnformatted($"{this.pendingPage + 1}/{pages} · {rows.Length} invites");
        ImGui.SameLine();
        if (ImGui.SmallButton("Next##pending-page")) this.pendingPage = Math.Min(pages - 1, this.pendingPage + 1);
        foreach (var row in rows.Skip(this.pendingPage * pageSize).Take(pageSize))
        {
            if (row.Local is CalendarEntry entry)
            {
                this.DrawPendingRow(entry);
                if (this.session.Appearance.Lists == InviteListStyle.Full) this.DrawNote(entry.EventText, "");
            }
            else if (row.Shared is SyncAnnouncement item)
            {
                this.DrawSharedRow(item);
                if (this.session.Appearance.Lists == InviteListStyle.Full) this.DrawNote(item.Text, "");
            }
        }
    }

    private string VisibleServerCounts()
    {
        var first = this.weekView || this.CompactMode ? this.weekStart : new DateOnly(this.session.Year, this.session.Month, 1);
        var last = this.weekView || this.CompactMode ? first.AddDays(this.VisibleDays - 1) : first.AddMonths(1).AddDays(-1);
        var key = $"{this.glanceKey}|{first}|{last}";
        if (key == this.serverCountKey) return this.serverCounts;
        var seen = new HashSet<string>();
        var counts = new SortedDictionary<string, int>();
        for (var date = first; date <= last; date = date.AddDays(1))
            foreach (var line in this.CachedGlance(date))
            {
                if (line.Entry is null && line.SyncId is null) continue;
                var start = line.Span && line.Time == TimeOnly.MinValue ? date.AddDays(-1) : date;
                if (!seen.Add($"{(line.Entry is null ? "sync" : "local")}|{line.Entry?.Id ?? line.SyncId}|{start}")) continue;
                var world = line.Entry is CalendarEntry entry ? ShareWorld.Choose("", entry.SpeakerWorld, entry.Server, entry.EventText)
                    : this.syncFrame.FirstOrDefault(item => item.Id == line.SyncId) is SyncAnnouncement item ? ListedWorld(item) : "";
                if (world.Length == 0) world = "Unspecified";
                counts[world] = counts.GetValueOrDefault(world) + 1;
            }
        this.serverCountKey = key;
        return this.serverCounts = $"Events shown · {first:MMM d}–{last:MMM d}: " + (counts.Count == 0 ? "none match the current filters" : string.Join(" · ", counts.Select(pair => $"{pair.Key} {pair.Value}")))
            + ". Counts use advertised destinations when available; otherwise the stored world. Recurring occurrences count separately; overnight continuations count once.";
    }
}
