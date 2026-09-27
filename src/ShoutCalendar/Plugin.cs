using System.Globalization;
using Dalamud.Game.Chat;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
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

    private readonly PluginConfig config;
    private readonly CalendarSession session;
    private readonly ClearPrompt clearPrompt = new();
    private readonly WindowSystem windowSystem;
    private readonly CalendarWindow window;

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
        this.session.Log.Restore(this.config.ToEntries());
        if (this.session.Log.ExpireUnaccepted(DateTimeOffset.UtcNow, this.session.UnacceptedHoldDays) > 0)
            this.Save();

        this.window = new CalendarWindow(this.session, this.clearPrompt, this.Save);
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
        this.ImportLogs();
    }

    public void Dispose()
    {
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

    private void OnChat(IHandleableChatMessage message)
    {
        var when = ChatTime.FromUnixOrNow(message.Timestamp, DateTimeOffset.UtcNow);

        var text = message.Message.TextValue;
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

    private void Save()
    {
        this.config.Events.Clear();
        foreach (var entry in this.session.Log.Entries)
            this.config.Add(entry);
        this.config.WatchedChannels = this.session.Channels.Order().ToList();
        this.config.UnacceptedHoldDays = this.session.UnacceptedHoldDays < 1 ? 14 : this.session.UnacceptedHoldDays;
        this.config.AggressiveFilter = this.session.AggressiveFilter;
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
