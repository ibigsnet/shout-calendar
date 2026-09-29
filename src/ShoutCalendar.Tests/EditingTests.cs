using System.Numerics;
using ShoutCalendar.Core;

namespace ShoutCalendar.Tests;

public class EditingTests
{
    private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private static readonly TimeZoneInfo Tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
    private static CalendarEntry Night() => new(new DateOnly(2026, 10, 1), new TimeOnly(21, 0), new TimeOnly(1, 0),
        1, "Diabolos", "Goblet W1 P1", "Community music", "Host", true, "edit", DateTimeOffset.Parse("2026-09-29T00:00:00Z"), Channel: 11);
    private static bool Save(CalendarLog log, string date, string time, out ClockInputErrors errors,
        string note = "Updated music note", Vector4? color = null, TimeZoneInfo? zone = null) =>
        log.TryRevise("edit", note, date, time, "Goblet W1 P1", color, DateTimeOffset.UtcNow,
            PlaceCatalog.Empty, ChatChannels.DefaultIds.ToHashSet(), out errors, zone ?? Eastern);

    [Fact]
    public void TheEditorRoundTripPreservesAnOvernightRangeAndCanonicalInstants()
    {
        var original = Night() with { SourceTimeZone = Eastern.Id, StartUtc = DateTimeOffset.Parse("2026-10-02T01:00:00Z"),
            EndUtc = DateTimeOffset.Parse("2026-10-02T05:00:00Z") };
        var log = new CalendarLog(); log.Add(original);
        var draft = ClockInput.FromEntry(original, Tokyo);
        Assert.Equal("2026-10-02", draft.DateText);
        Assert.Equal("10:00-14:00", draft.TimeText);
        Assert.True(Save(log, draft.DateText, draft.TimeText, out var error, color: Vector4.One, zone: Tokyo));
        Assert.False(error.Any);
        var saved = Assert.Single(log.Entries);
        Assert.Equal(original.End, saved.End);
        Assert.Equal(original.StartUtc, saved.StartUtc);
        Assert.Equal(original.EndUtc, saved.EndUtc);
        Assert.Equal(original.SourceTimeZone, saved.SourceTimeZone);
        Assert.Equal(Vector4.One, saved.Color);
        var shared = SyncAnnouncement.FromLocal(saved, "Diabolos", calendarZone: Tokyo);
        Assert.Equal(Eastern.Id, shared.SourceTimeZone);
        Assert.Equal("2026-10-01", shared.Date);
        Assert.Equal("21:00", shared.Time);
    }

    [Fact]
    public void NoteAndColorEditsPreserveARecurringSourceZoneAcrossDst()
    {
        var original = Night() with { Date = new DateOnly(2026, 10, 25), Time = new TimeOnly(23, 0),
            SourceTimeZone = Eastern.Id, Repeat = new EventRepeat(EventRepeat.Weekly, DayOfWeek.Sunday, 0) };
        var log = new CalendarLog(); log.Add(original);
        var draft = ClockInput.FromEntry(original, Tokyo);
        Assert.True(Save(log, draft.DateText, draft.TimeText, out _, color: Vector4.One, zone: Tokyo));
        var saved = Assert.Single(log.Entries);
        Assert.Equal(original.Date, saved.Date);
        Assert.Equal(original.Time, saved.Time);
        Assert.Equal(original.Repeat, saved.Repeat);
        var shared = SyncAnnouncement.FromLocal(saved, "Diabolos", calendarZone: Tokyo);
        Assert.Equal(Eastern.Id, shared.SourceTimeZone);
        var recurring = SyncClock.Entry(shared, Tokyo);
        var before = SyncClock.OnDate(recurring, new DateOnly(2026, 10, 26), Tokyo)!;
        var after = SyncClock.OnDate(recurring, new DateOnly(2026, 11, 2), Tokyo)!;
        Assert.Equal(new TimeOnly(12, 0), ZoneClock.ShownRange(before, Tokyo).Start);
        Assert.Equal(new TimeOnly(13, 0), ZoneClock.ShownRange(after, Tokyo).Start);
    }

    [Fact]
    public void EditingALegacyLabeledNoteRetainsItsOriginalClock()
    {
        var original = ShoutHarvest.TryHarvest("Music October 1, 2026 at 11pm-1am ET at Goblet W1 P1", 11,
            DateTimeOffset.Parse("2026-09-29T00:00:00Z"), zone: Tokyo)! with { Id = "edit" };
        var log = new CalendarLog(); log.Add(original);
        var draft = ClockInput.FromEntry(original, Tokyo);
        Assert.True(Save(log, draft.DateText, draft.TimeText, out _, note: "Music with friends", zone: Tokyo));
        var saved = Assert.Single(log.Entries);
        Assert.Equal(draft, ClockInput.FromEntry(saved, Tokyo));
        var wire = SyncAnnouncement.FromLocal(saved, "Diabolos", calendarZone: Tokyo);
        Assert.Equal("23:00", wire.Time);
        Assert.Equal(Eastern.Id, wire.SourceTimeZone);
    }

    [Theory]
    [InlineData("2026-13-99", "21:00-23:00", true)]
    [InlineData("2026-02-30", "21:00-23:00", true)]
    [InlineData("2026-10-01", "25:00-23:00", false)]
    [InlineData("2026-10-01", "21:00-25:00", false)]
    [InlineData("2026-10-01", "21:00 garbage", false)]
    [InlineData("2026-10-01", "13pm-11pm", false)]
    public void InvalidDraftsDoNotPartiallyChangeAnySavedField(string date, string time, bool dateError)
    {
        var original = Night(); var log = new CalendarLog(); log.Add(original); var revision = log.Revision;
        Assert.False(Save(log, date, time, out var errors, color: Vector4.One));
        Assert.NotNull(dateError ? errors.Date : errors.Time);
        Assert.Equal(original, Assert.Single(log.Entries));
        Assert.Equal(revision, log.Revision);
    }

    [Fact]
    public void AnExplicitlyRemovedEndOrClockDoesNotReturnFromTheNote()
    {
        var original = Night() with { EventText = "Music at 9pm-1am ET in Goblet W1 P1" };
        var log = new CalendarLog(); log.Add(original);
        Assert.True(Save(log, "2026-10-01", "21:00", out _, original.EventText));
        Assert.Null(ClockInput.FromEntry(Assert.Single(log.Entries), Eastern).End);
        Assert.True(Save(log, "2026-10-01", "", out _, original.EventText));
        var dateOnly = Assert.Single(log.Entries);
        Assert.Null(dateOnly.StartUtc);
        Assert.Null(ClockInput.FromEntry(dateOnly, Tokyo).Start);
        Assert.True(Save(log, "", "", out _, original.EventText));
        Assert.Null(Assert.Single(log.Entries).Date);
    }

    [Theory]
    [InlineData("2026-10-01", "21:00-00:00", 3)]
    [InlineData("2026-03-07", "23:00-04:00", 4)]
    [InlineData("2026-10-31", "23:00-04:00", 6)]
    public void EditedEndsUseTheFollowingCivilDayAcrossMidnightAndDst(string date, string time, int hours)
    {
        var log = new CalendarLog(); log.Add(Night());
        Assert.True(Save(log, date, time, out _));
        var saved = Assert.Single(log.Entries);
        Assert.Equal(TimeSpan.FromHours(hours), saved.EndUtc - saved.StartUtc);
        var wire = RelayCodec.Decode(RelayCodec.Encode(SyncAnnouncement.FromLocal(saved, "Diabolos", calendarZone: Eastern)))!;
        Assert.Equal(saved.StartUtc, wire.StartUtc); Assert.Equal(saved.EndUtc, wire.EndUtc);
        Assert.Equal(saved, StoredEvent.TryToEntry(StoredEvent.From(saved), out var restored) ? restored : null);
        if (saved.End == TimeOnly.MinValue) Assert.Empty(Overnight.Spans(saved, saved.Date!.Value, saved.Date.Value.AddDays(1), Eastern));
    }

    [Fact]
    public void RecurringMidnightUsesTheEndDaysOffsetWithoutDrawingAnotherDay()
    {
        var night = Night() with { Date = new DateOnly(2026, 11, 1), Time = new TimeOnly(1, 30), End = TimeOnly.MinValue,
            SourceTimeZone = Eastern.Id, Repeat = new EventRepeat(EventRepeat.Weekly, DayOfWeek.Sunday, 0) };
        var wire = SyncAnnouncement.FromLocal(night, "Diabolos", calendarZone: Eastern);
        Assert.True(wire.EndUtc > wire.StartUtc);
        var shown = ZoneClock.ShownRange(night, Tokyo);
        Assert.Equal(new TimeOnly(14, 0), shown.End);
        Assert.Empty(Overnight.Spans(night, night.Date!.Value, night.Date.Value.AddDays(1), Eastern));
    }

    [Fact]
    public void SkippedSpringClockAndOverflowingEndLeaveTheOriginalUntouched()
    {
        var log = new CalendarLog(); var original = Night(); log.Add(original);
        Assert.False(Save(log, "2026-03-08", "02:30-04:00", out var spring));
        Assert.NotNull(spring.Time);
        Assert.False(Save(log, "9999-12-31", "23:00-01:00", out var overflow));
        Assert.NotNull(overflow.Date);
        Assert.Equal(original, Assert.Single(log.Entries));
    }

    [Fact]
    public void PersonalClockEditsRefreshTheViewWithoutChangingTheRelayRevision()
    {
        var book = new SyncBook("Diabolos");
        book.Events.Add(new SyncAnnouncement { Id = "clock", World = "Diabolos", Date = "2026-10-01", Time = "21:00", End = "23:00",
            Text = "Community music", Revision = 7, FromSync = true });
        var before = SyncDisplay.Stamp(book.CopyEvents());
        Assert.True(book.SetClock("clock", "2026-10-01", "22:00-01:00", out var errors, Eastern));
        Assert.False(errors.Any);
        var during = SyncDisplay.Stamp(book.CopyEvents());
        Assert.NotEqual(before, during);
        var row = Assert.Single(book.Events);
        Assert.Equal(7, row.Revision);
        Assert.Single(Overnight.Spans(SyncClock.Entry(row, Eastern), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2), Eastern));
        Assert.True(book.SetClock("clock", "2026-10-01", "20:00-22:00", out _, Eastern));
        Assert.NotEqual(during, SyncDisplay.Stamp(book.CopyEvents()));
        Assert.Empty(Overnight.Spans(SyncClock.Entry(row, Eastern), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2), Eastern));
        var snapshot = book.ToJson();
        Assert.False(book.SetClock("clock", "2026-13-99", "20:00", out var invalid, Eastern));
        Assert.True(invalid.Any);
        Assert.Equal(snapshot, book.ToJson());
    }
}
