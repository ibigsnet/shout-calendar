using ShoutCalendar.Core;
using System.Text.Json;
namespace ShoutCalendar.Tests;
public class DeletionTests
{
    [Fact]
    public void PluginConfigurationRetainsRecurringDeletionChoices()
    {
        var entry = ShoutHarvest.TryHarvest("Music every Sunday 8PM-12AM EST Zalera Goblet W7 P5", 11,
            DateTimeOffset.Parse("2026-09-27T12:00:00Z"))!;
        entry = EventDeletion.Apply(entry, new DateOnly(2026, 10, 4), DeleteScope.Occurrence);
        entry = EventDeletion.Apply(entry, new DateOnly(2026, 10, 11), DeleteScope.Following);
        var encoded = JsonSerializer.Serialize(StoredEvent.From(entry));
        Assert.True(StoredEvent.TryToEntry(JsonSerializer.Deserialize<StoredEvent>(encoded)!, out var restored));
        Assert.Equal(entry.ExcludedDates, restored.ExcludedDates);
        Assert.Equal(entry.RepeatUntil, restored.RepeatUntil);
        var log = new CalendarLog(); log.Restore([restored]);
        log.Delete(restored.Id, restored.Date!.Value, DeleteScope.Series);
        Assert.True(StoredEvent.TryToEntry(StoredEvent.From(Assert.Single(log.Entries)), out restored));
        Assert.True(restored.SeriesDeleted);
    }
    [Fact]
    public void HarvestRetainsTheObservationTimeUsedToOrderCorrections()
    {
        var observed = DateTimeOffset.Parse("2026-09-27T18:00:00Z");
        var entry = ShoutHarvest.TryHarvest("Music Sunday 8PM EST Zalera Goblet W7 P5", 11, observed)!;
        Assert.Equal(observed, entry.DetectedAt);
    }

}
