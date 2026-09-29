using ShoutCalendar.Core;
using Xunit;

namespace ShoutCalendar.Tests;

public sealed class SyncStatusBufferTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 29, 18, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(-5, "0 seconds")]
    [InlineData(0, "0 seconds")]
    [InlineData(1, "1 second")]
    [InlineData(59, "59 seconds")]
    [InlineData(60, "1 minute")]
    [InlineData(119, "1 minute")]
    [InlineData(120, "2 minutes")]
    [InlineData(3599, "59 minutes")]
    [InlineData(3600, "1 hour")]
    [InlineData(7200, "2 hours")]
    public void LastSuccessAgeFollowsRelayAndUsesWholeUnits(int seconds, string age)
    {
        var progress = new SyncProgress(SyncPhase.Current, At, "Public relay", LastSuccess: At);
        Assert.Equal($"Last success {At.LocalDateTime:HH:mm:ss} · Public relay ({age} ago)",
            progress.LastSuccessSummary(At.AddSeconds(seconds)));
        Assert.Equal($"Last success {At.LocalDateTime:HH:mm:ss} ({age} ago)",
            (progress with { Relay = "" }).LastSuccessSummary(At.AddSeconds(seconds)));
    }

    [Fact]
    public void NoSuccessDoesNotInventAnAge()
    {
        var progress = new SyncProgress(SyncPhase.Starting, At, "Public relay");
        Assert.Equal("No successful check yet · Public relay", progress.LastSuccessSummary(At.AddHours(1)));
    }

    [Fact]
    public void FastPassPublishesLatestResultWithoutShowingTransientPhases()
    {
        var book = new SyncBook("Diabolos");
        var buffer = new SyncStatusBuffer();
        book.PublishStatus("Done", progress: new(SyncPhase.Current, At, LastSuccess: At));
        var initial = buffer.Update(book, 0);
        book.PublishStatus("Uploading", progress: new(SyncPhase.Uploading, At.AddSeconds(12)));
        Assert.Same(initial, buffer.Update(book, 1000));
        book.PublishStatus("Downloading", progress: new(SyncPhase.Downloading, At.AddSeconds(12)));
        Assert.Same(initial, buffer.Update(book, 1040));
        book.PublishStatus("Done", progress: new(SyncPhase.Current, At.AddSeconds(12), LastSuccess: At.AddSeconds(12), Applied: 2));
        Assert.Same(initial, buffer.Update(book, 1099));
        var displayed = buffer.Update(book, 1100)!;
        Assert.Equal("Done", displayed.Status);
        Assert.Equal(SyncPhase.Current, displayed.Progress!.Phase);
        Assert.Equal(2, displayed.Progress.Applied);
        Assert.Equal(At.AddSeconds(12), displayed.Progress.LastSuccess);
    }

    [Fact]
    public void ContinuousChangesCannotKeepFailuresHiddenBeyondTheBufferWindow()
    {
        var book = new SyncBook("Diabolos");
        var buffer = new SyncStatusBuffer();
        var initial = buffer.Update(book, 0);
        for (var i = 0; i <= 5; i++)
        {
            book.PublishStatus("Retry " + i, progress: new(SyncPhase.Retry, At.AddMilliseconds(i)));
            var displayed = buffer.Update(book, 1000 + i * 20);
            if (i < 5) Assert.Same(initial, displayed);
            else Assert.Equal("Retry 5", displayed!.Status);
        }
    }

    [Fact]
    public void DetachAndChangingBooksClearOldStatusImmediately()
    {
        var buffer = new SyncStatusBuffer();
        var book = new SyncBook("Diabolos");
        book.PublishStatus("Done", progress: new(SyncPhase.Current, At, LastSuccess: At));
        buffer.Update(book, 0);
        Assert.Null(buffer.Update(null, 1));
        book.Detach();
        Assert.Null(buffer.Update(book, 2)!.Progress);
        book.PublishStatus("Pending old status");
        buffer.Update(book, 3);
        var next = new SyncBook("Zalera") { SyncStatus = "New connection" };
        Assert.Equal("New connection", buffer.Update(next, 4)!.Status);
        Assert.Null(buffer.Current!.Progress);
    }
}
