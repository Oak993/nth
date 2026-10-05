using Marrow.Services.Port.Downloads;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing.Downloads;

namespace Marrow.Services.Tests.Port.Downloads;

// SK-3 (DL-3, section 3.3 and 3.9): every artifact's length must equal the pinned size and its SHA-256 the pinned hash; a mismatch deletes the
// partial file and fails with "The file didn't match its checksum."; the destination is never touched by a failed download.
[Trait("Spec", "DL-3")]
public sealed class DownloaderVerifyTests
{
    private static readonly FakeArtifact Artifact = FakeArtifact.Pattern(60_000, seed: 7);

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task One_wrong_byte_fails_with_the_checksum_message_and_deletes_the_partial()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact, new FakeBehavior { WrongByteAt = 12_345 });

        var outcome = await rig.Run(rig.Request("/a.bin", Artifact));

        Assert.Equal(DownloadStatus.Failed, outcome.Status);
        Assert.Equal(DownloadFailureKind.Mismatch, outcome.Failure!.Kind);
        Assert.Equal("The file didn't match its checksum.", outcome.Failure.Message);
        Assert.Empty(outcome.Files);
        Assert.False(File.Exists(rig.PartPath(Artifact)));
        Assert.False(File.Exists(rig.Dest("a.bin")));
        Assert.Single(await rig.Server.WaitForRequests(1)); // a fresh download is not retried behind the user's back
    }

    [Theory]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    [InlineData(7)]
    [InlineData(-7)]
    public async Task A_body_of_the_wrong_length_fails_the_same_way_and_deletes_the_partial(int delta)
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact, new FakeBehavior { WrongLengthDelta = delta });

        var outcome = await rig.Run(rig.Request("/a.bin", Artifact));

        Assert.Equal(DownloadFailureKind.Mismatch, outcome.Failure!.Kind);
        Assert.False(File.Exists(rig.PartPath(Artifact)));
        Assert.False(File.Exists(rig.Dest("a.bin")));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task Right_bytes_against_the_wrong_pin_fail_the_same_way()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact);
        var request = rig.Request("/a.bin", Artifact) with { Sha256 = new string('0', 64) };

        var outcome = await rig.Run(request);

        Assert.Equal(DownloadFailureKind.Mismatch, outcome.Failure!.Kind);
        Assert.False(File.Exists(request.DestinationPath));
        Assert.Empty(Directory.GetFiles(rig.PartFolder));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task A_failed_download_leaves_an_older_file_at_the_destination_alone()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact, new FakeBehavior { WrongByteAt = 10 });
        var request = rig.Request("/a.bin", Artifact);
        Directory.CreateDirectory(Path.GetDirectoryName(request.DestinationPath)!);
        await File.WriteAllTextAsync(request.DestinationPath, "installed before");

        await rig.Run(request);

        Assert.Equal("installed before", await File.ReadAllTextAsync(request.DestinationPath));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task A_mismatch_after_a_failed_fetch_needs_nothing_from_the_next_run_which_starts_clean()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact, new FakeBehavior { WrongByteAt = 10, FaultyRequests = 1 });

        var first = await rig.Run(rig.Request("/a.bin", Artifact));
        var second = await rig.Run(rig.Request("/a.bin", Artifact));

        Assert.Equal(DownloadFailureKind.Mismatch, first.Failure!.Kind);
        Assert.True(second.Succeeded);
        Assert.Equal([null, null], (await rig.Server.WaitForRequests(2)).Select(r => r.RangeStart)); // "deletes the .part, downloads again" (3.9)
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public void The_messages_are_the_words_of_section_3_9()
    {
        Assert.Equal("The file didn't match its checksum.", DownloadMessages.Mismatch);
        Assert.Equal("The file is no longer at its source. A Marrow update will fix this.", DownloadMessages.NotFound);
        Assert.Equal("Couldn't reach libreoffice.org.", DownloadMessages.Unreachable("libreoffice.org"));
    }
}
