using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Downloads;
using Marrow.Services.Port.Skills.Install;
namespace Marrow.Services.Tests.Port.Skills.Install;
[Trait("Spec", "DL-4")]
public sealed class InstallerBoundaryTests
{
    [Fact]
    [Trait("Feature", "SK-4")]
    [Trait("Rule", "R-TEST-1")]
    public async Task SK3_outcome_and_SK7_checking_pin_are_required_without_persisting_state()
    {
        using var rig = new InstallRig();
        var plan = rig.Plan(InstallerKind.Zip);
        var files = plan.LocalFiles.Values.Select(path => new DownloadFileResult(path, new FileInfo(path).Length, InstallHash.Sha256File(path), "fixture.invalid", 0, false)).ToArray();
        Assert.Throws<InvalidDataException>(() => plan.WithDownloads(new DownloadOutcome(DownloadStatus.Cancelled, files, null)));
        Assert.Throws<InvalidDataException>(() => plan.WithDownloads(new DownloadOutcome(DownloadStatus.Succeeded, files[..1], null)));
        var valid = plan.WithDownloads(new DownloadOutcome(DownloadStatus.Succeeded, files, null));
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Installer().InstallAsync(valid with { Record = plan.Record with { TargetPin = "different" } }));
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Installer().InstallAsync(valid with { Record = ItemRecord.Available(plan.Item.Id) }));
        await rig.Installer().InstallAsync(valid);
        Assert.Equal(ItemState.Checking, plan.Record.State);
        Assert.False(File.Exists(Path.Combine(rig.Root, "addons", "state.json")));
    }

    [Theory]
    [InlineData(InstallerKind.MsiAdmin)]
    [InlineData(InstallerKind.Sfx7z)]
    [Trait("Feature", "SK-4")]
    [Trait("Rule", "R-TEST-1")]
    public async Task An_extractor_that_returns_zero_without_payload_does_not_install(InstallerKind kind)
    {
        using var rig = new InstallRig();
        var plan = rig.Plan(kind);
        if (kind == InstallerKind.MsiAdmin) plan = plan with { Extractor = Path.Combine(rig.Root, "msiexec.exe") };
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Installer().InstallAsync(plan));
        rig.AssertOnlyTemporaryFolders();
    }

    [Fact]
    [Trait("Feature", "SK-4")]
    [Trait("Rule", "R-TEST-1")]
    public async Task Cancel_before_install_creates_no_final_item_or_process_and_collision_preserves_previous_install()
    {
        using var rig = new InstallRig();
        var plan = rig.Plan(InstallerKind.Zip);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Installer().InstallAsync(plan, cancellation.Token));
        rig.AssertOnlyTemporaryFolders(); Assert.Empty(rig.Process.Commands);
        var result = await rig.Installer().InstallAsync(plan);
        var original = File.ReadAllBytes(Path.Combine(result.Path, "payload.txt"));
        await Assert.ThrowsAsync<IOException>(() => rig.Installer().InstallAsync(plan));
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(result.Path, "payload.txt")));
        Assert.True(rig.Lock.IsLocked(result.Path));
    }
}
