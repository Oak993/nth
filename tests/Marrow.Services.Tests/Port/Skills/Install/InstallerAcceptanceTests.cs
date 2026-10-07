using System.Text;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Install;
namespace Marrow.Services.Tests.Port.Skills.Install;

[Trait("Spec", "DL-4")]
public sealed class InstallerAcceptanceTests
{
    [Fact]
    [Trait("Feature", "SK-4")]
    [Trait("Rule", "R-TEST-1")]
    public async Task Zip_keeps_licence_and_publishes_only_after_rename_lock_verify()
    {
        using var rig = new InstallRig();
        var steps = new List<InstallStep>();
        var result = await rig.Installer((step, path) => { steps.Add(step); if (step == InstallStep.Locked) Assert.True(rig.Lock.IsLocked(path)); }).InstallAsync(rig.Plan(InstallerKind.Zip));
        Assert.Equal(new[] { InstallStep.Created, InstallStep.Built, InstallStep.Renamed, InstallStep.Locked, InstallStep.Verified, InstallStep.Published }, steps);
        Assert.Equal("offline fixture", File.ReadAllText(Path.Combine(result.Path, "payload.txt")));
        Assert.Equal("fixture licence", File.ReadAllText(Path.Combine(result.Path, "LICENSE")));
        Assert.Equal(ItemState.Installed, result.Record.State);
        Assert.Empty(rig.Process.Commands);
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("C:\\escape.txt")]
    [InlineData("safe/../escape.txt")]
    [InlineData("file:stream")]
    [InlineData("CON")]
    [InlineData("trailing. ")]
    [Trait("Feature", "SK-4")]
    [Trait("Rule", "R-TEST-1")]
    public async Task Zip_refuses_unsafe_Windows_entries_on_every_host(string entry)
    {
        using var rig = new InstallRig();
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Installer().InstallAsync(rig.Plan(InstallerKind.Zip, InstallRig.Zip((entry, "escape")))));
        rig.AssertOnlyTemporaryFolders();
        Assert.False(File.Exists(Path.Combine(rig.Root, "escape.txt")));
    }

    [Fact]
    [Trait("Feature", "SK-4")]
    [Trait("Rule", "R-TEST-1")]
    public async Task Python_embed_writes_exact_pth_and_unpacks_pip_wheel()
    {
        using var rig = new InstallRig();
        var plan = rig.Plan(InstallerKind.PythonEmbed, InstallRig.Zip(("python.exe", "stand-in bytes"), ("python314._pth", "old pth")));
        var pipBytes = InstallRig.Zip(("pip/__init__.py", "fixture pip"));
        var pip = rig.Input("pip.whl", pipBytes);
        plan = rig.AddArtifact(plan, pip) with { Python = new PythonEmbedOptions("python314._pth", "python314.zip", "3.14", new LocalWheel(pip, pipBytes.Length, InstallHash.Sha256(pipBytes), "pip==26.2.1")) };
        var result = await rig.Installer().InstallAsync(plan);
        var agent = Path.GetFullPath(Path.Combine(rig.Root, "addons", "work", "python-agent", "3.14"));
        Assert.Equal("python314.zip\n.\nLib\\site-packages\n" + agent + "\nimport site\n", File.ReadAllText(Path.Combine(result.Path, "python314._pth")));
        Assert.Equal("fixture pip", File.ReadAllText(Path.Combine(result.Path, "Lib", "site-packages", "pip", "__init__.py")));
        Assert.Empty(rig.Process.Commands);
    }

    [Fact]
    [Trait("Feature", "SK-4")]
    [Trait("Rule", "R-TEST-1")]
    public async Task Pyset_requires_local_hashed_wheels_and_precompiles_before_lock()
    {
        using var rig = new InstallRig();
        var plan = rig.Plan(InstallerKind.Pyset);
        var wheel = rig.Input("fixture.whl", "fixture wheel"u8.ToArray());
        var pin = new LocalWheel(wheel, new FileInfo(wheel).Length, InstallHash.Sha256(File.ReadAllBytes(wheel)), "fixture==1.0");
        string? lockText = null; // Read while pip runs: the staging folder is removed afterwards (integ-sk4-sk5).
        rig.Process.OnRun = command => { if (command.Arguments.Contains("pip", StringComparer.Ordinal)) { lockText = File.ReadAllText(command.Arguments[11]); var target = command.Arguments[command.Arguments.ToList().IndexOf("--target") + 1]; File.WriteAllText(Path.Combine(target, "fixture.py"), "fixture module"); } };
        plan = rig.AddArtifact(plan, wheel) with { Pyset = new PysetOptions(Path.Combine(rig.Root, "python.exe"), [pin]) };
        var result = await rig.Installer().InstallAsync(plan);
        var argv = rig.Process.Commands[0].Arguments;
        Assert.Equal(new[] { "-m", "pip", "install", "--no-index", "--find-links", rig.Process.Commands[0].WorkingDirectory, "--require-hashes", "--no-deps", "--target", argv[9], "-r", argv[11] }, argv);
        Assert.Contains("fixture==1.0 --hash=sha256:" + pin.Sha256, lockText, StringComparison.Ordinal);
        Assert.Equal(new[] { "-m", "compileall", "--invalidation-mode", "unchecked-hash", argv[9] }, rig.Process.Commands[1].Arguments);
        Assert.True(File.Exists(Path.Combine(result.Path, "fixture.py")));
        File.AppendAllText(wheel, "changed");
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Installer().InstallAsync(plan));
        Assert.Equal(2, rig.Process.Commands.Count);
    }

    [Fact]
    [Trait("Feature", "SK-4")]
    [Trait("Rule", "R-TEST-1")]
    public async Task Unbound_process_and_lock_fail_closed()
    {
        using var rig = new InstallRig();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ItemInstaller().InstallAsync(rig.Plan(InstallerKind.Zip)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ItemInstaller(rig.Lock).InstallAsync(rig.Plan(InstallerKind.Sfx7z) with { Extractor = Path.Combine(rig.Root, "fake.exe") }));
        rig.AssertOnlyTemporaryFolders();
    }

    [Fact]
    [Trait("Feature", "SK-4")]
    [Trait("Rule", "R-TEST-1")]
    public async Task Changed_download_missing_licence_extra_file_and_lost_lock_are_refused()
    {
        using var rig = new InstallRig();
        var plan = rig.Plan(InstallerKind.Zip);
        File.AppendAllText(plan.LocalFiles["payload.zip"], "tampered");
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Installer().InstallAsync(plan));
        plan = rig.Plan(InstallerKind.Zip);
        File.Delete(plan.LocalFiles["LICENSE"]);
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Installer().InstallAsync(plan));
        plan = rig.Plan(InstallerKind.Zip);
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Installer((step, p) => { if (step == InstallStep.Locked) File.WriteAllText(Path.Combine(p, "extra.py"), "extra"); }).InstallAsync(plan));
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Installer((step, p) => { if (step == InstallStep.Locked) rig.Lock.Unlock(p); }).InstallAsync(plan));
        rig.AssertOnlyTemporaryFolders();
    }
}
