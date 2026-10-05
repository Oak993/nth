using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Skills.Install;
namespace Marrow.Services.Tests.Port.Skills.Install;
[Trait("Spec", "DL-4")]
public sealed class InstallerReviewTests
{
    [Theory]
    [InlineData(InstallerKind.Zip)]
    [InlineData(InstallerKind.PythonEmbed)]
    [InlineData(InstallerKind.Pyset)]
    [InlineData(InstallerKind.Nodeset)]
    [InlineData(InstallerKind.MsiAdmin)]
    [InlineData(InstallerKind.Sfx7z)]
    [InlineData(InstallerKind.Skillset)]
    [Trait("Feature", "SK-4")]
    [Trait("Rule", "R-TEST-1")]
    public async Task Every_kind_retains_the_pinned_licence(InstallerKind kind)
    {
        using var rig = new InstallRig();
        var plan = rig.AddPip(rig.Plan(kind)) with { Pyset = new PysetOptions(Path.Combine(rig.Root, "python.exe"), []), NodePackages = [], Spares = [], Extractor = kind == InstallerKind.MsiAdmin ? Path.Combine(rig.Root, "msiexec.exe") : null };
        rig.Process.OnRun = cmd => { var target = kind == InstallerKind.MsiAdmin ? cmd.Arguments[3]["TARGETDIR=".Length..] : kind == InstallerKind.Sfx7z ? cmd.Arguments[0][2..] : cmd.Arguments.Contains("--target", StringComparer.Ordinal) ? cmd.Arguments[cmd.Arguments.ToList().IndexOf("--target") + 1] : null; if (target is not null) File.WriteAllText(Path.Combine(target, "fixture.exe"), "offline fixture"); };
        var result = await rig.Installer().InstallAsync(plan);
        Assert.Equal("fixture licence", File.ReadAllText(Path.Combine(result.Path, "LICENSE")));
        Assert.True(rig.Lock.IsLocked(result.Path));
    }

    [Fact]
    [Trait("Feature", "SK-4")]
    [Trait("Rule", "R-TEST-1")]
    public async Task Same_length_artifact_and_wheel_changes_fail_sha256_checks()
    {
        using var rig = new InstallRig();
        var plan = rig.Plan(InstallerKind.Zip);
        var file = plan.LocalFiles["payload.zip"];
        var bytes = File.ReadAllBytes(file); bytes[^1] ^= 1; File.WriteAllBytes(file, bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Installer().InstallAsync(plan));
        plan = rig.Plan(InstallerKind.Pyset);
        var wheel = rig.Input("fixture.whl", "fixture wheel"u8.ToArray());
        var pin = new LocalWheel(wheel, new FileInfo(wheel).Length, InstallHash.Sha256(File.ReadAllBytes(wheel)), "fixture==1.0");
        plan = rig.AddArtifact(plan, wheel) with { Pyset = new PysetOptions(Path.Combine(rig.Root, "python.exe"), [pin]) };
        bytes = File.ReadAllBytes(wheel); bytes[0] ^= 1; File.WriteAllBytes(wheel, bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Installer().InstallAsync(plan));
        Assert.Empty(rig.Process.Commands);
    }

    [Theory]
    [InlineData("extra-directory")]
    [InlineData("missing-file")]
    [InlineData("changed-file")]
    [Trait("Feature", "SK-4")]
    [Trait("Rule", "R-TEST-1")]
    public async Task Full_verify_refuses_extra_directory_missing_or_changed_file(string fault)
    {
        using var rig = new InstallRig();
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Installer((step, path) =>
        {
            if (step != InstallStep.Locked) return;
            if (fault == "extra-directory") Directory.CreateDirectory(Path.Combine(path, "extra"));
            if (fault == "missing-file") File.Delete(Path.Combine(path, "payload.txt"));
            if (fault == "changed-file") File.WriteAllText(Path.Combine(path, "payload.txt"), "changed fixture");
        }).InstallAsync(rig.Plan(InstallerKind.Zip)));
        rig.AssertOnlyTemporaryFolders();
    }

    [Fact]
    [Trait("Feature", "SK-4")]
    [Trait("Rule", "R-TEST-1")]
    public async Task Skillset_does_not_bless_a_file_planted_before_snapshot()
    {
        using var rig = new InstallRig();
        var plan = rig.Plan(InstallerKind.Skillset) with { Spares = [] };
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Installer((step, path) => { if (step == InstallStep.Created) File.WriteAllText(Path.Combine(path, "extra.py"), "planted"); }).InstallAsync(plan));
        rig.AssertOnlyTemporaryFolders();
    }

    [Fact]
    [Trait("Feature", "SK-4")]
    [Trait("Rule", "R-TEST-1")]
    public async Task Supplemental_packages_must_be_pinned_in_the_catalogue_and_pip_is_required()
    {
        using var rig = new InstallRig();
        var plan = rig.Plan(InstallerKind.PythonEmbed);
        var bytes = InstallRig.Zip(("pip/__init__.py", "untrusted"));
        var wheel = rig.Input("extra.whl", bytes);
        plan = plan with { Python = new PythonEmbedOptions("python314._pth", "python314.zip", "3.14", new LocalWheel(wheel, bytes.Length, InstallHash.Sha256(bytes), "pip==26.2.1")) };
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Installer().InstallAsync(plan));
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Installer().InstallAsync(plan with { Python = plan.Python with { PipWheel = null } }));
        Assert.Empty(rig.Process.Commands);
    }

    [Theory]
    [InlineData(InstallStep.Created)]
    [InlineData(InstallStep.Built)]
    [InlineData(InstallStep.Renamed)]
    [InlineData(InstallStep.Locked)]
    [InlineData(InstallStep.Verified)]
    [InlineData(InstallStep.Published)]
    [Trait("Feature", "SK-4")]
    [Trait("Rule", "R-TEST-1")]
    public async Task Crash_during_spare_build_and_publish_quarantines_store_and_every_view(InstallStep crash)
    {
        using var rig = new InstallRig();
        var plan = rig.Plan(InstallerKind.Skillset) with { Spares = [new SpareProjection("codex", "01234567", ["LICENSE"]), new SpareProjection("claude", "01234567", ["LICENSE"])] };
        await Assert.ThrowsAsync<InjectedCrashException>(() => rig.Installer((step, path) => { if (step == crash && path.Contains(Path.Combine("views", "claude"), StringComparison.Ordinal)) throw new InjectedCrashException(); }).InstallAsync(plan));
        rig.AssertOnlyTemporaryFolders();
    }

    [Fact]
    [Trait("Feature", "SK-4")]
    [Trait("Rule", "R-TEST-1")]
    public async Task Claude_projection_builds_plugin_layout_and_spares_are_independent_copies()
    {
        using var rig = new InstallRig();
        var plan = rig.Plan(InstallerKind.Skillset);
        var data = "---\nname: fixture\n---\nFixture body"u8.ToArray();
        var file = rig.Input("SKILL.md", data);
        plan = plan with { Item = plan.Item with { Artifacts = [], Files = [plan.Item.Files[0], new CatalogueFile { Path = "fixture/SKILL.md", Size = data.Length, Sha256 = InstallHash.Sha256(data) }] },
            LocalFiles = new Dictionary<string, string>(plan.LocalFiles) { ["fixture/SKILL.md"] = file },
            Spares = [new SpareProjection("claude", "12345678", ["fixture/SKILL.md"]) { Prefix = "skills", GeneratedFiles = new Dictionary<string, string> { [".claude-plugin/plugin.json"] = "{\"name\":\"marrow\",\"version\":\"fixture\"}" } }, new SpareProjection("codex", "12345678", ["fixture/SKILL.md"])] };
        var result = await rig.Installer().InstallAsync(plan);
        Assert.All(result.SparePaths, p => Assert.True(rig.Lock.IsLocked(p)));
        var claude = result.SparePaths[0]; var codex = result.SparePaths[1];
        Assert.True(File.Exists(Path.Combine(claude, ".claude-plugin", "plugin.json")));
        File.WriteAllText(Path.Combine(claude, "skills", "fixture", "SKILL.md"), "changed view");
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(codex, "fixture", "SKILL.md")));
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(result.Path, "fixture", "SKILL.md")));
    }
}
