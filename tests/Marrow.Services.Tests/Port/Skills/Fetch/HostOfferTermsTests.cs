// PORT-ONLY: first-release feature unit SK-14 (ONDEMAND 5.4: "Items with non-open terms add the terms step inside asked"; DL-15). The offer's terms seam over the host's terms step. No network.

namespace Marrow.Services.Tests.Port.Skills.Fetch;

[Trait("Feature", "SK-14")]
[Trait("Spec", "DL-18")]
[Trait("Rule", "R-TEST-9")]
[Collection("Dev data root env")]
public sealed class HostOfferTermsTests
{
    [Fact]
    public async Task The_offer_sees_the_terms_an_item_owes_and_an_acceptance_is_the_hosts_one_record()
    {
        using var rig = new FetchRig();
        var terms = rig.Host.OfferTerms;
        var offer = terms.Offer("N1")!;
        Assert.Equal("N1", offer.ItemId);
        Assert.Null(terms.Offer("S1")); // an open item owes none
        terms.Accept("N1");
        Assert.Null(terms.Offer("N1"));
        Assert.Single(rig.Host.Pipeline.State.AcceptedTerms);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task The_licence_text_is_the_hosts_terms_steps_and_fails_closed_while_no_licence_seam_is_bound()
    {
        using var rig = new FetchRig();
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Host.OfferTerms.LicenceTextAsync("N1", CancellationToken.None));
    }
}
