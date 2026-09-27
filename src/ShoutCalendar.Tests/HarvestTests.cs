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
        Assert.Null(tonight.Time);

        var later = ShoutHarvest.TryHarvest("in 20 minutes on Faerie", ShoutHarvest.ShoutChannel, ShoutAt);
        Assert.NotNull(later);
        Assert.Null(later.Date);
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
        Assert.True(OngoingCheck.IsOngoing(entry, new DateTimeOffset(2026, 9, 26, 8, 0, 0, TimeSpan.Zero)));
        Assert.True(OngoingCheck.IsOngoing(entry, new DateTimeOffset(2026, 9, 26, 9, 15, 0, TimeSpan.Zero)));
        Assert.True(OngoingCheck.IsOngoing(entry, new DateTimeOffset(2026, 9, 26, 10, 0, 59, TimeSpan.Zero)));
        Assert.False(OngoingCheck.IsOngoing(entry, new DateTimeOffset(2026, 9, 26, 7, 59, 0, TimeSpan.Zero)));
        Assert.False(OngoingCheck.IsOngoing(entry, new DateTimeOffset(2026, 9, 26, 10, 1, 0, TimeSpan.Zero)));
        Assert.False(OngoingCheck.IsOngoing(entry, new DateTimeOffset(2026, 9, 27, 9, 0, 0, TimeSpan.Zero)));
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

        var plain = ShoutHarvest.TryHarvest("Open at 3pm ward 14 plot 8", ShoutHarvest.ShoutChannel, when, places, aggressive: true, zone: eastern);
        Assert.NotNull(plain);
        Assert.Equal(new TimeOnly(15, 0), plain.Time);

        var serverTime = ShoutHarvest.TryHarvest("8:00pm ST ward 4 on Diabolos", ShoutHarvest.ShoutChannel, when, zone: eastern);
        Assert.NotNull(serverTime);
        Assert.Equal(new TimeOnly(20, 0), serverTime.Time);

        Assert.Equal(new TimeOnly(12, 0), ShoutHarvest.TryHarvest("Open at 3pm ET ward 1", ShoutHarvest.ShoutChannel, when, aggressive: true, zone: pacific)!.Time);
        Assert.Equal(new TimeOnly(16, 0), ShoutHarvest.TryHarvest("Open at 3pm CT ward 1", ShoutHarvest.ShoutChannel, when, aggressive: true, zone: eastern)!.Time);
        Assert.Equal(new TimeOnly(17, 0), ShoutHarvest.TryHarvest("Open at 3pm MT ward 1", ShoutHarvest.ShoutChannel, when, aggressive: true, zone: eastern)!.Time);
        Assert.Null(ShoutHarvest.TryHarvest("in 20 minutes on Faerie", ShoutHarvest.ShoutChannel, when, zone: eastern)!.Time);

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
}
