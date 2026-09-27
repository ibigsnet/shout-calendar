using System.Text;
using ShoutCalendar.Core;

namespace ShoutCalendar.Tests;

public class ReviewTests
{
    private static readonly DateTimeOffset ShoutAt = new(2026, 9, 26, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EditingADateMovesAnUnscheduledInviteOntoThatDay()
    {
        var session = new CalendarSession(new DateOnly(2026, 9, 1));
        session.AggressiveFilter = false;
        Assert.True(session.TryAddShout("ward 13 on Faerie", ShoutHarvest.ShoutChannel, ShoutAt, "Mina"));
        var pending = Assert.Single(session.Log.Entries);
        Assert.Null(pending.Date);

        Assert.True(session.Log.Revise(
            pending.Id,
            "ward 13 on Faerie at 6:30pm",
            "2026-10-13",
            "18:30",
            "Mist",
            ShoutAt,
            PlaceCatalog.Empty,
            session.Channels));
        session.Show(new DateOnly(2026, 10, 13));
        var moved = Assert.Single(session.CurrentMonth().OnDay(13));
        Assert.Equal(new TimeOnly(18, 30), moved.Time);
        Assert.Equal("Mist", moved.Place);
        Assert.False(moved.Accepted);
    }

    [Fact]
    public void LimsaHintsTheMistWardWhenTheShoutNamesOnlyAWard()
    {
        var entry = ShoutHarvest.TryHarvest(
            "8pm W3",
            ShoutHarvest.ShoutChannel,
            ShoutAt,
            housingHint: HousingDistrict.FromZone("Limsa Lominsa Lower Decks"));

        Assert.NotNull(entry);
        Assert.Equal(3, entry.Ward);
        Assert.Contains("Mist", entry.Place);
    }

    [Fact]
    public void DeclineAndDeleteRemovePendingAndAcceptedInvites()
    {
        var session = new CalendarSession(new DateOnly(2026, 9, 1));
        Assert.True(session.TryAddShout("8:00pm ward 13", ShoutHarvest.ShoutChannel, ShoutAt, "Mina"));
        var pending = session.Log.Entries[0].Id;
        Assert.True(session.Log.Remove(pending));
        Assert.Empty(session.Log.Entries);

        Assert.True(session.TryAddShout("8:00pm ward 13", ShoutHarvest.ShoutChannel, ShoutAt, "Mina"));
        var accepted = session.Log.Entries[0];
        Assert.True(session.Log.Accept(accepted.Id));
        Assert.True(session.Log.Revise(accepted.Id, "edited note", "2026-09-26", "20:00", "Mist", ShoutAt, PlaceCatalog.Empty, session.Channels));
        var revised = Assert.Single(session.Log.Entries);
        Assert.True(revised.Accepted);
        Assert.Equal("edited note", revised.EventText);
        Assert.Equal("Mist", revised.Place);
        Assert.True(session.Log.Remove(accepted.Id));
        Assert.Empty(session.Log.Entries);
    }

    [Fact]
    public void UnacceptedInvitesExpireAfterTheHoldAndAcceptedOnesStay()
    {
        var now = DateTimeOffset.UtcNow;
        var log = new CalendarLog();
        var oldPending = ShoutHarvest.TryHarvest("8pm W3", ShoutHarvest.ShoutChannel, now)! with
        {
            Id = "old",
            DetectedAt = now.AddDays(-20),
        };
        var freshPending = ShoutHarvest.TryHarvest("9pm W4", ShoutHarvest.ShoutChannel, now)! with
        {
            Id = "fresh",
            DetectedAt = now.AddDays(-2),
        };
        var accepted = ShoutHarvest.TryHarvest("10pm W5", ShoutHarvest.ShoutChannel, now)! with
        {
            Id = "kept",
            DetectedAt = now.AddDays(-40),
            Accepted = true,
        };
        Assert.True(log.Add(oldPending));
        Assert.True(log.Add(freshPending));
        Assert.True(log.Add(accepted));

        Assert.Equal(1, log.ExpireUnaccepted(now, 14));
        Assert.Equal(2, log.Entries.Count);
        Assert.Contains(log.Entries, entry => entry.Id == "fresh" && !entry.Accepted);
        Assert.Contains(log.Entries, entry => entry.Id == "kept" && entry.Accepted);
    }

    [Fact]
    public void DetectedShoutStaysPendingUntilAccepted()
    {
        var session = new CalendarSession(new DateOnly(2026, 9, 1));
        Assert.True(session.TryAddShout("8:00pm ward 13 on Faerie", ShoutHarvest.ShoutChannel, ShoutAt, "Aria Sky"));

        var pending = Assert.Single(session.CurrentMonth().OnDay(26));
        Assert.False(pending.Accepted);
        Assert.Equal("Aria Sky", pending.Sender);
        Assert.Equal("8:00pm ward 13 on Faerie", pending.EventText);

        Assert.True(session.Log.Accept(pending.Id));
        var accepted = Assert.Single(session.CurrentMonth().OnDay(26));
        Assert.True(accepted.Accepted);
        Assert.Equal(pending.Id, accepted.Id);
        Assert.False(session.Log.Accept(pending.Id));
    }

    [Fact]
    public void ClearAllRemovesPendingAndAcceptedOnlyAfterYes()
    {
        var session = new CalendarSession(new DateOnly(2026, 9, 1));
        Assert.True(session.TryAddShout("8:00pm ward 4", ShoutHarvest.ShoutChannel, ShoutAt, "Mina"));
        var id = session.Log.Entries[0].Id;
        session.Log.Accept(id);

        var prompt = new ClearPrompt();
        Assert.False(prompt.IsOpen);
        prompt.Ask();
        Assert.True(prompt.IsOpen);
        prompt.AnswerNo();
        Assert.False(prompt.IsOpen);
        Assert.Single(session.Log.Entries);

        prompt.Ask();
        prompt.AnswerYes(session.Log);
        Assert.False(prompt.IsOpen);
        Assert.Empty(session.Log.Entries);
        Assert.Empty(session.CurrentMonth().OnDay(26));
    }

    [Fact]
    public void ClearAcceptedLeavesPendingAndClearUnacceptedLeavesAccepted()
    {
        var session = new CalendarSession(new DateOnly(2026, 9, 1));
        Assert.True(session.TryAddShout("8:00pm ward 1", ShoutHarvest.ShoutChannel, ShoutAt, "Mina"));
        Assert.True(session.TryAddShout("9:00pm ward 2", ShoutHarvest.ShoutChannel, ShoutAt, "Ada"));
        var acceptedId = session.Log.Entries[0].Id;
        Assert.True(session.Log.Accept(acceptedId));

        var prompt = new ClearPrompt();
        prompt.Ask(ClearTarget.Accepted);
        prompt.AnswerNo();
        Assert.Equal(2, session.Log.Entries.Count);

        prompt.Ask(ClearTarget.Accepted);
        prompt.AnswerYes(session.Log);
        var pending = Assert.Single(session.Log.Entries);
        Assert.False(pending.Accepted);

        prompt.Ask(ClearTarget.Unaccepted);
        prompt.AnswerYes(session.Log);
        Assert.Empty(session.Log.Entries);
    }

    [Fact]
    public void ClientLogStoresShoutTypeInTheFourthByte()
    {
        var shoutAt = new DateTimeOffset(2026, 9, 26, 16, 0, 0, TimeSpan.Zero);
        var unix = (uint)shoutAt.ToUnixTimeSeconds();
        var name = Encoding.UTF8.GetBytes("Mina Star");
        var sender = new byte[3 + name.Length + 1];
        sender[0] = 0x02;
        sender[1] = 0x27;
        sender[2] = (byte)(name.Length + 1);
        name.CopyTo(sender.AsSpan(3));
        sender[^1] = 0x03;
        var entry = LogFixture.Entry(unix, 0x0B, 0x00, sender, LogFixture.Utf8("party at 8:00pm ward 9"));
        var line = Assert.Single(ChatLogReader.Read(LogFixture.File(0, entry)));

        Assert.Equal(0x0B, line.Channel);
        Assert.Equal("Mina Star", line.Sender);
        var harvested = Assert.Single(ShoutHarvest.HarvestLog(LogFixture.File(0, entry)));
        Assert.Equal(9, harvested.Ward);
        Assert.False(harvested.Accepted);
    }
}
