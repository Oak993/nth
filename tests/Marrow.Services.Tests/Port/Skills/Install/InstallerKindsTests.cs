using System.Security.Cryptography;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Skills.Install;
namespace Marrow.Services.Tests.Port.Skills.Install;
[Trait("Spec", "DL-4")]
public sealed class InstallerKindsTests
{
    [Fact]
    [Trait("Feature", "SK-4")]
    [Trait("Rule", "R-TEST-1")]
    public async Task Nodeset_builds_scoped_and_nested_tree_without_postinstall()
    {
        using var rig = new InstallRig();
        var sentinel = Path.Combine(rig.Root, "postinstall-ran");
        var json = System.Text.Json.JsonSerializer.Serialize(new { name = "fixture", scripts = new { postinstall = "node -e \"require('fs').writeFileSync('" + sentinel.Replace("\\", "/", StringComparison.Ordinal) + "','ran')\"" } });
        var tar = TarFixture.Package(("package/package.json", json), ("package/index.js", "module.exports = 'fixture'"));
        var file = rig.Input("fixture.tgz", tar);
        var package = new LocalNodePackage(file, tar.Length, InstallHash.Sha256(tar), "sha512-" + Convert.ToBase64String(SHA512.HashData(tar)), "node_modules/@fixture/tool");
        var plan = rig.AddArtifact(rig.Plan(InstallerKind.Nodeset), file) with { NodePackages = [package, package with { TreePath = "node_modules/parent/node_modules/child" }] };
        var result = await rig.Installer().InstallAsync(plan);
        Assert.Equal(json, File.ReadAllText(Path.Combine(result.Path, "node_modules", "@fixture", "tool", "package.json")));
        Assert.True(File.Exists(Path.Combine(result.Path, "node_modules", "parent", "node_modules", "child", "index.js")));
        Assert.False(File.Exists(sentinel));
        Assert.Empty(rig.Process.Commands);
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Installer().InstallAsync(plan with { NodePackages = [package with { Integrity = "sha512-" + Convert.ToBase64String(new byte[64]) }] }));
    }

    [Theory]
    [InlineData("package/../../escape")]
    [InlineData("../package/escape")]
    [Trait("Feature", "SK-4")]
    [Trait("Rule", "R-TEST-1")]
    public async Task Nodeset_refuses_tar_traversal(string entry)
    {
        using var rig = new InstallRig();
        var bytes = TarFixture.Package((entry, "escape"));
        var file = rig.Input("bad.tgz", bytes);
        var plan = rig.AddArtifact(rig.Plan(InstallerKind.Nodeset), file) with { NodePackages = [new LocalNodePackage(file, bytes.Length, InstallHash.Sha256(bytes), "sha512-" + Convert.ToBase64String(SHA512.HashData(bytes)), "node_modules/fixture")] };
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Installer().InstallAsync(plan));
        rig.AssertOnlyTemporaryFolders();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3010)]
    [InlineData(1641)]
    [InlineData(1603)]
    [InlineData(1618)]
    [Trait("Feature", "SK-4")]
    [Trait("Rule", "R-TEST-1")]
    public async Task Msi_admin_argv_and_exit_classification_are_offline(int code)
    {
        using var rig = new InstallRig();
        rig.Process.ExitCode = code;
        rig.Process.OnRun = cmd => File.WriteAllText(Path.Combine(cmd.Arguments[3]["TARGETDIR=".Length..], "soffice.exe"), "fake admin image");
        var plan = rig.Plan(InstallerKind.MsiAdmin) with { Extractor = Path.Combine(rig.Root, "msiexec.exe") };
        if (code is 0 or 3010) await rig.Installer().InstallAsync(plan);
        else { var error = await Assert.ThrowsAsync<InstallProcessException>(() => rig.Installer().InstallAsync(plan)); Assert.Equal(code, error.ExitCode); rig.AssertOnlyTemporaryFolders(); }
        var command = Assert.Single(rig.Process.Commands);
        Assert.Equal(new[] { "/a", plan.LocalFiles["payload.zip"], "/qn", command.Arguments[3] }, command.Arguments);
        Assert.StartsWith("TARGETDIR=", command.Arguments[3], StringComparison.Ordinal);
        Assert.Equal(plan.Extractor, command.Executable);
    }

    [Fact]
    [Trait("Feature", "SK-4")]
    [Trait("Rule", "R-TEST-1")]
    public async Task Sfx_uses_documented_argv_and_requires_zero_exit()
    {
        using var rig = new InstallRig();
        var plan = rig.Plan(InstallerKind.Sfx7z) with { Extractor = rig.Plan(InstallerKind.Zip).LocalFiles["payload.zip"] };
        rig.Process.OnRun = cmd => File.WriteAllText(Path.Combine(cmd.Arguments[0][2..], "bash.exe"), "fake sfx");
        await rig.Installer().InstallAsync(plan);
        var cmd = Assert.Single(rig.Process.Commands);
        Assert.Equal(new[] { "-o" + cmd.Arguments[0][2..], "-y" }, cmd.Arguments);
        rig.Process.ExitCode = 1;
        await Assert.ThrowsAsync<InstallProcessException>(() => rig.Installer().InstallAsync(plan));
    }

    [Fact]
    [Trait("Feature", "SK-4")]
    [Trait("Rule", "R-TEST-1")]
    public async Task Skillset_keeps_source_pin8_skill_and_builds_independent_spares()
    {
        using var rig = new InstallRig();
        var plan = rig.Plan(InstallerKind.Skillset);
        var data = "---\nname: fixture\n---\nOffline fixture"u8.ToArray();
        var file = rig.Input("SKILL.md", data);
        plan = plan with { Item = plan.Item with { Kind = ItemKind.Skillset, Artifacts = [], Files = [plan.Item.Files[0], new CatalogueFile { Path = "fixture/SKILL.md", Size = data.Length, Sha256 = InstallHash.Sha256(data) }] },
            LocalFiles = new Dictionary<string, string>(plan.LocalFiles) { ["fixture/SKILL.md"] = file },
            Spares = [new SpareProjection("codex", "01234567", ["fixture/SKILL.md"]), new SpareProjection("claude", "01234567", ["fixture/SKILL.md"])] };
        var result = await rig.Installer().InstallAsync(plan);
        Assert.EndsWith(Path.Combine("skills", "store", "test-source@12345678"), result.Path, StringComparison.Ordinal);
        Assert.Equal(2, result.SparePaths.Count);
        foreach (var spare in result.SparePaths)
        {
            Assert.StartsWith("spare-01234567-", Path.GetFileName(spare), StringComparison.Ordinal);
            Assert.Equal(data, File.ReadAllBytes(Path.Combine(spare, "fixture", "SKILL.md")));
            File.WriteAllText(Path.Combine(spare, "fixture", "SKILL.md"), "edit in one view");
        }
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(result.Path, "fixture", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(result.Path, "LICENSE")));
    }
}
