using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Window;
using Marrow.Core.Port;
using Marrow.Core.Port.SelfUpdate;
using Marrow.Core.Support;
using Marrow.Services.Port;
using Marrow.Services.Store;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;
using Xunit;

namespace Marrow.App.Tests.App;

// UE-1 (OD-0062 Velopack; OD-0100 the GitHub feed), Windows review (unit/integ-ue1): the whole chain on a LOOPBACK fake GitHub, with nothing faked
// between Marrow and the socket. Velopack's own GithubSource and its own HTTP downloader read a GitHub-shaped release list from a server on
// 127.0.0.1 (a GitHub address on another host than github.com is read at <host>/api/v3/, GitHub Enterprise's layout); the real UpdateChecker
// finds the offer, the real AppUpdater downloads it with percent progress, the real WindowsUpdateInstallBoundary stages, verifies and reads a
// real NthApp package (a zip with its nuspec), and "Restart Marrow" reaches the engine's apply step, a recorder here (Update.exe never starts).
[Collection("AppSettings")]
public sealed class VelopackLoopbackFeedTests
{
    private const string RelaunchVersionKey = "appUpdater_pendingRelaunchVersion";

    private static byte[] MarrowPackage(string id, string version)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var nuspec = new StreamWriter(zip.CreateEntry(id + ".nuspec").Open()))
                nuspec.Write($"<?xml version=\"1.0\"?><package xmlns=\"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd\"><metadata><id>{id}</id><version>{version}</version><title>Marrow</title></metadata></package>");
            using (var file = new StreamWriter(zip.CreateEntry("lib/app/Marrow.App.exe").Open()))
                file.Write(string.Concat(Enumerable.Repeat("not a real executable ", 5000)));
        }
        return memory.ToArray();
    }

    // GitHub's release list and the assets vpk upload github leaves: releases.win.json and the full package
    private static Func<ReceivedRequest, CannedResponse?> GitHub(Func<string, string> url, string version, byte[] package)
    {
        var file = $"NthApp-{version}-full.nupkg";
        var feed = JsonSerializer.Serialize(new
        {
            Assets = new[]
            {
                new
                {
                    PackageId = "NthApp", Version = version, Type = "Full", FileName = file,
                    SHA1 = Convert.ToHexString(SHA1.HashData(package)), SHA256 = Convert.ToHexString(SHA256.HashData(package)),
                    Size = package.Length, NotesMarkdown = "## " + version, NotesHTML = "<h2>" + version + "</h2>",
                },
            },
        });
        string Release() => JsonSerializer.Serialize(new[]
        {
            new
            {
                name = "Marrow " + version, prerelease = false, published_at = "2026-10-04T10:00:00Z",
                assets = new[]
                {
                    new { name = "releases.win.json", url = url("/api/v3/assets/1"), browser_download_url = url("/dl/releases.win.json") },
                    new { name = file, url = url("/api/v3/assets/2"), browser_download_url = url("/dl/" + file) },
                },
            },
        });
        return request =>
        {
            var path = request.Target.Split('?')[0];
            if (path == "/api/v3/repos/Oak993/marrow-releases/releases") return CannedResponse.Json(Release());
            if (path is "/dl/releases.win.json" or "/api/v3/assets/1") return CannedResponse.Json(feed);
            if (path == "/dl/" + file || path == "/api/v3/assets/2")
                return new CannedResponse(200, "OK", package, [new("Content-Type", "application/octet-stream")]);
            return CannedResponse.Plain(404, "Not Found");
        };
    }

    [Fact]
    [Trait("Decision", "ED-235")]
    public void A_check_a_download_with_percent_and_restart_run_through_velopack_on_a_loopback_github()
    {
        using var scope = AppSettingsScope.Create();
        using var release = AppInfo.Running.Override(AppInfo.FromAssembly(typeof(VelopackLoopbackFeedTests).Assembly, isDevelopment: false));
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        using var storage = Storage.Locations.Override(StorageLocations.Create(Path.Combine(scope.Folder, "root"), Path.Combine(scope.Folder, "home")));
        var manager = scope.Runtime.OnMain(() => new WindowManager());
        using var windows = WindowManager.OverrideShared(manager);
        var closed = 0;
        manager.MainWindowClose = () => closed++;

        var package = MarrowPackage("NthApp", "1.8.0");
        LoopbackFeedServer server = null!;
        server = new LoopbackFeedServer(request => GitHub(server.Url, "1.8.0", package)(request));
        using var _ = server;
        var packages = Directory.CreateTempSubdirectory("ue1-loopback-packages-").FullName;
        var root = Directory.CreateTempSubdirectory("ue1-loopback-root-").FullName;
        var locator = new TestVelopackLocator("NthApp", "1.7.0", packages, root, root, Path.Combine(root, "Update.exe"), "win", null, null, "Marrow.App.exe");
        var applied = new List<VelopackAsset>();
        // Velopack's own downloader (HttpClientFileDownloader, a real HTTP client) kept on 127.0.0.1 (LoopbackPortDownloader puts back the port GithubSource drops)
        var downloader = new LoopbackPortDownloader(server.Port);
        var engine = new VelopackSelfUpdateEngine(new GithubSource(server.Url("/Oak993/marrow-releases"), accessToken: null, prerelease: false, downloader), locator, (_, asset) => applied.Add(asset));

        // the check: UpdateChecker over the engine finds 1.8.0
        var checker = scope.Runtime.OnMain(() => new UpdateChecker(AppInfo.Running.Current, [], MarrowReleaseFeed.Url, engine));
        var check = scope.Runtime.OnMain(() => checker.Check());
        scope.Runtime.Loop.RunUntil(() => check.IsCompleted, TimeSpan.FromSeconds(20));
        Assert.True(check.IsCompletedSuccessfully && scope.Runtime.OnMain(() => checker.UpdateAvailable), "the check offered nothing: " + scope.Runtime.OnMain(() => checker.LastError));
        var update = scope.Runtime.OnMain(() => checker.Update)!;
        Assert.Equal(("1.8.0", "v1.8.0", (long?)package.Length, "## 1.8.0"), (update.Version, update.Tag, update.Size, update.Notes));

        // the install: the real boundary over the package Velopack wrote; the pill moves through the download to the hand-off
        scope.OnMain(() => new AppUpdater(new WindowsUpdateInstallBoundary(), engine).Install(update));
        scope.Runtime.Loop.RunUntil(() => UpdateInstallProgress.Shared.UpdateInstallProgressPhase == UpdateInstallProgress.Phase.Handoff, TimeSpan.FromSeconds(20));
        scope.Runtime.Loop.RunUntilIdle();
        Assert.Equal(UpdateInstallProgress.Phase.Handoff, scope.Runtime.OnMain(() => UpdateInstallProgress.Shared.UpdateInstallProgressPhase));
        var asset = Assert.Single(applied);
        Assert.Equal("NthApp-1.8.0-full.nupkg", asset.FileName);
        Assert.Equal(package, File.ReadAllBytes(Path.Combine(packages, asset.FileName)));
        Assert.NotEmpty(downloader.Percents);                                                  // Velopack's own percent, which the pill takes (SelfUpdateProgress)
        Assert.Equal(100, downloader.Percents[^1]);      // Velopack's package, kept where Update.exe applies it from
        Assert.Equal("1.8.0", scope.Defaults.Object(RelaunchVersionKey));
        scope.Runtime.Advance(TimeSpan.FromMilliseconds(400));
        Assert.Equal(1, closed);

        // every request went to the loopback server, to GitHub's API layout and the asset addresses it gave
        var targets = server.Requests.Select(r => r.Target.Split('?')[0]).ToArray();
        Assert.Contains("/api/v3/repos/Oak993/marrow-releases/releases", targets);
        Assert.Contains(targets, t => t.EndsWith("NthApp-1.8.0-full.nupkg", StringComparison.Ordinal) || t == "/api/v3/assets/2");
        Assert.Empty(server.Refused);
    }
}
