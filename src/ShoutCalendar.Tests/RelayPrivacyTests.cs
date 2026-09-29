using ShoutCalendar.Core;
using Xunit;

namespace ShoutCalendar.Tests;

public sealed class RelayPrivacyTests
{
    [Fact]
    public void PublicAddressesUseFriendlyLabelsAndCustomAddressesStayUseful()
    {
        Assert.Equal("Public relay", SyncRelays.Display(SyncRelays.PublicHost, 443));
        Assert.Equal("Public relay", SyncRelays.Display("https://" + SyncRelays.PublicHost.ToUpperInvariant() + ".:443/", 443));
        Assert.Equal("192.0.2.5:8787", SyncRelays.Display("192.0.2.5", 8787));
        Assert.Equal("private.example:9443", SyncRelays.Display("https://user:password@private.example:9443/path?token=secret", 443));
    }

    [Fact]
    public void OlderStatusAndDiagnosticsCannotEchoThePublicAddress()
    {
        var address = SyncRelays.PublicHost + ":443";
        var book = new SyncBook("Diabolos") { DebugPerf = true };
        book.PublishStatus("Connected to " + address, "Checking " + address,
            new SyncProgress(SyncPhase.Current, DateTimeOffset.UtcNow, address, Detail: "Checked " + address));
        Assert.Equal("Public relay", book.Progress!.Relay);
        Assert.DoesNotContain(SyncRelays.PublicHost, book.Progress.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SyncRelays.PublicHost, book.SyncStatus, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SyncRelays.PublicHost, Assert.Single(book.CopyPerf()), StringComparison.OrdinalIgnoreCase);
        var restored = new SyncBook("Diabolos");
        restored.ApplyJson("{\"syncStatus\":\"" + address + "\",\"perfLog\":[\"" + address + "\"]}");
        Assert.Equal("Public relay", restored.SyncStatus);
        Assert.Equal("Public relay", Assert.Single(restored.CopyPerf()));
    }

    [Fact]
    public void PublicBackupIsAutomaticAndNotDuplicatedForThePublicPrimary()
    {
        var book = new SyncBook("Diabolos");
        book.ForceShare();
        Assert.Equal(SyncRelays.PublicLabel, book.BackupRelayChoice);
        Assert.Null(book.DistinctBackup);
        book.RelayChoice = SyncRelays.CustomLabel;
        book.RelayHost = "192.0.2.5";
        book.RelayPort = 8787;
        Assert.Equal(new RelayEndpoint(SyncRelays.PublicHost, 443), book.DistinctBackup);
        book.RelayHost = "https://" + SyncRelays.PublicHost.ToUpperInvariant() + ".:443/";
        Assert.Null(book.DistinctBackup);
    }

    [Theory]
    [InlineData("custom.example", 443, "https://CUSTOM.example:443/", 443, true)]
    [InlineData("custom.example", 8787, "CUSTOM.example.", 8787, true)]
    [InlineData("custom.example", 8787, "custom.example", 8788, false)]
    [InlineData("custom.example", 443, "http://custom.example", 443, false)]
    [InlineData("https://custom.example/a", 443, "https://custom.example/b", 443, false)]
    [InlineData("custom.example:9443", 443, "https://custom.example:9443/", 443, true)]
    [InlineData("custom.example:9443", 443, "custom.example", 443, false)]
    [InlineData("https://[2001:db8::5]:9443", 443, "https://[2001:db8::5]:9443/", 443, true)]
    public void BackupIdentityMatchesTransportEndpoints(string host, int port, string backup, int backupPort, bool same) =>
        Assert.Equal(same, SyncRelays.SameEndpoint(host, port, backup, backupPort));

    [Fact]
    public void OldBackupChoicesMigrateAndExplicitOffSurvivesReload()
    {
        var book = new SyncBook("Diabolos");
        book.ApplyJson("{}");
        Assert.Equal(SyncRelays.PublicLabel, book.BackupRelayChoice);
        book.ApplyJson("{\"backupRelayHost\":\"custom.example\",\"backupRelayPort\":8787}");
        Assert.Equal(SyncRelays.CustomLabel, book.BackupRelayChoice);
        Assert.Equal(new RelayEndpoint("custom.example", 8787), book.Backup);
        book.BackupRelayChoice = SyncRelays.OffLabel;
        var restored = new SyncBook("Diabolos");
        restored.ApplyJson(book.ToJson());
        Assert.Null(restored.DistinctBackup);
        Assert.Equal(SyncRelays.OffLabel, restored.BackupRelayChoice);
        Assert.True(SyncResume.Choose(new SyncBook("Diabolos").ToJson(), restored.ToJson()).RestoreStore);
    }
}
