using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Core.Tests.Port.Skills.Catalogue;

namespace Marrow.Core.Tests.Port.Skills.State;

// Unit SK-7: "accepted terms keyed by licence hash". ONDEMAND-PLAN.md 2.5: "Items whose termsClass is not open (A2, O5) show the licence text before the
// download with a separate 'I have read these terms' control ... The acceptance is stored with the date and the licence file's hash, so a later pin
// with changed terms asks again"; 3.7: "A changed licence text in a non-open item asks for the terms again before updating". The hash of an item's
// licence is the SHA-256 the catalogue holds for its licence file (SK-1's files list); where the catalogue has none yet the terms cannot be keyed and
// are required (fail closed, ED-295).
[Trait("Spec", "DL-7")]
public sealed class ItemTermsTests
{
    private static CatalogueItem WithLicence(string termsClass, string? hash)
    {
        var item = CatalogueFixtures.Item(CatalogueFixtures.Pinned(), "X3") with { TermsClass = Enum.Parse<TermsClass>(termsClass) };
        return hash is null ? item with { Files = item.Files.Where(f => f.Path != "LICENSE").ToList() } : item with { Files = [.. item.Files, new CatalogueFile { Path = "LICENSE", Size = 5, Sha256 = hash }] };
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void The_licence_hash_of_an_item_is_the_catalogue_s_hash_of_its_licence_file_and_null_when_the_catalogue_has_none()
    {
        Assert.Equal(StateFixtures.Hash1, ItemTerms.LicenceHash(WithLicence("Proprietary", StateFixtures.Hash1)));
        Assert.Null(ItemTerms.LicenceHash(WithLicence("Proprietary", null)));
        // ED-NEW-dl-11: pin run 3 pinned A2's licence file (skills/docx/LICENSE.txt at 8a1541c4), so its terms are accepted against that file's hash; O5 is still blocked.
        Assert.Equal("79f6d8f5b427252fa3b1c11ecdbdb6bf610b944f7530b4de78f770f38741cfaa", ItemTerms.LicenceHash(CatalogueFixtures.Item(CatalogueFixtures.Real, "A2")));
        Assert.Null(ItemTerms.LicenceHash(CatalogueFixtures.Item(CatalogueFixtures.Real, "O5")));
        Assert.Null(ItemTerms.LicenceHash(CatalogueFixtures.Item(CatalogueFixtures.Pinned(), "X1")));
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void An_open_item_never_asks_and_a_proprietary_or_unlicensed_one_asks_until_its_hash_is_accepted()
    {
        var state = AddonsState.Empty;
        Assert.False(ItemTerms.Required(WithLicence("Open", StateFixtures.Hash1), state));
        Assert.False(ItemTerms.Required(WithLicence("Open", null), state));
        foreach (var terms in new[] { "Proprietary", "None" })
        {
            var item = WithLicence(terms, StateFixtures.Hash1);
            Assert.True(ItemTerms.Required(item, state));
            var accepted = state.AcceptTerms(StateFixtures.Hash1, item.Id, StateFixtures.Noon);
            Assert.False(ItemTerms.Required(item, accepted));
            Assert.True(ItemTerms.Required(item, state));
        }
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void A_changed_licence_hash_asks_again_even_though_the_old_text_was_accepted()
    {
        var accepted = AddonsState.Empty.AcceptTerms(StateFixtures.Hash1, "X3", StateFixtures.Noon);
        Assert.False(ItemTerms.Required(WithLicence("Proprietary", StateFixtures.Hash1), accepted));
        Assert.True(ItemTerms.Required(WithLicence("Proprietary", StateFixtures.Hash2), accepted));
        Assert.True(accepted.HasAccepted(StateFixtures.Hash1));
        Assert.False(accepted.HasAccepted(StateFixtures.Hash2));
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void Terms_that_cannot_be_keyed_are_always_required_and_cannot_be_accepted()
    {
        var item = WithLicence("Proprietary", null);
        var state = AddonsState.Empty;
        Assert.True(ItemTerms.Required(item, state));
        Assert.Throws<ArgumentException>(() => state.AcceptTerms("", "X3", StateFixtures.Noon));
        Assert.Throws<ArgumentException>(() => state.AcceptTerms("abc", "X3", StateFixtures.Noon));
        Assert.Throws<ArgumentException>(() => state.AcceptTerms(new string('A', 64), "X3", StateFixtures.Noon));
        Assert.Throws<ArgumentException>(() => state.AcceptTerms(StateFixtures.Hash1, "", StateFixtures.Noon));
        Assert.Throws<ArgumentException>(() => state.AcceptTerms(StateFixtures.Hash1, "X3", default));
        Assert.Empty(state.AcceptedTerms);
    }

    [Fact]
    [Trait("Feature", "SK-7")]
    [Trait("Rule", "R-TEST-4")]
    public void An_acceptance_is_stored_with_its_date_and_hash_is_keyed_by_the_hash_and_the_first_acceptance_stands()
    {
        var state = AddonsState.Empty.AcceptTerms(StateFixtures.Hash1, "A2", StateFixtures.Noon);
        var term = state.AcceptedTerms.Single();
        Assert.Equal(new AcceptedTerm(StateFixtures.Hash1, "A2", StateFixtures.Noon), term);
        var again = state.AcceptTerms(StateFixtures.Hash1, "O5", StateFixtures.Noon.AddDays(3));
        Assert.Equal(term, again.AcceptedTerms.Single());
        var other = again.AcceptTerms(StateFixtures.Hash2, "O5", StateFixtures.Noon.AddDays(3));
        Assert.Equal([StateFixtures.Hash1, StateFixtures.Hash2], other.AcceptedTerms.Select(t => t.LicenceSha256));
        Assert.DoesNotContain(state.AcceptedTerms, t => t.LicenceSha256 == StateFixtures.Hash2);
        var shared = WithLicence("Proprietary", StateFixtures.Hash1) with { Id = "X9" };
        Assert.False(ItemTerms.Required(shared, state));
    }
}
