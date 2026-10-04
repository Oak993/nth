using System.Runtime.InteropServices;
using Marrow.Services.Port.Skills.Install;
using Marrow.Services.Port.Skills.Store;
using Marrow.Services.Tests.Fixtures;
namespace Marrow.Services.Tests.Port.Skills.Install;

// PORT-ONLY: integ-sk4-sk5 Windows review of DL-4 (skills-ONDEMAND-PLAN section 7) on real NTFS and real process launch.
// The stand-ins are test-built PE images; no real msiexec, 7-Zip, Python or installer ever runs.
public sealed class InstallerWindowsReviewTests
{
    public static IEnumerable<object[]> RealLockCrashes() => new[] { InstallerKind.Zip, InstallerKind.Skillset }
        .SelectMany(k => Enum.GetValues<InstallStep>().Select(s => new object[] { k, s }));

    [WindowsOnlyFact]
    [Trait("Feature", "SK-4")]
    [Trait("Rule", "R-TEST-1")]
    public async Task WINDOWS_ONLY_msiexec_gets_documented_PROPERTY_quoted_value_for_a_data_root_with_spaces()
    {
        // Windows Installer, "Setting Public Property Values on the Command Line": a value with spaces is written
        // PROPERTY="value". The production data root is %LOCALAPPDATA%\Marrow Code, so TARGETDIR always has a space.
        using var rig = new InstallRig();
        var exe = rig.Input("fake-msiexec-" + Guid.NewGuid().ToString("N") + ".exe", StandInExecutable.BuildSpinning());
        var runner = new WindowsInstallProcess([new LocalExecutable(exe, new FileInfo(exe).Length, InstallHash.Sha256(File.ReadAllBytes(exe)))]);
        var dataRoot = Path.Combine(rig.Root, "Marrow Code");
        var msi = Path.Combine(dataRoot, "addons", "downloads", "LibreOffice fixture.msi");
        var plan = rig.Plan(InstallerKind.MsiAdmin);
        Directory.CreateDirectory(Path.GetDirectoryName(msi)!);
        File.Copy(plan.LocalFiles["payload.zip"], msi);
        plan = plan with { Extractor = exe, DataRoot = dataRoot, LocalFiles = new Dictionary<string, string>(plan.LocalFiles) { ["payload.zip"] = msi } };
        using var cancel = new CancellationTokenSource();
        try
        {
            var install = new ItemInstaller(rig.Lock, runner).InstallAsync(plan, cancel.Token);
            var line = await ChildCommandLine.WaitForAsync(exe, TimeSpan.FromSeconds(30));
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => install);
            var target = Assert.Single(Directory.GetDirectories(Path.Combine(dataRoot, "addons", "tools")));
            Assert.StartsWith("tmp-", Path.GetFileName(target), StringComparison.Ordinal);
            Assert.Equal("\"" + exe + "\" /a \"" + msi + "\" /qn TARGETDIR=\"" + target + "\"", line);
        }
        finally { cancel.Cancel(); ChildCommandLine.KillAll(exe); }
    }

    [WindowsOnlyFact]
    [Trait("Feature", "SK-4")]
    [Trait("Rule", "R-TEST-1")]
    public async Task WINDOWS_ONLY_sfx_7z_receives_its_documented_switches_as_two_arguments()
    {
        using var rig = new InstallRig();
        var sfx = rig.Input("PortableGit fixture " + Guid.NewGuid().ToString("N") + ".7z.exe", StandInExecutable.BuildSpinning());
        var runner = new WindowsInstallProcess([new LocalExecutable(sfx, new FileInfo(sfx).Length, InstallHash.Sha256(File.ReadAllBytes(sfx)))]);
        var dataRoot = Path.Combine(rig.Root, "Marrow Code");
        Directory.CreateDirectory(dataRoot);
        var plan = rig.Plan(InstallerKind.Sfx7z);
        var bytes = File.ReadAllBytes(sfx);
        plan = plan with { DataRoot = dataRoot, Extractor = sfx, Item = plan.Item with { Artifacts = [plan.Item.Artifacts[0] with { Size = bytes.Length, Sha256 = InstallHash.Sha256(bytes) }] },
            LocalFiles = new Dictionary<string, string>(plan.LocalFiles) { ["payload.zip"] = sfx } };
        using var cancel = new CancellationTokenSource();
        try
        {
            var install = new ItemInstaller(rig.Lock, runner).InstallAsync(plan, cancel.Token);
            var line = await ChildCommandLine.WaitForAsync(sfx, TimeSpan.FromSeconds(30));
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => install);
            var target = Assert.Single(Directory.GetDirectories(Path.Combine(dataRoot, "addons", "tools")));
            Assert.Equal(new[] { sfx, "-o" + target, "-y" }, Split(line));
        }
        finally { cancel.Cancel(); ChildCommandLine.KillAll(sfx); }
    }

    [WindowsOnlyTheory]
    [MemberData(nameof(RealLockCrashes))]
    [Trait("Feature", "SK-4")]
    [Trait("Rule", "R-TEST-1")]
    public async Task WINDOWS_ONLY_a_crash_at_every_step_under_the_real_ACL_leaves_only_unlocked_tmp_folders(InstallerKind kind, InstallStep step)
    {
        using var rig = new InstallRig();
        var seal = new WindowsInstallLock();
        var plan = rig.Plan(kind);
        if (kind == InstallerKind.Skillset) plan = plan with { Spares = [new SpareProjection("codex", "01234567", ["LICENSE"]), new SpareProjection("claude", "01234567", ["LICENSE"])] };
        try
        {
            await Assert.ThrowsAsync<InjectedCrashException>(() => new ItemInstaller(seal, rig.Process, (s, path) =>
            {
                if (s == step && (kind == InstallerKind.Zip || path.Contains(Path.Combine("views", "claude"), StringComparison.Ordinal))) throw new InjectedCrashException();
            }).InstallAsync(plan));
            rig.AssertOnlyTemporaryFolders();
            var protection = new WindowsStoreProtection();
            foreach (var entry in Directory.EnumerateFileSystemEntries(rig.Root, "*", SearchOption.AllDirectories))
                Assert.False(protection.IsLocked(entry), "A deny entry survived the rollback: " + entry);
            // DL-5 owns tmp- recovery: SK-5's GC with the real unlock order removes every leftover (read-only copies included).
            var paths = new StorePaths(rig.Root);
            new SkillsStore(paths, [], Marrow.Core.Port.Skills.State.AddonsState.Empty, protection).Collect();
            foreach (var area in new[] { paths.Tools, paths.Store })
                Assert.True(!Directory.Exists(area) || Directory.GetFileSystemEntries(area).Length == 0, area);
            if (Directory.Exists(paths.Views)) Assert.All(Directory.GetDirectories(paths.Views), p => Assert.Empty(Directory.GetFileSystemEntries(p)));
        }
        finally { UnlockAll(rig.Root, seal); }
    }

    [WindowsOnlyFact]
    [Trait("Feature", "SK-4")]
    [Trait("Rule", "R-TEST-1")]
    public async Task WINDOWS_ONLY_an_installed_tool_and_skill_set_verify_under_SK5_seal_and_full_check()
    {
        using var rig = new InstallRig();
        var seal = new WindowsInstallLock();
        try
        {
            // An archive-created empty folder (PortableGit ships empty dev\shm, dev\mqueue and tmp) must survive the handoff.
            var zip = await new ItemInstaller(seal, rig.Process).InstallAsync(rig.Plan(InstallerKind.Zip, InstallRig.Zip(("bin/tool.exe", "x"), ("dev/shm/", ""), ("LICENSE", "fixture licence"))));
            Assert.True(Directory.Exists(Path.Combine(zip.Path, "dev", "shm")));
            var set = await new ItemInstaller(seal, rig.Process).InstallAsync(rig.Plan(InstallerKind.Skillset) with { Spares = [new SpareProjection("codex", "01234567", ["LICENSE"])] });
            var verifier = new StoreVerifier(new WindowsStoreProtection());
            foreach (var result in new[] { zip, set })
            {
                var manifest = new StoreManifest(result.Manifest, result.Directories);
                Assert.True(verifier.Verify(result.Path, manifest).IsValid, result.Path);
                Assert.True(verifier.Verify(result.Path, manifest, sealOnly: true).IsValid, result.Path);
            }
            Assert.True(verifier.Verify(set.SparePaths[0], new StoreManifest(set.Manifest)).IsValid);
        }
        finally { UnlockAll(rig.Root, seal); }
    }

    private static void UnlockAll(string root, WindowsInstallLock seal)
    {
        foreach (var area in new[] { Path.Combine(root, "addons", "tools"), Path.Combine(root, "skills", "store") })
            if (Directory.Exists(area)) foreach (var folder in Directory.GetDirectories(area)) seal.Unlock(folder);
        var views = Path.Combine(root, "skills", "views");
        if (Directory.Exists(views)) foreach (var provider in Directory.GetDirectories(views)) foreach (var folder in Directory.GetDirectories(provider)) seal.Unlock(folder);
    }

    private static string[] Split(string commandLine)
    {
        var argv = CommandLineToArgvW(commandLine, out var count);
        try { return Enumerable.Range(0, count).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))!).ToArray(); }
        finally { LocalFree(argv); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr LocalFree(IntPtr memory);
}
