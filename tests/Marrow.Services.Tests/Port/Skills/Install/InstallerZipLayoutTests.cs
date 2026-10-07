using System.IO.Compression;
using Marrow.Services.Port.Skills.Install;
namespace Marrow.Services.Tests.Port.Skills.Install;
[Trait("Spec", "DL-4")]
public sealed class InstallerZipLayoutTests
{
    [Fact]
    [Trait("Feature", "SK-4")]
    [Trait("Rule", "R-TEST-1")]
    public async Task Explicit_multi_archive_layout_and_empty_directory_manifest_are_kept()
    {
        using var rig = new InstallRig();
        var plan = rig.Plan(InstallerKind.Zip, InstallRig.Zip(("chrome-win64/chrome.exe", "fixture browser")));
        var second = rig.Input("headless.zip", InstallRig.Zip(("shell.exe", "fixture shell"), ("empty/", "")));
        plan = rig.AddArtifact(plan, second) with { ZipArchives = [new ZipProjection("payload.zip", "chromium-1243"), new ZipProjection("headless.zip", "chromium_headless_shell-1243")] };
        var result = await rig.Installer().InstallAsync(plan);
        Assert.True(File.Exists(Path.Combine(result.Path, "chromium-1243", "chrome-win64", "chrome.exe")));
        Assert.True(File.Exists(Path.Combine(result.Path, "chromium_headless_shell-1243", "shell.exe")));
        Assert.Contains("chromium_headless_shell-1243/empty", result.Directories);
    }

    [Fact]
    [Trait("Feature", "SK-4")]
    [Trait("Rule", "R-TEST-1")]
    public async Task Zip_refuses_Unix_symlink_metadata_and_case_collisions_on_Linux_too()
    {
        using var rig = new InstallRig();
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry("link"); entry.ExternalAttributes = unchecked((int)0xa1ff0000);
            using var writer = new StreamWriter(entry.Open()); writer.Write("../escape");
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Installer().InstallAsync(rig.Plan(InstallerKind.Zip, bytes.ToArray())));
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Installer().InstallAsync(rig.Plan(InstallerKind.Zip, InstallRig.Zip(("same.txt", "one"), ("SAME.txt", "two")))));
        rig.AssertOnlyTemporaryFolders();
    }
}
