using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Chat;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using Lumina.Excel.Sheets;
using ShoutCalendar.Core;

namespace ShoutCalendar;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;

    private readonly PluginConfig config;
    private readonly CalendarSession session;
    private readonly ClearPrompt clearPrompt = new();
    private readonly WindowSystem windowSystem;
    private readonly CalendarWindow window;
    private readonly FileDialogManager dialogs = new();
    private readonly Dictionary<string, uint> questIds = new(StringComparer.OrdinalIgnoreCase);
    private bool questsScanned;
    private DateTime? alarmMinute;

    public Plugin()
    {
        if ((int)XivChatType.Shout != ShoutHarvest.ShoutChannel)
            throw new InvalidOperationException("XivChatType.Shout does not match the documented shout channel byte.");

        this.config = PluginInterface.GetPluginConfig() as PluginConfig ?? new PluginConfig();
        this.session = new CalendarSession(DateOnly.FromDateTime(DateTime.UtcNow));
        try
        {
            this.session.Places = PlaceCatalogLoader.Load(DataManager);
            Log.Information("Loaded {Count} place names from the game data.", this.session.Places.Count);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Could not read place names from the game data.");
        }

        this.session.UseChannels(this.config.WatchedChannels);
        this.session.UnacceptedHoldDays = this.config.UnacceptedHoldDays < 1 ? 14 : this.config.UnacceptedHoldDays;
        this.session.AggressiveFilter = this.config.AggressiveFilter;
        this.session.AlarmAccepted = this.config.AlarmAccepted;
        this.session.AlarmUnaccepted = this.config.AlarmUnaccepted;
        this.session.AcceptedSound = EventAlarm.ClampSound(this.config.AcceptedSound);
        this.session.UnacceptedSound = EventAlarm.ClampSound(this.config.UnacceptedSound);
        this.session.ResetSound = EventAlarm.ClampSound(this.config.ResetSound);
        this.session.AcceptedSoundFile = this.config.AcceptedSoundFile ?? "";
        this.session.UnacceptedSoundFile = this.config.UnacceptedSoundFile ?? "";
        this.session.ResetSoundFile = this.config.ResetSoundFile ?? "";
        this.session.AlarmResets = this.config.AlarmResets;
        this.session.PendingColor = Shown(this.config.PendingColor, new Vector4(0.93f, 0.62f, 0.12f, 0.95f));
        this.session.AcceptedColor = Shown(this.config.AcceptedColor, new Vector4(0.12f, 0.48f, 0.24f, 0.95f));
        this.session.TodayColor = Shown(this.config.TodayColor, new Vector4(0.183f, 0.183f, 0.183f, 1f));
        this.session.CrystalColor = Shown(this.config.CrystalColor, new Vector4(0.18f, 0.52f, 0.86f, 0.95f));
        this.session.CactusColor = Shown(this.config.CactusColor, new Vector4(0.55f, 0.78f, 0.22f, 0.95f));
        this.session.EventColor = Shown(this.config.EventColor, new Vector4(0f, 0f, 1f, 0.64f));
        this.session.AlarmMinutesBefore = this.config.AlarmMinutesBefore < 0 ? 0 : this.config.AlarmMinutesBefore;
        this.session.UseResets(this.config.EnabledResets);
        this.session.CactpotRegion = GameSchedule.NormalizeRegion(this.config.CactpotRegion);
        this.session.Log.Restore(this.config.ToEntries());
        if (this.session.Log.ExpireUnaccepted(DateTimeOffset.UtcNow, this.session.UnacceptedHoldDays) > 0)
            this.Save();

        this.window = new CalendarWindow(this.session, this.clearPrompt, this.Save, this.PlayAlarm, this.dialogs, this.OpenPin);
        this.windowSystem = new WindowSystem("ShoutCalendar");
        this.windowSystem.AddWindow(this.window);

        CommandManager.AddHandler(CalendarCommand.Open, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Open the calendar. '/shoutcalendar test' tries a local shout and does not send it.",
        });

        PluginInterface.UiBuilder.Draw += this.windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += this.OpenMain;
        PluginInterface.UiBuilder.OpenConfigUi += this.OpenMain;
        ChatGui.ChatMessage += this.OnChat;
        Framework.Update += this.OnFramework;
        this.ImportLogs();
    }

    public void Dispose()
    {
        Framework.Update -= this.OnFramework;
        ChatGui.ChatMessage -= this.OnChat;
        PluginInterface.UiBuilder.Draw -= this.windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= this.OpenMain;
        PluginInterface.UiBuilder.OpenConfigUi -= this.OpenMain;
        CommandManager.RemoveHandler(CalendarCommand.Open);
        this.windowSystem.RemoveAllWindows();
    }

    private void OnCommand(string command, string args)
    {
        if (args.StartsWith("test", StringComparison.OrdinalIgnoreCase))
        {
            this.RunLocalTest(args["test".Length..].Trim());
            return;
        }

        this.window.Toggle();
    }

    private void RunLocalTest(string text)
    {
        if (text.Length == 0)
            text = "8:00pm at The Goblet W22 P35";
        var added = this.session.TryAddShout(text, ShoutHarvest.ShoutChannel, DateTimeOffset.UtcNow, "Test Shout");
        if (added)
            this.Save();
        this.window.IsOpen = true;
        ChatGui.Print(added
            ? "Shout Calendar kept that as a pending shout."
            : this.session.AggressiveFilter
                ? "Shout Calendar did not keep that. Aggressive filter needs two of a date, a time, and a place."
                : "Shout Calendar did not keep that. It needs a time or a place.");
    }

    private void OpenMain()
    {
        this.window.Toggle();
    }

    private void OnFramework(IFramework framework)
    {
        var now = DateTime.Now;
        var hits = EventAlarm.Due(
            this.session.Log.Entries,
            now,
            this.alarmMinute,
            this.session.AlarmAccepted,
            this.session.AlarmUnaccepted,
            this.session.AlarmMinutesBefore);
        if (this.session.AlarmResets)
        {
            var resets = GameSchedule.Due(
                now,
                this.alarmMinute,
                TimeZoneInfo.Local,
                this.session.CactpotRegion,
                this.session.Resets,
                this.session.AlarmMinutesBefore);
            if (resets.Count > 0)
            {
                ChatGui.Print("Shout Calendar: reset " + string.Join(", ", resets.Select(mark => mark.Name)) + ".");
                this.PlayAlarm(this.session.ResetSound, this.session.ResetSoundFile);
            }
        }

        var minute = EventAlarm.MinuteOf(now);
        if (this.alarmMinute != minute)
            this.alarmMinute = minute;

        foreach (var hit in hits)
        {
            var entry = this.session.Log.Entries.FirstOrDefault(candidate => candidate.Id == hit.Id);
            if (entry is null)
                continue;
            this.Announce(entry, hit.Accepted);
        }
    }

    private void Announce(CalendarEntry entry, bool accepted)
    {
        var when = entry.Time?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "";
        var place = string.IsNullOrWhiteSpace(entry.Place) ? "" : " " + entry.Place;
        var note = entry.EventText.Length > 80 ? entry.EventText[..80] : entry.EventText;
        var kind = accepted ? "accepted" : "pending";
        ChatGui.Print($"Shout Calendar: {kind} {when}{place}. {note}");
        this.PlayAlarm(
            accepted ? this.session.AcceptedSound : this.session.UnacceptedSound,
            accepted ? this.session.AcceptedSoundFile : this.session.UnacceptedSoundFile);
    }

    private void PlayAlarm(int sound, string? file)
    {
        if (AlarmPlayback.TryPlayFile(file))
            return;
        this.PreviewSound(sound);
        if (!string.IsNullOrWhiteSpace(file))
            ChatGui.Print($"Shout Calendar: could not play that file. Using <se.{EventAlarm.ClampSound(sound)}>.");
    }

    private void OpenPin(string place, float x, float y, bool hasMap, string? quest)
    {
        if (hasMap)
            this.OpenMap(place, x, y);
        if (!string.IsNullOrWhiteSpace(quest))
            this.PrintQuest(quest);
    }

    private void OpenMap(string place, float x, float y)
    {
        SeString? link = null;
        if (!string.IsNullOrWhiteSpace(place))
            link = SeString.CreateMapLink(place, x, y);
        if (link is null && ClientState.TerritoryType != 0
            && DataManager.GetExcelSheet<TerritoryType>().TryGetRow(ClientState.TerritoryType, out var territory))
        {
            link = SeString.CreateMapLink(ClientState.TerritoryType, territory.Map.RowId, x, y);
        }

        if (link is null)
        {
            ChatGui.Print($"Shout Calendar: could not find a map for {place}.");
            return;
        }

        var payload = link.Payloads.OfType<MapLinkPayload>().FirstOrDefault();
        if (payload is not null)
        {
            GameGui.OpenMapWithMapLink(payload);
            ImGui.SetClipboardText(payload.CoordinateString);
        }

        ChatGui.Print(link);
    }

    private void PrintQuest(string name)
    {
        var id = this.FindQuest(name);
        if (id is null)
        {
            ChatGui.Print($"Shout Calendar: could not find the quest {name}.");
            return;
        }

        ChatGui.Print(new SeStringBuilder().AddText("Shout Calendar: ").AddQuestLink(id.Value).Build());
    }

    private uint? FindQuest(string name)
    {
        if (!this.questsScanned)
        {
            foreach (var row in DataManager.GetExcelSheet<Quest>())
            {
                var text = row.Name.ExtractText().Trim();
                if (text.Length > 0)
                    this.questIds.TryAdd(text, row.RowId);
            }

            this.questsScanned = true;
        }

        return this.questIds.TryGetValue(name, out var id) ? id : null;
    }

    private void PreviewSound(int sound)
    {
        try
        {
            UIGlobals.PlayChatSoundEffect((uint)EventAlarm.ClampSound(sound));
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Could not play chat sound {Sound}.", sound);
        }
    }

    private void OnChat(IHandleableChatMessage message)
    {
        var when = ChatTime.FromUnixOrNow(message.Timestamp, DateTimeOffset.UtcNow);

        var text = message.Message.TextValue;
        if (text.StartsWith("Shout Calendar:", StringComparison.Ordinal))
            return;
        var sender = message.Sender.TextValue;
        this.session.HousingHint = this.CurrentHousingDistrict();
        if (!this.session.TryAddShout(text, (int)message.LogKind, when, sender))
            return;

        this.Save();
    }

    private string? CurrentHousingDistrict()
    {
        try
        {
            if (ClientState.TerritoryType == 0)
                return null;
            var row = DataManager.GetExcelSheet<TerritoryType>().GetRow(ClientState.TerritoryType);
            var zone = row.PlaceName.Value.Name.ExtractText();
            var region = row.PlaceNameRegion.Value.Name.ExtractText();
            return HousingDistrict.FromZone($"{zone} {region}");
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Vector4 Shown(Vector4 color, Vector4 fallback) => color.W <= 0f ? fallback : color;

    private void Save()
    {
        this.config.Events.Clear();
        foreach (var entry in this.session.Log.Entries)
            this.config.Add(entry);
        this.config.WatchedChannels = this.session.Channels.Order().ToList();
        this.config.UnacceptedHoldDays = this.session.UnacceptedHoldDays < 1 ? 14 : this.session.UnacceptedHoldDays;
        this.config.AggressiveFilter = this.session.AggressiveFilter;
        this.config.AlarmAccepted = this.session.AlarmAccepted;
        this.config.AlarmUnaccepted = this.session.AlarmUnaccepted;
        this.config.AcceptedSound = EventAlarm.ClampSound(this.session.AcceptedSound);
        this.config.UnacceptedSound = EventAlarm.ClampSound(this.session.UnacceptedSound);
        this.config.ResetSound = EventAlarm.ClampSound(this.session.ResetSound);
        this.config.AcceptedSoundFile = this.session.AcceptedSoundFile ?? "";
        this.config.UnacceptedSoundFile = this.session.UnacceptedSoundFile ?? "";
        this.config.ResetSoundFile = this.session.ResetSoundFile ?? "";
        this.config.AlarmResets = this.session.AlarmResets;
        this.config.PendingColor = this.session.PendingColor;
        this.config.AcceptedColor = this.session.AcceptedColor;
        this.config.TodayColor = this.session.TodayColor;
        this.config.CrystalColor = this.session.CrystalColor;
        this.config.CactusColor = this.session.CactusColor;
        this.config.EventColor = this.session.EventColor;
        this.config.AlarmMinutesBefore = this.session.AlarmMinutesBefore < 0 ? 0 : this.session.AlarmMinutesBefore;
        this.config.EnabledResets = this.session.Resets.Order().ToList();
        this.config.CactpotRegion = GameSchedule.NormalizeRegion(this.session.CactpotRegion);
        PluginInterface.SavePluginConfig(this.config);
    }

    private void ImportLogs()
    {
        var seen = new HashSet<string>(this.config.ImportedLogLines, StringComparer.Ordinal);
        var added = 0;
        foreach (var directory in LogLocations.LogDirectories())
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*.log", SearchOption.TopDirectoryOnly).ToList();
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
                added += this.ImportFile(file, seen);
        }

        if (added > 0)
            this.Save();
        if (added > 0)
            Log.Information("Recorded {Count} pending shouts from chat logs.", added);
    }

    private int ImportFile(string file, HashSet<string> seen)
    {
        byte[] bytes;
        try
        {
            var info = new FileInfo(file);
            if (info.Length <= 0 || info.Length > 8 * 1024 * 1024)
                return 0;
            bytes = File.ReadAllBytes(file);
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }

        var added = 0;
        foreach (var line in ChatLogReader.Read(bytes))
        {
            var key = line.TimestampUnix.ToString(CultureInfo.InvariantCulture)
                + ":"
                + line.Channel.ToString(CultureInfo.InvariantCulture)
                + ":"
                + line.Message;
            if (seen.Contains(key))
                continue;

            DateTimeOffset when;
            try
            {
                var stamped = DateTimeOffset.FromUnixTimeSeconds(line.TimestampUnix);
                if (stamped.UtcDateTime.Year is < 2000 or > 2100)
                    continue;
                when = stamped;
            }
            catch (ArgumentOutOfRangeException)
            {
                continue;
            }

            if (!this.session.TryAddShout(line.Message, line.Channel, when, line.Sender))
                continue;

            seen.Add(key);
            this.config.ImportedLogLines.Add(key);
            added++;
        }

        return added;
    }
}
