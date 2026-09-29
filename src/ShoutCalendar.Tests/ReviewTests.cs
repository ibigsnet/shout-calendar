using System.Numerics;
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

        prompt.Ask(ClearTarget.SyncAccepted);
        var question = prompt.Question(["Diabolos", "Goblin"], "Diabolos");
        Assert.Contains("You currently have the calendars for the following servers open: Diabolos, Goblin.", question, StringComparison.Ordinal);
        Assert.Contains("not only Diabolos", question, StringComparison.Ordinal);
        prompt.AnswerYes(session.Log);
        Assert.Empty(session.Log.Entries);

        var book = new SyncBook("Diabolos");
        book.Worlds.SetChecked("Goblin", true);
        book.Worlds.SetViewing("Goblin", true);
        book.Events.Add(new SyncAnnouncement { Id = "home", World = "Diabolos", Channel = 11, Text = "Maps at 8:00pm ward 13", Accepted = true, FromSync = true });
        book.Events.Add(new SyncAnnouncement { Id = "gob", World = "Goblin", Channel = 11, Text = "Open at 3pm ward 2", FromSync = true });
        book.Events.Add(new SyncAnnouncement { Id = "zal", World = "Zalera", Channel = 11, Text = "Open at 4pm ward 3", FromSync = true });
        Assert.Equal(1, book.DismissOpen(ClearTarget.SyncUnaccepted));
        Assert.Contains(book.Events, row => row.Id == "zal");
        Assert.DoesNotContain(book.Events, row => row.Id == "gob");
        Assert.Equal(1, book.DismissOpen(ClearTarget.SyncAccepted));
        Assert.DoesNotContain(book.Events, row => row.Id == "home");
    }

    [Fact]
    public void HideParksALocalInviteUntilShowHidden()
    {
        var session = new CalendarSession(new DateOnly(2026, 9, 27));
        Assert.True(session.TryAddShout("8:00pm ward 13", ShoutHarvest.ShoutChannel, ShoutAt, "Mina"));
        var id = session.Log.Entries[0].Id;
        Assert.True(session.Log.SetHidden(id, true));
        var hidden = Assert.Single(session.Log.Entries);
        Assert.True(hidden.Hidden);
        Assert.False(hidden.Accepted);
        Assert.False(session.ShowHidden);
        Assert.True(session.Log.SetHidden(id, false));
        Assert.False(session.Log.Entries[0].Hidden);
        Assert.True(session.Log.SetHidden(id, true));
        Assert.True(session.Log.Accept(id));
        Assert.True(session.Log.Entries[0].Accepted);
        Assert.False(session.Log.Entries[0].Hidden);
        Assert.True(session.Log.Remove(id));
        Assert.Empty(session.Log.Entries);
    }

    [Fact]
    public void HideAllParksPendingAndUnhideAllBringsThemBack()
    {
        var session = new CalendarSession(new DateOnly(2026, 9, 27));
        Assert.True(session.TryAddShout("8:00pm ward 13", ShoutHarvest.ShoutChannel, ShoutAt, "Mina"));
        Assert.True(session.TryAddShout("9:00pm ward 4", ShoutHarvest.ShoutChannel, ShoutAt, "Ada"));
        var kept = session.Log.Entries[0].Id;
        Assert.True(session.Log.Accept(kept));
        Assert.Equal(1, session.Log.SetPendingHidden(true));
        Assert.True(session.Log.Entries.Single(entry => entry.Id == kept).Accepted);
        Assert.False(session.Log.Entries.Single(entry => entry.Id == kept).Hidden);
        Assert.Single(session.Log.Entries, entry => entry.Hidden);
        Assert.Equal(1, session.Log.SetPendingHidden(false));
        Assert.DoesNotContain(session.Log.Entries, entry => entry.Hidden);

        var book = new SyncBook("Diabolos");
        book.Events.Add(new SyncAnnouncement { Id = "park", World = "Diabolos", Channel = 11, Text = "Maps at 8:00pm ward 13", FromSync = true });
        book.Events.Add(new SyncAnnouncement { Id = "no", World = "Diabolos", Channel = 11, Text = "Maps at 9:00pm ward 2", FromSync = true, Declined = true });
        Assert.Equal(1, book.HidePending(true));
        Assert.True(book.Events.Single(row => row.Id == "park").Hidden);
        Assert.False(book.Events.Single(row => row.Id == "no").Hidden);
        Assert.Equal(1, book.HidePending(false));
        Assert.False(book.Events.Single(row => row.Id == "park").Hidden);
    }

    [Fact]
    public void SyncHideIsSeparateFromDeclineAndDelete()
    {
        var book = new SyncBook("Diabolos");
        book.ForceShare();
        var item = new SyncAnnouncement
        {
            Id = "park",
            World = "Diabolos",
            Channel = 11,
            Text = "Maps at 8:00pm ward 13",
            FromSync = true,
        };
        book.Events.Add(item);
        Assert.True(item.IsSyncPending);
        Assert.True(book.HideRemote(item.Id));
        Assert.True(item.Hidden);
        Assert.False(item.Declined);
        Assert.False(item.IsSyncPending);
        Assert.True(book.HideRemote(item.Id, false));
        Assert.False(item.Hidden);
        Assert.True(item.IsSyncPending);
        Assert.True(book.DeclineRemote(item.Id));
        Assert.True(item.Declined);
        Assert.False(item.Hidden);
        item.Declined = false;
        Assert.True(book.Dismiss(item.Id));
        Assert.DoesNotContain(book.Events, row => row.Id == "park");
        Assert.NotEmpty(book.DismissedKeys);
    }

    [Fact]
    public void DeletePastRemovesEndedEventsAndKeepsLaterAndRepeatingOnes()
    {
        var session = new CalendarSession(new DateOnly(2026, 9, 27));
        var heard = new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);
        Assert.True(session.TryAddShout("Open 8:00-9:00 ward 1", ShoutHarvest.ShoutChannel, heard, "Mina"));
        Assert.True(session.TryAddShout("Open 8pm-11pm ward 2", ShoutHarvest.ShoutChannel, heard, "Ada"));
        Assert.True(session.TryAddShout("tomorrow at 8pm ward 3", ShoutHarvest.ShoutChannel, heard, "Mina"));
        Assert.True(session.TryAddShout("every Tuesday at 6pm on W4 P4", ShoutHarvest.ShoutChannel, heard, "Ada"));
        var now = new DateTime(2026, 9, 27, 21, 0, 0);
        Assert.Equal(1, session.Log.ClearPast(now));
        Assert.DoesNotContain(session.Log.Entries, entry => entry.EventText.Contains("8:00-9:00", StringComparison.Ordinal));
        Assert.Contains(session.Log.Entries, entry => entry.EventText.Contains("8pm-11pm", StringComparison.Ordinal));
        Assert.Contains(session.Log.Entries, entry => entry.EventText.Contains("tomorrow", StringComparison.Ordinal));
        Assert.Contains(session.Log.Entries, entry => entry.Repeat is not null);

        var prompt = new ClearPrompt();
        prompt.Ask(ClearTarget.LocalPast);
        Assert.Equal("Delete local events whose end time has passed on this computer?", prompt.Question([], ""));
        prompt.Ask(ClearTarget.SyncPast);
        Assert.Equal("Delete shared events whose end time has passed on this computer?", prompt.Question(["Diabolos"], "Diabolos"));

        var book = new SyncBook("Diabolos");
        book.Events.Add(new SyncAnnouncement
        {
            Id = "ended",
            World = "Diabolos",
            Date = "2026-09-27",
            Time = "08:00",
            Text = "Open 8:00-9:00 ward 1",
            FromSync = true,
            Accepted = true,
        });
        book.Events.Add(new SyncAnnouncement
        {
            Id = "later",
            World = "Diabolos",
            Date = "2026-09-27",
            Time = "20:00",
            Text = "Open 8pm-11pm ward 2",
            FromSync = true,
            Accepted = true,
        });
        Assert.Equal(1, book.DismissPast(now));
        Assert.Contains(book.Events, row => row.Id == "later");
        Assert.DoesNotContain(book.Events, row => row.Id == "ended");
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

    [Fact]
    public void PausingChatRestoresOnlyTheChannelsThatWereOn()
    {
        var session = new CalendarSession(new DateOnly(2026, 9, 27));
        session.UseChannels(ChatChannels.DefaultIds);
        Assert.Contains(ShoutHarvest.ShoutChannel, session.Channels);
        Assert.DoesNotContain(14, session.Channels);
        session.SetChannel(10, false);
        var before = session.Channels.Order().ToArray();

        session.SetListening(false);
        Assert.False(session.Listening);
        Assert.Empty(session.Channels);
        Assert.False(session.TryAddShout("8:00pm ward 4", ShoutHarvest.ShoutChannel, new DateTimeOffset(2026, 9, 27, 18, 0, 0, TimeSpan.Zero), "Mina"));
        Assert.Empty(session.Log.Entries);

        session.SetListening(true);
        Assert.Equal(before, session.Channels.Order().ToArray());
        Assert.DoesNotContain(10, session.Channels);
        Assert.DoesNotContain(14, session.Channels);
        Assert.Contains(ShoutHarvest.ShoutChannel, session.Channels);
        session.SetChannel(ShoutHarvest.ShoutChannel, false);
        Assert.False(session.ShowsChannel(ShoutHarvest.ShoutChannel));
        Assert.True(session.ShowsChannel(0));
        Assert.True(session.ShowsChannel(SharePolicy.YellChannel));
        session.SetInkFlip("Pending", true);
        Assert.True(session.InkFlipped("Pending"));
        session.SetInkFlip("Pending", false);
        Assert.False(session.InkFlipped("Pending"));
    }

    [Fact]
    public void AnAlarmNamesTheEventAndHowSoonItStarts()
    {
        var entry = new CalendarEntry(
            new DateOnly(2026, 9, 27),
            new TimeOnly(18, 0),
            null,
            14,
            "Goblin",
            "The Lavender Beds, ward 14, plot 8",
            "Open at 3pm PT, Goblin > Lavender Beds > Ward 14, Plot 8",
            "Tirita Rita",
            true,
            "toast",
            default);
        var line = AlarmNotice.Line(entry, 15, "Diabolos");
        Assert.StartsWith("Shout Calendar:", line, StringComparison.Ordinal);
        Assert.Contains("Server hop to Goblin first", line, StringComparison.Ordinal);
        Assert.Contains("You are on Diabolos (Crystal)", line, StringComparison.Ordinal);
        Assert.Contains("Visit Another World Server", line, StringComparison.Ordinal);
        Assert.Contains("Teleport: New Gridania aetheryte", line, StringComparison.Ordinal);
        Assert.Contains("The Lavender Beds ward 14", line, StringComparison.Ordinal);
        Assert.Contains("starts in 15 minutes", line, StringComparison.Ordinal);
        Assert.True(line.IndexOf("starts in 15 minutes", StringComparison.Ordinal) < line.IndexOf("Server hop", StringComparison.Ordinal));
        Assert.Contains("18:00", line, StringComparison.Ordinal);
        Assert.Contains("2026-09-27", line, StringComparison.Ordinal);
        Assert.DoesNotContain("3pm PT", line, StringComparison.Ordinal);
        var home = AlarmNotice.Line(entry, 15, "Goblin");
        Assert.DoesNotContain("Server hop", home, StringComparison.Ordinal);
        Assert.Contains("Teleport: New Gridania aetheryte", home, StringComparison.Ordinal);
        Assert.Contains("is starting", AlarmNotice.Line(entry, 0), StringComparison.Ordinal);
    }

    [Fact]
    public void AShoutNoteBreaksIntoReadableLines()
    {
        var lines = NoteLayout.Lines("OPEN TONIGHT 8-12 / Crystal Zalera Goblet W7 P5 / MIL + Giveaways. Art sketches.");
        Assert.Equal(
            ["OPEN TONIGHT 8-12", "Crystal Zalera Goblet W7 P5", "MIL + Giveaways. Art sketches."],
            lines);
    }

    [Fact]
    public void APassedAcceptedColorKeepsAGreyerTone()
    {
        var green = new Vector4(0.12f, 0.48f, 0.24f, 0.95f);
        var grey = PastTone.Grey(green);
        Assert.True(grey.Y < green.Y);
        Assert.InRange(Math.Abs(grey.X - grey.Y) + Math.Abs(grey.Y - grey.Z), 0f, Math.Abs(green.X - green.Y) + Math.Abs(green.Y - green.Z));
        var evening = new DateTime(2026, 9, 27, 21, 0, 0);
        Assert.True(PastTone.Ended(new DateOnly(2026, 9, 27), new TimeOnly(18, 0), null, evening));
        Assert.False(PastTone.Ended(new DateOnly(2026, 9, 27), new TimeOnly(22, 0), null, evening));
        Assert.False(PastTone.Ended(new DateOnly(2026, 9, 27), new TimeOnly(20, 0), new TimeOnly(0, 0), evening));
    }

    [Fact]
    public void ATwitchOrDiscordLinkPicksThatBrand()
    {
        var twitch = EventKind.Find("Live at https://twitch.tv/examplecaster tonight");
        Assert.Equal("Twitch", twitch?.Name);
        Assert.InRange(twitch!.Value.Color.Z, 0.9f, 1f);
        Assert.True(twitch.Value.Color.X > twitch.Value.Color.Y);

        var discord = EventKind.Find("join discord.gg/exampleclub for the night");
        Assert.Equal("Discord", discord?.Name);
        Assert.True(discord!.Value.Color.Z > discord.Value.Color.X);
        Assert.True(discord.Value.Color.Z > discord.Value.Color.Y);

        Assert.Equal("Twitch", EventKind.Find("twitch first, then discord.gg/room")?.Name);
        Assert.Equal("YouTube", EventKind.Find("watch https://youtu.be/abc")?.Name);
        Assert.Null(EventKind.Find("bards and prizes, no stream"));
    }
}
