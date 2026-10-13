using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-20 (DESIGN.md rev 5, 12.7 "The boundary": nth fills only the shop's own card field, same origin as the approved
// checkout; ED-NEW-dextra-as20-4 and -11): the checkout fixture on local hosts, through the desk. A same-origin checkout is
// filled and paid; a card field in a payment provider's frame (its test-mode checkout on a .test host) and a form that posts
// to a third origin are refused before Windows Hello and handed to the owner; a top frame that moved after the approval is
// refused at the fill, after Windows Hello, and nothing is filled.
[Trait("Feature", "AS-20")]
[Trait("Decision", "ED-NEW-dextra-as20-4")]
public sealed class PurchaseCheckoutTests
{
    [Fact]
    public async Task A_same_origin_checkout_on_a_local_host_is_filled_and_paid()
    {
        using var rig = await new PurchaseRig().On();
        var page = rig.SameOrigin();
        var card = Guid.Parse(rig.Desk.Request(rig.Call(PurchaseRig.Arguments())).DecisionId!);
        Assert.Null(await rig.Desk.Pay(card));
        Assert.Equal(["csc", "exp", "number"], page.Filled.Keys.Order(StringComparer.Ordinal));
        Assert.Contains("submit (held)", page.Events);
        Assert.Equal(PurchaseRowState.Paid, Assert.Single(rig.Desk.State.History).State);
    }

    [Fact]
    public async Task A_providers_test_mode_checkout_in_a_cross_origin_frame_is_refused_before_windows_hello_and_handed_over()
    {
        using var rig = await new PurchaseRig().On();
        var page = rig.ProviderFrame();
        var card = Guid.Parse(rig.Desk.Request(rig.Call(PurchaseRig.Arguments())).DecisionId!);
        var asked = rig.Hello.Asked;
        Assert.Null(await rig.Desk.Pay(card));
        Assert.Equal(asked, rig.Hello.Asked);                                           // nothing to verify: nth will not fill it
        Assert.Equal(["snapshot"], page.Events);
        Assert.Empty(page.Filled);
        var handed = Assert.Single(rig.Queue.Waiting);
        Assert.Equal(PurchaseWords.HandOverRule, handed.Rule);
        Assert.Contains("pay.test", handed.Why, StringComparison.Ordinal);
        Assert.Equal([PurchaseWords.IllPay, PurchaseWords.DontBuy], handed.Options);
        Assert.Equal(DecisionState.Withdrawn, rig.Queue.Find(card)!.State);
        Assert.Equal(PurchaseWords.ToAgentHandOver(handed.Id, handed.Why), rig.Delivered[^1].Text);
    }

    [Fact]
    public async Task A_form_that_posts_the_card_to_a_third_origin_is_refused_and_handed_over()
    {
        using var rig = await new PurchaseRig().On();
        var page = rig.PostsElsewhere();
        var card = Guid.Parse(rig.Desk.Request(rig.Call(PurchaseRig.Arguments())).DecisionId!);
        Assert.Null(await rig.Desk.Pay(card));
        Assert.Empty(page.Filled);
        Assert.Contains("collect.test", Assert.Single(rig.Queue.Waiting).Why, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_top_frame_that_moved_after_the_approval_is_refused_at_the_fill_and_nothing_is_filled()
    {
        using var rig = await new PurchaseRig().On();
        var page = rig.SameOrigin();
        page.Then(page.Page).Then(new CheckoutSnapshot(PurchaseRig.Third, page.Page.Fields) { ShownTotal = 46.80m });   // fine at Pay, moved by the fill
        var card = Guid.Parse(rig.Desk.Request(rig.Call(PurchaseRig.Arguments())).DecisionId!);
        var asked = rig.Hello.Asked;
        Assert.Null(await rig.Desk.Pay(card));
        Assert.Equal(asked + 1, rig.Hello.Asked);
        Assert.Empty(page.Filled);
        Assert.DoesNotContain(page.Events, e => e.StartsWith("fill", StringComparison.Ordinal) || e.StartsWith("submit", StringComparison.Ordinal));
        var handed = Assert.Single(rig.Queue.Waiting);
        Assert.Equal(PurchaseWords.HandOverRule, handed.Rule);
        Assert.Empty(rig.Desk.State.History);
    }

    [Fact]
    public async Task A_checkout_whose_total_changed_or_whose_page_went_away_is_not_paid()
    {
        using var rig = await new PurchaseRig().On();
        var page = rig.SameOrigin(total: 52.10m);                                       // the page now shows another total
        var card = Guid.Parse(rig.Desk.Request(rig.Call(PurchaseRig.Arguments())).DecisionId!);
        Assert.Null(await rig.Desk.Pay(card));
        Assert.Empty(page.Filled);
        Assert.Equal(DecisionState.Withdrawn, rig.Queue.Find(card)!.State);

        rig.Pages.Clear();
        var gone = Guid.Parse(rig.Desk.Request(rig.Call(PurchaseRig.Arguments(12m))).DecisionId!);
        Assert.Null(await rig.Desk.Pay(gone));
        Assert.Equal((DecisionState.Withdrawn, PurchaseWords.PageGone), (rig.Queue.Find(gone)!.State, rig.Queue.Find(gone)!.Note));
        Assert.Empty(rig.Desk.State.History);
    }
}
