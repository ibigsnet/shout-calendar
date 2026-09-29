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
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
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
    [PluginService] internal static IObjectTable Objects { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IAetheryteList AetheryteList { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;

    private readonly PluginConfig config;
    private readonly CalendarSession session;
    private readonly ClearPrompt clearPrompt = new();
    private readonly WindowSystem windowSystem;
    private readonly CalendarWindow window;
    private readonly MacroHelperWindow macroHelper;
    private bool restoreCalendarAfterMacro;
    private readonly FileDialogManager dialogs = new();
    private readonly Dictionary<string, uint> questIds = new(StringComparer.OrdinalIgnoreCase);
    private bool questsScanned;
    private DateTime? alarmMinute;
    private readonly HashSet<string> alarmSeen = new();
    private bool alarmSeeded;
    private SyncBook? syncBook;
    private ICallGateProvider<byte[], string, bool>? syncAttach;
    private ICallGateProvider<byte[], bool>? syncDetach;
    private ICallGateProvider<byte[], string>? syncPull;
    private ICallGateProvider<byte[], int, string, string>? syncIngest;
    private ICallGateProvider<byte[], string>? syncRead;
    private ICallGateProvider<byte[], string, bool>? syncApply;
    private ICallGateProvider<byte[], string, bool>? syncStatus;
    private ICallGateProvider<byte[], int, string, string>? syncIngestV2;
    private ICallGateProvider<bool>? openCalendar;
    private readonly CancellationTokenSource relayCancel = new();
    private Task? relayWatch;

    public Plugin()
    {
        if ((int)XivChatType.Shout != ShoutHarvest.ShoutChannel)
            throw new InvalidOperationException("XivChatType.Shout does not match the documented shout channel byte.");

        this.config = PluginInterface.GetPluginConfig() as PluginConfig ?? new PluginConfig();
        this.session = new CalendarSession(DateOnly.FromDateTime(DateTime.Now))
        {
            Zone = TimeZoneInfo.Local,
        };
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
        if (this.config.Listening == false)
            this.session.RememberPaused(this.config.PausedChannels);
        this.session.UnacceptedHoldDays = this.config.UnacceptedHoldDays < 1 ? 14 : this.config.UnacceptedHoldDays;
        this.session.AggressiveFilter = this.config.AggressiveFilter;
        this.session.Informedaholic = this.config.Informedaholic;
        this.session.RememberLinkChoice = this.config.RememberLinkChoice;
        this.session.OpenRememberedLinks = this.config.OpenRememberedLinks;
        this.session.AlarmAccepted = this.config.AlarmAccepted;
        this.session.AlarmChat = this.config.AlarmChat ?? true;
        this.session.DropPastEvents = this.config.DropPastEvents;
        this.session.ShowPastLocal = this.config.ShowPastLocal;
        this.session.ShowPastSync = this.config.ShowPastSync;
        this.session.TextScale = this.config.TextScale is >= 0.85f and <= 2f ? this.config.TextScale : 1f;
        this.session.AlarmUnaccepted = this.config.AlarmUnaccepted;
        this.session.AcceptedSound = EventAlarm.ClampSound(this.config.AcceptedSound);
        this.session.UnacceptedSound = EventAlarm.ClampSound(this.config.UnacceptedSound);
        this.session.ResetSound = EventAlarm.ClampSound(this.config.ResetSound);
        this.session.AcceptedSoundFile = this.config.AcceptedSoundFile ?? "";
        this.session.UnacceptedSoundFile = this.config.UnacceptedSoundFile ?? "";
        this.session.ResetSoundFile = this.config.ResetSoundFile ?? "";
        this.session.AlarmResets = this.config.AlarmResets;
        this.session.PendingColor = Shown(this.config.PendingColor, new Vector4(0.93f, 0.62f, 0.12f, 0.95f));
        this.session.ShadeStrength = Math.Clamp(this.config.ShadeStrength ?? 0f, 0f, 3f);
        foreach (var key in this.config.InkFlips ?? [])
        {
            if (!string.IsNullOrWhiteSpace(key))
                this.session.InkFlips.Add(key);
        }
        this.session.AcceptedColor = Shown(this.config.AcceptedColor, new Vector4(0.12f, 0.48f, 0.24f, 0.95f));
        this.session.TodayColor = Shown(this.config.TodayColor, new Vector4(1f, 1f, 1f, 0.19f));
        this.session.OutsideColor = Shown(this.config.OutsideColor, new Vector4(0.22f, 0.22f, 0.24f, 0.427f));
        this.session.CrystalColor = Shown(this.config.CrystalColor, new Vector4(0.18f, 0.52f, 0.86f, 0.95f));
        this.session.CactusColor = Shown(this.config.CactusColor, new Vector4(0.55f, 0.78f, 0.22f, 0.95f));
        this.session.EventColor = Shown(this.config.EventColor, new Vector4(0.144f, 0f, 1f, 0.64f));
        this.session.AlarmMinutesBefore = this.config.AlarmMinutesBefore < 0 ? 0 : this.config.AlarmMinutesBefore;
        this.session.AlarmAtStart = this.config.AlarmAtStart;
        this.session.ShowLocal = this.config.ShowLocal ?? true;
        this.session.ShowLocalAccepted = this.config.ShowLocalAccepted ?? this.config.ShowLocal ?? true;
        this.session.ShowLocalUnaccepted = this.config.ShowLocalUnaccepted ?? this.config.ShowLocal ?? true;
        this.session.ShowSyncAccepted = this.config.ShowSyncAccepted ?? true;
        this.session.ShowSyncUnaccepted = this.config.ShowSyncUnaccepted ?? true;
        this.session.ShowHidden = this.config.ShowHidden ?? false;
        this.session.NewestFirst = this.config.NewestFirst;
        this.session.WeekDetailShare = this.config.WeekDetailShare is > 0.08f and < 0.85f
            ? this.config.WeekDetailShare
            : 0.28f;
        this.session.ShowAllServers = this.config.ShowAllServers;
        this.session.PendingScope = this.config.PendingScope ?? (this.config.ShowAllServers ? PendingScope.OpenCalendars : PendingScope.CurrentWorld);
        this.session.PauseInPvp = this.config.PauseInPvp ?? true;
        this.session.ShowResets = this.config.ShowResets ?? true;
        this.session.LightCalendar = this.config.FastCalendar ?? true;
        this.session.Appearance = this.config.Appearance ?? CalendarAppearance.Migrate(this.config.ShadeStrength, this.config.InkFlips);
        this.session.Appearance.Normalize();
        this.session.ParseDebug = this.config.ParseDebug;
        this.session.ParseDebugSound = this.config.ParseDebugSound;
        var debugSound = this.config.ParseDebugSoundEffect;
        this.session.ParseDebugSoundEffect = debugSound == 0 ? 2 : EventAlarm.ClampSound(debugSound);
        this.session.ParseDebugSoundFile = this.config.ParseDebugSoundFile ?? "";
        this.session.WeekView = this.config.WeekView;
        this.session.SyncPendingColor = Shown(this.config.SyncPendingColor, new Vector4(0.63f, 0.28f, 0.72f, 0.95f));
        this.session.SharedBarColor = Shown(this.config.SharedBarColor, new Vector4(0.95f, 0.05f, 0.05f, 1f));
        this.session.TwitchColor = Shown(this.config.TwitchColor, new Vector4(0.569f, 0.275f, 1f, 0.95f));
        this.session.DiscordColor = Shown(this.config.DiscordColor, new Vector4(0.345f, 0.396f, 0.949f, 0.95f));
        this.session.UseResets(this.config.EnabledResets);
        this.session.CactpotRegion = GameSchedule.NormalizeRegion(this.config.CactpotRegion);
        this.session.Log.Restore(this.config.ToEntries());
        foreach (var id in this.config.PinnedSync ?? [])
        {
            if (!string.IsNullOrWhiteSpace(id))
                this.session.PinnedSync.Add(id);
        }
        var repaired = this.session.Log.Reharvest(entry =>
        {
            var when = entry.DetectedAt == default ? DateTimeOffset.Now : entry.DetectedAt;
            var channel = entry.Channel == 0 ? ShoutHarvest.ShoutChannel : entry.Channel;
            return ShoutHarvest.TryHarvest(
                entry.EventText,
                channel,
                when,
                this.session.Places,
                this.session.Channels,
                this.session.HousingHint,
                aggressive: false,
                zone: this.session.Zone);
        });
        var folded = this.session.Log.FoldReposts();
        if (repaired > 0 || folded > 0 || this.session.Log.ExpireUnaccepted(DateTimeOffset.UtcNow, this.session.UnacceptedHoldDays) > 0)
            this.Save();

        this.window = new CalendarWindow(this.session, this.clearPrompt, this.Save, this.PlayAlarm, this.dialogs, this.OpenPin, this.OpenHousing, this.RequestSyncNow, this.ShowMacroHelper, this.RequestSyncResync);
        this.macroHelper = new MacroHelperWindow(this.OpenUserMacros, this.RestoreCalendar);
        this.windowSystem = new WindowSystem("ShoutCalendar");
        this.windowSystem.AddWindow(this.window);
        this.windowSystem.AddWindow(this.macroHelper);

        CommandManager.AddHandler(CalendarCommand.Open, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Open the calendar. '/shoutcalendar test' tries a local shout and does not send it.",
        });

        PluginInterface.UiBuilder.Draw += this.windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += this.OpenMain;
        PluginInterface.UiBuilder.OpenConfigUi += this.OpenMain;
        ChatGui.ChatMessage += this.OnChat;
        Framework.Update += this.OnFramework;
        this.RegisterSyncHook();
        this.ImportLogs();
        this.relayWatch = Task.Run(() => this.WatchRelay(this.relayCancel.Token));
    }

    public void Dispose()
    {
        this.relayCancel.Cancel();
        try
        {
            this.relayWatch?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) { }

        this.relayCancel.Dispose();
        this.UnregisterSyncHook();
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
            ? this.session.Informedaholic
                ? "Shout Calendar accepted that invite."
                : "Shout Calendar kept that as a pending shout."
            : this.session.AggressiveFilter
                ? "Shout Calendar did not keep that. Aggressive filter needs two of a date, a time, and a place."
                : "Shout Calendar did not keep that. It needs a time or a place.");
    }

    private void OpenMain()
    {
        this.window.Toggle();
    }

    private bool PausedForPvp() => this.session.PauseInPvp && ClientState.IsPvPExcludingDen;

    private long nextCalendarTick;

    private void OnFramework(IFramework framework)
    {
        // Alarm clocks have minute precision; world tracking, cleanup and acceptance notices need no per-frame scan.
        if (Environment.TickCount64 < this.nextCalendarTick) return;
        this.nextCalendarTick = Environment.TickCount64 + 1000;
        if (this.PausedForPvp())
            return;
        var standing = this.DetectedWorld("");
        if (standing.Length > 0)
            this.session.CurrentWorld = standing;
        if (this.syncBook is not null)
            this.syncBook.Worlds.Notice(this.DetectedWorld(this.syncBook.Worlds.Here));
        var now = DateTime.Now;
        if (this.session.DropPastEvents)
        {
            var dropped = this.session.Log.ClearPast(now);
            var shared = this.syncBook?.DismissPast(now) ?? 0;
            if (dropped + shared > 0)
                this.Save();
        }

        var alarmEntries = this.session.Log.Entries.Where(entry => !entry.Hidden && this.session.ShowsChannel(entry.Channel)).Concat(
            (this.syncBook?.CopyEvents() ?? []).Where(item => item.FromSync && !item.Hidden && !item.Declined && this.session.ShowsChannel(item.Channel))
            .Select(item => SyncClock.Entry(item) with { Id = "sync:" + item.Id })).ToArray();
        var hits = EventAlarm.Due(
            alarmEntries,
            now,
            this.alarmMinute,
            this.session.AlarmAccepted,
            this.session.AlarmUnaccepted,
            this.session.AlarmMinutesBefore,
            TimeZoneInfo.Local);
        if (this.session.AlarmAtStart && this.session.AlarmMinutesBefore > 0)
        {
            var starting = EventAlarm.Due(
                alarmEntries,
                now,
                this.alarmMinute,
                this.session.AlarmAccepted,
                this.session.AlarmUnaccepted,
                0,
                TimeZoneInfo.Local);
            if (starting.Count > 0)
                hits = hits.Concat(starting.Select(hit => hit with { AtStart = true })).ToList();
        }

        hits = hits.Where(hit =>
        {
            var entry = this.session.Log.Entries.FirstOrDefault(candidate => candidate.Id == hit.Id);
            return entry is null || this.session.ShowsChannel(entry.Channel);
        }).ToList();
        if (this.session.AlarmResets && this.session.ShowResets)
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
                if (this.session.AlarmChat)
                    ChatGui.Print("Shout Calendar: reset " + string.Join(", ", resets.Select(mark => mark.Name)) + ".");
                this.PlayAlarm(this.session.ResetSound, this.session.ResetSoundFile);
            }
        }

        var minute = EventAlarm.MinuteOf(now);
        if (this.alarmMinute != minute)
            this.alarmMinute = minute;

        var rings = new List<AlarmNotice.Ring>();
        foreach (var hit in hits)
        {
            var entry = alarmEntries.FirstOrDefault(candidate => candidate.Id == hit.Id);
            if (entry is null)
                continue;
            var lead = hit.AtStart ? 0 : this.session.AlarmMinutesBefore;
            rings.Add(new AlarmNotice.Ring(entry, lead, hit.Accepted));
        }

        this.CollectNewlyAccepted(rings, now);
        var unique = AlarmNotice.Dedupe(rings);
        if (unique.Count == 0)
            return;
        if (this.session.AlarmChat)
        {
            var text = AlarmNotice.Broadcast(unique, this.session.CurrentWorld);
            if (text.Length > 0)
                ChatGui.Print(text);
        }

        if (unique.Any(ring => ring.Accepted))
            this.PlayAlarm(this.session.AcceptedSound, this.session.AcceptedSoundFile);
        if (unique.Any(ring => !ring.Accepted))
            this.PlayAlarm(this.session.UnacceptedSound, this.session.UnacceptedSoundFile);
    }

    private void CollectNewlyAccepted(List<AlarmNotice.Ring> rings, DateTime now)
    {
        void Consider(CalendarEntry entry, string key)
        {
            if (string.IsNullOrEmpty(entry.Id) || !this.alarmSeen.Add(key))
                return;
            if (!this.alarmSeeded || !this.session.AlarmAccepted)
                return;
            if (rings.Any(ring => ring.Entry.Id == entry.Id))
                return;
            if (!EventAlarm.AlreadyDue(entry, now, this.session.AlarmMinutesBefore, TimeZoneInfo.Local))
                return;
            var lead = this.session.AlarmAtStart && EventAlarm.IsStartMinute(entry, now, TimeZoneInfo.Local)
                ? 0
                : this.session.AlarmMinutesBefore;
            rings.Add(new AlarmNotice.Ring(entry, lead, true));
        }

        foreach (var entry in this.session.Log.Entries)
        {
            if (entry.Accepted)
                Consider(entry, "local:" + entry.Id);
        }

        if (this.syncBook is SyncBook book)
        {
            foreach (var item in book.CopyEvents())
            {
                if (!item.Accepted || string.IsNullOrEmpty(item.Id))
                    continue;
                Consider(SyncClock.Entry(item) with { Id = "sync:" + item.Id }, "sync:" + item.Id);
            }
        }

        if (!this.alarmSeeded)
            this.alarmSeeded = true;
    }

    private void PlayAlarm(int sound, string? file)
    {
        if (AlarmPlayback.TryPlayFile(file))
            return;
        this.PreviewSound(sound);
        if (!string.IsNullOrWhiteSpace(file))
            ChatGui.Print($"Shout Calendar: could not play that file. Using <se.{EventAlarm.ClampSound(sound)}>.");
    }

    private void OpenPin(string place, float x, float y, bool hasMap, string? quest, string? world)
    {
        if (hasMap)
            this.OpenMap(place, x, y, world);
        if (!string.IsNullOrWhiteSpace(quest))
            this.PrintQuest(quest);
    }

    private readonly record struct AetherytePin(uint AetheryteId, uint TerritoryId, uint MapId, float X, float Y, string Name);

    private void RequestSyncNow()
    {
        try
        {
            PluginInterface.GetIpcSubscriber<bool>("ShoutCalendar.Sync.RequestNow").InvokeFunc();
        }
        catch (IpcError)
        {
            ChatGui.Print("Shout Calendar: Sync is not loaded.");
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Sync now failed.");
            ChatGui.Print("Shout Calendar: Sync now failed.");
        }
    }

    private void RequestSyncResync()
    {
        try
        {
            PluginInterface.GetIpcSubscriber<bool>("ShoutCalendar.Sync.RequestResync").InvokeFunc();
        }
        catch (IpcError)
        {
            ChatGui.Print("Shout Calendar: Sync is not loaded.");
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Re-sync from relay failed.");
            ChatGui.Print("Shout Calendar: Re-sync from relay failed.");
        }
    }

    private void ShowMacroHelper()
    {
        if (this.window.IsOpen)
            this.restoreCalendarAfterMacro = true;
        this.window.IsOpen = false;
        if (this.macroHelper.IsOpen)
            this.macroHelper.BringToFront();
        else
            this.macroHelper.IsOpen = true;
        this.OpenUserMacros();
    }

    private void RestoreCalendar()
    {
        if (!this.restoreCalendarAfterMacro)
            return;
        this.restoreCalendarAfterMacro = false;
        this.window.IsOpen = true;
    }

    private unsafe void OpenUserMacros()
    {
        try
        {
            var agent = AgentModule.Instance()->GetAgentByInternalId(AgentId.Macro);
            if (agent is null)
            {
                ChatGui.Print("Shout Calendar: open User Macros from the system menu (or Alt+R).");
                return;
            }

            agent->Show();
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Could not open User Macros.");
            ChatGui.Print("Shout Calendar: open User Macros from the system menu (or Alt+R).");
        }
    }

    private void OpenHousing(HousingSpot spot)
    {
        unsafe
        {
            var telepo = Telepo.Instance();
            if (telepo is not null)
                telepo->UpdateAetheryteList();
        }

        AetherytePin pin;
        if (spot.CityAetheryteId is uint cityId && this.TryAetheryteById(cityId, spot.City, out pin))
        {
            // City aetheryte from the known housing map.
        }
        else if (!this.TryAetheryteByName(spot.City, out pin))
        {
            ChatGui.Print($"Shout Calendar: {spot.Label}. Teleport from the {spot.City} aetheryte.");
            return;
        }

        if (!this.TryFlagPlot(spot))
            this.OpenMapPin(pin);

        if (this.WrongWorld(spot.World, out var needed, out var current))
        {
            ChatGui.Print("Shout Calendar: " + DataCenters.TravelLine(needed, current));
            return;
        }

        var wardStep = spot.Ward is int ward
            ? $" Select {spot.District} ward {ward.ToString(CultureInfo.InvariantCulture)} from the residential menu."
            : "";
        if (this.AlreadyAtHousing(spot, pin))
        {
            ChatGui.Print($"Shout Calendar: you are already closer than {pin.Name}. The flag is set.{wardStep}");
            return;
        }

        if (wardStep.Length > 0)
            ChatGui.Print($"Shout Calendar: teleporting to {pin.Name}.{wardStep}");
        this.TryTeleport(pin);
    }

    private bool WrongWorld(string? world, out string needed, out string current)
    {
        needed = "";
        current = "";
        if (string.IsNullOrWhiteSpace(world) || !PlayableWorlds.TryCanonical(world, out needed))
            return false;
        var here = this.DetectedWorld("");
        if (here.Length == 0 || !PlayableWorlds.TryCanonical(here, out current))
            return false;
        return !current.Equals(needed, StringComparison.OrdinalIgnoreCase);
    }

    private bool AlreadyAtHousing(HousingSpot spot, AetherytePin pin)
    {
        if (!this.TryPlayerMap(out var territory, out var playerX, out var playerY))
            return false;
        if (spot.WardTerritoryId is uint ward && territory == ward)
            return true;
        if (territory != pin.TerritoryId)
            return false;
        return TravelNear.Distance(playerX, playerY, pin.X, pin.Y) <= TravelNear.Leeway;
    }

    private bool TryFlagPlot(HousingSpot spot)
    {
        if (spot.Plot is not int plot || plot is < 1 or > 60 || spot.WardTerritoryId is not uint territoryId)
            return false;
        var sheet = DataManager.GetSubrowExcelSheet<HousingMapMarkerInfo>();
        if (sheet is null)
            return false;
        var parent = sheet.GetRowOrDefault(territoryId);
        if (parent is null)
            return false;
        foreach (var marker in parent)
        {
            if (marker.SubrowId != plot - 1 || !marker.Map.IsValid)
                continue;
            var map = marker.Map.Value;
            var x = MapUtil.ConvertWorldCoordXZToMapCoord(marker.X, map.SizeFactor, map.OffsetX);
            var y = MapUtil.ConvertWorldCoordXZToMapCoord(marker.Z, map.SizeFactor, map.OffsetY);
            return this.OpenMapAt(territoryId, marker.Map.RowId, x, y);
        }

        return false;
    }

    private bool TryPlayerMap(out uint territoryId, out float x, out float y)
    {
        territoryId = ClientState.TerritoryType;
        x = 0f;
        y = 0f;
        var player = Objects.LocalPlayer;
        if (player is null || territoryId == 0)
            return false;
        if (!DataManager.GetExcelSheet<TerritoryType>().TryGetRow(territoryId, out var territory) || !territory.Map.IsValid)
            return false;
        var map = territory.Map.Value;
        x = MapUtil.ConvertWorldCoordXZToMapCoord(player.Position.X, map.SizeFactor, map.OffsetX);
        y = MapUtil.ConvertWorldCoordXZToMapCoord(player.Position.Z, map.SizeFactor, map.OffsetY);
        return true;
    }

    private bool TryClosestAetheryte(uint territoryId, float x, float y, out AetherytePin pin)
    {
        pin = default;
        var sheet = DataManager.GetExcelSheet<Aetheryte>();
        if (sheet is null)
            return false;
        AetherytePin? bestUnlocked = null;
        var bestUnlockedDistance = float.PositiveInfinity;
        AetherytePin? bestAny = null;
        var bestAnyDistance = float.PositiveInfinity;
        foreach (var row in sheet)
        {
            if (!row.IsAetheryte || row.Territory.RowId != territoryId)
                continue;
            var candidate = this.BuildPin(row, "");
            if (candidate.X <= 0f || candidate.Y <= 0f)
                continue;
            var distance = TravelNear.Distance(candidate.X, candidate.Y, x, y);
            if (distance < bestAnyDistance)
            {
                bestAnyDistance = distance;
                bestAny = candidate;
            }

            if (this.IsUnlocked(candidate.AetheryteId) && distance < bestUnlockedDistance)
            {
                bestUnlockedDistance = distance;
                bestUnlocked = candidate;
            }
        }

        if (bestUnlocked is AetherytePin unlocked)
        {
            pin = unlocked;
            return true;
        }

        if (bestAny is AetherytePin any)
        {
            pin = any;
            return true;
        }

        return false;
    }

    private bool TryTerritoryForPlace(string place, out uint territoryId)
    {
        territoryId = 0;
        var wanted = NormalizePlace(place);
        if (wanted.Length == 0)
            return false;
        var sheet = DataManager.GetExcelSheet<TerritoryType>();
        if (sheet is null)
            return false;
        uint fallback = 0;
        foreach (var row in sheet)
        {
            if (row.PlaceName.RowId == 0)
                continue;
            string name;
            try
            {
                name = NormalizePlace(row.PlaceName.Value.Name.ExtractText());
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            if (!name.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                continue;
            if (fallback == 0)
                fallback = row.RowId;
            if (this.TerritoryHasAetheryte(row.RowId))
            {
                territoryId = row.RowId;
                return true;
            }
        }

        territoryId = fallback;
        return territoryId != 0;
    }

    private bool TerritoryHasAetheryte(uint territoryId)
    {
        var sheet = DataManager.GetExcelSheet<Aetheryte>();
        if (sheet is null)
            return false;
        foreach (var row in sheet)
        {
            if (row.IsAetheryte && row.Territory.RowId == territoryId)
                return true;
        }

        return false;
    }

    private bool TryAetheryteById(uint aetheryteId, string fallbackName, out AetherytePin pin)
    {
        pin = default;
        var sheet = DataManager.GetExcelSheet<Aetheryte>();
        if (sheet is null || !sheet.TryGetRow(aetheryteId, out var row) || !row.IsAetheryte)
        {
            pin = new AetherytePin(aetheryteId, 0, 0, 0f, 0f, fallbackName);
            return aetheryteId != 0;
        }

        pin = this.BuildPin(row, fallbackName);
        return true;
    }

    private bool TryAetheryteByName(string place, out AetherytePin pin)
    {
        pin = default;
        var sheet = DataManager.GetExcelSheet<Aetheryte>();
        if (sheet is null)
            return false;
        var wanted = NormalizePlace(place);
        if (wanted.Length == 0)
            return false;

        AetherytePin? unlocked = null;
        AetherytePin? any = null;
        foreach (var row in sheet)
        {
            if (!row.IsAetheryte || row.PlaceName.RowId == 0 || row.Territory.RowId == 0)
                continue;
            if (!this.TryPlaceNames(row, out var name, out var territoryName))
                continue;
            if (!PlaceMatches(wanted, name) && !PlaceMatches(wanted, territoryName))
                continue;

            var candidate = this.BuildPin(row, name.Length > 0 ? name : wanted);
            if (this.IsUnlocked(candidate.AetheryteId))
            {
                unlocked = candidate;
                break;
            }

            any ??= candidate;
        }

        if (unlocked is AetherytePin foundUnlocked)
        {
            pin = foundUnlocked;
            return true;
        }

        if (any is AetherytePin foundAny)
        {
            pin = foundAny;
            return true;
        }

        return false;
    }

    private AetherytePin BuildPin(Aetheryte row, string fallbackName)
    {
        var name = fallbackName;
        uint territoryId = row.Territory.RowId;
        uint mapId = 0;
        float x = 0f;
        float y = 0f;

        try
        {
            if (row.PlaceName.RowId != 0)
            {
                var extracted = NormalizePlace(row.PlaceName.Value.Name.ExtractText());
                if (extracted.Length > 0)
                    name = extracted;
            }
        }
        catch (InvalidOperationException)
        {
            // Keep fallbackName.
        }

        try
        {
            if (row.Territory.RowId != 0)
            {
                var territory = row.Territory.Value;
                territoryId = row.Territory.RowId;
                if (territory.Map.RowId != 0)
                    mapId = territory.Map.RowId;
            }
        }
        catch (InvalidOperationException)
        {
            // Territory / map may be missing for some rows.
        }

        try
        {
            foreach (var levelRef in row.Level)
            {
                if (!levelRef.IsValid)
                    continue;
                var level = levelRef.Value;
                if (!level.Map.IsValid)
                    continue;
                var map = level.Map.Value;
                mapId = level.Map.RowId;
                x = MapUtil.ConvertWorldCoordXZToMapCoord(level.X, map.SizeFactor, map.OffsetX);
                y = MapUtil.ConvertWorldCoordXZToMapCoord(level.Z, map.SizeFactor, map.OffsetY);
                break;
            }
        }
        catch (InvalidOperationException)
        {
            // Level rows on this sheet do not resolve.
        }

        if (x <= 0f || y <= 0f)
        {
            if (this.TryAetheryteMarker(row.RowId, out var markerX, out var markerY))
            {
                ushort scale = 100;
                try
                {
                    if (row.Map.IsValid && row.Map.Value.SizeFactor != 0)
                        scale = row.Map.Value.SizeFactor;
                }
                catch (InvalidOperationException)
                {
                    scale = 100;
                }

                x = MarkerToMap(markerX, scale);
                y = MarkerToMap(markerY, scale);
            }
        }

        if (mapId == 0)
        {
            try
            {
                if (row.Map.RowId != 0)
                    mapId = row.Map.RowId;
            }
            catch (InvalidOperationException)
            {
            }
        }

        return new AetherytePin(row.RowId, territoryId, mapId, x, y, name);
    }

    private bool TryPlaceNames(Aetheryte row, out string name, out string territoryName)
    {
        name = "";
        territoryName = "";
        try
        {
            name = NormalizePlace(row.PlaceName.Value.Name.ExtractText());
            if (row.Territory.RowId != 0)
            {
                var territory = row.Territory.Value;
                if (territory.PlaceName.RowId != 0)
                    territoryName = NormalizePlace(territory.PlaceName.Value.Name.ExtractText());
            }

            return name.Length > 0 || territoryName.Length > 0;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private bool IsUnlocked(uint aetheryteId)
    {
        for (var i = 0; i < AetheryteList.Length; i++)
        {
            var entry = AetheryteList[i];
            if (entry is not null && entry.AetheryteId == aetheryteId)
                return true;
        }

        return false;
    }

    private static bool PlaceMatches(string wanted, string candidate)
    {
        if (candidate.Length == 0)
            return false;
        if (candidate.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            return true;
        if (wanted.Contains(candidate, StringComparison.OrdinalIgnoreCase))
            return true;
        return candidate.Contains(wanted, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePlace(string? place) =>
        (place ?? "").Replace('’', '\'').Replace('‘', '\'').Replace('ʼ', '\'').Trim();

    private readonly Dictionary<uint, (short X, short Y)> aetheryteMarkers = new();
    private bool aetheryteMarkersReady;

    /// <summary>
    /// Aetheryte.Level does not resolve. The crystal's map position is the marker whose data key is the aetheryte id.
    /// </summary>
    private bool TryAetheryteMarker(uint aetheryteId, out short x, out short y)
    {
        x = 0;
        y = 0;
        this.EnsureAetheryteMarkers();
        if (!this.aetheryteMarkers.TryGetValue(aetheryteId, out var marker))
            return false;
        x = marker.X;
        y = marker.Y;
        return true;
    }

    private void EnsureAetheryteMarkers()
    {
        if (this.aetheryteMarkersReady)
            return;
        this.aetheryteMarkersReady = true;
        var sheet = DataManager.GetSubrowExcelSheet<MapMarker>();
        if (sheet is null)
            return;
        foreach (var parent in sheet)
        {
            foreach (var marker in parent)
            {
                if (marker.DataType != 3 || marker.Icon != 60453 || marker.DataKey.RowId == 0)
                    continue;
                this.aetheryteMarkers.TryAdd(marker.DataKey.RowId, (marker.X, marker.Y));
            }
        }
    }

    private static float MarkerToMap(short marker, ushort sizeFactor)
    {
        var scale = sizeFactor == 0 ? 100f : sizeFactor;
        return marker / scale * 2f + 1f;
    }

    private void OpenMapPin(AetherytePin pin)
    {
        // Sit the flag just off the crystal so the aetheryte marker stays visible.
        const float nudgeX = 0.1f;
        const float nudgeY = -0.1f;
        var x = pin.X + nudgeX;
        var y = pin.Y + nudgeY;
        SeString? link = null;
        if (pin.X > 0f && pin.Y > 0f && pin.TerritoryId != 0 && pin.MapId != 0)
            link = SeString.CreateMapLink(pin.TerritoryId, pin.MapId, x, y);
        if (link is null && pin.X > 0f && pin.Y > 0f)
            link = SeString.CreateMapLink(pin.Name, x, y);
        if (link is null)
        {
            ChatGui.Print($"Shout Calendar: could not find a map for {pin.Name}.");
            return;
        }

        this.ShowMapLink(link);
    }

    private bool OpenMapAt(uint territoryId, uint mapId, float x, float y)
    {
        if (territoryId == 0 || mapId == 0 || x <= 0f || y <= 0f)
            return false;
        var link = SeString.CreateMapLink(territoryId, mapId, x, y);
        this.ShowMapLink(link);
        return true;
    }

    private void ShowMapLink(SeString link)
    {
        var payload = link.Payloads.OfType<MapLinkPayload>().FirstOrDefault();
        if (payload is not null)
        {
            GameGui.OpenMapWithMapLink(payload);
            ImGui.SetClipboardText(payload.CoordinateString);
        }

        ChatGui.Print(link);
    }

    private void OpenMap(string place, float x, float y, string? world)
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

        this.ShowMapLink(link);
        if (this.WrongWorld(world, out var needed, out var current))
        {
            ChatGui.Print("Shout Calendar: " + DataCenters.TravelLine(needed, current).Replace("that plot", "that spot", StringComparison.Ordinal));
            return;
        }

        if (!this.TryTerritoryForPlace(place, out var territoryId) || !this.TryClosestAetheryte(territoryId, x, y, out var pin))
            return;
        if (this.TryPlayerMap(out var here, out var playerX, out var playerY)
            && TravelNear.Skip(
                here == territoryId,
                TravelNear.Distance(playerX, playerY, x, y),
                TravelNear.Distance(pin.X, pin.Y, x, y)))
        {
            ChatGui.Print($"Shout Calendar: you are already closer than {pin.Name}. The flag is set.");
            return;
        }

        this.TryTeleport(pin);
    }

    private unsafe void TryTeleport(AetherytePin pin)
    {
        if (Condition[ConditionFlag.InCombat]
            || Condition[ConditionFlag.BetweenAreas]
            || Condition[ConditionFlag.BetweenAreas51]
            || Condition[ConditionFlag.Casting]
            || Condition[ConditionFlag.OccupiedInEvent]
            || Condition[ConditionFlag.OccupiedInQuestEvent]
            || Condition[ConditionFlag.OccupiedInCutSceneEvent])
        {
            ChatGui.Print("Shout Calendar: map flag set. Finish what you are doing, then teleport.");
            return;
        }

        if (Condition[ConditionFlag.InThatPosition])
        {
            ChatGui.Print("Shout Calendar: map flag set. Stand up, then try again.");
            return;
        }

        var telepo = Telepo.Instance();
        if (telepo is null)
        {
            ChatGui.Print("Shout Calendar: map flag set. Could not reach Teleport.");
            return;
        }

        telepo->UpdateAetheryteList();

        var action = ActionManager.Instance();
        if (action is not null && action->GetActionStatus(ActionType.Action, 5) != 0)
        {
            ChatGui.Print("Shout Calendar: map flag set. Teleport is not ready yet — stand up or leave the current action, then try again.");
            return;
        }

        byte subIndex = 0;
        var unlocked = false;
        for (var i = 0; i < AetheryteList.Length; i++)
        {
            var entry = AetheryteList[i];
            if (entry is null || entry.AetheryteId != pin.AetheryteId)
                continue;
            // Prefer the main crystal (subIndex 0) over estate/apartment shares of the same id.
            if (!unlocked || entry.SubIndex == 0)
            {
                subIndex = (byte)entry.SubIndex;
                unlocked = true;
                if (entry.SubIndex == 0)
                    break;
            }
        }

        if (!unlocked)
        {
            ChatGui.Print($"Shout Calendar: {pin.Name} is not on your Teleport list. The map flag is set.");
            return;
        }

        var aetheryteId = pin.AetheryteId;
        var chosenSub = subIndex;
        Framework.RunOnTick(() =>
        {
            if (Condition[ConditionFlag.InThatPosition])
            {
                ChatGui.Print("Shout Calendar: map flag set. Stand up, then try again.");
                return;
            }

            var live = Telepo.Instance();
            if (live is null)
            {
                ChatGui.Print("Shout Calendar: map flag set. Open Teleport if the cast did not start.");
                return;
            }

            live->UpdateAetheryteList();
            if (!live->Teleport(aetheryteId, chosenSub))
                ChatGui.Print("Shout Calendar: map flag set. Open Teleport if the cast did not start.");
        });
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
        if (this.PausedForPvp())
            return;
        var when = ChatTime.FromUnixOrNow(message.Timestamp, DateTimeOffset.UtcNow);

        var text = message.Message.TextValue;
        if (text.StartsWith("Shout Calendar:", StringComparison.Ordinal))
            return;
        var sender = message.Sender.TextValue;
        this.session.HousingHint = this.CurrentHousingDistrict();
        var kept = this.session.KeepShout(text, (int)message.LogKind, when, sender, SpeakerHome(message));
        if (kept is null)
            return;

        this.Save();
        if (this.session.ParseDebug)
        {
            ChatGui.Print(ParseDebug.Line(kept));
            if (this.session.ParseDebugSound)
                this.PlayAlarm(this.session.ParseDebugSoundEffect, this.session.ParseDebugSoundFile);
        }
    }

    internal static unsafe bool AskTell(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return false;
        if (TryFillChat(command))
        {
            ChatGui.Print("Shout Calendar: the tell is in the chat box. Press Enter to send.");
            return true;
        }

        ImGui.SetClipboardText(command);
        ChatGui.Print("Shout Calendar: tell copied. Paste it into chat.");
        return false;
    }

    private static unsafe bool TryFillChat(string command)
    {
        try
        {
            var addon = GameGui.GetAddonByName("ChatLog", 1);
            if (addon.Address == nint.Zero)
                return false;
            var unit = (AtkUnitBase*)addon.Address;
            var list = unit->UldManager.NodeList;
            var count = unit->UldManager.NodeListCount;
            AtkComponentTextInput* best = null;
            var bestWidth = 0;
            for (var i = 0; i < count; i++)
            {
                var node = list[i];
                if (node == null)
                    continue;
                var componentNode = node->GetAsAtkComponentNode();
                if (componentNode == null || componentNode->Component == null)
                    continue;
                if (componentNode->Component->GetComponentType() != ComponentType.TextInput)
                    continue;
                var input = (AtkComponentTextInput*)componentNode->Component;
                var width = componentNode->AtkResNode.Width;
                if (width < bestWidth)
                    continue;
                bestWidth = width;
                best = input;
            }

            if (best == null)
                return false;
            var bytes = System.Text.Encoding.UTF8.GetBytes(command + "\0");
            fixed (byte* ptr = bytes)
                best->SetText(ptr);
            return true;
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Could not place a tell in the chat box.");
            return false;
        }
    }

    private static string SpeakerHome(IHandleableChatMessage message)
    {
        if (message is ILogMessage log && log.SourceEntity is { IsPlayer: true })
        {
            var id = log.SourceEntity.HomeWorldId;
            if (id != 0)
            {
                var world = DataManager.GetExcelSheet<World>().GetRowOrDefault(id);
                var name = world?.Name.ExtractText() ?? "";
                if (name.Length > 0)
                    return name;
            }
        }

        foreach (var payload in message.Sender.Payloads)
        {
            if (payload is not PlayerPayload player || !player.World.IsValid)
                continue;
            var name = player.World.Value.Name.ExtractText();
            if (name.Length > 0)
                return name;
        }

        return "";
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
        this.config.Listening = this.session.Listening;
        this.config.WatchedChannels = this.session.Channels.Order().ToList();
        this.config.PausedChannels = this.session.Listening ? null : this.session.PausedChannels().ToList();
        this.config.UnacceptedHoldDays = this.session.UnacceptedHoldDays < 1 ? 14 : this.session.UnacceptedHoldDays;
        this.config.AggressiveFilter = this.session.AggressiveFilter;
        this.config.Informedaholic = this.session.Informedaholic;
        this.config.RememberLinkChoice = this.session.RememberLinkChoice;
        this.config.OpenRememberedLinks = this.session.OpenRememberedLinks;
        this.config.AlarmAccepted = this.session.AlarmAccepted;
        this.config.AlarmChat = this.session.AlarmChat;
        this.config.DropPastEvents = this.session.DropPastEvents;
        this.config.ShowPastLocal = this.session.ShowPastLocal;
        this.config.ShowPastSync = this.session.ShowPastSync;
        this.config.TextScale = this.session.TextScale is >= 0.85f and <= 2f ? this.session.TextScale : 1f;
        this.config.AlarmUnaccepted = this.session.AlarmUnaccepted;
        this.config.AcceptedSound = EventAlarm.ClampSound(this.session.AcceptedSound);
        this.config.UnacceptedSound = EventAlarm.ClampSound(this.session.UnacceptedSound);
        this.config.ResetSound = EventAlarm.ClampSound(this.session.ResetSound);
        this.config.AcceptedSoundFile = this.session.AcceptedSoundFile ?? "";
        this.config.UnacceptedSoundFile = this.session.UnacceptedSoundFile ?? "";
        this.config.ResetSoundFile = this.session.ResetSoundFile ?? "";
        this.config.AlarmResets = this.session.AlarmResets;
        this.config.PendingColor = this.session.PendingColor;
        this.config.ShadeStrength = this.session.ShadeStrength;
        this.config.InkFlips = this.session.InkFlips.Order(StringComparer.Ordinal).ToList();
        this.config.AcceptedColor = this.session.AcceptedColor;
        this.config.TodayColor = this.session.TodayColor;
        this.config.OutsideColor = this.session.OutsideColor;
        this.config.CrystalColor = this.session.CrystalColor;
        this.config.CactusColor = this.session.CactusColor;
        this.config.EventColor = this.session.EventColor;
        this.config.AlarmMinutesBefore = this.session.AlarmMinutesBefore < 0 ? 0 : this.session.AlarmMinutesBefore;
        this.config.AlarmAtStart = this.session.AlarmAtStart;
        this.config.ShowLocal = this.session.ShowLocal;
        this.config.ShowLocalAccepted = this.session.ShowLocalAccepted;
        this.config.ShowLocalUnaccepted = this.session.ShowLocalUnaccepted;
        this.config.ShowSyncAccepted = this.session.ShowSyncAccepted;
        this.config.ShowSyncUnaccepted = this.session.ShowSyncUnaccepted;
        this.config.ShowHidden = this.session.ShowHidden;
        this.config.NewestFirst = this.session.NewestFirst;
        this.config.WeekDetailShare = this.session.WeekDetailShare;
        this.config.ShowAllServers = this.session.PendingScope != PendingScope.CurrentWorld;
        this.config.PendingScope = this.session.PendingScope;
        this.config.PauseInPvp = this.session.PauseInPvp;
        this.config.ShowResets = this.session.ShowResets;
        this.config.FastCalendar = this.session.LightCalendar;
        this.config.Appearance = this.session.Appearance;
        this.config.ParseDebug = this.session.ParseDebug;
        this.config.ParseDebugSound = this.session.ParseDebugSound;
        this.config.ParseDebugSoundEffect = EventAlarm.ClampSound(this.session.ParseDebugSoundEffect);
        this.config.ParseDebugSoundFile = this.session.ParseDebugSoundFile ?? "";
        this.config.WeekView = this.session.WeekView;
        this.config.PinnedSync = this.session.PinnedSync.Order(StringComparer.Ordinal).ToList();
        this.config.SyncPendingColor = this.session.SyncPendingColor;
        this.config.SharedBarColor = this.session.SharedBarColor;
        this.config.TwitchColor = this.session.TwitchColor;
        this.config.DiscordColor = this.session.DiscordColor;
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
        var joinSender = "";
        var joinAt = DateTimeOffset.MinValue;
        foreach (var line in ChatLogReader.Read(bytes))
        {
            var key = line.TimestampUnix.ToString(CultureInfo.InvariantCulture)
                + ":"
                + line.Channel.ToString(CultureInfo.InvariantCulture)
                + ":"
                + line.Message;
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

            var clean = SenderName.Clean(line.Sender);
            var join = joinSender.Length > 0
                && clean.Equals(joinSender, StringComparison.OrdinalIgnoreCase)
                && when >= joinAt
                && when - joinAt <= TimeSpan.FromMinutes(3);
            if (seen.Contains(key) && !join)
                continue;
            if (!this.session.TryAddShout(line.Message, line.Channel, when, line.Sender))
            {
                joinSender = clean;
                joinAt = when;
                continue;
            }

            joinSender = "";
            if (!seen.Add(key))
                continue;
            this.config.ImportedLogLines.Add(key);
            added++;
        }

        return added;
    }

    private void RegisterSyncHook()
    {
        try
        {
            this.syncAttach = PluginInterface.GetIpcProvider<byte[], string, bool>("ShoutCalendar.Sync.Attach");
            this.syncAttach.RegisterFunc(this.OnSyncAttach);
            this.syncDetach = PluginInterface.GetIpcProvider<byte[], bool>("ShoutCalendar.Sync.Detach");
            this.syncDetach.RegisterFunc(this.OnSyncDetach);
            this.syncPull = PluginInterface.GetIpcProvider<byte[], string>("ShoutCalendar.Sync.Pull");
            this.syncPull.RegisterFunc(this.OnSyncPull);
            this.syncIngest = PluginInterface.GetIpcProvider<byte[], int, string, string>("ShoutCalendar.Sync.Ingest");
            this.syncIngest.RegisterFunc(this.OnSyncIngest);
            this.syncRead = PluginInterface.GetIpcProvider<byte[], string>("ShoutCalendar.Sync.Read");
            this.syncRead.RegisterFunc(this.OnSyncRead);
            this.syncApply = PluginInterface.GetIpcProvider<byte[], string, bool>("ShoutCalendar.Sync.Apply");
            this.syncApply.RegisterFunc(this.OnSyncApply);
            this.syncStatus = PluginInterface.GetIpcProvider<byte[], string, bool>("ShoutCalendar.Sync.Status");
            this.syncStatus.RegisterFunc(this.OnSyncStatus);
            this.syncIngestV2 = PluginInterface.GetIpcProvider<byte[], int, string, string>("ShoutCalendar.Sync.IngestV2");
            this.syncIngestV2.RegisterFunc(this.OnSyncIngestV2);
            this.openCalendar = PluginInterface.GetIpcProvider<bool>("ShoutCalendar.Open");
            this.openCalendar.RegisterFunc(this.OnOpenCalendar);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Shout Calendar Sync hook was not registered.");
        }
    }

    private void UnregisterSyncHook()
    {
        this.syncAttach?.UnregisterFunc();
        this.syncDetach?.UnregisterFunc();
        this.syncPull?.UnregisterFunc();
        this.syncIngest?.UnregisterFunc();
        this.syncRead?.UnregisterFunc();
        this.syncApply?.UnregisterFunc();
        this.syncStatus?.UnregisterFunc();
        this.syncIngestV2?.UnregisterFunc();
        this.openCalendar?.UnregisterFunc();
        this.syncBook?.Detach();
        SyncGate.Detach();
    }

    private byte[]? syncAttachment;

    private bool OnSyncAttach(byte[] signature, string world)
    {
        if (!Framework.IsInFrameworkUpdateThread) return false;
        if (this.PausedForPvp() || !SyncGate.AllowWrite(signature))
            return false;
        var home = this.DetectedWorld(world);
        if (!PlayableWorlds.TryCanonical(home, out var canonical))
            return false;
        this.syncBook ??= new SyncBook(canonical);
        if (!SyncGate.TryAttach(signature, this.syncBook)) return false;
        this.syncAttachment = signature.ToArray();
        return true;
    }

    private bool OnSyncDetach(byte[] signature)
    {
        if (!Framework.IsInFrameworkUpdateThread) return false;
        if (!SyncGate.AllowWrite(signature))
            return false;
        // A delayed unload callback from an older Sync instance must not detach its replacement.
        if (this.syncAttachment is null || !signature.AsSpan().SequenceEqual(this.syncAttachment)) return false;
        this.syncAttachment = null;
        this.syncBook?.Detach();
        SyncGate.Detach();
        return true;
    }

    internal static bool SyncEnabled()
    {
        if (SyncGate.IsAttached)
            return true;
        foreach (var plugin in PluginInterface.InstalledPlugins)
        {
            if (plugin.IsLoaded && plugin.InternalName.Equals("ShoutCalendar.Sync", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string PluginVersion()
    {
        var version = typeof(Plugin).Assembly.GetName().Version;
        if (version is null)
            return "";
        var text = version.ToString();
        return text.EndsWith(".0", StringComparison.Ordinal) ? text[..^2] : text;
    }

    private string OnSyncPull(byte[] signature)
    {
        if (!Framework.IsInFrameworkUpdateThread) return "";
        if (this.PausedForPvp() || !SyncGate.AllowRead(signature) || this.syncBook is null)
            return "";
        var rows = SyncExport.FromLocal(this.syncBook, this.session.Log.Entries, this.session.Places, PluginVersion());
        return System.Text.Encoding.UTF8.GetString(RelayCodec.EncodeList(rows));
    }

    private async Task WatchRelay(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var snapshot = await Framework.RunOnTick(() =>
                {
                    token.ThrowIfCancellationRequested();
                    var book = this.syncBook;
                    if (book is null || !SyncGate.IsAttached || this.PausedForPvp()) return null;
                    return new RelayProbe(book.BookId, book.RelayHost, book.RelayPort,
                        book.DistinctBackup is { } backup && SyncRelays.IsPublic(backup.Host, backup.Port));
                }, cancellationToken: token).WaitAsync(token);
                if (snapshot is { Port: > 0 } && !string.IsNullOrWhiteSpace(snapshot.Host))
                {
                    var status = await RelayReach.ReadAsync(snapshot.Host, snapshot.Port, snapshot.Mirror, token);
                    await Framework.RunOnTick(() =>
                    {
                        token.ThrowIfCancellationRequested();
                        var book = this.syncBook;
                        if (book?.BookId == snapshot.BookId && book.RelayHost == snapshot.Host && book.RelayPort == snapshot.Port)
                            book.RelayStatus = status;
                    }, cancellationToken: token).WaitAsync(token);
                }
                await Task.Delay(TimeSpan.FromSeconds(10 + Random.Shared.NextDouble() * 2), token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                Log.Debug("Relay status was not updated ({FailureType}).", exception.GetType().Name);
                try { await Task.Delay(TimeSpan.FromSeconds(20), token); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private sealed record RelayProbe(string BookId, string Host, int Port, bool Mirror);

    private string OnSyncIngest(byte[] signature, int openConnections, string json)
    {
        if (!Framework.IsInFrameworkUpdateThread) return RelayProtocol.Denied;
        if (this.PausedForPvp())
            return "0";
        if (!SyncGate.AllowWrite(signature) || this.syncBook is null)
            return RelayProtocol.Denied;
        var rows = RelayCodec.DecodeList(System.Text.Encoding.UTF8.GetBytes(json ?? ""));
        var added = this.syncBook.Ingest(signature, rows, openConnections, this.session.Places);
        if (added < 0)
            return RelayProtocol.Denied;
        if (this.session.Log.Absorb(this.syncBook.CopyEvents()) > 0) this.Save();
        return added.ToString(CultureInfo.InvariantCulture);
    }

    private string OnSyncRead(byte[] signature)
    {
        if (!Framework.IsInFrameworkUpdateThread) return "";
        if (!SyncGate.AllowRead(signature) || this.syncBook is null)
            return "";
        this.syncBook.ForceShare();
        return this.syncBook.ToJson();
    }

    private bool OnOpenCalendar()
    {
        this.OpenMain();
        return true;
    }

    private bool OnSyncStatus(byte[] signature, string json)
    {
        if (!Framework.IsInFrameworkUpdateThread || !SyncGate.AllowWrite(signature) || this.syncBook is null) return false;
        var update = System.Text.Json.JsonSerializer.Deserialize<SyncStatusUpdate>(json);
        if (update is null) return false;
        if (update.ResetDismissed) this.syncBook.PrepareDebugResync();
        this.syncBook.PublishStatus(update.Status, update.PerformanceLine, update.Progress);
        return true;
    }

    private string OnSyncIngestV2(byte[] signature, int openConnections, string json)
    {
        var added = this.OnSyncIngest(signature, openConnections, json);
        if (!int.TryParse(added, out var count)) return added;
        return System.Text.Json.JsonSerializer.Serialize(new SyncIngestReply(count, this.syncBook?.LastFill));
    }

    private bool OnSyncApply(byte[] signature, string json)
    {
        if (!Framework.IsInFrameworkUpdateThread) return false;
        if (!SyncGate.AllowWrite(signature) || this.syncBook is null)
            return false;
        if (!this.syncBook.RestoreIfEmpty(json))
            return false;
        if (this.session.Log.Absorb(this.syncBook.CopyEvents()) > 0)
            this.Save();
        return true;
    }

    private string DetectedWorld(string fallback)
    {
        try
        {
            if (PlayerState.IsLoaded && PlayerState.CurrentWorld.IsValid)
            {
                var name = PlayerState.CurrentWorld.Value.Name.ExtractText();
                if (PlayableWorlds.TryCanonical(name, out var world))
                    return world;
            }
        }
        catch (Exception)
        {
            // Character data is not available at the title screen.
        }

        return fallback ?? "";
    }
}
