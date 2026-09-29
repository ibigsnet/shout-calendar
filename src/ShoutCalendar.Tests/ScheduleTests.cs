using ShoutCalendar.Core;

namespace ShoutCalendar.Tests;

public class ScheduleTests
{
    private static readonly TimeZoneInfo Central =
        TimeZoneInfo.CreateCustomTimeZone("test-ct", TimeSpan.FromHours(-5), "CT", "CT");

    [Fact]
    public void NorthAmericaCactpotLandsOnSaturdayEvening()
    {
        var marks = GameSchedule.InMonth(2026, 9, Central, "na", new HashSet<string> { GameSchedule.Cactpot });
        var drawing = marks.Single(mark => mark.LocalStart == new DateTime(2026, 9, 26, 21, 0, 0));

        Assert.Equal(ResetTone.Cactus, drawing.Tone);
        Assert.Equal(drawing.StartDate, drawing.EndDate);
    }

    [Fact]
    public void EuropeCactpotUsesTheEuropeClock()
    {
        var marks = GameSchedule.InMonth(2026, 9, Central, "eu", new HashSet<string> { GameSchedule.Cactpot });

        Assert.Contains(marks, mark => mark.LocalStart == new DateTime(2026, 9, 26, 14, 0, 0));
    }

    [Fact]
    public void WeeklyResetIsTuesdayMorningLocal()
    {
        var marks = GameSchedule.InMonth(2026, 9, Central, "na", new HashSet<string> { GameSchedule.Weekly });
        var reset = marks.Single(mark => mark.LocalStart.Day == 29);

        Assert.Equal(new DateTime(2026, 9, 29, 3, 0, 0), reset.LocalStart);
        Assert.Equal(ResetTone.Crystal, reset.Tone);
    }

    [Fact]
    public void NocturneSpansTheLocalEventWindow()
    {
        var marks = GameSchedule.InMonth(2026, 9, Central, "na", new HashSet<string> { GameSchedule.Nocturne });
        var nocturne = Assert.Single(marks);

        Assert.Equal(new DateTime(2026, 9, 24, 3, 0, 0), nocturne.LocalStart);
        Assert.Equal(new DateOnly(2026, 10, 13), nocturne.EndDate);
        Assert.Equal(ResetTone.Event, nocturne.Tone);

        var month = CalendarMonth.Create(2026, 9, Array.Empty<CalendarEntry>());
        var segments = GameSchedule.Segments(month, nocturne.StartDate, nocturne.EndDate);
        Assert.True(segments.Count >= 2);
        Assert.Equal(6, segments[0].LastColumn);
        Assert.Equal(0, segments[^1].FirstColumn);
        var week = new DateOnly(2026, 9, 27).AddDays(-(int)new DateOnly(2026, 9, 27).DayOfWeek);
        var slice = GameSchedule.WeekSegment(week, nocturne.StartDate, nocturne.EndDate);
        Assert.Equal(0, slice!.Value.FirstColumn);
        Assert.Equal(6, slice.Value.LastColumn);
    }

    [Fact]
    public void WondrousTailsUsesTheTuesdayReset()
    {
        var marks = GameSchedule.InMonth(2026, 9, Central, "na", new HashSet<string> { GameSchedule.Tails });
        var tails = marks.Single(mark => mark.LocalStart.Day == 29);

        Assert.Equal(new DateTime(2026, 9, 29, 3, 0, 0), tails.LocalStart);
        Assert.Contains("Khloe", tails.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void NocturnePinsIncludeTheQuestGiver()
    {
        var pins = GameSchedule.Guide("Nocturne");
        var giver = pins.Single(pin => pin.Label == "Kipih Jakkya");

        Assert.Equal("Ul'dah - Steps of Nald", giver.PlaceName);
        Assert.Equal(8.5f, giver.X);
        Assert.Equal(9.7f, giver.Y);
        Assert.Equal("The Man in Black", giver.Quest);
    }

    [Fact]
    public void LaneSlotsReserveMaxLaneNotSpanCount()
    {
        var items = new (string Key, DateOnly Start, DateOnly End)[]
        {
            ("a", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3)),
            ("b", new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 2)),
            ("c", new DateOnly(2026, 9, 4), new DateOnly(2026, 9, 5)),
        };
        var lanes = GameSchedule.Lanes(items);
        Assert.Equal(0, lanes["a"]);
        Assert.Equal(1, lanes["b"]);
        Assert.Equal(0, lanes["c"]);

        var slots = GameSchedule.LaneSlotsByDay(lanes, items);
        Assert.Equal(1, slots[new DateOnly(2026, 9, 1)]);
        Assert.Equal(2, slots[new DateOnly(2026, 9, 2)]);
        Assert.Equal(1, slots[new DateOnly(2026, 9, 3)]);
        Assert.Equal(1, slots[new DateOnly(2026, 9, 4)]);
        Assert.False(slots.ContainsKey(new DateOnly(2026, 9, 6)));
    }

    [Fact]
    public void ParenthesesAndShoutClocksBothCountAsMapSpots()
    {
        var spots = MapMentions.Read("meet (12.4, 8.1) or x 3.0, y 4.5");

        Assert.Equal(2, spots.Count);
        Assert.Equal(12.4f, spots[0].X);
        Assert.Equal(4.5f, spots[1].Y);
    }

    [Fact]
    public void ASavedResetListIsKept()
    {
        var saved = new[] { GameSchedule.Cactpot, GameSchedule.Weekly, GameSchedule.Nocturne };
        var merged = GameSchedule.MergeSaved(saved).ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(GameSchedule.Tails, merged);
        Assert.Contains(GameSchedule.Nocturne, merged);
    }

    [Fact]
    public void ATurnedOffResetIsAbsent()
    {
        var marks = GameSchedule.InMonth(2026, 9, Central, "na", new HashSet<string> { GameSchedule.Weekly });

        Assert.DoesNotContain(marks, mark => mark.Id == GameSchedule.Daily);
        Assert.DoesNotContain(marks, mark => mark.Id == GameSchedule.Cactpot);
    }
}