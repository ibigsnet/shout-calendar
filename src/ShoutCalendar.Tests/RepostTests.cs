using System.Numerics;
using ShoutCalendar.Core;

namespace ShoutCalendar.Tests;

public class RepostTests
{
    private static CalendarEntry Solace(string id, string text, TimeOnly time, TimeOnly? end) => new(
        new DateOnly(2026, 9, 28),
        time,
        end,
        3,
        "Mateus",
        "The Goblet ward 3 plot 12",
        text,
        "Host",
        false,
        id,
        DateTimeOffset.UnixEpoch,
        Channel: 11);

    [Fact]
    public void ANowRepostKeepsTheExplicitClock()
    {
        var log = new CalendarLog();
        log.Restore(
        [
            Solace(
                "now",
                "open now-5a at Mateus W3 P12 with bards and cards tonight",
                new TimeOnly(21, 6),
                new TimeOnly(5, 0)),
        ]);
        log.Add(Solace(
            "later",
            "open tonight 11p-5a at Mateus W3 P12 with bards and cards tonight",
            new TimeOnly(23, 0),
            new TimeOnly(5, 0)));

        var kept = Assert.Single(log.Entries);
        Assert.Equal("now", kept.Id);
        Assert.Equal(new TimeOnly(23, 0), kept.Time);
        Assert.Contains("11p-5a", kept.EventText, StringComparison.Ordinal);
        Assert.Equal("The Goblet ward 3 plot 12", kept.Place);
    }

    [Fact]
    public void AShorterRepostKeepsTheVenueName()
    {
        var named = Announce(
            "named",
            "Halicarnassus",
            "2026-09-27",
            "20:00",
            "Cards and music tonight at THE NORTH HOUSE. Doors at 8p-12a est for drinks, songs, and a quiet garden.");
        var shorter = Announce(
            "short",
            "Halicarnassus",
            "2026-09-27",
            "20:00",
            "Cards and music tonight. Doors at 8p-12a est for drinks, songs, and a quiet garden.");
        var rows = new List<SyncAnnouncement>();
        SyncMerge.Apply(rows, named);
        SyncMerge.Apply(rows, shorter);

        var kept = Assert.Single(rows);
        Assert.Equal("named", kept.Id);
        Assert.Contains("NORTH HOUSE", kept.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSameWordingOnAnotherNightStaysSeparate()
    {
        const string text = "Live bard music tonight from the lucky ensemble at the aetheryte. Come dance and sing along to classic hits.";
        var rows = new List<SyncAnnouncement>();
        SyncMerge.Apply(rows, Announce("fri", "Hyperion", "2026-09-27", "22:00", text));
        SyncMerge.Apply(rows, Announce("sat", "Hyperion", "2026-09-28", "22:00", text));

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void ADifferentPlotIsNotTheSameParty()
    {
        var four = Announce("four", "Rafflesia", "2026-09-28", "00:49", "Bard night now at Ward 3 Plot 12 with cards and music.");
        var fortyOne = Announce("forty", "Rafflesia", "2026-09-28", "02:13", "Bard night now at Ward 3 Plot 41 with cards and music.");

        Assert.False(EventIdentity.SameRepost(four, fortyOne));
    }

    [Fact]
    public void TheSameWordingOnAnotherWorldStaysSeparate()
    {
        const string text = "Bard night every sunday from 8pm until midnight.";
        var rows = new List<SyncAnnouncement>();
        SyncMerge.Apply(rows, Announce("zal", "Zalera", "2026-09-27", "20:00", text));
        SyncMerge.Apply(rows, Announce("dia", "Diabolos", "2026-09-27", "20:00", text));

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void ALocalCopyCoversTheSharedRow()
    {
        var local = Solace(
            "local",
            "open tonight 11p-5a at Mateus W3 P12 with bards and cards tonight",
            new TimeOnly(23, 0),
            new TimeOnly(5, 0));
        var shared = Announce(
            "shared",
            "Mateus",
            "2026-09-28",
            "23:00",
            "open tonight 11p-5a at Mateus W3 P12 with bards and cards tonight");

        Assert.True(EventIdentity.SameRepost(local, shared));
    }

    [Fact]
    public void AFinishedNightIsPastAndARepeatIsNot()
    {
        var done = Solace("done", "open tonight 11p-5a at Mateus W3 P12 with bards and cards tonight", new TimeOnly(23, 0), new TimeOnly(5, 0));
        var later = done with { Date = new DateOnly(2026, 10, 3), Id = "later" };
        var weekly = done with { Repeat = new EventRepeat(EventRepeat.Weekly, DayOfWeek.Monday, 0), Id = "weekly" };
        var morningAfter = new DateTime(2026, 9, 29, 12, 0, 0);

        Assert.True(PastEvents.Ended(done, morningAfter));
        Assert.False(PastEvents.Ended(later, morningAfter));
        Assert.False(PastEvents.Ended(weekly, morningAfter));
    }

    [Fact]
    public void PinnedInvitesStayInTimeOrderAboveTheRest()
    {
        var evening = new TimeOnly(22, 0);
        var afternoon = new TimeOnly(18, 0);
        Assert.True(DaySort.Compare(true, 1, evening, "Late", false, 1, afternoon, "Early") < 0);
        Assert.True(DaySort.Compare(true, 1, afternoon, "Early pin", true, 1, evening, "Late pin") < 0);
        Assert.True(DaySort.Compare(false, 1, afternoon, "Early", false, 1, evening, "Late") < 0);
        Assert.True(DaySort.Compare(false, 1, new TimeOnly(0, 30), "Morning", false, 0, new TimeOnly(21, 0), "Span") < 0);
        Assert.Equal("12:00-04:00 ", DaySort.RangeLabel(new TimeOnly(12, 0), new TimeOnly(4, 0)));
        Assert.Equal("00:00-04:00 ", DaySort.SliceLabel(new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 29), new TimeOnly(12, 0), new TimeOnly(4, 0)));
        var arch = NightCurve.Between(new Vector2(40f, 80f), new Vector2(120f, 90f));
        Assert.Equal(new Vector2(80f, 85f), arch.C1);
        Assert.Equal(arch.C1, arch.C2);
        var parents = DayNest.Parents(
        [
            new DayInterval(new TimeOnly(0, 30), new TimeOnly(5, 0), true, false),
            new DayInterval(new TimeOnly(4, 0), new TimeOnly(4, 0), false, false),
            new DayInterval(new TimeOnly(12, 0), new TimeOnly(4, 0), true, true),
        ]);
        Assert.Equal(-1, parents[0]);
        Assert.Equal(0, parents[1]);
        Assert.Equal(-1, parents[2]);
        var orange = new Vector4(0.85f, 0.45f, 0.12f, 1f);
        var lifted = LayerShade.Lift(orange, 1);
        Assert.True(lifted.X > orange.X);
        Assert.True(LayerShade.Shadow(orange).X < orange.X);
        Assert.Equal(orange.X, LayerShade.Lift(orange, 2, 0f).X);
        Assert.Equal(orange.X, LayerShade.Shadow(orange, 0f).X);
        Assert.Equal("04:00 ", DaySort.RangeLabel(new TimeOnly(4, 0), new TimeOnly(4, 0)));
    }

    [Fact]
    public void FoldingRepostsKeepsAPin()
    {
        const string text = "open tonight 11p-5a at Mateus W3 P12 with bards and cards tonight";
        var log = new CalendarLog();
        log.Restore(
        [
            Solace("pinned", text, new TimeOnly(23, 0), new TimeOnly(5, 0)) with { Pinned = true },
            Solace("copy", text, new TimeOnly(23, 0), new TimeOnly(5, 0)),
        ]);

        Assert.Equal(1, log.FoldReposts());
        Assert.True(Assert.Single(log.Entries).Pinned);
    }

    [Fact]
    public void TwoPartiesOnTheSameNightStaySeparate()
    {
        var ravish = Announce("rav", "Brynhildr", "2026-09-28", "21:00", "North hall for three hours of music. Monday bass and rhythms.");
        var asylum = Announce("asy", "Brynhildr", "2026-09-28", "20:00", "South hall from 8. A live player and cards all night.");

        Assert.False(EventIdentity.SameRepost(ravish, asylum));
    }

    private static SyncAnnouncement Announce(string id, string world, string date, string time, string text) => new()
    {
        Id = id,
        World = world,
        Channel = 11,
        Date = date,
        Time = time,
        Text = text,
        FromSync = true,
        Revision = 1,
    };
}
