using Marrow.Services.Port.Downloads;
using Marrow.Services.Port.Skills.Install;
using Marrow.Services.Port.Skills.Pipelines;
using Marrow.Services.Tests.Port.Skills.Install;
using Marrow.Testing.Downloads;
namespace Marrow.Services.Tests.Port.Skills.Pipelines;
[Trait("Spec", "DL-8")]
public sealed class PipelineDownloadTests
{
    [Fact]
    [Trait("Feature", "SK-8")]
    [Trait("Rule", "R-TEST-10")]
    public async Task SK3_bridge_fetches_pinned_fixture_inputs_only_from_loopback_and_installs_offline()
    {
        using var rig = new InstallRig();
        await using var server = FakeDownloadServer.StartHttps();
        var plan = rig.Plan(InstallerKind.Zip);
        var archive = new FakeArtifact(File.ReadAllBytes(plan.LocalFiles["payload.zip"]));
        var licence = new FakeArtifact(File.ReadAllBytes(plan.LocalFiles["LICENSE"]));
        server.Serve("/payload.zip", archive).Serve("/LICENSE", licence);
        plan = plan with { Item = plan.Item with { Artifacts = plan.Item.Artifacts.Select(a => a with { Url = server.Url("/payload.zip").AbsoluteUri }).ToArray() } };
        foreach (var input in plan.LocalFiles.Values) File.Delete(input);
        using var handler = FakeDownloadTransport.CreateHandler(server);
        var downloader = new Downloader(Path.Combine(rig.Root, "parts"), new DownloadPolicy([server.EndPoint.Address.ToString()]),
            new DownloadOptions { RetryDelays = [], FreeSpace = _ => long.MaxValue }, handler);
        var requests = new[]
        {
            new DownloadRequest(server.Url("/payload.zip"), plan.LocalFiles["payload.zip"], archive.Length, archive.Sha256Hex),
            new DownloadRequest(server.Url("/LICENSE"), plan.LocalFiles["LICENSE"], licence.Length, licence.Sha256Hex),
        };
        var source = new PipelineDownloadSource(downloader, (_, _) => plan, _ => requests);
        var verified = await source.DownloadAsync(plan.Item, plan.Record, new PipelineProgress([]), default);
        var result = await new ItemInstaller(new PipelineProtection()).InstallAsync(verified);
        Assert.Equal("offline fixture", File.ReadAllText(Path.Combine(result.Path, "payload.txt")));
        Assert.Equal(2, server.Requests.Count);
        Assert.Empty(handler.Refusals);
    }

    [Fact]
    [Trait("Feature", "SK-8")]
    [Trait("Rule", "R-TEST-10")]
    public async Task SK3_bridge_without_a_bound_fake_fails_closed_and_produces_no_install()
    {
        using var rig = new InstallRig();
        var plan = rig.Plan(InstallerKind.Zip);
        using var handler = FakeDownloadTransport.CreateHandler();
        var downloader = new Downloader(Path.Combine(rig.Root, "parts"), DownloadPolicy.Catalogue(),
            new DownloadOptions { RetryDelays = [], FreeSpace = _ => long.MaxValue }, handler);
        var artifact = plan.Item.Artifacts.Single();
        var source = new PipelineDownloadSource(downloader, (_, _) => plan, _ =>
            [new DownloadRequest(new Uri(artifact.Url!), plan.LocalFiles[artifact.Name], artifact.Size!.Value, artifact.Sha256!)]);
        await Assert.ThrowsAsync<IOException>(() => source.DownloadAsync(plan.Item, plan.Record, new PipelineProgress([]), default));
        Assert.NotEmpty(handler.Refusals);
        Assert.False(Directory.Exists(Path.Combine(rig.Root, "addons", "tools")));
    }
}
