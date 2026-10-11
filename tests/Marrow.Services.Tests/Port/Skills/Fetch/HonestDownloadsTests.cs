// PORT-ONLY: ED-NEW-dlhonest-1. The installed release showed every tool and plugin as "Didn't download" with "Downloading isn't available in this build yet." after Download all ("0 of 16 downloaded"): the
// production seams were bound (so the page offered every download), but the shipped catalogue pins no item and CatalogueInstallPlans can plan no tool, so every press failed closed. A page and an offer
// now offer a download only for an item the bound seam can really fetch and install. Loopback fake only; no network, CLI or installer.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Fetch;
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Port.Skills.Pages;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Pipelines;

namespace Marrow.Services.Tests.Port.Skills.Fetch;

[Trait("Feature", "SK-16")]
[Trait("Spec", "DL-19")]
[Trait("Decision", "ED-NEW-dlhonest-1")]
[Collection("Dev data root env")]
public sealed class HonestDownloadsTests
{
    [Fact]
    public async Task The_production_seams_offer_no_download_their_install_plans_cannot_make()
    {
        await using var rig = new LoopbackRig();
        Assert.True(CatalogueInstallPlans.CanPlan(rig.Item));
        Assert.False(CatalogueInstallPlans.CanPlan(rig.Tool));
        Assert.Equal(PinState.Pinned, rig.Tool.PinState);
        Assert.True(rig.Host.Downloads.CanFetch(rig.Item));
        Assert.False(rig.Host.Downloads.CanFetch(rig.Tool)); // pinned, but no plan can install it: it would only fail closed after the press
        Assert.False(rig.Host.OfferDownloads.CanFetch(rig.Tool));
        var backend = new HostLibraryBackend(rig.Host, rig.Catalogue);
        Assert.True(backend.CanFetch);
        Assert.True(backend.CanFetchItem("L1"));
        Assert.False(backend.CanFetchItem("T1"));
        Assert.Empty(rig.Server.Requests);
    }

    [Theory]
    [InlineData(LibrarySection.Plugins)]
    [InlineData(LibrarySection.Skills)]
    public async Task With_the_shipped_catalogue_a_page_offers_and_starts_no_download_that_would_only_fail(LibrarySection section)
    {
        await using var rig = new LoopbackRig();
        var catalogue = SkillsCatalogueLoader.LoadEmbedded();
        var root = SkillsHostRig.FreshRoot();
        try
        {
            using var host = SkillsHost.Open(root, SkillsHostRig.Options(new PipelineProtection(), catalogue).WithDownloads(root, rig.Downloader(), rig.RawBase));
            var backend = new HostLibraryBackend(host, catalogue);
            using var controller = new LibraryController(section, backend);
            Assert.True(backend.CanFetch); // the seams are bound, as in the release
            var rows = controller.Rows;
            Assert.NotEmpty(rows);
            foreach (var row in rows)
                Assert.Equal(host.Downloads.CanFetch(catalogue.Items.Single(i => i.Id == row.Id)), controller.CanFetchItem(row.Id));
            var fetchable = rows.Where(r => !r.OnThisPc && !r.TermsOwed && r.SizeMb is not null && controller.CanFetchItem(r.Id)).Select(r => r.Id).ToList();
            Assert.Equal(fetchable, controller.Plan.ToDownload);
            Assert.Equal(fetchable.Count > 0, controller.CanDownloadAll);
            foreach (var row in rows.Where(r => !controller.CanFetchItem(r.Id)))
            {
                await controller.Download(row.Id);
                var after = controller.Rows.Single(r => r.Id == row.Id);
                Assert.Equal((ItemState.Available, null), (after.State, after.Failure)); // the release turned these into "Didn't download"
            }
            Assert.Empty(rig.Server.Requests);
        }
        finally { SkillsHostRig.Delete(root); }
    }
}
