using Marrow.Services.Port.Skills.Install;
using Marrow.Services.Port.Skills.Store;
using Marrow.Services.Tests.Port.Skills.Store.Fixtures;
namespace Marrow.Services.Tests.Port.Skills.Install;

// PORT-ONLY: integ-sk4-sk5 review. DL-4's output must be what DL-5 verifies, and DL-4 must not leave staging behind.
[Trait("Spec", "DL-4")]
public sealed class InstallerHandoffReviewTests
{
    [Fact]
    [Trait("Feature", "SK-4")]
    [Trait("Rule", "R-TEST-1")]
    public async Task An_archive_created_empty_folder_survives_the_handoff_to_the_SK5_verifier()
    {
        using var rig = new InstallRig();
        var result = await rig.Installer().InstallAsync(rig.Plan(InstallerKind.Zip, InstallRig.Zip(("bin/tool.exe", "x"), ("dev/shm/", ""), ("LICENSE", "fixture licence"))));
        Assert.Contains("dev/shm", result.Directories);
        var protection = new FakeStoreProtection();
        new StoreTrees(protection).Lock(result.Path);
        var verifier = new StoreVerifier(protection);
        Assert.True(verifier.Verify(result.Path, new StoreManifest(result.Manifest, result.Directories)).IsValid);
        // Without SK-4's directory list the same folder is an extra empty folder, which PLAN WU-4 calls damage.
        Assert.False(verifier.Verify(result.Path, new StoreManifest(result.Manifest)).IsValid);
        Assert.Throws<ArgumentException>(() => new StoreManifest(result.Manifest, ["../escape"]));
        Assert.Throws<ArgumentException>(() => new StoreManifest(result.Manifest, ["LICENSE"]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [Trait("Feature", "SK-4")]
    [Trait("Rule", "R-TEST-1")]
    public async Task Pyset_leaves_no_wheel_staging_after_success_or_failure(int exitCode)
    {
        using var rig = new InstallRig();
        var plan = rig.Plan(InstallerKind.Pyset);
        var wheel = rig.Input("fixture.whl", "fixture wheel"u8.ToArray());
        var pin = new LocalWheel(wheel, new FileInfo(wheel).Length, InstallHash.Sha256(File.ReadAllBytes(wheel)), "fixture==1.0");
        rig.Process.ExitCode = exitCode;
        rig.Process.OnRun = command =>
        {
            Assert.True(File.Exists(Path.Combine(command.WorkingDirectory, "fixture.whl")));
            if (command.Arguments.Contains("pip", StringComparer.Ordinal))
                File.WriteAllText(Path.Combine(command.Arguments[command.Arguments.ToList().IndexOf("--target") + 1], "fixture.py"), "fixture module");
        };
        plan = rig.AddArtifact(plan, wheel) with { Pyset = new PysetOptions(Path.Combine(rig.Root, "python.exe"), [pin]) };
        if (exitCode == 0) await rig.Installer().InstallAsync(plan);
        else await Assert.ThrowsAsync<InstallProcessException>(() => rig.Installer().InstallAsync(plan));
        Assert.NotEmpty(rig.Process.Commands);
        var downloads = Path.Combine(rig.Root, "addons", "downloads");
        Assert.True(!Directory.Exists(downloads) || Directory.GetFileSystemEntries(downloads).Length == 0);
    }
}
