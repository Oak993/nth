using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Tests.Fixtures;
using static Marrow.Core.Tests.Fixtures.CheckoutPages;

namespace Marrow.Core.Tests.Port.Assistant;

// Dextra AS-20 (DESIGN.md rev 5, 12.7 "The fill is bound to the merchant", "Cross-origin payment frames"; 16 row AS-20's
// checkout fixture on local hosts; ED-NEW-dextra-as20-4): the real number goes in only on the approved origin, in the shop's
// own frame and form, into a visible card field; a payment provider's frame, a form that posts elsewhere and a redirect
// after approval each fill nothing and hand the step to the owner.
[Trait("Feature", "AS-20")]
[Trait("Decision", "ED-NEW-dextra-as20-4")]
public sealed class CheckoutBoundaryTests
{
    [Fact]
    public void The_shops_own_form_fills_its_visible_card_fields_in_page_order()
    {
        var (plan, refusal) = CheckoutBoundary.Check(Shop, Total, SameOrigin());
        Assert.Null(refusal);
        Assert.Equal(new[] { ("number", CardFieldKind.Number), ("exp", CardFieldKind.Expiry), ("csc", CardFieldKind.SecurityCode) }, plan!.Fields);
    }

    [Fact]
    public void A_cross_origin_payment_frame_is_refused_and_handed_over_naming_the_provider()
    {
        var (plan, refusal) = CheckoutBoundary.Check(Shop, Total, ProviderFrame());
        Assert.Null(plan);
        Assert.Equal(FillRefusalKind.CrossOriginFrame, refusal!.Kind);
        Assert.Equal(Provider, refusal.Elsewhere);
        Assert.Equal($"This checkout takes the card in a frame from {Provider}. Tap \"I'll pay\" to finish it yourself.", refusal.Words);
    }

    [Fact]
    public void A_form_whose_action_posts_to_a_third_origin_is_refused()
    {
        var (plan, refusal) = CheckoutBoundary.Check(Shop, Total, PostsElsewhere());
        Assert.Null(plan);
        Assert.Equal(FillRefusalKind.FormPostsElsewhere, refusal!.Kind);
        Assert.Equal(Third, refusal.Elsewhere);
    }

    [Fact]
    public void A_top_frame_redirect_after_approval_is_refused()
    {
        var (plan, refusal) = CheckoutBoundary.Check(Shop, Total, Redirected());
        Assert.Null(plan);
        Assert.Equal(FillRefusalKind.TopFrameMoved, refusal!.Kind);
        Assert.Contains("after you approved shop.localhost", refusal.Words, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hidden_card_field_is_never_filled_and_split_expiry_fields_take_their_parts()
    {
        var (plan, refusal) = CheckoutBoundary.Check(Shop, Total, HiddenTwin());
        Assert.Null(refusal);
        Assert.Equal(new[] { ("shown", CardFieldKind.Number), ("exp-month", CardFieldKind.ExpiryMonth), ("exp-year", CardFieldKind.ExpiryYear) }, plan!.Fields);
    }

    [Fact]
    public void A_hidden_card_field_in_another_frame_still_refuses_the_whole_fill()
    {
        var page = SameOrigin() with { Fields = new ValueList<CheckoutField>([.. SameOrigin().Fields, Field("trap", "cc-number", frame: Third, visible: false)]) };
        Assert.Equal(FillRefusalKind.CrossOriginFrame, CheckoutBoundary.Check(Shop, Total, page).Refusal!.Kind);
    }

    [Fact]
    public void A_changed_total_and_a_page_without_a_card_number_fill_nothing()
    {
        Assert.Equal(FillRefusalKind.TotalChanged, CheckoutBoundary.Check(Shop, Total, SameOrigin() with { ShownTotal = 52.10m }).Refusal!.Kind);
        var noNumber = new CheckoutSnapshot(Shop, new ValueList<CheckoutField>([Field("exp", "cc-exp"), Field("number", "cc-number", visible: false)]));
        Assert.Equal(FillRefusalKind.NoCardField, CheckoutBoundary.Check(Shop, Total, noNumber).Refusal!.Kind);
    }

    [Fact]
    public void An_origin_is_scheme_host_and_port_and_only_https_counts_outside_this_pc()
    {
        Assert.Equal("https://shop.example", CheckoutOrigins.Of("https://Shop.Example:443/checkout?step=pay"));
        Assert.Equal("https://shop.example:8443", CheckoutOrigins.Of("https://shop.example:8443/"));
        Assert.Equal("https://xn--bcher-kva.example", CheckoutOrigins.Of("https://bücher.example/kasse"));
        Assert.Null(CheckoutOrigins.Of("http://shop.example/checkout"));
        Assert.Null(CheckoutOrigins.Of("http://shop.example/checkout", allowLoopbackHttp: true));
        Assert.Null(CheckoutOrigins.Of("https://shop.example@evil.example/"));
        Assert.Null(CheckoutOrigins.Of("javascript:alert(1)"));
        Assert.Null(CheckoutOrigins.Of("ftp://shop.example/"));
        Assert.Null(CheckoutOrigins.Of("http://127.0.0.1:8080/"));
        Assert.Equal("http://127.0.0.1:8080", CheckoutOrigins.Of("http://127.0.0.1:8080/", allowLoopbackHttp: true));
        Assert.Equal(Shop, CheckoutOrigins.Of(Shop + "/checkout", allowLoopbackHttp: true));
    }

    [Fact]
    public void An_https_shop_is_never_matched_by_a_plain_http_page_of_the_same_host()
    {
        var page = new CheckoutSnapshot("http://shop.example", new ValueList<CheckoutField>([Field("number", "cc-number", frame: "http://shop.example", action: null)]));
        Assert.Equal(FillRefusalKind.TopFrameMoved, CheckoutBoundary.Check("https://shop.example", Total, page).Refusal!.Kind);
    }
}
