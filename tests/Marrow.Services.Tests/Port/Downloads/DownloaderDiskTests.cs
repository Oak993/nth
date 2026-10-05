using Marrow.Services.Port.Downloads;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing.Downloads;

namespace Marrow.Services.Tests.Port.Downloads;

// SK-3 (DL-3, sections 3.2 and 3.9): the disk-space check runs before connecting: the download plus the item's unpacked size plus 10% must fit on the
// data root's volume, or the item fails at once with "Needs 1.2 GB free on C:." and a retry re-checks first.
[Trait("Spec", "DL-3")]
public sealed class DownloaderDiskTests
{
    private static readonly FakeArtifact Artifact = FakeArtifact.Pattern(10_000, seed: 41);

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task Too_little_room_fails_before_any_connection_with_the_amount_needed()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact);
        var request = rig.Request("/a.bin", Artifact) with { Size = 600_000_000, UnpackedBytes = 400_000_000 };
        var downloader = new Downloader(@"C:\Marrow Code\addons\downloads", new DownloadPolicy(["127.0.0.1"]),
            new DownloadOptions { FreeSpace = _ => 900_000_000 }, rig.Counting);

        var outcome = await downloader.Start(request).Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal(DownloadStatus.Failed, outcome.Status);
        Assert.Equal(DownloadFailureKind.DiskFull, outcome.Failure!.Kind);
        Assert.Equal("Needs 1.1 GB free on C:.", outcome.Failure.Message); // (600 MB + 400 MB) + 10% = 1.1 GB
        Assert.Equal(0, rig.Counting.Sent);
        Assert.Equal(0, rig.Server.ConnectionCount);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task The_check_passes_when_the_sum_with_its_margin_fits_exactly()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact);
        var request = rig.Request("/a.bin", Artifact) with { UnpackedBytes = 1_000 };
        const long needed = 12_100; // (10,000 + 1,000) + 10%
        var tight = rig.Create(new DownloadOptions { FreeSpace = _ => needed });
        var outcome = await tight.Start(request).Completion.WaitAsync(FakeDownloadServer.Guard);
        Assert.True(outcome.Succeeded);

        var short1 = rig.Create(new DownloadOptions { FreeSpace = _ => needed - 1 });
        var failed = await short1.Start(request with { DestinationPath = rig.Dest("b.bin") }).Completion.WaitAsync(FakeDownloadServer.Guard);
        Assert.Equal(DownloadFailureKind.DiskFull, failed.Failure!.Kind);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task Bytes_a_partial_already_holds_are_not_needed_again()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact);
        rig.SeedPart(Artifact, Artifact.Bytes[..8_000]);
        var request = rig.Request("/a.bin", Artifact);

        // 2,000 bytes remain; 2,200 with the margin.
        var outcome = await rig.Create(new DownloadOptions { FreeSpace = _ => 2_200 }).Start(request).Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.True(outcome.Succeeded);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task A_set_is_checked_as_a_whole_before_the_first_connection()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact);
        var one = rig.Request("/a.bin", Artifact);
        var requests = new[] { one, one with { DestinationPath = rig.Dest("b.bin") }, one with { DestinationPath = rig.Dest("c.bin") } };

        var outcome = await rig.Create(new DownloadOptions { FreeSpace = _ => 30_000 }).StartSet(requests).Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal(DownloadFailureKind.DiskFull, outcome.Failure!.Kind); // 3 x 10,000 + 10% = 33,000 > 30,000
        Assert.Equal(0, rig.Counting.Sent);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task A_retry_checks_the_room_again_and_goes_ahead_once_it_is_there()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact);
        var free = 100L;
        var downloader = rig.Create(new DownloadOptions { FreeSpace = _ => Interlocked.Read(ref free) });
        var request = rig.Request("/a.bin", Artifact);

        var first = await downloader.Start(request).Completion.WaitAsync(FakeDownloadServer.Guard);
        Interlocked.Exchange(ref free, 1_000_000);
        var second = await downloader.Start(request).Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal(DownloadFailureKind.DiskFull, first.Failure!.Kind);
        Assert.True(second.Succeeded);
    }

    [Theory]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    [InlineData(1_000_000_000L, "1.0 GB")]
    [InlineData(1_000_000_001L, "1.1 GB")] // rounded UP: the figure must be enough
    [InlineData(1_100_000_000L, "1.1 GB")]
    [InlineData(1_200_000_000L, "1.2 GB")]
    [InlineData(12_345_000_000L, "12.4 GB")]
    [InlineData(999_999_999L, "1.0 GB")] // 1000 MB is written as the GB it is
    [InlineData(999_000_000L, "999 MB")]
    [InlineData(45_000_001L, "46 MB")]
    [InlineData(45_000_000L, "45 MB")]
    [InlineData(1L, "1 MB")]
    public void Sizes_are_written_in_decimal_units_and_rounded_up(long bytes, string expected) => Assert.Equal(expected, DownloadMessages.Size(bytes));

    [Theory]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    [InlineData(@"C:\Marrow Code\addons\downloads", "C:")]
    [InlineData(@"d:/x/y", "D:")]
    [InlineData(@"\\server\share\x", @"\\server\share")]
    [InlineData("/var/lib/x", "/")]
    public void The_drive_is_named_as_the_message_writes_it(string path, string expected) => Assert.Equal(expected, DownloadMessages.DriveLabel(path));

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public void The_disk_full_message_is_section_3_9s() => Assert.Equal("Needs 1.2 GB free on C:.", DownloadMessages.DiskFull(1_200_000_000, "C:"));
}
