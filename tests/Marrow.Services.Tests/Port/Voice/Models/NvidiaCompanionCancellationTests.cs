using Marrow.Services.Tests.Fixtures;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using Marrow.Services.Port.Downloads;
using Marrow.Services.Port.Voice.Models;
namespace Marrow.Services.Tests.Port.Voice.Models;

[Trait("Decision", "ED-351")]
public sealed class NvidiaCompanionCancellationTests
{
    [Fact]
    public async Task Cancel_in_flight_publishes_no_DLLs_and_retry_finishes_without_network()
    {
        var root = Path.Combine(Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR")!, "CudaCancellation", Guid.NewGuid().ToString("N"));
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
            foreach (var name in new[] { "cublas64_12.dll", "cublasLt64_12.dll", "cudart64_12.dll" })
            { using var entry = zip.CreateEntry("fake/bin/" + name).Open(); entry.Write("test"u8); }
        var body = bytes.ToArray();
        var pin = new NvidiaCompanionArchive("fake", "https://developer.download.nvidia.com/fake.zip", body.Length, Convert.ToHexStringLower(SHA256.HashData(body)),
            ["fake/bin/cublas64_12.dll", "fake/bin/cublasLt64_12.dll", "fake/bin/cudart64_12.dll"]);
        using var handler = new NvidiaHeldHandler(body);
        using var cancel = new CancellationTokenSource();
        var installer = new NvidiaCompanionInstaller(root, new Downloader(Path.Combine(root, "parts"), new DownloadPolicy(["developer.download.nvidia.com"]), handler: handler), [pin]);
        var folder = Path.Combine(root, "runtimes/cuda12/win-x64");
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "whisper.dll"), "original pack");
            var running = installer.InstallAsync(folder, _ => { }, cancel.Token);
            await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancel.Cancel();
            Assert.Equal(NvidiaCompanionResult.Cancelled, await running.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(new[] { "whisper.dll" }, Directory.GetFiles(folder).Select(Path.GetFileName));
            handler.Hold = false;
            Assert.Equal(NvidiaCompanionResult.Installed, await installer.InstallAsync(folder, _ => { }, CancellationToken.None));
            Assert.True(installer.IsInstalled(folder));
            Assert.Equal("original pack", File.ReadAllText(Path.Combine(folder, "whisper.dll")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
