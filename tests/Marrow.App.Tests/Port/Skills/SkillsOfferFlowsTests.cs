using Marrow.App.Port.Skills;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.Detect;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Services.Port.Skills.Fetch;
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Port.Skills.Offers;

namespace Marrow.App.Tests.Port.Skills;

// PORT-ONLY (no Swift twin): SK-14 (DL-18, DL-19): an offer built for a chat is wired to the app's one host: its downloads are the host's shared ones (SkillsHost.OfferDownloads), its state is the pipeline's, and a decline is
// written to state.json before the provider is answered. Fresh data roots under MARROW_PORT_DATA_DIR. Since pin run 3 (ED-NEW-dl-11) LibreOffice (T3) is fetchable, so the host here
// holds the release catalogue with T3's installer parameters taken out: the offered item cannot be fetched, no download can start and nothing reaches a network.
public sealed class SkillsOfferFlowsTests
{
    private static SkillsHostOptions Unfetchable(string root)
    {
        var real = SkillsCatalogueLoader.LoadEmbedded();
        var catalogue = real with { Items = real.Items.Select(i => i.Id == "T3" ? i with { Install = null } : i).ToList() };
        Assert.False(CatalogueInstallPlans.CanPlan(catalogue.Items.Single(i => i.Id == "T3")));
        return SkillsHostOptions.Production(catalogue).WithDownloads(root);
    }

    private static string FreshRoot()
    {
        var parent = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        Assert.False(string.IsNullOrWhiteSpace(parent), "MARROW_PORT_DATA_DIR must name a disposable folder");
        var root = Path.Combine(parent!, "sk14 flows " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    [Trait("Decision", "ED-326")]
    public void Accepting_goes_through_the_hosts_shared_downloads_and_an_item_that_cannot_be_fetched_fails_with_the_one_unavailable_message()
    {
        var root = FreshRoot();
        try
        {
            using var host = SkillsHost.Open(root, Unfetchable(root));
            using var r = new SkillsOfferRig(runtime => SkillsOfferFlows.Create(runtime, host));
            r.Turn.Send("fixture");
            r.Offer();
            Assert.NotNull(r.Flow.Card);
            r.Run(r.Flow.Accept);
            Assert.Equal((OfferStage.Failed, LibraryWords.DownloadsUnavailable), (r.Flow.Stage, r.Flow.Failure));
            Assert.Null(host.Downloads.Running("T3"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    [Trait("Decision", "ED-326")]
    public void A_decline_is_written_to_the_pipelines_state_for_the_chat()
    {
        var root = FreshRoot();
        try
        {
            using var host = SkillsHost.Open(root, Unfetchable(root));
            using var r = new SkillsOfferRig(runtime => SkillsOfferFlows.Create(runtime, host));
            r.Turn.Send("fixture");
            r.Offer();
            r.Main(r.Flow.Decline);
            var declined = Assert.Single(host.Pipeline.State.Declined);
            Assert.Equal(r.Turn.Runtime.ThreadID.ToString(), declined.ChatId);
            Assert.Contains("T3", declined.ItemIds);
            Assert.Equal(OfferStage.Declined, r.Flow.Stage);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
