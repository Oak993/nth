using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Downloads;
using Marrow.Services.Port.Skills.Install;
using Marrow.Services.Port.Skills.Offers;
using Marrow.Services.Port.Skills.Pipelines;
using Marrow.Services.Tests.Fixtures;
namespace Marrow.Services.Tests.Port.Skills.Offers;
public sealed class SkillsOfferDownloadTests
{
    [Fact, Trait("Feature", "SK-12"), Trait("Rule", "R-SKILLS-12")]
    public async Task Unpinned_item_cannot_reach_a_download_plan_or_installer()
    {
        using var handler = new OfferHttpForbidden();
        var downloader = new Downloader("/offline", new DownloadPolicy([]), handler: handler);
        var source = new PipelineDownloadSource(downloader, (_, _) => throw new InvalidOperationException("No plan may be requested."), _ => throw new InvalidOperationException("No download may be requested."));
        var bridge = new SkillsOfferDownload(source, new ItemInstaller(), (_, _) => throw new InvalidOperationException("No publication may occur."));
        var item = SkillsCatalogueLoader.LoadEmbedded().Items.Single(i => i.Id == "T3") with { PinState = PinState.Unpinned };
        await Assert.ThrowsAsync<InvalidDataException>(() => bridge.Install([item], _ => { }, CancellationToken.None));
    }
    [Fact, Trait("Feature", "SK-12"), Trait("Rule", "R-SKILLS-12")]
    public async Task Cancelled_offer_cannot_reach_a_download_plan_or_installer()
    {
        using var handler = new OfferHttpForbidden();
        var downloader = new Downloader("/offline", new DownloadPolicy([]), handler: handler);
        var source = new PipelineDownloadSource(downloader, (_, _) => throw new InvalidOperationException("No plan may be requested."), _ => throw new InvalidOperationException("No download may be requested."));
        var bridge = new SkillsOfferDownload(source, new ItemInstaller(), (_, _) => throw new InvalidOperationException("No publication may occur."));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bridge.Install([SkillsCatalogueLoader.LoadEmbedded().Items.Single(i => i.Id == "T3")], _ => { }, cancellation.Token));
    }

    [Fact, Trait("Feature", "SK-12"), Trait("Rule", "R-SKILLS-12")]
    public async Task Checked_download_is_installed_and_published_before_the_offer_can_resume()
    {
        using var r = new OfferInstallRig(); var progress = new List<OfferProgress>();
        var bridge = new SkillsOfferDownload(r.Source, r.Installer, r.Publish);
        var result = await bridge.Install([r.Item], p => { lock (progress) progress.Add(p); }, CancellationToken.None);
        Assert.Equal(1, r.Publications); var folder = result[r.Item.Id]; Assert.True(r.Lock.IsLocked(folder));
        Assert.Equal("fixture skill instructions", File.ReadAllText(Path.Combine(folder, "fixture", "SKILL.md")));
        Assert.Contains(progress, p => p.Stage == OfferStage.Downloading && p.BytesDone > 0);
        Assert.Contains(progress, p => p.Stage == OfferStage.Checking);
    }

    [Fact, Trait("Feature", "SK-12"), Trait("Rule", "R-SKILLS-12")]
    public async Task Failed_routing_publication_never_returns_an_installed_path()
    {
        using var r = new OfferInstallRig();
        var bridge = new SkillsOfferDownload(r.Source, r.Installer, (_, _) => throw new IOException("Fixture publication failed."));
        await Assert.ThrowsAsync<IOException>(() => bridge.Install([r.Item], _ => { }, CancellationToken.None));
        Assert.Equal(0, r.Publications);
    }
}
