// PORT-ONLY: SK-13 / DL-15 (skills-ONDEMAND-PLAN.md section 7 and 2.5). The terms step of an item whose terms class is not open: what is offered, what is stored when the user accepts,
// and when it asks again. Marrow-made fixtures only; no vendor text, no network (the licence file comes through a scripted seam).
using System.Security.Cryptography;
using System.Text;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Terms;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.Skills.Terms;

[Trait("Feature", "SK-13")]
[Trait("Spec", "DL-15")]
public sealed class TermsStepTests
{
    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void A_non_open_item_is_offered_with_Under_its_vendors_terms_and_the_licence_hash_the_catalogue_holds()
    {
        var rig = new TermsRig("Anthropic");
        var offer = rig.Step().Offer("A2");
        Assert.NotNull(offer);
        Assert.Equal("A2", offer.ItemId);
        Assert.Equal("Under Anthropic's terms", offer.Label);
        Assert.Equal(ItemTerms.LicenceHash(rig.Item), offer.LicenceSha256);
        Assert.Equal("Proprietary", offer.LicenceName);
        Assert.Equal(rig.Item.LicenceUrl, offer.LicenceUrl);
        Assert.Equal("I have read these terms", offer.ReadControl);
        Assert.True(offer.CanAccept);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void An_OpenAI_item_reads_Under_OpenAIs_terms()
    {
        Assert.Equal("Under OpenAI's terms", new TermsRig("OpenAI").Step().Offer("A2")!.Label);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void An_open_item_has_no_terms_step()
    {
        var rig = new TermsRig(terms: TermsClass.Open);
        Assert.Null(rig.Step().Offer("A2"));
        Assert.Throws<InvalidOperationException>(() => rig.Step().Accept("A2", readControlChecked: true));
        Assert.Empty(rig.Accepted);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void Accepting_without_the_read_control_stores_nothing_and_the_step_stays_owed()
    {
        var rig = new TermsRig();
        var step = rig.Step();
        Assert.Throws<InvalidOperationException>(() => step.Accept("A2", readControlChecked: false));
        Assert.Empty(rig.Accepted);
        Assert.Empty(rig.State.AcceptedTerms);
        Assert.NotNull(step.Offer("A2"));
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void Accepting_stores_the_date_the_item_and_the_licence_hash_and_the_step_is_done()
    {
        var rig = new TermsRig();
        var step = rig.Step();
        step.Accept("A2", readControlChecked: true);
        var stored = Assert.Single(rig.State.AcceptedTerms);
        Assert.Equal(ItemTerms.LicenceHash(rig.Item), stored.LicenceSha256);
        Assert.Equal("A2", stored.ItemId);
        Assert.Equal(TermsRig.At, stored.AcceptedAt);
        Assert.Null(step.Offer("A2"));
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void A_changed_licence_hash_asks_again_even_though_an_earlier_text_was_accepted()
    {
        var first = new TermsRig();
        first.Step().Accept("A2", readControlChecked: true);
        var changed = new TermsRig(licence: "A later text of the Marrow-made fixture terms.\n") { State = first.State };
        Assert.NotEqual(ItemTerms.LicenceHash(first.Item), ItemTerms.LicenceHash(changed.Item));
        Assert.NotNull(changed.Step().Offer("A2"));
        Assert.True(ItemTerms.Required(changed.Item, changed.State));
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void A_licence_whose_hash_the_catalogue_does_not_hold_is_owed_but_cannot_be_accepted()
    {
        var rig = new TermsRig();
        var unpinned = rig.Item with { LicenceFile = null, Files = [] };
        var step = new TermsStep(new SkillsCatalogue { Schema = 1, ReadOn = "2026-10-05", Items = [unpinned] }, () => rig.State, (_, _) => throw new InvalidOperationException("must not store"), rig.Text);
        var offer = step.Offer("A2");
        Assert.NotNull(offer);
        Assert.False(offer.CanAccept);
        Assert.Null(offer.LicenceSha256);
        Assert.Throws<InvalidOperationException>(() => step.Accept("A2", readControlChecked: true));
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public async Task The_licence_text_is_the_pinned_file_decoded_after_its_size_and_hash_match()
    {
        var rig = new TermsRig();
        Assert.Equal(TermsRig.LicenceBody, await rig.Step().LicenceTextAsync("A2"));
        Assert.Equal(1, rig.Text.Reads);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_licence_text_that_does_not_match_its_size_or_hash_is_refused()
    {
        var rig = new TermsRig();
        rig.Text.Bytes = Encoding.UTF8.GetBytes("Marrow-made fixture terms. Not a vendor text!\n");
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Step().LicenceTextAsync("A2"));
        rig.Text.Bytes = Encoding.UTF8.GetBytes("longer than the pinned size by a few bytes, though its hash is not even looked at");
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Step().LicenceTextAsync("A2"));
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public async Task The_licence_text_seam_fails_closed_when_it_is_not_bound()
    {
        var rig = new TermsRig();
        var step = new TermsStep(rig.Catalogue, () => rig.State, (_, _) => { });
        await Assert.ThrowsAsync<InvalidOperationException>(() => step.LicenceTextAsync("A2"));
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_licence_that_is_not_text_is_refused()
    {
        var rig = new TermsRig();
        var binary = new byte[] { 0xFF, 0xFE, 0x00, 0x80 };
        var item = rig.Item with { Files = [new CatalogueFile { Path = "LICENSE.txt", Size = binary.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(binary)) }] };
        rig.Text.Bytes = binary;
        var step = new TermsStep(new SkillsCatalogue { Schema = 1, ReadOn = "2026-10-05", Items = [item] }, () => rig.State, (_, _) => { }, rig.Text);
        await Assert.ThrowsAsync<InvalidDataException>(() => step.LicenceTextAsync("A2"));
    }
}
