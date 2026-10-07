using Marrow.Services.Tests.Fixtures;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Marrow.Services.Port.Downloads;
using Marrow.Services.Port.Voice.Models;

namespace Marrow.Services.Tests.Port.Voice.Models;

// OD-0104 / RR-10: tiny in-memory archives, never a connection or a native library.
[Trait("Decision", "ED-351")]
public sealed class NvidiaCompanionInstallerTests : IDisposable
{
    private readonly string root = Path.Combine(Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR") ?? throw new InvalidOperationException("Throwaway data root required"), "NvidiaCompanions", Guid.NewGuid().ToString("N"));

    private static (byte[] Bytes, NvidiaCompanionArchive Pin) Archive(string id, params string[] names)
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        {
            foreach (var name in names)
            {
                using var stream = zip.CreateEntry(id + "/bin/" + name).Open();
                stream.Write(System.Text.Encoding.UTF8.GetBytes("fake DLL " + name));
            }
            using var ignored = zip.CreateEntry("../../outside.dll").Open();
            ignored.Write("ignored"u8);
        }
        var body = bytes.ToArray();
        return (body, new NvidiaCompanionArchive(id, "https://developer.download.nvidia.com/" + id + ".zip", body.Length,
            Convert.ToHexStringLower(SHA256.HashData(body)), names.Select(n => id + "/bin/" + n).ToArray()));
    }

    private (NvidiaCompanionInstaller Installer, NvidiaMemoryHandler Handler, string Folder) Make(bool corrupt = false, bool missing = false)
    {
        var (blas, bpin) = Archive("blas", "cublas64_12.dll", "cublasLt64_12.dll");
        var (runtime, rpin) = Archive("runtime", "cudart64_12.dll");
        if (corrupt) runtime[^1] ^= 1;
        if (missing) rpin = rpin with { Entries = ["runtime/bin/missing.dll"] };
        var handler = new NvidiaMemoryHandler(new() { ["/blas.zip"] = blas, ["/runtime.zip"] = runtime });
        var downloader = new Downloader(Path.Combine(root, "partials"), new DownloadPolicy(["developer.download.nvidia.com"]), handler: handler);
        return (new NvidiaCompanionInstaller(root, downloader, [bpin, rpin]), handler, Path.Combine(root, "runtimes", "cuda12", "win-x64"));
    }

    [Fact]
    public void Production_archives_match_the_existing_VT0_pins()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Marrow.Services/Port/Voice/Assets/voice-pins.json"));
        using var json = JsonDocument.Parse(File.ReadAllBytes(path));
        var pins = json.RootElement.GetProperty("cuda").GetProperty("nvidiaCompanions");
        foreach (var (key, archive) in new[] { ("libcublas", NvidiaCompanionArchive.Cublas), ("cuda_cudart", NvidiaCompanionArchive.Cudart) })
        {
            var source = pins.GetProperty(key);
            Assert.Equal(source.GetProperty("url").GetString(), archive.Url);
            Assert.Equal(source.GetProperty("bytes").GetInt64(), archive.Bytes);
            Assert.Equal(source.GetProperty("sha256").GetString(), archive.Sha256);
        }
        Assert.Equal(new[] { "cublas64_12.dll", "cublasLt64_12.dll", "cudart64_12.dll" }, NvidiaCompanionArchive.All.SelectMany(p => p.Entries).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Verified_archives_publish_all_three_libraries_together_and_keep_the_pack()
    {
        var (installer, handler, folder) = Make();
        using (handler)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "whisper.dll"), "pack");
            var progress = new List<double>();
            Assert.False(installer.IsInstalled(folder));
            Assert.Equal(NvidiaCompanionResult.Installed, await installer.InstallAsync(folder, progress.Add, CancellationToken.None));
            Assert.True(installer.IsInstalled(folder));
            Assert.Equal("pack", File.ReadAllText(Path.Combine(folder, "whisper.dll")));
            Assert.Equal(2, handler.Requests);
            Assert.Equal(1, progress[^1]);
            Assert.True(progress.SequenceEqual(progress.Order()));
            Assert.False(File.Exists(Path.Combine(root, "outside.dll")));
            Assert.Equal(NvidiaCompanionResult.Installed, await installer.InstallAsync(folder, _ => { }, CancellationToken.None));
            Assert.Equal(2, handler.Requests);
            File.WriteAllText(Path.Combine(folder, "cudart64_12.dll"), "corrupt");
            Assert.False(installer.IsInstalled(folder));
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Corrupt_or_missing_library_never_publishes_any_companion(bool corrupt, bool missing)
    {
        var (installer, handler, folder) = Make(corrupt, missing);
        using (handler)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "whisper.dll"), "unchanged");
            Assert.Equal(NvidiaCompanionResult.Failed, await installer.InstallAsync(folder, _ => { }, CancellationToken.None));
            Assert.False(installer.IsInstalled(folder));
            Assert.Equal(new[] { "whisper.dll" }, Directory.GetFiles(folder).Select(Path.GetFileName));
            Assert.Equal("unchanged", File.ReadAllText(Path.Combine(folder, "whisper.dll")));
        }
    }

    [Fact]
    public async Task Cancel_before_start_fetches_nothing_and_leaves_the_pack_untouched()
    {
        var (installer, handler, folder) = Make();
        using (handler)
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel();
            Assert.Equal(NvidiaCompanionResult.Cancelled, await installer.InstallAsync(folder, _ => { }, cancel.Token));
            Assert.Equal(0, handler.Requests);
            Assert.False(Directory.Exists(folder));
        }
    }

    [Fact]
    public async Task A_receipt_for_different_archive_pins_cannot_skip_a_new_install()
    {
        var (installer, handler, folder) = Make();
        using (handler)
        {
            Assert.Equal(NvidiaCompanionResult.Installed, await installer.InstallAsync(folder, _ => { }, CancellationToken.None));
            var (_, pin) = Archive("runtime", "cudart64_12.dll");
            var changed = pin with { Id = "new-release", Sha256 = new string('a', 64) };
            var next = new NvidiaCompanionInstaller(root, new Downloader(Path.Combine(root, "parts"), new DownloadPolicy(["developer.download.nvidia.com"]), handler: handler), [changed]);
            Assert.False(next.IsInstalled(folder));
        }
    }

    [Fact]
    public async Task Insufficient_staging_space_leaves_the_pack_and_publishes_no_companions()
    {
        var (body, pin) = Archive("runtime", "cudart64_12.dll");
        using var handler = new NvidiaMemoryHandler(new() { ["/runtime.zip"] = body });
        var downloader = new Downloader(Path.Combine(root, "parts"), new DownloadPolicy(["developer.download.nvidia.com"]), handler: handler);
        var installer = new NvidiaCompanionInstaller(root, downloader, [pin], _ => 0);
        var folder = Path.Combine(root, "cuda");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "whisper.dll"), "unchanged");
        Assert.Equal(NvidiaCompanionResult.Failed, await installer.InstallAsync(folder, _ => { }, CancellationToken.None));
        Assert.Equal(new[] { "whisper.dll" }, Directory.GetFiles(folder).Select(Path.GetFileName));
        Assert.False(installer.IsInstalled(folder));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
