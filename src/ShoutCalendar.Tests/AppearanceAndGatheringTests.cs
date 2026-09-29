using System.Numerics;
using ShoutCalendar.Core;
using Xunit;

namespace ShoutCalendar.Tests;

public sealed class AppearanceAndGatheringTests
{
    [Theory]
    [InlineData(768, true)]
    [InlineData(720, true)]
    [InlineData(769, false)]
    [InlineData(1080, false)]
    [InlineData(0, false)]
    public void LowHeightHelperUsesDisplayThresholdAndHonorsOverride(float height, bool expected)
    {
        var appearance = new CalendarAppearance();
        Assert.Equal(expected, appearance.Compact(height));
        appearance.LowHeight = LowHeightMode.Off;
        Assert.False(appearance.Compact(height));
        appearance.LowHeight = LowHeightMode.On;
        Assert.True(appearance.Compact(height));
    }

    [Fact]
    public void PendingScopeDoesNotChangeDownloadsOrCalendarSelection()
    {
        var worlds = new WorldCalendar("Diabolos");
        worlds.SetChecked("Zalera", true);
        Assert.False(worlds.PendingVisible("Zalera", PendingScope.CurrentWorld, "Diabolos"));
        Assert.False(worlds.PendingVisible("Zalera", PendingScope.OpenCalendars, "Diabolos"));
        Assert.True(worlds.PendingVisible("Zalera", PendingScope.SyncedWorlds, "Diabolos"));
        Assert.False(worlds.PendingVisible("Balmung", PendingScope.SyncedWorlds, "Diabolos"));
        Assert.Equal(["Diabolos"], worlds.Viewing());
        worlds.SetViewing("Zalera", true);
        Assert.True(worlds.PendingVisible("Zalera", PendingScope.OpenCalendars, "Diabolos"));
        Assert.Equal(2, worlds.Fetched().Count);
    }

    [Fact]
    public void StatusDoesNotClaimFreshSuccessAfterItGoesStaleAndPreservesLastSuccessOnFailure()
    {
        var now = DateTimeOffset.UtcNow;
        var book = new SyncBook("Diabolos");
        var progress = new SyncProgress(SyncPhase.Current, now, LastSuccess: now, Catalog: 12);
        book.PublishStatus("done", progress: progress);
        Assert.Contains("Up to date", progress.Summary(now));
        Assert.Contains("fresh sync", progress.Summary(now.AddMinutes(1)));
        book.PublishStatus("failed", progress: new SyncProgress(SyncPhase.Retry, now.AddSeconds(10), NextAttempt: now.AddMinutes(1)));
        Assert.Equal(now, book.Progress!.LastSuccess);
        Assert.Contains("retry in 50s", book.Progress.Summary(now.AddSeconds(10)));
    }

    [Fact]
    public void BackupChoiceSurvivesSaveWithoutEnablingPublicMirroring()
    {
        var book = new SyncBook("Diabolos") { BackupRelayHost = "backup.example", BackupRelayPort = 443 };
        var restored = new SyncBook("Diabolos");
        restored.ApplyJson(book.ToJson());
        Assert.Equal("backup.example", restored.BackupRelayHost);
        Assert.Equal(443, restored.BackupRelayPort);
        Assert.False(restored.MirrorRelay);
    }

    [Fact]
    public void MigratesOldAppearanceWithoutChangingColorsAndPresetsPreserveContrastChoices()
    {
        var appearance = CalendarAppearance.Migrate(1.5f, ["Pending", "Shared bar"]);
        Assert.Equal(0.5f, appearance.Shade);
        Assert.Equal(TextContrast.Light, appearance.Text["Pending"]);
        Assert.Equal(TextContrast.Dark, appearance.Text["Shared bar"]);
        appearance.Preset("Minimal");
        Assert.Equal(OvernightStyle.Split, appearance.Overnight);
        Assert.Equal(TextContrast.Dark, appearance.Text["Shared bar"]);
        Assert.True(appearance.Ink("Unknown", Vector4.One).X < 0.2f);
        Assert.True(appearance.Ink("Unknown", new Vector4(0, 0, 0, 1)).X > 0.9f);
    }

    [Theory]
    [InlineData("Hello. There is a Shady Hunt Train assembling at Urqopacha ( 28.2  , 13.0 ) if you'd like to join.")]
    [InlineData("Hunt train gathering at Urqopacha (28.2, 13.0), come join us!")]
    [InlineData("FATE train forming at Urqopacha (28.2, 13.0)!")]
    [InlineData("S rank, meet at Urqopacha (28.2, 13.0)!")]
    public void GatheringWithMapDestinationHasObservedClock(string text)
    {
        var now = new DateTimeOffset(2026, 9, 29, 18, 25, 0, TimeSpan.Zero);
        var entry = ShoutHarvest.TryHarvest(text, 11, now, aggressive: true, zone: TimeZoneInfo.Utc);
        Assert.NotNull(entry);
        Assert.Equal(new DateOnly(2026, 9, 29), entry.Date);
        Assert.Equal(new TimeOnly(18, 25), entry.Time);
        Assert.Null(entry.End);
        Assert.Contains("Urqopacha", entry.Place);
        Assert.True(LiveInvite.IsRecent(entry.EventText, entry.DetectedAt, now.AddMinutes(1)));
        Assert.False(LiveInvite.IsRecent(entry.EventText, entry.DetectedAt, now.AddMinutes(31)));
    }

    [Theory]
    [InlineData("A hunt train was assembling at Urqopacha (28.2, 13.0).")]
    [InlineData("Hunt train assembling tomorrow at Urqopacha (28.2, 13.0).")]
    [InlineData("Cancelled: hunt train assembling at Urqopacha (28.2, 13.0).")]
    [InlineData("I'm gathering at Urqopacha (28.2, 13.0).")]
    [InlineData("Hunt train assembling, come join us!")]
    public void DoesNotInferNowFromHistoricalCancelledOrUnlocatedMessages(string text) => Assert.False(LiveInvite.IsGathering(text));

    [Fact]
    public void AnAssemblingHuntUsesTheHeardWorldAndAnAbsoluteSharedClock()
    {
        const string text = "Hunt train assembling at Urqopacha (28.2, 13.0)!";
        Assert.Equal("Zalera", ShareWorld.Choose("Zalera", "Diabolos", "", text));
        Assert.Equal("Balmung", ShareWorld.Choose("Zalera", "Diabolos", "Balmung", text));
        var at = new DateTimeOffset(2026, 9, 29, 18, 25, 0, TimeSpan.Zero);
        var entry = ShoutHarvest.TryHarvest(text, 11, at, aggressive: true, zone: TimeZoneInfo.Utc)!;
        var shared = new SyncAnnouncement();
        SyncClock.Stamp(shared, entry, TimeZoneInfo.Utc);
        Assert.Equal(at, shared.StartUtc);
        Assert.Null(shared.EndUtc);
    }

    private static SyncAnnouncement Mass(string id, string world, string date, bool explicitWorld) => new()
    {
        Id = id, World = world, Date = date, Time = "20:00", Channel = 11, ShareFormat = 0,
        Text = "Don't miss Midnight Mass at " + (explicitWorld ? $"Catholic Guilt! {world}'s premium night club! " : "")
            + "Come unleash your desires and leave feeling cleansed. Every Sunday @ 8PM - 12AM EST! Allow our clergy to dote on you and indulge in your wildest fantasies. Courts, gamba, booze and drugs! Vices galore! We have both forms of wifi and DJs on deck!",
    };

    [Fact]
    public void SavedCrossWorldWeeklyCopiesFoldInEitherOrderAndKeepSingleSundayDeletion()
    {
        foreach (var reverse in new[] { false, true })
        {
            var book = new SyncBook("Diabolos");
            var inferred = Mass("brief", "Diabolos", "2026-09-27", false);
            inferred.ExcludedDates.Add(new DateOnly(2026, 9, 27));
            var full = Mass("full", "Zalera", "2026-10-04", true);
            book.Events.AddRange(reverse ? [full, inferred] : [inferred, full]);
            var restored = new SyncBook("Diabolos");
            Assert.True(restored.ApplyJson(book.ToJson()));
            var kept = Assert.Single(restored.Events);
            Assert.Equal("Zalera", kept.World);
            Assert.Contains("Catholic Guilt", kept.Text);
            Assert.Contains(new DateOnly(2026, 9, 27), kept.ExcludedDates);
            Assert.Equal("2026-09-27", kept.Date);
        }
    }

    [Fact]
    public void InferredCopyDoesNotPickBetweenConflictingExplicitWorlds()
    {
        var book = new SyncBook("Diabolos");
        book.Events.AddRange([Mass("brief", "Diabolos", "2026-09-27", false), Mass("one", "Zalera", "2026-09-27", true), Mass("two", "Balmung", "2026-09-27", true)]);
        var restored = new SyncBook("Diabolos");
        restored.ApplyJson(book.ToJson());
        Assert.Equal(3, restored.Events.Count);
    }
}
