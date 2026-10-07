using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Thread;

namespace Marrow.Core.Tests.Port.MultiAccount;

[Trait("Rule", "R-TEST-4")]
public sealed class LinkColumnOfferTests
{
    [Theory]
    [MemberData(nameof(ConversationHeadOfferTests.Unverified), MemberType = typeof(ConversationHeadOfferTests))]
    public void Unverified_CLI_rows_and_stale_links_are_absent_and_cannot_be_linked(ProviderKind service)
    {
        var primary = new HeadOfferAccountPort(ProviderKind.Claude, ConversationHeadOfferTests.Row(ProviderKind.Claude));
        var account = ConversationHeadOfferTests.Row(service);
        var other = new HeadOfferAccountPort(service, account);
        var links = new HeadOfferLinkPort([account.Id]);
        using var model = new LinkColumnModel(Guid.NewGuid(), primary, [other], links);
        Assert.Empty(model.Rows);
        Assert.Empty(model.Linked);
        model.Toggle(account.Id);
        Assert.Empty(links.Actions);
        Assert.Single(other.Accounts);
    }

    [Fact]
    public void A_primary_without_safe_turns_can_still_be_checked_by_verified_heads_only()
    {
        var active = ConversationHeadOfferTests.Row(ProviderKind.Codex);
        var primary = new HeadOfferAccountPort(ProviderKind.Codex, active);
        var rows = Enum.GetValues<ProviderKind>().Select(ConversationHeadOfferTests.Row).ToArray();
        var other = new HeadOfferAccountPort(ProviderKind.Claude, rows);
        var expected = rows.Where(a => a.Service is ProviderKind.Claude or ProviderKind.Pi or ProviderKind.Deepseek
            or ProviderKind.Meta or ProviderKind.Zai or ProviderKind.OpenRouter or ProviderKind.Local).ToArray();
        var links = new HeadOfferLinkPort([]);
        using var model = new LinkColumnModel(Guid.NewGuid(), primary, [other, other], links);
        Assert.Equal(expected, model.Rows);
        foreach (var account in rows) model.Toggle(account.Id);
        Assert.Equal(expected.Select(a => a.Id), model.Linked);
        Assert.Equal(expected.Select(a => a.Id), links.Actions.Select(a => a.Id));
        model.Toggle(expected[0].Id);
        Assert.Equal(expected.Skip(1).Select(a => a.Id), model.Linked);
        Assert.Equal((expected[0].Id, false), links.Actions.Last());
        Assert.Equal(rows.Length, other.Accounts.Count);
    }

    [Fact]
    public void Live_account_changes_remove_disabled_out_and_unverified_heads_before_a_tap()
    {
        var primary = new HeadOfferAccountPort(ProviderKind.Claude, ConversationHeadOfferTests.Row(ProviderKind.Claude));
        var a = ConversationHeadOfferTests.Row(ProviderKind.Pi);
        var other = new HeadOfferAccountPort(ProviderKind.Pi, a);
        var hasRoom = true;
        var links = new HeadOfferLinkPort([a.Id]);
        using var model = new LinkColumnModel(Guid.NewGuid(), primary, [other], links, _ => hasRoom);
        Assert.Single(model.Rows);
        other.Replace(a with { Enabled = false });
        Assert.Empty(model.Rows);
        other.Replace(a);
        hasRoom = false;
        Assert.Empty(model.Rows);
        hasRoom = true;
        other.Replace(a with { Service = ProviderKind.Cursor });
        Assert.Empty(model.Rows);
        Assert.Empty(model.Linked);
        model.Toggle(a.Id);
        Assert.Empty(links.Actions);
    }
}
