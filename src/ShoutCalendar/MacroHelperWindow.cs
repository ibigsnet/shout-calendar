using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace ShoutCalendar;

/// <summary>Macro guide drawn as its own window, so the game UI stays usable.</summary>
public sealed class MacroHelperWindow : Window
{
    private const string MacroName = "Shout Calendar";
    private const string MacroBody = "/micon \"Decipher\" general\n/shoutcalendar";

    private readonly Action? openMacros;
    private readonly Action? onClose;
    private string copied = "";

    public MacroHelperWindow(Action? openMacros, Action? onClose)
        : base("Make a macro###shout-calendar-macro")
    {
        this.openMacros = openMacros;
        this.onClose = onClose;
        this.Size = new Vector2(460f, 300f);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.Position = new Vector2(40f, 60f);
        this.PositionCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420f, 260f),
            MaximumSize = new Vector2(680f, 460f),
        };
        this.RespectCloseHotkey = false;
        this.AllowPinning = false;
        this.AllowClickthrough = false;
    }

    public override void OnClose() => this.onClose?.Invoke();

    public override void Draw()
    {
        ImGui.TextWrapped("Drag this window beside User Macros. The calendar steps aside until you close this.");
        ImGui.Separator();
        ImGui.TextDisabled("Name");
        ImGui.TextUnformatted(MacroName);
        if (ImGui.Button("Copy name##macro-name"))
        {
            ImGui.SetClipboardText(MacroName);
            this.copied = "Name copied.";
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Macro lines");
        ImGui.BeginChild("##macro-body", new Vector2(-1f, 52f), true);
        ImGui.TextUnformatted(MacroBody);
        ImGui.EndChild();
        if (ImGui.Button("Copy macro lines##macro-copy"))
        {
            ImGui.SetClipboardText(MacroBody);
            this.copied = "Macro lines copied.";
        }

        ImGui.SameLine();
        if (ImGui.Button("Open User Macros##macro-open"))
            this.openMacros?.Invoke();

        if (!string.IsNullOrWhiteSpace(this.copied))
            ImGui.TextUnformatted(this.copied);

        ImGui.Separator();
        ImGui.TextWrapped("In User Macros: pick an empty slot, set the name, paste the lines, then drag the macro to a hotbar.");
    }
}
