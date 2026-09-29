using ShoutCalendar.Core;

namespace ShoutCalendar.Tests;

public class ReadinessTests
{
    private static CalendarEntry Night(DateOnly day, string id, string world = "Diabolos") => new(day,
        new TimeOnly(21, 0), new TimeOnly(23, 0), 1, world, "Goblet W1 P1",
        "Moonlight music and dancing tonight at Goblet W1 P1 at 9pm", "Same Host", false, id,
        DateTimeOffset.UtcNow, Channel: 11);

    [Fact]
    public void AllMergePathsKeepDifferentNightsAndVenuesSeparate()
    {
        var first = Night(new DateOnly(2026, 10, 1), "first");
        var second = Night(new DateOnly(2026, 10, 2), "second");
        var log = new CalendarLog();
        log.Add(first); log.Add(second);
        Assert.Equal(2, log.Entries.Count);
        Assert.False(EventIdentity.SameSpeaker(first, second, DateTimeOffset.UtcNow));
        var rows = new List<SyncAnnouncement>();
        SyncMerge.Apply(rows, SyncAnnouncement.FromLocal(first, "Diabolos"));
        SyncMerge.Apply(rows, SyncAnnouncement.FromLocal(second, "Diabolos"));
        Assert.Equal(2, rows.Count);
        Assert.False(EventIdentity.SameEntry(first, first with { Server = "Goblin", SpeakerWorld = "Diabolos" }));
        Assert.False(EventIdentity.SameRepost(first, first with { EventText = first.EventText.Replace("Goblet", "Mist") }));
    }

    [Theory]
    [InlineData("10/1/26", "2026-10-01", 23, 3)]
    [InlineData("12/1/26", "2026-12-01", 23, 4)]
    public void WireClocksSurviveTheDateLineAndSeasonalOffsets(string written, string expectedDay, int hour, int utcHour)
    {
        var tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var parsed = ShoutHarvest.TryHarvest($"Live music on {written} at 11:00pm ET in Goblet W1 P1", 11,
            new DateTimeOffset(2026, 9, 29, 0, 0, 0, TimeSpan.Zero), zone: tokyo);
        Assert.NotNull(parsed);
        var sent = SyncAnnouncement.FromLocal(parsed with { Id = "zone" }, "Diabolos", calendarZone: tokyo);
        var wire = RelayCodec.Decode(RelayCodec.Encode(sent))!;
        Assert.Equal(expectedDay, wire.Date);
        Assert.Equal(utcHour, wire.StartUtc!.Value.Hour);
        var received = SyncClock.Entry(wire, eastern);
        var shown = ZoneClock.ShownRange(received, eastern);
        Assert.Equal(DateOnly.Parse(expectedDay), shown.Date);
        Assert.Equal(new TimeOnly(hour, 0), shown.Start);
        var localHeard = ShoutHarvest.TryHarvest($"Live music on {written} at 11:00pm ET in Goblet W1 P1", 11,
            new DateTimeOffset(2026, 9, 29, 0, 0, 0, TimeSpan.Zero), zone: eastern)!;
        var localWire = SyncAnnouncement.FromLocal(localHeard with { Id = "other" }, "Diabolos", calendarZone: eastern);
        Assert.Equal(SyncMerge.Key(wire), SyncMerge.Key(localWire));
        var savedInTokyo = SyncClock.Entry(wire, tokyo);
        var reshared = SyncAnnouncement.FromLocal(savedInTokyo, "Diabolos", calendarZone: tokyo);
        Assert.Equal(wire.Date, reshared.Date);
        Assert.Equal(wire.Time, reshared.Time);
        Assert.True(EventIdentity.SameRepost(savedInTokyo, wire));
    }

    [Fact]
    public void DateOnlyInvitesStayFloatingAndRepeatsKeepTheirCivilZoneAcrossDst()
    {
        var dateOnly = SyncAnnouncement.FromLocal(Night(new DateOnly(2026, 10, 1), "date") with
            { Time = null, End = null, EventText = "Party October 1 at the Goblet" }, "Diabolos");
        Assert.Null(dateOnly.StartUtc);
        var tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        var recurring = new SyncAnnouncement { Id = "repeat", World = "Diabolos", Channel = 11,
            Date = "2026-10-25", Time = "23:00", End = "01:00", SourceTimeZone = "America/New_York",
            Repeat = "weekly:0", Text = "Live music every Sunday 11pm-1am ET at Goblet W1 P1" };
        var entry = SyncClock.Entry(recurring, tokyo);
        var before = SyncClock.OnDate(entry, new DateOnly(2026, 10, 26), tokyo);
        var after = SyncClock.OnDate(entry, new DateOnly(2026, 11, 2), tokyo);
        Assert.NotNull(before); Assert.NotNull(after);
        Assert.Equal(new TimeOnly(12, 0), ZoneClock.ShownRange(before, tokyo).Start);
        Assert.Equal(new TimeOnly(13, 0), ZoneClock.ShownRange(after, tokyo).Start);
        Assert.False(PastEvents.Ended(recurring, new DateTime(2027, 1, 1), tokyo));
    }

    [Fact]
    public void OvernightBarsUseTheViewersDateForCanonicalAndFloatingClocks()
    {
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var item = new SyncAnnouncement { Id = "night", Date = "2026-12-02", Time = "13:00", End = "15:00",
            SourceTimeZone = "Asia/Tokyo", StartUtc = DateTimeOffset.Parse("2026-12-02T04:00:00Z"),
            EndUtc = DateTimeOffset.Parse("2026-12-02T06:00:00Z") };
        // A local copy can retain a date from a different viewer's zone.
        var entry = SyncClock.Entry(item, TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo"));
        var day = new DateOnly(2026, 12, 1);
        var bar = Assert.Single(Overnight.Spans(entry, day, day.AddDays(1), eastern));
        Assert.Equal(day, bar.Start);
        Assert.Equal(day.AddDays(1), bar.End);
        Assert.Equal(new TimeOnly(23, 0), bar.StartClock);
        Assert.Equal(new TimeOnly(1, 0), bar.EndClock);
        Assert.Empty(Overnight.Spans(entry, day.AddDays(2), day.AddDays(3), eastern));

        var floating = Night(day, "floating") with { Time = new TimeOnly(23, 0), End = new TimeOnly(1, 0), EventText = "Music" };
        Assert.Equal(day, Assert.Single(Overnight.Spans(floating, day, day, eastern)).Start);
        Assert.Empty(Overnight.Spans(floating with { End = TimeOnly.MinValue }, day, day.AddDays(1), eastern));
        Assert.Empty(Overnight.Spans(floating with { Date = null }, day, day.AddDays(1), eastern));
    }

    [Fact]
    public void RecurringBarsFollowDstAndKeepTheTailAtTheVisibleBoundary()
    {
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var item = new SyncAnnouncement { Id = "repeat-night", Date = "2026-10-25", Time = "13:30", End = "15:30",
            SourceTimeZone = "Asia/Tokyo", Repeat = "weekly:0", Text = "Music every Sunday" };
        var entry = SyncClock.Entry(item, eastern);
        var bars = Overnight.Spans(entry, new DateOnly(2026, 10, 24), new DateOnly(2026, 11, 16), eastern).ToArray();
        Assert.Equal(new[] { new DateOnly(2026, 11, 7), new DateOnly(2026, 11, 14) }, bars.Select(bar => bar.Start));
        Assert.All(bars, bar => Assert.Equal(new TimeOnly(23, 30), bar.StartClock));
        Assert.All(bars, bar => Assert.Equal(new TimeOnly(1, 30), bar.EndClock));
        Assert.Equal(new DateOnly(2026, 11, 7), Assert.Single(Overnight.Spans(entry,
            new DateOnly(2026, 11, 8), new DateOnly(2026, 11, 8), eastern)).Start);
        Assert.Equal(new DateOnly(2026, 10, 25), entry.Date);
    }

    [Fact]
    public void InvalidSpringClockDoesNotInventAnInstantAndAmbiguousClockUsesStandardTime()
    {
        Assert.Null(SyncClock.Instant(new DateOnly(2026, 3, 8), new TimeOnly(2, 30), "America/New_York"));
        var autumn = SyncClock.Instant(new DateOnly(2026, 11, 1), new TimeOnly(1, 30), "America/New_York");
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 6, 30, 0, TimeSpan.Zero), autumn);
    }

    [Fact]
    public void StatusUpdateCannotUndoAcceptanceAndEmptyRestoreCannotReplaceUserState()
    {
        var book = new SyncBook("Diabolos");
        book.Events.Add(new SyncAnnouncement { Id = "remote", World = "Diabolos", FromSync = true });
        var stale = book.ToJson();
        Assert.True(book.AcceptRemote("remote"));
        book.PublishStatus("Sync complete!");
        Assert.False(book.RestoreIfEmpty(stale));
        Assert.True(Assert.Single(book.Events).Accepted);
    }

    [Fact]
    public void AnAcceptedSharedEventStillHasAScheduledWarning()
    {
        var item = new SyncAnnouncement { Id = "remote", World = "Diabolos", Channel = 11, Accepted = true,
            Date = "2026-10-01", Time = "20:00", Text = "Live music at Goblet W1 P1" };
        var entry = SyncClock.Entry(item);
        var now = new DateTime(2026, 10, 1, 19, 45, 0);
        var hit = Assert.Single(EventAlarm.Due([entry], now, now.AddMinutes(-1), true, false, 15));
        Assert.Equal("remote", hit.Id);
        Assert.True(hit.Accepted);
    }

    [Fact]
    public void LocalSaveRoundTripRetainsCanonicalClocksAndRevisions()
    {
        var original = Night(new DateOnly(2026, 10, 1), "persist") with
        { StartUtc = DateTimeOffset.Parse("2026-10-02T01:00:00Z"), EndUtc = DateTimeOffset.Parse("2026-10-02T03:00:00Z"),
          SourceTimeZone = "America/New_York", Revision = 7, NoteUpdated = true };
        var json = System.Text.Json.JsonSerializer.Serialize(StoredEvent.From(original));
        var saved = System.Text.Json.JsonSerializer.Deserialize<StoredEvent>(json)!;
        Assert.True(StoredEvent.TryToEntry(saved, out var restored));
        Assert.Equal(original, restored);
    }

    [Fact]
    public void RelayRevisionsPreservePersonalClockCorrections()
    {
        var book = new SyncBook("Diabolos");
        var remote = SyncAnnouncement.FromLocal(Night(new DateOnly(2026, 10, 1), "clock"), "Diabolos");
        remote.FromSync = true;
        book.Events.Add(remote);
        Assert.True(book.SetClock("clock", "2026-10-02", "19:00-21:00"));
        var newer = SyncAnnouncement.FromLocal(Night(new DateOnly(2026, 10, 1), "clock"), "Diabolos");
        newer.Revision = 3;
        SyncMerge.Apply(book.Events, newer);
        var kept = Assert.Single(book.Events);
        Assert.Equal("2026-10-02", kept.Date);
        Assert.Equal("19:00", kept.Time);
        Assert.Equal("21:00", kept.End);
        Assert.True(kept.ClockEditedLocally);
        Assert.DoesNotContain("clockEdited", System.Text.Encoding.UTF8.GetString(RelayCodec.Encode(kept)), StringComparison.OrdinalIgnoreCase);
        var copy = new SyncBook("Diabolos");
        Assert.True(copy.ApplyJson(book.ToJson()));
        Assert.True(Assert.Single(copy.Events).ClockEditedLocally);
        Assert.True(copy.SetClock("clock", "2026-10-02", ""));
        Assert.Null(SyncClock.Entry(Assert.Single(copy.Events)).Time);
    }
    [Fact]
    public void AnOlderRevisionCannotRevertAChangedClock()
    {
        var original = SyncAnnouncement.FromLocal(Night(new DateOnly(2026, 10, 1), "revision"), "Diabolos");
        var updated = RelayCodec.Decode(RelayCodec.Encode(original))!;
        updated.Time = "20:00"; updated.Revision = 3;
        var events = new List<SyncAnnouncement>();
        SyncMerge.Apply(events, updated);
        Assert.Equal(SyncMergeResult.Duplicate, SyncMerge.Apply(events, original));
        Assert.Equal("20:00", Assert.Single(events).Time);
        Assert.Equal(3, Assert.Single(events).Revision);
    }
}
