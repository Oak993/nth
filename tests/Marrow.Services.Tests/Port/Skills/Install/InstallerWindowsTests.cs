using System.IO.Compression;
using Marrow.Services.Port.Skills.Install;
namespace Marrow.Services.Tests.Port.Skills.Install;
public sealed class InstallerWindowsTests
{
    [WindowsOnlyFact]
    [Trait("Feature", "SK-4")]
    [Trait("Rule", "R-TEST-1")]
    public async Task WINDOWS_ONLY_zip_refuses_reparse_entry_and_junction_root()
    {
        using var rig = new InstallRig();
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry("linked.txt");
            entry.ExternalAttributes = (int)FileAttributes.ReparsePoint;
            using var writer = new StreamWriter(entry.Open()); writer.Write("refused");
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Installer().InstallAsync(rig.Plan(InstallerKind.Zip, bytes.ToArray())));
        var plan = rig.Plan(InstallerKind.Zip);
        var outside = Path.Combine(rig.Root, "outside"); Directory.CreateDirectory(outside);
        var linked = Path.Combine(rig.Root, "linked-root");
        // A mount-point junction does not need symlink privileges.
        JunctionFixture.Create(linked, outside);
        try { await Assert.ThrowsAsync<InvalidDataException>(() => rig.Installer().InstallAsync(plan with { DataRoot = linked })); Assert.Empty(Directory.GetFileSystemEntries(outside)); }
        finally { Directory.Delete(linked); }
    }

    [WindowsOnlyFact]
    [Trait("Feature", "SK-4")]
    [Trait("Rule", "R-TEST-1")]
    public async Task WINDOWS_ONLY_python_embed_runs_tiny_test_built_exe()
    {
        using var rig = new InstallRig();
        var plan = rig.Plan(InstallerKind.PythonEmbed, InstallRig.Zip(("placeholder", "fixture"))) with { Python = new PythonEmbedOptions("python314._pth", "python314.zip", "3.14", null) };
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, true))
            using (var entry = archive.CreateEntry("python.exe").Open()) entry.Write(StandInExecutable.Build(73));
        var exeBytes = bytes.ToArray();
        var artifact = rig.Input("stand-in.zip", exeBytes);
        plan = plan with { Item = plan.Item with { Artifacts = [plan.Item.Artifacts[0] with { Size = exeBytes.Length, Sha256 = InstallHash.Sha256(exeBytes) }] }, LocalFiles = new Dictionary<string, string>(plan.LocalFiles) { ["payload.zip"] = artifact } };
        plan = rig.AddPip(plan);
        var result = await rig.Installer().InstallAsync(plan);
        var exe = Path.Combine(result.Path, "python.exe");
        var runner = new WindowsInstallProcess([new LocalExecutable(exe, new FileInfo(exe).Length, InstallHash.Sha256(File.ReadAllBytes(exe)))]);
        Assert.Equal(73, await runner.RunAsync(new InstallCommand(exe, ["-c", "stand-in, never Python"], result.Path), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new WindowsInstallProcess([]).RunAsync(new InstallCommand(exe, [], result.Path), CancellationToken.None));
    }

    [WindowsOnlyFact]
    [Trait("Feature", "SK-4")]
    [Trait("Rule", "R-TEST-1")]
    public async Task WINDOWS_ONLY_fake_msiexec_exe_returns_success_and_failure_codes()
    {
        using var rig = new InstallRig();
        var exe = rig.Input("msiexec.exe", StandInExecutable.Build(0));
        var runner = new WindowsInstallProcess([new LocalExecutable(exe, new FileInfo(exe).Length, InstallHash.Sha256(File.ReadAllBytes(exe)))]);
        rig.Process.OnRun = command =>
        {
            rig.Process.ExitCode = runner.RunAsync(command, CancellationToken.None).GetAwaiter().GetResult();
            if (rig.Process.ExitCode == 0) File.WriteAllText(Path.Combine(command.Arguments[3]["TARGETDIR=".Length..], "soffice.exe"), "fixture admin image");
        };
        await new ItemInstaller(rig.Lock, rig.Process).InstallAsync(rig.Plan(InstallerKind.MsiAdmin) with { Extractor = exe });
        File.WriteAllBytes(exe, StandInExecutable.Build(99));
        runner = new WindowsInstallProcess([new LocalExecutable(exe, new FileInfo(exe).Length, InstallHash.Sha256(File.ReadAllBytes(exe)))]);
        var error = await Assert.ThrowsAsync<InstallProcessException>(() => new ItemInstaller(rig.Lock, rig.Process).InstallAsync(rig.Plan(InstallerKind.MsiAdmin) with { Extractor = exe }));
        Assert.Equal(99, error.ExitCode);
    }

    [WindowsOnlyFact]
    [Trait("Feature", "SK-4")]
    [Trait("Rule", "R-TEST-1")]
    public async Task WINDOWS_ONLY_lock_deny_and_readonly_survive_publish_and_teardown_unlocks()
    {
        using var rig = new InstallRig();
        var seal = new WindowsInstallLock();
        string? installed = null;
        try
        {
            var result = await new ItemInstaller(seal, rig.Process).InstallAsync(rig.Plan(InstallerKind.Zip)); installed = result.Path;
            Assert.True(seal.IsLocked(installed));
            Assert.Throws<UnauthorizedAccessException>(() => File.WriteAllText(Path.Combine(installed, "extra"), "refused"));
            Assert.Throws<UnauthorizedAccessException>(() => Directory.Delete(installed, true));
        }
        finally
        {
            foreach (var folder in Directory.GetDirectories(Path.Combine(rig.Root, "addons", "tools"))) seal.Unlock(folder);
        }
        Assert.False(seal.IsLocked(installed!));
    }

    [WindowsOnlyFact]
    [Trait("Feature", "SK-4")]
    [Trait("Rule", "R-TEST-1")]
    public async Task WINDOWS_ONLY_named_data_stream_is_refused()
    {
        using var rig = new InstallRig();
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Installer((step, path) => { if (step == InstallStep.Locked) File.WriteAllText(Path.Combine(path, "payload.txt") + ":planted", "hidden stream"); }).InstallAsync(rig.Plan(InstallerKind.Zip)));
        rig.AssertOnlyTemporaryFolders();
    }
}
