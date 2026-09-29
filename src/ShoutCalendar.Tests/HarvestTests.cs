using ShoutCalendar.Core;

namespace ShoutCalendar.Tests;

public class HarvestTests
{
    private static readonly DateTimeOffset ShoutAt = new(2026, 9, 26, 18, 30, 0, TimeSpan.Zero);

    [Fact]
    public void WardAndClockTimeBecomeOneEntryOnTheShoutDate()
    {
        var entry = ShoutHarvest.TryHarvest("Maps at 8:00pm ward 13", ShoutHarvest.ShoutChannel, ShoutAt);

        Assert.NotNull(entry);
        Assert.Equal(new DateOnly(2026, 9, 26), entry.Date);
        Assert.Equal(new TimeOnly(20, 0), entry.Time);
        Assert.Null(entry.End);
        Assert.Equal(13, entry.Ward);
        Assert.Contains("ward 13", entry.Place);
        Assert.Equal("Maps at 8:00pm ward 13", entry.EventText);
        Assert.False(OngoingCheck.IsOngoing(entry, ShoutAt));
    }

    [Fact]
    public void WorldNameOtherThanDiabolosIsKept()
    {
        var entry = ShoutHarvest.TryHarvest("Party at 19:30 on Faerie", ShoutHarvest.ShoutChannel, ShoutAt);

        Assert.NotNull(entry);
        Assert.Equal("Faerie", entry.Server);
        Assert.Equal(new TimeOnly(19, 30), entry.Time);
        Assert.Null(entry.Ward);
        Assert.Contains("Faerie", entry.Place);
    }

    [Fact]
    public void DataCenterNameIsKept()
    {
        var entry = ShoutHarvest.TryHarvest("Venues at 8 pm on Primal", ShoutHarvest.ShoutChannel, ShoutAt);

        Assert.NotNull(entry);
        Assert.Equal("Primal", entry.Server);
        Assert.Equal(new TimeOnly(20, 0), entry.Time);
    }

    [Fact]
    public void DiabolosIsAlsoKept()
    {
        var entry = ShoutHarvest.TryHarvest("Club night 21:00 Diabolos ward 4 plot 30", ShoutHarvest.ShoutChannel, ShoutAt);

        Assert.NotNull(entry);
        Assert.Equal("Diabolos", entry.Server);
        Assert.Equal(4, entry.Ward);
        Assert.Equal("ward 4, plot 30, Diabolos", entry.Place);
    }

    [Fact]
    public void APlaceWithoutATimeWaitsForADate()
    {
        var entry = ShoutHarvest.TryHarvest("come to ward 13 on Faerie", ShoutHarvest.ShoutChannel, ShoutAt);

        Assert.NotNull(entry);
        Assert.Null(entry.Date);
        Assert.Equal(13, entry.Ward);
        Assert.Contains("Faerie", entry.Place);
    }

    [Fact]
    public void ATimeWithoutAPlaceStillLandsOnTheMessageDay()
    {
        var entry = ShoutHarvest.TryHarvest("starting at 8:00pm", ShoutHarvest.ShoutChannel, ShoutAt);

        Assert.NotNull(entry);
        Assert.Equal(new DateOnly(2026, 9, 26), entry.Date);
        Assert.Equal(new TimeOnly(20, 0), entry.Time);
        Assert.Equal("", entry.Place);
    }

    [Fact]
    public void RelativeAndBellTimesAreNotClocks()
    {
        var tonight = ShoutHarvest.TryHarvest("tonight ward 13", ShoutHarvest.ShoutChannel, ShoutAt);
        Assert.NotNull(tonight);
        Assert.Equal(new DateOnly(2026, 9, 26), tonight.Date);
        var afterEight = new DateTimeOffset(2026, 9, 28, 1, 30, 0, TimeSpan.Zero);
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var evening = ShoutHarvest.TryHarvest("8pm ward 4", ShoutHarvest.ShoutChannel, afterEight, zone: eastern);
        Assert.NotNull(evening);
        Assert.Equal(new DateOnly(2026, 9, 27), evening.Date);
        Assert.Equal(new TimeOnly(20, 0), evening.Time);
        var stillTonight = ShoutHarvest.TryHarvest("tonight ward 4", ShoutHarvest.ShoutChannel, afterEight, zone: eastern);
        Assert.NotNull(stillTonight);
        Assert.Equal(new DateOnly(2026, 9, 27), stillTonight.Date);
        Assert.Null(tonight.Time);

        var later = ShoutHarvest.TryHarvest("in 20 minutes on Faerie", ShoutHarvest.ShoutChannel, ShoutAt, zone: TimeZoneInfo.Utc);
        Assert.NotNull(later);
        Assert.Equal(new DateOnly(2026, 9, 26), later.Date);
        Assert.Equal(new TimeOnly(18, 50), later.Time);
        Assert.Contains("Faerie", later.Place);

        var bells = ShoutHarvest.TryHarvest("12 bells ward 13", ShoutHarvest.ShoutChannel, ShoutAt);
        Assert.NotNull(bells);
        Assert.Null(bells.Date);
        Assert.Equal(13, bells.Ward);
    }

    [Fact]
    public void ZoneLabelDoesNotMoveTheClock()
    {
        var entry = ShoutHarvest.TryHarvest("8:00pm ST ward 4 on Diabolos", ShoutHarvest.ShoutChannel, ShoutAt);

        Assert.NotNull(entry);
        Assert.Equal(new TimeOnly(20, 0), entry.Time);
        Assert.Equal(4, entry.Ward);
        Assert.Equal("Diabolos", entry.Server);
    }

    [Fact]
    public void NonShoutChannelIsNotAdded()
    {
        Assert.Null(ShoutHarvest.TryHarvest("Maps at 8:00pm ward 13", 14, ShoutAt));
    }

    [Fact]
    public void TwoOrderedClocksAreOngoingInsideTheIntervalOnly()
    {
        var entry = ShoutHarvest.TryHarvest("8:00 to 10:00 ward 4", ShoutHarvest.ShoutChannel, ShoutAt);

        Assert.NotNull(entry);
        Assert.Equal(new TimeOnly(8, 0), entry.Time);
        Assert.Equal(new TimeOnly(10, 0), entry.End);
        var range = ShoutHarvest.TryHarvest("OPEN TONIGHT 8-12 ward 4", ShoutHarvest.ShoutChannel, ShoutAt);
        Assert.NotNull(range);
        Assert.Equal(new TimeOnly(20, 0), range.Time);
        Assert.Equal(new TimeOnly(0, 0), range.End);
        var posted = ShoutHarvest.TryHarvest("9p-12a ET ward 4", ShoutHarvest.ShoutChannel, ShoutAt, zone: TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));
        Assert.NotNull(posted);
        Assert.Equal(new TimeOnly(21, 0), posted.Time);
        Assert.Equal(new TimeOnly(0, 0), posted.End);
        var central = ShoutHarvest.TryHarvest("8-11 CT ward 4", ShoutHarvest.ShoutChannel, ShoutAt, zone: TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));
        Assert.NotNull(central);
        Assert.Equal(new TimeOnly(21, 0), central.Time);
        Assert.Equal(new TimeOnly(0, 0), central.End);
        var cafe = "The Skylight Cafe will be open for business from 9pm to 12am (CT)! Enjoy refreshments at Lavender Beds Ward 1 Plot 51";
        var cafeEntry = ShoutHarvest.TryHarvest(cafe, ShoutHarvest.ShoutChannel, ShoutAt, zone: TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));
        Assert.NotNull(cafeEntry);
        Assert.Equal(new TimeOnly(22, 0), cafeEntry.Time);
        Assert.Equal(new TimeOnly(1, 0), cafeEntry.End);
        var saved = cafeEntry with { Time = new TimeOnly(21, 0), End = new TimeOnly(0, 0) };
        var shown = ZoneClock.ShownRange(saved, TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));
        Assert.Equal(new TimeOnly(22, 0), shown.Start);
        Assert.Equal(new TimeOnly(1, 0), shown.End);
        Assert.True(OngoingCheck.IsOngoing(entry, new DateTimeOffset(2026, 9, 26, 8, 0, 0, TimeSpan.Zero)));
        Assert.True(OngoingCheck.IsOngoing(entry, new DateTimeOffset(2026, 9, 26, 9, 15, 0, TimeSpan.Zero)));
        Assert.True(OngoingCheck.IsOngoing(entry, new DateTimeOffset(2026, 9, 26, 10, 0, 59, TimeSpan.Zero)));
        Assert.False(OngoingCheck.IsOngoing(entry, new DateTimeOffset(2026, 9, 26, 7, 59, 0, TimeSpan.Zero)));
        Assert.False(OngoingCheck.IsOngoing(entry, new DateTimeOffset(2026, 9, 26, 10, 1, 0, TimeSpan.Zero)));
        Assert.False(OngoingCheck.IsOngoing(entry, new DateTimeOffset(2026, 9, 27, 9, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void GameClockIconsBecomeARange()
    {
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var places = new PlaceCatalog(["The Goblet"]);
        const string text = "Open \uE031 9\uE06E-12\uE06D EST at The Goblet W\uE096-P\uE093\uE092";
        var entry = ShoutHarvest.TryHarvest(text, ShoutHarvest.ShoutChannel, ShoutAt, places, aggressive: true, zone: eastern);
        Assert.NotNull(entry);
        Assert.Equal(new TimeOnly(21, 0), entry.Time);
        Assert.Equal(new TimeOnly(0, 0), entry.End);
        Assert.Equal(7, entry.Ward);
        Assert.Contains("plot 43", entry.Place);
        Assert.Contains("\uE031", entry.EventText);

        var glued = ShoutHarvest.TryHarvest(
            "Open 9\uE06E-2\uE06DEST at The Goblet ward 8 plot 5",
            ShoutHarvest.ShoutChannel,
            ShoutAt,
            places,
            aggressive: true,
            zone: eastern);
        Assert.NotNull(glued);
        Assert.Equal(new TimeOnly(21, 0), glued.Time);
        Assert.Equal(new TimeOnly(2, 0), glued.End);

        Assert.Equal("7:30pm - 11:30pm ET", IconText.Plain("\uE096\uE0AD\uE06E - \uE09A\uE0AD\uE06E\uE0D2"));
        var half = ShoutHarvest.TryHarvest(
            "Open \uE096\uE0AD\uE06E - \uE09A\uE0AD\uE06E\uE0D2 at The Goblet ward 5 plot 30",
            ShoutHarvest.ShoutChannel,
            ShoutAt,
            places,
            aggressive: true,
            zone: eastern);
        Assert.NotNull(half);
        Assert.Equal(new TimeOnly(19, 30), half.Time);
        Assert.Equal(new TimeOnly(23, 30), half.End);
    }

    [Fact]
    public void SingleClockTimeIsOnTheCalendarAndNotOngoing()
    {
        var session = new CalendarSession(new DateOnly(2026, 9, 1));
        Assert.True(session.TryAddShout("8pm ward 2", ShoutHarvest.ShoutChannel, ShoutAt));

        var month = session.CurrentMonth();
        var onDay = month.OnDay(26);
        Assert.Single(onDay);
        Assert.False(OngoingCheck.IsOngoing(onDay[0], new DateTimeOffset(2026, 9, 26, 20, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void CalendarDateIsTheUtcDateOfTheTimestamp()
    {
        var late = new DateTimeOffset(2026, 9, 27, 2, 0, 0, TimeSpan.Zero);
        var entry = ShoutHarvest.TryHarvest("8:00 ward 9 on Faerie", ShoutHarvest.ShoutChannel, late);

        Assert.NotNull(entry);
        Assert.Equal(new DateOnly(2026, 9, 27), entry.Date);
    }

    [Fact]
    public void RepeatedCallsReturnTheSameEntry()
    {
        const string text = "Hunt train 8:00pm to 10:00pm ward 18 plot 5 on Faerie";
        var first = ShoutHarvest.TryHarvest(text, ShoutHarvest.ShoutChannel, ShoutAt);
        var second = ShoutHarvest.TryHarvest(text, ShoutHarvest.ShoutChannel, ShoutAt);

        Assert.Equal(first, second);
        Assert.NotNull(first);
        Assert.Equal(new TimeOnly(20, 0), first.Time);
        Assert.Equal(new TimeOnly(22, 0), first.End);
        Assert.Equal(18, first.Ward);
        Assert.Equal("Faerie", first.Server);
    }

    [Fact]
    public void APlaceNameAloneStaysUntilTheAggressiveFilter()
    {
        var places = new PlaceCatalog(["The Source"]);
        const string text = "went to the source";

        var kept = ShoutHarvest.TryHarvest(text, ShoutHarvest.ShoutChannel, ShoutAt, places);
        Assert.NotNull(kept);
        Assert.Contains("The Source", kept.Place);
        Assert.Null(kept.Date);
        Assert.Null(kept.Time);

        Assert.Null(ShoutHarvest.TryHarvest(text, ShoutHarvest.ShoutChannel, ShoutAt, places, aggressive: true));
    }

    [Fact]
    public void SplitLinesFromOnePlayerBecomeOneInviteOnTheNamedWorld()
    {
        var session = new CalendarSession(new DateOnly(2026, 9, 27));
        session.Places = new PlaceCatalog(["The Goblet", "Limsa Lominsa"]);
        var when = new DateTimeOffset(new DateTime(2026, 9, 27, 18, 0, 0));
        Assert.False(session.TryAddShout("Any events going on today?", ShoutHarvest.ShoutChannel, when, "Natsuki Sasahara"));
        Assert.False(session.TryAddShout(
            "Bring your black eyeliner, broken hearts and emo anthems! Tonight is having Emo Night!",
            ShoutHarvest.ShoutChannel,
            when.AddSeconds(5),
            "Femboi JeesusGoblin"));
        Assert.True(session.TryAddShout(
            "7 PM EST/ Crystal Zalera Goblet W7 P5/ glam contest",
            ShoutHarvest.ShoutChannel,
            when.AddSeconds(20),
            "Femboi JeesusGoblin"));

        var entry = Assert.Single(session.Log.Entries);
        Assert.Equal(new TimeOnly(19, 0), entry.Time);
        Assert.Equal(new DateOnly(2026, 9, 27), entry.Date);
        Assert.Equal(7, entry.Ward);
        Assert.Contains("plot 5", entry.Place, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Goblet", entry.Place, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Zalera", entry.Server, StringComparison.Ordinal);
        Assert.Contains("Emo Night", entry.EventText, StringComparison.Ordinal);
        Assert.DoesNotContain("Any events", entry.EventText, StringComparison.Ordinal);
        Assert.Equal("Zalera", SyncAnnouncement.FromLocal(entry, "Mateus").World);

        var crystalOnly = ShoutHarvest.TryHarvest("7 PM at the Goblet on Crystal", ShoutHarvest.ShoutChannel, when, session.Places, aggressive: true);
        Assert.NotNull(crystalOnly);
        Assert.Equal("Mateus", SyncAnnouncement.FromLocal(crystalOnly, "Mateus").World);
    }

    [Fact]
    public void AGoblinAtHomeIsSharedOnGoblinUnlessTheTextNamesAWorld()
    {
        var heard = new CalendarEntry(
            new DateOnly(2026, 9, 27),
            new TimeOnly(19, 0),
            null,
            7,
            null,
            "The Goblet",
            "Maps at 7:00 PM Goblet W7 P5",
            "Traveler",
            false,
            "row",
            DateTimeOffset.UtcNow,
            null,
            11,
            false,
            false,
            "Goblin");
        Assert.Equal("Goblin", SyncAnnouncement.FromLocal(heard, "Diabolos").World);

        var named = heard with { Server = "Crystal, Zalera" };
        Assert.Equal("Zalera", SyncAnnouncement.FromLocal(named, "Diabolos").World);

        var crystalOnly = heard with { Server = "Crystal" };
        Assert.Equal("Goblin", SyncAnnouncement.FromLocal(crystalOnly, "Diabolos").World);

        var unknown = heard with { SpeakerWorld = "" };
        Assert.Equal("Diabolos", SyncAnnouncement.FromLocal(unknown, "Diabolos").World);

        var raff = heard with
        {
            Server = "Diabolos",
            SpeakerWorld = "Diabolos",
            EventText = "NOW @ Raff•Goblet•Ward 21•Plot 4 at 8pm",
        };
        Assert.Equal("Rafflesia", SyncAnnouncement.FromLocal(raff, "Diabolos").World);
        Assert.True(ServerNames.TryAdvertised("Diablos Goblet W1 P1", out var typo));
        Assert.Equal("Diabolos", typo);
        Assert.True(ServerNames.ForServer(raff.EventText, "Diabolos", "Rafflesia"));
        Assert.False(ServerNames.ForServer(raff.EventText, "Diabolos", "Diabolos"));
        Assert.False(ServerNames.TryAdvertised("Ser party", out _));

        var hand = heard with { Manual = true, Channel = 0 };
        Assert.Empty(SyncExport.FromLocal(new SyncBook("Diabolos"), [hand]));
        var plugin = File.ReadAllText(Path.Combine(RepoRoot(), "src", "ShoutCalendar", "Plugin.cs"));
        Assert.Contains("HomeWorldId", plugin, StringComparison.Ordinal);
        Assert.Contains("SpeakerHome", plugin, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "QUESTIONS.md")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }

    [Fact]
    public void AZaleraInviteStaysVisibleOnTheHomeCalendar()
    {
        var worlds = new WorldCalendar("Diabolos");
        var oryn = new CalendarEntry(
            new DateOnly(2026, 9, 27),
            null,
            null,
            17,
            "Crystal, Zalera",
            "ward 17, plot 60, The Goblet, Crystal, Zalera",
            "Tonight Flying HIGH for EGGYs Birthday Goblet W17 P60",
            "Oryn Ka'ge",
            true,
            "oryn",
            DateTimeOffset.UtcNow,
            null,
            0,
            false,
            true);
        Assert.True(worlds.ShowsLocal(oryn));
        worlds.SetDataCenter("Crystal", true);
        Assert.True(worlds.Select("Zalera"));
        Assert.True(worlds.ShowsLocal(oryn));
        Assert.True(worlds.Select("Goblin"));
        Assert.True(worlds.ShowsLocal(oryn));
        var heard = oryn with { Manual = false, Channel = 11 };
        Assert.False(worlds.ShowsLocal(heard));
        Assert.True(worlds.Select("Diabolos"));
        Assert.True(worlds.ShowsLocal(heard));
    }

    [Fact]
    public void CheckingCrystalSelectsItsWorlds()
    {
        var worlds = new WorldCalendar("Diabolos");
        Assert.False(worlds.DataCenterChecked("Crystal"));
        worlds.SetDataCenter("Crystal", true);
        Assert.True(worlds.IsChecked("Goblin"));
        Assert.True(worlds.IsChecked("Zalera"));
        Assert.True(worlds.IsChecked("Diabolos"));
        Assert.True(worlds.DataCenterChecked("Crystal"));
        Assert.False(worlds.IsChecked("Faerie"));
        worlds.SetDataCenter("Crystal", false);
        Assert.True(worlds.IsChecked("Diabolos"));
        Assert.False(worlds.IsChecked("Goblin"));
        Assert.False(worlds.DataCenterChecked("Crystal"));
        worlds.SetChecked("Zalera", true);
        Assert.True(worlds.SharesPending("Zalera", true, "Diabolos"));
        Assert.False(worlds.SharesPending("Faerie", true, "Diabolos"));
        Assert.True(worlds.SharesPending("Diabolos", true, "Diabolos"));
        Assert.False(worlds.SharesPending("Zalera", false, "Diabolos"));
        Assert.True(worlds.SharesPending("Faerie", false, "Faerie"));
        Assert.False(worlds.DrawnPending("Zalera", true, "Diabolos"));
        Assert.True(worlds.DrawnPending("Diabolos", true, "Diabolos"));
        worlds.SetViewing("Zalera", true);
        Assert.True(worlds.DrawnPending("Zalera", true, "Diabolos"));
        Assert.False(worlds.DrawnPending("Faerie", true, "Diabolos"));
        Assert.False(worlds.DrawnPending("Zalera", false, "Diabolos"));
    }

    [Fact]
    public void ARepeatedEmoNightUpdatesTheAcceptedRow()
    {
        var session = new CalendarSession(new DateOnly(2026, 9, 27));
        session.Places = new PlaceCatalog(["The Goblet"]);
        var when = new DateTimeOffset(new DateTime(2026, 9, 27, 18, 0, 0));
        Assert.True(session.TryAddShout(
            "Bring your black eyeliner and emo anthems! Tonight Emo Night Goblet W7 P5 7:00 PM",
            ShoutHarvest.ShoutChannel,
            when,
            "Femboi"));
        var first = Assert.Single(session.Log.Entries);
        Assert.True(session.Log.Accept(first.Id));
        Assert.True(session.TryAddShout(
            "Emo Night tonight. Bring black eyeliner and emo anthems. Goblet W7 P5 at 8:00 PM.",
            ShoutHarvest.ShoutChannel,
            when.AddMinutes(30),
            "Someone"));
        var updated = Assert.Single(session.Log.Entries);
        Assert.True(updated.Accepted);
        Assert.Equal(new TimeOnly(20, 0), updated.Time);
        Assert.Equal(first.Id, updated.Id);
        Assert.True(session.TryAddShout(
            "Emo Night tonight. Bring black eyeliner and emo anthems. Goblet W9 P5 at 8:00 PM.",
            ShoutHarvest.ShoutChannel,
            when.AddMinutes(40),
            "Someone"));
        Assert.Equal(2, session.Log.Entries.Count);
    }

    [Fact]
    public void OrynBirthdayLineIsKeptOnZalera()
    {
        var places = new PlaceCatalog(["The Goblet"]);
        var when = new DateTimeOffset(new DateTime(2026, 9, 27, 18, 0, 0));
        const string text = "Tonight: \uE000\uE005\uE002\uE075 \uE03C\uE07A\uE075\uE084 \u2665 Flying HIGH for EGGYs Birthday Edition! \u2665 Featuring Captains \u266A Raindrop, Yams, Aemilia, Keshi & Swage! \u266A \uE031 Wings Up @ \uE06F \uE015\uE06E ET Nyoooom! Dress code: Party Hats! \u2665 Join us for \uE06F Drinks, Music, Dancing, Tarot, Prizes \uE03E & Best Frens! - All that's missing is YOU! \u2605 \uE03C\uE07A\uE075\uE084 \uE008 Crystal, Zalera, Goblet, W17, P60 \u2665";
        var entry = ShoutHarvest.TryHarvest(text, ShoutHarvest.ShoutChannel, when, places, aggressive: true);
        Assert.NotNull(entry);
        Assert.Equal(17, entry.Ward);
        Assert.Contains("plot 60", entry.Place, StringComparison.OrdinalIgnoreCase);
        var shared = SyncAnnouncement.FromLocal(entry with { SpeakerWorld = "Goblin", Channel = ShoutHarvest.ShoutChannel }, "Diabolos");
        Assert.Equal("Zalera", shared.World);
    }

    [Fact]
    public void ABirthdayShoutWithAWardIsKept()
    {
        var places = new PlaceCatalog(["The Goblet"]);
        var when = new DateTimeOffset(new DateTime(2026, 9, 27, 18, 0, 0));
        var entry = ShoutHarvest.TryHarvest(
            "Tonight: Flying HIGH for EGGYs Birthday. Wings Up at 8 PM ET. Crystal, Zalera, Goblet, W17, P60",
            ShoutHarvest.ShoutChannel,
            when,
            places,
            aggressive: true);
        Assert.NotNull(entry);
        Assert.Equal(17, entry.Ward);
        Assert.Contains("plot 60", entry.Place, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Goblet", entry.Place, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Zalera", entry.Server, StringComparison.Ordinal);
    }

    [Fact]
    public void InformedaholicAcceptsWhatTheFilterKeeps()
    {
        var session = new CalendarSession(DateOnly.FromDateTime(ShoutAt.UtcDateTime));
        session.Informedaholic = true;
        Assert.False(session.TryAddShout("hello", ShoutHarvest.ShoutChannel, ShoutAt, "Mina"));
        Assert.True(session.TryAddShout("Maps at 8:00pm ward 13", ShoutHarvest.ShoutChannel, ShoutAt, "Mina"));
        var entry = Assert.Single(session.Log.Entries);
        Assert.True(entry.Accepted);

        session.Informedaholic = false;
        Assert.True(session.TryAddShout("tomorrow at 6:30 in ward 4", ShoutHarvest.ShoutChannel, ShoutAt.AddMinutes(2), "Mina"));
        Assert.Contains(session.Log.Entries, row => !row.Accepted);
        Assert.Equal(1, session.Log.AcceptPending());
        Assert.DoesNotContain(session.Log.Entries, row => !row.Accepted);
    }

    [Fact]
    public void SharedEventsRequireTwoOfThreeEvenWhenTheLocalFilterIsOff()
    {
        var places = new PlaceCatalog(["The Source"]);
        var session = new CalendarSession(DateOnly.FromDateTime(ShoutAt.UtcDateTime));
        session.Places = places;
        session.AggressiveFilter = false;
        Assert.True(session.TryAddShout("went to the source", ShoutHarvest.ShoutChannel, ShoutAt, "Tester"));
        Assert.False(ShoutHarvest.IsSharedEvent("went to the source", ShoutHarvest.ShoutChannel, ShoutAt, places));
        Assert.False(ShoutHarvest.IsSharedEvent("starting at 8:00pm", ShoutHarvest.ShoutChannel, ShoutAt, places));
        Assert.False(ShoutHarvest.IsSharedEvent("hello there", SharePolicy.YellChannel, ShoutAt, places));
        Assert.False(ShoutHarvest.IsSharedEvent("tomorrow at 6:30", 10, ShoutAt, places));
        Assert.True(ShoutHarvest.IsSharedEvent("Maps at 8:00pm ward 13", ShoutHarvest.ShoutChannel, ShoutAt, places));
        Assert.True(ShoutHarvest.IsSharedEvent("tomorrow at 6:30", SharePolicy.YellChannel, ShoutAt, places));
    }

    [Fact]
    public void RightNowAndAMisspelledLimsaAreAnInvite()
    {
        var places = new PlaceCatalog(["Limsa Lominsa Lower Decks", "Limsa Lominsa", "The Goblet"]);
        var when = new DateTimeOffset(new DateTime(2026, 9, 27, 19, 42, 0));
        var entry = ShoutHarvest.TryHarvest(
            "Come join me on a day of fun, in Limsa Lomniski right",
            ShoutHarvest.FreeCompanyChannel,
            when,
            places,
            aggressive: true);

        Assert.NotNull(entry);
        Assert.Equal(new TimeOnly(19, 42), entry.Time);
        Assert.Equal(new DateOnly(2026, 9, 27), entry.Date);
        Assert.Contains("Limsa Lominsa", entry.Place);
        Assert.DoesNotContain("Lower Decks", entry.Place);

        Assert.Null(ShoutHarvest.TryHarvest("nowadays in Limsa", ShoutHarvest.ShoutChannel, when, places, aggressive: true));
        var clock = ShoutHarvest.TryHarvest("8:00pm right now at the Goblet", ShoutHarvest.ShoutChannel, when, places, aggressive: true);
        Assert.NotNull(clock);
        Assert.Equal(new TimeOnly(20, 0), clock.Time);
    }

    [Fact]
    public void AggressiveFilterKeepsTwoOfDateTimeAndPlace()
    {
        Assert.Null(ShoutHarvest.TryHarvest("starting at 8:00pm", ShoutHarvest.ShoutChannel, ShoutAt, aggressive: true));
        Assert.Null(ShoutHarvest.TryHarvest("come to ward 13 on Faerie", ShoutHarvest.ShoutChannel, ShoutAt, aggressive: true));
        Assert.Null(ShoutHarvest.TryHarvest("see you tonight on Faerie", ShoutHarvest.ShoutChannel, ShoutAt, aggressive: true));
        Assert.Null(ShoutHarvest.TryHarvest("heading to the Goblet now", ShoutHarvest.ShoutChannel, ShoutAt, aggressive: true));
        Assert.Null(ShoutHarvest.TryHarvest("I'm free tonight", ShoutHarvest.ShoutChannel, ShoutAt, aggressive: true));

        var datedPlace = ShoutHarvest.TryHarvest("W3 Plot 27 on 10/13/26", ShoutHarvest.ShoutChannel, ShoutAt, aggressive: true);
        Assert.NotNull(datedPlace);
        Assert.Equal(new DateOnly(2026, 10, 13), datedPlace.Date);
        Assert.Null(datedPlace.Time);
        Assert.Equal(3, datedPlace.Ward);

        var timedPlace = ShoutHarvest.TryHarvest("Maps at 8:00pm ward 13", ShoutHarvest.ShoutChannel, ShoutAt, aggressive: true);
        Assert.NotNull(timedPlace);

        var datedTime = ShoutHarvest.TryHarvest("tomorrow at 6:30", ShoutHarvest.ShoutChannel, ShoutAt, aggressive: true);
        Assert.NotNull(datedTime);
        Assert.Equal(new DateOnly(2026, 9, 27), datedTime.Date);
        Assert.Equal(new TimeOnly(6, 30), datedTime.Time);
        Assert.Equal("", datedTime.Place);

        Assert.NotNull(ShoutHarvest.TryHarvest("tonight at the Goblet", ShoutHarvest.ShoutChannel, ShoutAt, aggressive: true));
        Assert.NotNull(ShoutHarvest.TryHarvest("Open now Goblet W7 P5", ShoutHarvest.ShoutChannel, ShoutAt, aggressive: true));
    }

    [Fact]
    public void NextTuesdayAndGluedWardPlotAreKeptWhenTheFilterIsOff()
    {
        var sunday = new DateTimeOffset(2026, 9, 27, 18, 0, 0, TimeSpan.Zero);
        var entry = ShoutHarvest.TryHarvest(
            "Next Tuesday on our home plot, W3P26",
            ShoutHarvest.FreeCompanyChannel,
            sunday);

        Assert.NotNull(entry);
        Assert.Equal(new DateOnly(2026, 9, 29), entry.Date);
        Assert.Equal(3, entry.Ward);
        Assert.Contains("plot 26", entry.Place);
        Assert.Null(entry.Time);
    }

    [Fact]
    public void TuesdayAfterNextSkipsTheComingTuesday()
    {
        var sunday = new DateTimeOffset(2026, 9, 27, 18, 0, 0, TimeSpan.Zero);
        var entry = ShoutHarvest.TryHarvest(
            "The Tuesday after next, doing an event on our home plot, W3P26 ~6:00am",
            ShoutHarvest.FreeCompanyChannel,
            sunday);

        Assert.NotNull(entry);
        Assert.Equal(new DateOnly(2026, 10, 6), entry.Date);
        Assert.Equal(new TimeOnly(6, 0), entry.Time);
        Assert.Equal(3, entry.Ward);
        Assert.Contains("plot 26", entry.Place);
    }

    [Fact]
    public void EveryOtherTuesdayLandsOnAlternateTuesdays()
    {
        var sunday = new DateTimeOffset(2026, 9, 27, 18, 0, 0, TimeSpan.Zero);
        var entry = ShoutHarvest.TryHarvest(
            "every other Tuesday at 6pm on W3P26",
            ShoutHarvest.ShoutChannel,
            sunday);

        Assert.NotNull(entry);
        Assert.Equal(new DateOnly(2026, 9, 29), entry.Date);
        Assert.True(EventRepeat.FallsOn(entry, new DateOnly(2026, 10, 13)));
        Assert.False(EventRepeat.FallsOn(entry, new DateOnly(2026, 10, 6)));
    }

    [Fact]
    public void FirstAndLastWednesdayRepeatThroughTheMonth()
    {
        var sunday = new DateTimeOffset(2026, 9, 27, 18, 0, 0, TimeSpan.Zero);
        var entry = ShoutHarvest.TryHarvest(
            "every 1st and last Wednesday at 8pm in the Goblet",
            ShoutHarvest.ShoutChannel,
            sunday,
            new PlaceCatalog(["The Goblet"]));

        Assert.NotNull(entry);
        Assert.True(EventRepeat.FallsOn(entry, new DateOnly(2026, 10, 7)));
        Assert.True(EventRepeat.FallsOn(entry, new DateOnly(2026, 10, 28)));
        Assert.False(EventRepeat.FallsOn(entry, new DateOnly(2026, 10, 14)));
    }

    [Fact]
    public void BoxedTonightAndLavBedsStillMakeAnInvite()
    {
        const string first = "\u25CE \uE082\uE075\uE086\uE075\uE082\uE072 \uE088 \uE075\uE082\uE071\uE083\uE075 \u25CE \uE07D\uE084\uE086 [21+ \uE07E\uE083\uE076\uE087] DJs Bella Doll & Snowing Sky";
        const string second = "\u300B \uE084\uE07F\uE07E\uE079\uE077\uE078\uE084 @ \uE031 7 pm - 11 pm EST \u21D4 Dynamis - Cuchulainn - Lav Beds - Ward 20 - Plot 57 \u300A More Information discord.gg/exampleclub OR example.carrd.co/";
        var when = new DateTimeOffset(2026, 9, 28, 22, 0, 0, TimeSpan.Zero);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var alone = ShoutHarvest.TryHarvest(second, ShoutHarvest.ShoutChannel, when, aggressive: true, zone: zone);
        var joined = ShoutHarvest.TryHarvest(first + " " + second, ShoutHarvest.ShoutChannel, when, aggressive: true, zone: zone);
        Assert.NotNull(alone);
        Assert.NotNull(joined);
        Assert.Equal(new TimeOnly(19, 0), joined!.Time);
        Assert.Equal(new TimeOnly(23, 0), joined.End);
        Assert.Equal(20, joined.Ward);
        Assert.Contains("Cuchulainn", joined.Server, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Lavender", joined.Place, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("plot 57", joined.Place, StringComparison.OrdinalIgnoreCase);
        var hinted = ShoutHarvest.TryHarvest(second, ShoutHarvest.ShoutChannel, when, housingHint: "Mist", aggressive: true, zone: zone);
        Assert.NotNull(hinted);
        Assert.Contains("Lavender", hinted.Place, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Mist", hinted.Place, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SharedOnSummarizesAFullDataCenter()
    {
        var crystal = DataCenters.All.First(group => group.Name == "Crystal").Worlds;
        Assert.Equal("Shared on all of Crystal", DataCenters.SharedOn(crystal));
        Assert.Equal("Shared on all of Crystal except Coeurl", DataCenters.SharedOn(crystal.Where(name => name != "Coeurl")));
        Assert.Equal("Shared on Diabolos, Mateus, and Zalera", DataCenters.SharedOn(["Diabolos", "Mateus", "Zalera"]));
        Assert.Equal("Shared on most of Crystal", DataCenters.SharedOn(crystal.Take(5)));
        var primal = DataCenters.All.First(group => group.Name == "Primal").Worlds;
        Assert.Equal("Shared on all of Primal and Crystal", DataCenters.SharedOn(crystal.Concat(primal)));
    }

    [Fact]
    public void BoxedLettersBecomeTheTitleAndASharedInviteCanBeSavedLocally()
    {
        var moonlit = "\uE07D\uE07F\uE07F\uE07E\uE07C\uE079\uE084 \uE07B\uE079\uE083\uE083 is having Emo Night";
        Assert.Equal("\uE07D\uE07F\uE07F\uE07E\uE07C\uE079\uE084 \uE07B\uE079\uE083\uE083", EventTitle.Choose(moonlit, "dance club"));
        var jet = "\uE080\uE085\uE082\uE075 \uE03C\uE07A\uE075\uE084 Flying HIGH";
        Assert.Equal("\uE080\uE085\uE082\uE075 \uE03C\uE07A\uE075\uE084", EventTitle.Choose(jet));
        var ugly = "\uE07D\uE079\uE081\uE07F'\uE084\uE075 \uE085\uE077\uE07C\uE089 is pourin'";
        Assert.Equal("\uE07D\uE079\uE081\uE07F'\uE084\uE075 \uE085\uE077\uE07C\uE089", EventTitle.Choose(ugly));
        Assert.Equal("bard show", EventTitle.Choose("open at 8pm ward 4", "bard show"));
        var beds = HousingTravel.Find(null, "Lavender Beds Ward 1 Plot51", null, null);
        Assert.NotNull(beds);
        Assert.Equal("The Lavender Beds", beds.Value.District);
        Assert.Equal(1, beds.Value.Ward);
        Assert.Equal(51, beds.Value.Plot);
        Assert.Equal("New Gridania", beds.Value.City);
        Assert.Equal("Ul'dah - Steps of Nald", HousingTravel.Find(null, "Goblet W3 P12", null, null)!.Value.City);
        var fromSpeaker = HousingTravel.FindVenue("ward 16, plot 13, The Goblet", "Maid cafe at the Goblet W16 P13", 16, null, "Malboro");
        Assert.Equal("Malboro", fromSpeaker!.Value.World);
        Assert.Equal("The Goblet", fromSpeaker.Value.District);
        var named = HousingTravel.FindVenue(null, "Goblet ward 3 plot 12 on Faerie", 3, null, "Malboro");
        Assert.Equal("Faerie", named!.Value.World);
        Assert.Equal("Limsa Lominsa Lower Decks", HousingTravel.Find(null, "Mist plot 8", null, null)!.Value.City);
        var glued = ShoutHarvest.TryHarvest(
            "9 PM Crystal | Coeurl | Lav Beds W16P6",
            ShoutHarvest.ShoutChannel,
            new DateTimeOffset(2026, 9, 28, 22, 0, 0, TimeSpan.Zero),
            new PlaceCatalog(["Hunter's Ring", "Company Workshop - Mist", "Crystal Tower Training Grounds"]),
            aggressive: true);
        Assert.NotNull(glued);
        Assert.Equal(16, glued.Ward);
        Assert.Contains("plot 6", glued.Place, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Lavender", glued.Place, StringComparison.Ordinal);
        Assert.DoesNotContain("Mist", glued.Place, StringComparison.Ordinal);
        var spot = HousingTravel.Find(
            "ward 16, plot 6, Company Workshop - Mist, Crystal, Coeurl",
            "Crystal | Coeurl | Lav Beds W16P6",
            16,
            "Crystal, Coeurl");
        Assert.Equal("The Lavender Beds", spot!.Value.District);
        Assert.Equal(16, spot.Value.Ward);
        Assert.Equal(6, spot.Value.Plot);
        var empyreum = ShoutHarvest.TryHarvest(
            "Grand opening tonight Dyn Raff Empy W5 P30 7:30pm-11:30pm ET",
            ShoutHarvest.ShoutChannel,
            new DateTimeOffset(2026, 9, 28, 22, 0, 0, TimeSpan.Zero),
            housingHint: "Mist",
            aggressive: true,
            zone: TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));
        Assert.NotNull(empyreum);
        Assert.Contains("Empyreum", empyreum.Place, StringComparison.Ordinal);
        Assert.Contains("Rafflesia", empyreum.Server, StringComparison.Ordinal);
        Assert.DoesNotContain("Mist", empyreum.Place, StringComparison.Ordinal);
        Assert.Equal(5, empyreum.Ward);
        var debug = ParseDebug.Line(empyreum);
        Assert.StartsWith("Shout Calendar: [Debug] Kept", debug, StringComparison.Ordinal);
        Assert.Contains("Empyreum", debug, StringComparison.Ordinal);
        Assert.Contains("Rafflesia", debug, StringComparison.Ordinal);
        var lalaween = "△\uE07C\uE071\uE07C\uE071\uE087\uE075\uE075\uE07E□ returns on Oct 3 to help kick off the spoopy season! Join us for a fun night of interactive games. lalaween2026.carrd.co";
        var heard = new DateTimeOffset(2026, 9, 28, 1, 30, 0, TimeSpan.Zero);
        var party = ShoutHarvest.TryHarvest(lalaween, ShoutHarvest.ShoutChannel, heard, aggressive: true, zone: TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));
        Assert.NotNull(party);
        Assert.Equal(new DateOnly(2026, 10, 3), party.Date);
        Assert.Null(party.Time);
        var catalog = new PlaceCatalog(["Information Center", "The Goblet"]);
        var withCatalog = ShoutHarvest.TryHarvest(lalaween, ShoutHarvest.ShoutChannel, heard, catalog, aggressive: true, zone: TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));
        Assert.NotNull(withCatalog);
        Assert.DoesNotContain("Information", withCatalog.Place ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Equal("\uE07C\uE071\uE07C\uE071\uE087\uE075\uE075\uE07E", EventTitle.Choose(party.EventText));
        Assert.Equal("lalaween", EventTitle.SearchKey(party.EventText).Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]);
        Assert.Contains("lalaween", EventTitle.SearchKey("Lalaween"), StringComparison.Ordinal);
        Assert.Contains("lalaween", EventTitle.SearchKey("LALAWEEN"), StringComparison.Ordinal);
        Assert.Equal("", EventTitle.Choose("open at 8pm ward 4"));

        var item = new SyncAnnouncement
        {
            Id = "goblin-night",
            World = "Goblin",
            Channel = 11,
            Text = "Open at 3pm ward 14 plot 8",
            Date = "2026-09-27",
            Time = "18:00",
            FromSync = true,
        };
        var copy = LocalCopy.From(item, new PlaceCatalog(["The Lavender Beds"]), new DateTimeOffset(2026, 9, 27, 21, 49, 0, TimeSpan.Zero));
        Assert.True(copy.Manual);
        Assert.True(copy.Accepted);
        Assert.Equal(0, copy.Channel);
        Assert.Equal(item.Text, copy.EventText);
        Assert.Equal(new DateOnly(2026, 9, 27), copy.Date);
        Assert.Equal(new TimeOnly(18, 0), copy.Time);
        var log = new CalendarLog();
        Assert.True(log.Add(copy));
        Assert.Contains(log.Entries, row => row.Manual && row.EventText == item.Text);
    }

    [Fact]
    public void PacificClockBecomesLocalAndAStoredFifteenHundredFollowsTheText()
    {
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var pacific = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        var central = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        var mountain = TimeZoneInfo.FindSystemTimeZoneById("America/Denver");
        var when = new DateTimeOffset(2026, 9, 27, 21, 49, 0, TimeSpan.Zero);
        const string toast = "The taproom, /Toast, is opening soon! Come for some drinks, live bard music and chill RP (RP not required). Open at 3pm PT, Goblin > Lavender Beds > Ward 14, Plot 8";
        var places = new PlaceCatalog(["The Lavender Beds"]);

        Assert.Null(ShoutHarvest.TryHarvest("Ely Sol'aris laughs at Mistress Boss'bunny.", ShoutHarvest.ShoutChannel, when, places, zone: eastern));

        var entry = ShoutHarvest.TryHarvest(toast, ShoutHarvest.ShoutChannel, when, places, aggressive: true, zone: eastern);
        Assert.NotNull(entry);
        Assert.Equal(toast, entry.EventText);
        Assert.Equal(14, entry.Ward);
        Assert.Contains("plot 8", entry.Place, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Goblin", entry.Server);
        Assert.Contains("The Lavender Beds", entry.Place, StringComparison.Ordinal);
        Assert.Equal(new DateOnly(2026, 9, 27), entry.Date);
        Assert.Equal(new TimeOnly(18, 0), entry.Time);
        Assert.False(OngoingCheck.IsOngoing(entry, when));

        var inLosAngeles = ShoutHarvest.TryHarvest(toast, ShoutHarvest.ShoutChannel, when, places, aggressive: true, zone: pacific);
        Assert.NotNull(inLosAngeles);
        Assert.Equal(new TimeOnly(15, 0), inLosAngeles.Time);
        Assert.Equal(new DateOnly(2026, 9, 27), inLosAngeles.Date);

        var late = ShoutHarvest.TryHarvest("Open at 11:30pm PT ward 14 plot 8", ShoutHarvest.ShoutChannel, when, places, aggressive: true, zone: eastern);
        Assert.NotNull(late);
        Assert.Equal(new TimeOnly(2, 30), late.Time);
        Assert.Equal(new DateOnly(2026, 9, 28), late.Date);
        var lateFace = ZoneClock.Shown(late, eastern);
        Assert.Equal(new DateOnly(2026, 9, 28), lateFace.Date);
        Assert.Equal(new TimeOnly(2, 30), lateFace.Time);
        late = late with { Id = "late", Accepted = true };
        var lateMorning = new DateTime(2026, 9, 28, 2, 30, 0);
        var lateHit = Assert.Single(EventAlarm.Due([late], lateMorning, lateMorning.AddMinutes(-1), true, false, 0, eastern));
        Assert.Equal("late", lateHit.Id);
        var dayAfter = new DateTime(2026, 9, 29, 2, 30, 0);
        Assert.Empty(EventAlarm.Due([late], dayAfter, dayAfter.AddMinutes(-1), true, false, 0, eastern));

        var early = ShoutHarvest.TryHarvest("Open at 1:30am ET ward 1", ShoutHarvest.ShoutChannel, when, aggressive: true, zone: pacific);
        Assert.NotNull(early);
        Assert.Equal(new DateOnly(2026, 9, 26), early.Date);
        Assert.Equal(new TimeOnly(22, 30), early.Time);
        var earlyFace = ZoneClock.Shown(early, pacific);
        Assert.Equal(new DateOnly(2026, 9, 26), earlyFace.Date);
        Assert.Equal(new TimeOnly(22, 30), earlyFace.Time);
        early = early with { Id = "early", Accepted = true };
        var earlyNight = new DateTime(2026, 9, 26, 22, 30, 0);
        var earlyHit = Assert.Single(EventAlarm.Due([early], earlyNight, earlyNight.AddMinutes(-1), true, false, 0, pacific));
        Assert.Equal("early", earlyHit.Id);
        var nightBefore = new DateTime(2026, 9, 25, 22, 30, 0);
        Assert.Empty(EventAlarm.Due([early], nightBefore, nightBefore.AddMinutes(-1), true, false, 0, pacific));

        var plain = ShoutHarvest.TryHarvest("Open at 3pm ward 14 plot 8", ShoutHarvest.ShoutChannel, when, places, aggressive: true, zone: eastern);
        Assert.NotNull(plain);
        Assert.Equal(new TimeOnly(15, 0), plain.Time);

        var serverTime = ShoutHarvest.TryHarvest("8:00pm ST ward 4 on Diabolos", ShoutHarvest.ShoutChannel, when, zone: eastern);
        Assert.NotNull(serverTime);
        Assert.Equal(new TimeOnly(20, 0), serverTime.Time);

        Assert.Equal(new TimeOnly(12, 0), ShoutHarvest.TryHarvest("Open at 3pm ET ward 1", ShoutHarvest.ShoutChannel, when, aggressive: true, zone: pacific)!.Time);
        Assert.Equal(new TimeOnly(16, 0), ShoutHarvest.TryHarvest("Open at 3pm CT ward 1", ShoutHarvest.ShoutChannel, when, aggressive: true, zone: eastern)!.Time);
        Assert.Equal(new TimeOnly(17, 0), ShoutHarvest.TryHarvest("Open at 3pm MT ward 1", ShoutHarvest.ShoutChannel, when, aggressive: true, zone: eastern)!.Time);
        var departing = ShoutHarvest.TryHarvest("in 20 minutes on Faerie", ShoutHarvest.ShoutChannel, when, zone: eastern);
        Assert.Equal(new TimeOnly(18, 9), departing!.Time);

        var stored = entry with { Time = new TimeOnly(15, 0), Id = "toast", Accepted = true };
        var face = ZoneClock.Shown(stored, eastern);
        Assert.Equal(new DateOnly(2026, 9, 27), face.Date);
        Assert.Equal(new TimeOnly(18, 0), face.Time);
        var afternoon = new DateTime(2026, 9, 27, 17, 49, 0);
        Assert.Empty(EventAlarm.Due([stored], afternoon, afternoon.AddMinutes(-1), true, false, 0, eastern));
        var evening = new DateTime(2026, 9, 27, 18, 0, 0);
        var hit = Assert.Single(EventAlarm.Due([stored], evening, evening.AddMinutes(-1), true, false, 0, eastern));
        Assert.Equal("toast", hit.Id);

        var directory = Path.Combine(Path.GetTempPath(), "shout-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var unix = (uint)when.ToUnixTimeSeconds();
        var file = LogFixture.File(
            0,
            LogFixture.Entry(unix, 0x0B, 0x0B, "Tirita Rita", LogFixture.Utf8("Ely Sol'aris laughs at Mistress Boss'bunny.")),
            LogFixture.Entry(unix, 0x0B, 0x0B, "Tirita Rita", LogFixture.Utf8(toast)));
        File.WriteAllBytes(Path.Combine(directory, "00000000.log"), file);
        var report = ChatWatch.Report([directory], places, eastern);
        Assert.Contains("kept: yes", report, StringComparison.Ordinal);
        Assert.Contains("clock: 18:00", report, StringComparison.Ordinal);
        Assert.DoesNotContain("Mistress Boss", report, StringComparison.Ordinal);
        Directory.Delete(directory, true);
    }

    [Fact]
    public void AbbreviatedHousingAndAGluedZoneClockStayOnTheCalendar()
    {
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var when = new DateTimeOffset(2026, 9, 27, 22, 0, 0, TimeSpan.Zero);
        const string goblet = "\u2605\uE072\uE082\uE079\uE071\uE082\uE084\uE078\uE07F\uE082\uE07E\u2605 Open @ 10ET! Dyn Krak Gob W6 P60";
        var entry = ShoutHarvest.TryHarvest(goblet, ShoutHarvest.ShoutChannel, when, aggressive: true, zone: eastern);
        Assert.NotNull(entry);
        Assert.Equal(new TimeOnly(22, 0), entry.Time);
        Assert.Equal(new DateOnly(2026, 9, 27), entry.Date);
        Assert.Equal(6, entry.Ward);
        Assert.Contains("plot 60", entry.Place, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("The Goblet", entry.Place, StringComparison.Ordinal);
        Assert.Equal("Dynamis, Kraken", entry.Server);
        Assert.Equal("\uE072\uE082\uE079\uE071\uE082\uE084\uE078\uE07F\uE082\uE07E", EventTitle.Choose(entry.EventText));
        var spot = HousingTravel.Find(entry.Place, entry.EventText, entry.Ward, entry.Server);
        Assert.Equal("The Goblet", spot!.Value.District);
        Assert.Equal(6, spot.Value.Ward);
        Assert.Equal(60, spot.Value.Plot);
        Assert.Equal("Kraken", spot.Value.World);
        Assert.Equal("Ul'dah - Steps of Nald", spot.Value.City);

        var goblin = ShoutHarvest.TryHarvest("8pm Goblin ward 3", ShoutHarvest.ShoutChannel, when, aggressive: true, zone: eastern);
        Assert.NotNull(goblin);
        Assert.Equal("Goblin", goblin.Server);
        Assert.DoesNotContain("Goblet", goblin.Place, StringComparison.OrdinalIgnoreCase);

        Assert.Null(ShoutHarvest.TryHarvest("A character attains level 6!", ShoutHarvest.ShoutChannel, when, aggressive: true, zone: eastern));
        Assert.Null(ShoutHarvest.TryHarvest("A character attains level 7!", ShoutHarvest.ShoutChannel, when, aggressive: true, zone: eastern));

        const string beds = "[18+] \uE073\uE071\uE084\uE083\uE085\uE07E\uE075 \uE073\uE071\uE072\uE071\uE082\uE075\uE084 Latin America Night Tonight 8PM-12AM EST Dynamis/Kraken/LB/W7/P3";
        var night = ShoutHarvest.TryHarvest(beds, ShoutHarvest.ShoutChannel, when, aggressive: true, zone: eastern);
        Assert.NotNull(night);
        Assert.Equal(new TimeOnly(20, 0), night.Time);
        Assert.Equal(new TimeOnly(0, 0), night.End);
        Assert.Equal(7, night.Ward);
        Assert.Contains("plot 3", night.Place, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("The Lavender Beds", night.Place, StringComparison.Ordinal);
        var city = HousingTravel.Find(night.Place, night.EventText, night.Ward, night.Server);
        Assert.Equal("The Lavender Beds", city!.Value.District);
        Assert.Equal("New Gridania", city.Value.City);
        Assert.Equal("Kraken", city.Value.World);
        Assert.Equal("\uE073\uE071\uE084\uE083\uE085\uE07E\uE075 \uE073\uE071\uE072\uE071\uE082\uE075\uE084", EventTitle.Choose(night.EventText));
    }

    [Fact]
    public void NowUntilAnEasternClockKeepsTheHeardMinuteThroughMidnight()
    {
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var when = new DateTimeOffset(2026, 9, 27, 21, 30, 0, TimeSpan.FromHours(-4));
        const string text = "Crys-Bryn-LB-W14-P58 | Now-12a ET";
        var entry = ShoutHarvest.TryHarvest(text, ShoutHarvest.ShoutChannel, when, aggressive: true, zone: eastern);
        Assert.NotNull(entry);
        Assert.Equal(new TimeOnly(21, 30), entry!.Time);
        Assert.Equal(new TimeOnly(0, 0), entry.End);
        Assert.Equal(new DateOnly(2026, 9, 27), entry.Date);
        Assert.Equal(14, entry.Ward);
        Assert.Contains("plot 58", entry.Place, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("The Lavender Beds", entry.Place, StringComparison.Ordinal);
        Assert.Contains("Brynhildr", entry.Server, StringComparison.Ordinal);
        var shown = ZoneClock.ShownRange(entry, eastern);
        Assert.Equal(new TimeOnly(21, 30), shown.Start);
        Assert.Equal(new TimeOnly(0, 0), shown.End);
    }

    [Fact]
    public void AStoredNowUntilLineGainsItsMidnightEnd()
    {
        var log = new CalendarLog();
        log.Add(new CalendarEntry(
            new DateOnly(2026, 9, 27),
            new TimeOnly(22, 17),
            null,
            14,
            null,
            "ward 14, plot 58",
            "Crys-Bryn-LB-W14-P58 | Now-12a ET",
            "Mina",
            false,
            "ugly",
            new DateTimeOffset(2026, 9, 28, 2, 17, 44, TimeSpan.Zero),
            Channel: ShoutHarvest.ShoutChannel));
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var changed = log.Reharvest(entry => ShoutHarvest.TryHarvest(
            entry.EventText,
            entry.Channel,
            entry.DetectedAt,
            aggressive: false,
            zone: eastern));
        Assert.Equal(1, changed);
        var kept = Assert.Single(log.Entries);
        Assert.Equal(new TimeOnly(22, 17), kept.Time);
        Assert.Equal(new TimeOnly(0, 0), kept.End);
        Assert.Equal(14, kept.Ward);
        Assert.Contains("Brynhildr", kept.Server, StringComparison.Ordinal);
        Assert.Contains("The Lavender Beds", kept.Place, StringComparison.Ordinal);
    }

    [Fact]
    public void ClearingADataCenterReturnsToTheLastWorldYouPicked()
    {
        var worlds = new WorldCalendar("Diabolos");
        foreach (var world in new[] { "Balmung", "Brynhildr", "Coeurl", "Goblin", "Malboro", "Mateus", "Zalera" })
            worlds.SetChecked(world, true);
        worlds.SetViewDataCenter("Crystal", true);
        Assert.Contains("Zalera", worlds.Viewing());
        Assert.Contains("Balmung", worlds.Viewing());
        worlds.SetViewDataCenter("Crystal", false);
        Assert.Equal(["Diabolos"], worlds.Viewing());

        worlds.SetViewing("Goblin", true);
        worlds.SetViewDataCenter("Crystal", true);
        worlds.SetViewDataCenter("Crystal", false);
        Assert.Equal(["Goblin"], worlds.Viewing());
    }

    [Fact]
    public void ClickingThroughViewedWorldsKeepsOnlyTheLastOne()
    {
        var worlds = new WorldCalendar("Diabolos");
        foreach (var world in new[] { "Balmung", "Goblin", "Mateus", "Zalera" })
            worlds.SetChecked(world, true);
        Assert.True(worlds.Select("Balmung"));
        Assert.True(worlds.Select("Goblin"));
        Assert.True(worlds.Select("Mateus"));
        Assert.Equal(["Mateus"], worlds.Viewing());
        Assert.Equal("Mateus", worlds.Selected);
        Assert.Equal("Mateus", worlds.LastPicked);
        worlds.SetViewing("Mateus", false);
        Assert.Equal(["Mateus"], worlds.Viewing());
        worlds.SetViewDataCenter("Crystal", true);
        worlds.SetViewing("Zalera", false);
        Assert.Contains("Mateus", worlds.Viewing());
        Assert.Equal("Mateus", worlds.Selected);
    }

    [Fact]
    public void MidnightEndStaysOnTheStartDayAndOneMinutePastSpans()
    {
        var day = new DateOnly(2026, 9, 27);
        var start = new TimeOnly(20, 0);
        Assert.False(Overnight.Is(start, new TimeOnly(0, 0)));
        Assert.True(Overnight.Covers(day, day, start, new TimeOnly(0, 0)));
        Assert.False(Overnight.Covers(day.AddDays(1), day, start, new TimeOnly(0, 0)));
        Assert.Null(Overnight.Span(day, start, new TimeOnly(0, 0)));

        Assert.True(Overnight.Is(start, new TimeOnly(0, 1)));
        Assert.True(Overnight.Covers(day.AddDays(1), day, start, new TimeOnly(0, 1)));
        Assert.Equal((day, day.AddDays(1)), Overnight.Span(day, start, new TimeOnly(0, 1)));

        var session = new CalendarSession(day);
        Assert.True(session.TryAddShout("Open 20:00-0:00 ward 3", ShoutHarvest.ShoutChannel, ShoutAt, "Mina"));
        var entry = Assert.Single(session.Log.Entries);
        Assert.Equal(start, entry.Time);
        Assert.Equal(new TimeOnly(0, 0), entry.End);
        var stored = entry.Date!.Value;
        var month = CalendarMonth.Create(stored.Year, stored.Month, session.Log.Entries);
        Assert.Contains(entry, month.OnDay(stored.Day));
        var following = stored.AddDays(1);
        if (following.Month == stored.Month)
            Assert.DoesNotContain(entry, month.OnDay(following.Day));
    }

    [Fact]
    public void AnOvernightRangeCoversTheNextCalendarDay()
    {
        var session = new CalendarSession(new DateOnly(2026, 9, 27));
        Assert.True(session.TryAddShout("Open 11pm-6am ward 3", ShoutHarvest.ShoutChannel, ShoutAt, "Mina"));
        var entry = Assert.Single(session.Log.Entries);
        Assert.Equal(new TimeOnly(23, 0), entry.Time);
        Assert.Equal(new TimeOnly(6, 0), entry.End);
        Assert.True(Overnight.Is(entry.Time, entry.End));
        var start = entry.Date!.Value;
        Assert.True(Overnight.Covers(start, start, entry.Time, entry.End));
        Assert.True(Overnight.Covers(start.AddDays(1), start, entry.Time, entry.End));
        Assert.False(Overnight.Covers(start.AddDays(2), start, entry.Time, entry.End));
        var month = CalendarMonth.Create(start.Year, start.Month, session.Log.Entries);
        Assert.Contains(entry, month.OnDay(start.Day));
        var next = start.AddDays(1);
        if (next.Month == start.Month)
            Assert.Contains(entry, month.OnDay(next.Day));
    }

    [Fact]
    public void AHuntTrainWithSpacedCoordinatesAndACountdownIsKept()
    {
        var heard = new DateTimeOffset(2026, 9, 28, 18, 0, 0, TimeSpan.Zero);
        const string text = "RELAY—> DIABOLOS DT/EW/SHB **TRIPLE** HUNT Train is leaving from \uE0BBUrqopacha ( 28.0  , 13.2 ) in 6 min";
        var entry = ShoutHarvest.TryHarvest(text, ShoutHarvest.ShoutChannel, heard, aggressive: true, zone: TimeZoneInfo.Utc);

        Assert.NotNull(entry);
        Assert.Equal(new TimeOnly(18, 6), entry!.Time);
        Assert.Contains("Urqopacha", entry.Place, StringComparison.Ordinal);
        Assert.Contains("x 28, y 13.2", entry.Place, StringComparison.Ordinal);
        Assert.Contains("Diabolos", entry.Place, StringComparison.Ordinal);
        var spot = Assert.Single(MapMentions.Read(text));
        Assert.Equal(28.0f, spot.X);
        Assert.Equal(13.2f, spot.Y);
        Assert.Equal("Urqopacha", spot.Place);
    }

    [Fact]
    public void AGobletNightWithAGluedClockRangeIsKept()
    {
        var entry = ShoutHarvest.TryHarvest(
            "Mood Swing 8PM-12AM EST. Golem Goblet W21 P35.",
            ShoutHarvest.ShoutChannel,
            new DateTimeOffset(2026, 9, 28, 22, 0, 0, TimeSpan.Zero),
            aggressive: true,
            zone: TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));

        Assert.NotNull(entry);
        Assert.Equal(new TimeOnly(20, 0), entry!.Time);
        Assert.Equal(new TimeOnly(0, 0), entry.End);
        Assert.Equal(21, entry.Ward);
        Assert.Contains("plot 35", entry.Place, StringComparison.Ordinal);
        Assert.Contains("The Goblet", entry.Place, StringComparison.Ordinal);
        Assert.Contains("Golem", entry.Place, StringComparison.Ordinal);
    }

    [Fact]
    public void HyphenatedRafflesiaGobletAddressIsAPlace()
    {
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var when = new DateTimeOffset(2026, 9, 28, 7, 0, 0, TimeSpan.Zero);
        const string text = "THE DOG HAUS NOW @ Raff-Goblet-Ward 21-Plot 4 discord.gg/exampleclub";
        var entry = ShoutHarvest.TryHarvest(text, ShoutHarvest.ShoutChannel, when, aggressive: true, zone: eastern);
        Assert.NotNull(entry);
        Assert.Equal(21, entry!.Ward);
        Assert.Contains("plot 4", entry.Place, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("The Goblet", entry.Place, StringComparison.Ordinal);
        Assert.Contains("Rafflesia", entry.Server, StringComparison.Ordinal);
        var spot = HousingTravel.Find(entry.Place, entry.EventText, entry.Ward, entry.Server);
        Assert.Equal("The Goblet", spot!.Value.District);
        Assert.Equal(21, spot.Value.Ward);
        Assert.Equal(4, spot.Value.Plot);
        Assert.Equal("Rafflesia", spot.Value.World);
        Assert.Equal("Ul'dah - Steps of Nald", spot.Value.City);
    }

    [Fact]
    public void TwoLinesFromOnePlayerKeepTheBoxedTitle()
    {
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var session = new CalendarSession(new DateOnly(2026, 9, 27)) { Zone = eastern };
        const string kiss = "\uE07D\uE07F\uE07F\uE07E\uE07C\uE079\uE084 \uE07B\uE079\uE083\uE083";
        var first = $"Bring your black eyeliner, broken hearts & emo anthems! Tonight {kiss} is having Emo Night!";
        var second = "Time: Open now/ Crystal Zalera Goblet W7 P5/ \uE091\uE08F MIL + Giveaways | discord.gg/exampleclub";
        var when = new DateTimeOffset(2026, 9, 28, 2, 20, 18, TimeSpan.Zero);
        Assert.False(session.TryAddShout(first, ShoutHarvest.ShoutChannel, when, "Mina Willow Mina Willow"));
        Assert.True(session.TryAddShout(second, ShoutHarvest.ShoutChannel, when.AddSeconds(2), "Mina Willow"));
        var entry = Assert.Single(session.Log.Entries);
        Assert.Equal(kiss, EventTitle.Choose(entry.EventText));
        Assert.Contains(kiss, entry.EventText, StringComparison.Ordinal);
        Assert.Contains("\uE091\uE08F", entry.EventText, StringComparison.Ordinal);
        Assert.Equal(new TimeOnly(22, 20), entry.Time);
        Assert.Equal(new DateOnly(2026, 9, 27), entry.Date);
        Assert.Equal(7, entry.Ward);
        Assert.Contains("Zalera", entry.Server, StringComparison.Ordinal);
        Assert.Contains("The Goblet", entry.Place, StringComparison.Ordinal);
        Assert.Contains("plot 5", entry.Place, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Mina Willow", entry.Sender);
    }
}
