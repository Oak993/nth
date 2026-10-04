using System.Runtime.CompilerServices;
using System.Text.Json;
using Marrow.Services.Port.Downloads;
using Marrow.Services.Port.Voice.Models;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Marrow.Testing.Downloads;

namespace Marrow.Services.Tests.Port.Voice.Models;

// VT-7 (OD-0099 Q-V-9 C): the CUDA runtime pack as a download: fetched through the shared downloader, verified (SHA-256 by the downloader, then the
// pinned SHA-512 and every unpacked file's size), unpacked into <data root>\runtimes\cuda12\win-x64 and nowhere else. On SK-0's fake server.
public sealed class CudaPackInstallerTests
{
    private static string PinsJson([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "..", "..", "src", "Marrow.Services", "Port", "Voice", "Assets", "voice-pins.json"));

    private static (DownloadRig Rig, CudaPackInstaller Installer, string Root) Make(DownloadRig rig, CudaRuntimePack pin, TimeProvider? clock = null)
    {
        var root = Path.Combine(rig.Root, "data");
        var downloader = new Downloader(rig.PartFolder, new DownloadPolicy(["127.0.0.1"]), handler: rig.Counting);
        return (rig, new CudaPackInstaller(root, downloader, _ => rig.Server.Url("/pack.nupkg"), clock), root);
    }

    /// <summary>Runs an install whose folder swap may wait on <paramref name="clock"/>, moving that clock whenever it waits (bounded).</summary>
    private static async Task<CudaInstallResult> InstallPumping(CudaPackInstaller installer, CudaRuntimePack pin, ManualTimeProvider clock, Action? onFirstWait = null)
    {
        var running = installer.Install(pin);
        var waited = false;
        using var stop = new CancellationTokenSource(FakeDownloadServer.Guard);
        while (!running.IsCompleted)
        {
            await DownloadRig.Until(() => clock.ActiveTimers > 0 || running.IsCompleted);
            if (running.IsCompleted) break;
            if (!waited) onFirstWait?.Invoke();
            waited = true;
            clock.Advance(TimeSpan.FromSeconds(5));
            await Task.Delay(1, stop.Token);
        }
        return await running.WaitAsync(FakeDownloadServer.Guard);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public void The_pinned_pack_is_VT_0s_including_the_SHA_256_the_lead_pinned()
    {
        using var json = JsonDocument.Parse(File.ReadAllBytes(PinsJson()));
        var pin = json.RootElement.GetProperty("cuda").GetProperty("packs").GetProperty("Whisper.net.Runtime.Cuda12.Windows");
        var pack = CudaRuntimePack.Cuda12;
        Assert.Equal(pin.GetProperty("url").GetString(), pack.Url);
        Assert.Equal(pin.GetProperty("bytes").GetInt64(), pack.Bytes);
        Assert.Equal(pin.GetProperty("sha512").GetString(), pack.Sha512);
        Assert.Equal(pin.GetProperty("version").GetString(), pack.Version);
        Assert.Equal(pin.GetProperty("loaderFolder").GetString(), pack.LoaderFolder);
        Assert.Equal(pin.GetProperty("packFolder").GetString(), pack.PackFolder);
        Assert.Equal(pin.GetProperty("files").EnumerateArray().Select(f => (f.GetProperty("name").GetString()!, f.GetProperty("bytes").GetInt64())),
            pack.Files.Select(f => (f.Name, f.Bytes)));
        Assert.Equal(pin.GetProperty("sha256").GetString(), pack.Sha256);
        Assert.Matches("^[0-9a-f]{64}$", pack.Sha256!);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public async Task An_unpinned_pack_is_refused_before_any_connection_or_folder()
    {
        await using var rig = new DownloadRig();
        // A deliberately unpinned stand-in (the real pack now carries its SHA-256): everything else is the real pin, so a refusal cannot come from it.
        var unpinned = CudaRuntimePack.Cuda12 with { Sha256 = null };
        var (_, installer, root) = Make(rig, unpinned);

        var result = await installer.Install(unpinned);

        Assert.Equal(CudaInstallStatus.Unpinned, result.Status);
        Assert.Equal(0, rig.Counting.Sent);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public async Task The_pack_is_downloaded_verified_and_only_its_listed_files_are_unpacked_under_the_data_root()
    {
        await using var rig = new DownloadRig();
        var (nupkg, pin) = VoiceModelFixtures.Pack();
        rig.Server.Serve("/pack.nupkg", nupkg);
        var (_, installer, root) = Make(rig, pin);
        Assert.False(installer.IsInstalled(pin));

        var result = await installer.Install(pin);

        var folder = Path.Combine(root, "runtimes", "cuda12", "win-x64");
        Assert.Equal((CudaInstallStatus.Installed, folder), (result.Status, result.Folder));
        Assert.Equal(folder, installer.FolderFor(pin));
        Assert.Equal(VoiceModelFixtures.PackNames.Order(), Directory.GetFiles(folder).Select(Path.GetFileName).Order());
        for (var i = 0; i < VoiceModelFixtures.PackNames.Length; i++)
            Assert.Equal(FakeArtifact.Pattern(2_000 + i * 100, seed: 700 + i).Bytes, await File.ReadAllBytesAsync(Path.Combine(folder, VoiceModelFixtures.PackNames[i])));
        Assert.True(installer.IsInstalled(pin));
        Assert.False(File.Exists(Path.Combine(root, "evil.dll")));
        Assert.False(File.Exists(Path.Combine(root, "runtimes", "evil.dll")));
        Assert.Empty(Directory.GetFiles(rig.PartFolder));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(root, "runtimes"), "*.nupkg*", SearchOption.AllDirectories)); // the 250 MB archive is not kept
        Assert.Empty(Directory.GetDirectories(Path.Combine(root, "runtimes"), "*tmp*"));
        Assert.Single(await rig.Server.WaitForRequests(1));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public async Task An_installed_pack_is_not_downloaded_again_and_Remove_deletes_it()
    {
        await using var rig = new DownloadRig();
        var (nupkg, pin) = VoiceModelFixtures.Pack();
        rig.Server.Serve("/pack.nupkg", nupkg);
        var (_, installer, _) = Make(rig, pin);
        await installer.Install(pin);

        var again = await installer.Install(pin);

        Assert.Equal(CudaInstallStatus.Installed, again.Status);
        Assert.Equal(1, rig.Counting.Sent);
        Assert.True(installer.Remove(pin));
        Assert.False(installer.IsInstalled(pin));
        Assert.False(Directory.Exists(installer.FolderFor(pin)));
        Assert.False(installer.Remove(pin));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public async Task A_half_installed_folder_is_replaced_by_a_clean_install()
    {
        await using var rig = new DownloadRig();
        var (nupkg, pin) = VoiceModelFixtures.Pack();
        rig.Server.Serve("/pack.nupkg", nupkg);
        var (_, installer, _) = Make(rig, pin);
        Directory.CreateDirectory(installer.FolderFor(pin));
        await File.WriteAllTextAsync(Path.Combine(installer.FolderFor(pin), "whisper.dll"), "half");
        await File.WriteAllTextAsync(Path.Combine(installer.FolderFor(pin), "leftover.dll"), "old");
        Assert.False(installer.IsInstalled(pin));

        var result = await installer.Install(pin);

        Assert.Equal(CudaInstallStatus.Installed, result.Status);
        Assert.False(File.Exists(Path.Combine(installer.FolderFor(pin), "leftover.dll")));
        Assert.True(installer.IsInstalled(pin));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public async Task A_wrong_SHA_512_is_Corrupt_and_nothing_is_unpacked()
    {
        await using var rig = new DownloadRig();
        var (nupkg, pin) = VoiceModelFixtures.Pack(sha512: _ => new string('0', 128));
        rig.Server.Serve("/pack.nupkg", nupkg);
        var (_, installer, root) = Make(rig, pin);

        var result = await installer.Install(pin);

        Assert.Equal(CudaInstallStatus.Corrupt, result.Status);
        Assert.False(Directory.Exists(installer.FolderFor(pin)));
        Assert.DoesNotContain(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories), f => !f.Contains("downloads", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public async Task A_file_of_the_wrong_size_or_a_missing_file_is_Corrupt_and_leaves_no_folder()
    {
        await using var rig = new DownloadRig();
        var (wrongSize, pinA) = VoiceModelFixtures.Pack(e => e[3] = (e[3].Path, new byte[10]));
        rig.Server.Serve("/pack.nupkg", wrongSize);
        var (_, installerA, _) = Make(rig, pinA);
        Assert.Equal(CudaInstallStatus.Corrupt, (await installerA.Install(pinA)).Status);
        Assert.False(Directory.Exists(installerA.FolderFor(pinA)));

        await using var rig2 = new DownloadRig();
        var (missing, pinB) = VoiceModelFixtures.Pack(e => e.RemoveAll(x => x.Path.EndsWith("whisper.dll", StringComparison.Ordinal) && !x.Path.Contains("ggml", StringComparison.Ordinal)));
        rig2.Server.Serve("/pack.nupkg", missing);
        var (_, installerB, _) = Make(rig2, pinB);
        Assert.Equal(CudaInstallStatus.Corrupt, (await installerB.Install(pinB)).Status);
        Assert.False(Directory.Exists(installerB.FolderFor(pinB)));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public async Task Wrong_bytes_on_the_wire_or_a_404_fail_the_download_and_install_nothing()
    {
        await using var rig = new DownloadRig();
        var (nupkg, pin) = VoiceModelFixtures.Pack();
        rig.Server.Serve("/pack.nupkg", nupkg, new FakeBehavior { WrongByteAt = 100 });
        var (_, installer, _) = Make(rig, pin);
        var corrupt = await installer.Install(pin);
        Assert.Equal((CudaInstallStatus.Corrupt, DownloadFailureKind.Mismatch), (corrupt.Status, corrupt.Failure!.Kind));

        rig.Server.Behave("/pack.nupkg", new FakeBehavior { Status = 404 });
        var gone = await installer.Install(pin);
        Assert.Equal((CudaInstallStatus.Failed, DownloadFailureKind.NotFound), (gone.Status, gone.Failure!.Kind));
        Assert.False(installer.IsInstalled(pin));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public async Task Cancelling_the_install_keeps_the_partial_and_installs_nothing()
    {
        await using var rig = new DownloadRig();
        var (nupkg, pin) = VoiceModelFixtures.Pack();
        rig.Server.Serve("/pack.nupkg", nupkg, new FakeBehavior { Trickle = new FakeTrickle(1_000, TimeSpan.FromMinutes(1)) });
        var (_, installer, _) = Make(rig, pin);
        using var cancel = new CancellationTokenSource();
        var running = installer.Install(pin, cancel.Token);
        await DownloadRig.Until(() => installer.Job?.BytesReceived >= 1_000);

        cancel.Cancel();
        var result = await running.WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal(CudaInstallStatus.Cancelled, result.Status);
        Assert.False(installer.IsInstalled(pin));
        Assert.Equal(1_000, new FileInfo(Path.Combine(rig.PartFolder, nupkg.Sha256Hex[..8] + ".part")).Length);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public void The_pinned_pack_url_is_on_the_packs_first_url_list()
    {
        Assert.Equal(["api.nuget.org"], CudaPackInstaller.AllowedHosts);
        Assert.True(new DownloadPolicy(CudaPackInstaller.AllowedHosts).AllowsFirstUrl(new Uri(CudaRuntimePack.Cuda12.Url)));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public async Task An_old_folder_a_handle_holds_is_left_whole_and_the_install_fails_without_throwing()
    {
        // WINDOWS-ONLY in effect (ED-305 (9)): Windows refuses to rename or empty a folder while a handle is open inside it (a loaded DLL, a
        // scanner); elsewhere the swap succeeds and the install ends Installed.
        await using var rig = new DownloadRig();
        var (nupkg, pin) = VoiceModelFixtures.Pack();
        rig.Server.Serve("/pack.nupkg", nupkg);
        var clock = new ManualTimeProvider();
        var (_, installer, root) = Make(rig, pin, clock);
        var folder = installer.FolderFor(pin);
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "whisper.dll"), "half");
        await File.WriteAllTextAsync(Path.Combine(folder, "leftover.dll"), "old");

        CudaInstallResult result;
        using (new FileStream(Path.Combine(folder, "leftover.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
            result = await InstallPumping(installer, pin, clock);

        var siblings = Directory.GetDirectories(Path.GetDirectoryName(folder)!).Select(Path.GetFileName);
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal((CudaInstallStatus.Failed, DownloadFailureKind.DiskError), (result.Status, result.Failure?.Kind));
            Assert.Equal("half", await File.ReadAllTextAsync(Path.Combine(folder, "whisper.dll"))); // nothing half-deleted
            Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(folder, "leftover.dll")));
        }
        else Assert.Equal(CudaInstallStatus.Installed, result.Status);
        Assert.Equal(["win-x64"], siblings); // no temp or set-aside folder is left
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(root, "runtimes"), "*.nupkg*", SearchOption.AllDirectories));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public async Task A_folder_held_for_a_moment_is_waited_for_on_the_clock_and_then_replaced()
    {
        // Windows review (ED-305 (9)): a scanner looks at fresh files for a moment; the swap tries again after a wait instead of failing.
        await using var rig = new DownloadRig();
        var (nupkg, pin) = VoiceModelFixtures.Pack();
        rig.Server.Serve("/pack.nupkg", nupkg);
        var clock = new ManualTimeProvider();
        var (_, installer, _) = Make(rig, pin, clock);
        var folder = installer.FolderFor(pin);
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "whisper.dll"), "half");
        var holder = new FileStream(Path.Combine(folder, "whisper.dll"), FileMode.Open, FileAccess.Read, FileShare.Read);

        CudaInstallResult result;
        try
        {
            result = await InstallPumping(installer, pin, clock, onFirstWait: holder.Dispose);
        }
        finally
        {
            await holder.DisposeAsync();
        }

        Assert.Equal(CudaInstallStatus.Installed, result.Status);
        Assert.True(installer.IsInstalled(pin));
        Assert.Equal(["win-x64"], Directory.GetDirectories(Path.GetDirectoryName(folder)!).Select(Path.GetFileName));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "VT-7")]
    public async Task Remove_of_a_pack_a_handle_holds_deletes_nothing_and_says_so()
    {
        // WINDOWS-ONLY in effect (ED-305 (9)): a loaded runtime cannot be deleted on Windows; Remove leaves the whole pack rather than half of it.
        await using var rig = new DownloadRig();
        var (nupkg, pin) = VoiceModelFixtures.Pack();
        rig.Server.Serve("/pack.nupkg", nupkg);
        var (_, installer, _) = Make(rig, pin);
        Assert.Equal(CudaInstallStatus.Installed, (await installer.Install(pin)).Status);

        bool removed;
        using (new FileStream(Path.Combine(installer.FolderFor(pin), "whisper.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
            removed = installer.Remove(pin);

        if (OperatingSystem.IsWindows())
        {
            Assert.False(removed);
            Assert.True(installer.IsInstalled(pin));
            Assert.True(installer.Remove(pin));
        }
        Assert.False(Directory.Exists(installer.FolderFor(pin)));
        Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(installer.FolderFor(pin))!));
    }
}
