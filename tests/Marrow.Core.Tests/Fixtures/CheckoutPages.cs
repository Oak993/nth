using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Fixtures;

/// <summary>
/// Dextra AS-20's checkout fixture (DESIGN.md rev 5, 12.7, 16 row AS-20): checkout pages on local hosts as a fill path
/// reads them at the moment of filling, one per case of the done-when list. The shop is http://shop.localhost:8080; a
/// payment provider's test-mode checkout keeps its card fields in its own frame (http://pay.test:8443), as the providers'
/// hosted fields do; the published test card is typed by the owner there, never by nth.
/// </summary>
public static class CheckoutPages
{
    public const string Shop = "http://shop.localhost:8080";
    public const string Provider = "http://pay.test:8443";
    public const string Third = "http://collect.test:9000";
    public const decimal Total = 46.80m;

    public static CheckoutField Field(string id, string autocomplete, string? frame = null, string? action = Shop, bool visible = true) =>
        new(id, frame ?? Shop, action, autocomplete, visible);

    /// <summary>The shop's own card form: number, expiry, code, all on its origin, posting to it.</summary>
    public static CheckoutSnapshot SameOrigin() => new(Shop, new ValueList<CheckoutField>([
        Field("email", "email"),
        Field("number", "cc-number"),
        Field("exp", "cc-exp"),
        Field("csc", "billing cc-csc"),
    ])) { ShownTotal = Total };

    /// <summary>The card fields in a payment provider's frame (hosted fields), the rest of the checkout on the shop.</summary>
    public static CheckoutSnapshot ProviderFrame() => new(Shop, new ValueList<CheckoutField>([
        Field("email", "email"),
        Field("number", "cc-number", frame: Provider, action: null),
        Field("exp", "cc-exp", frame: Provider, action: null),
        Field("csc", "cc-csc", frame: Provider, action: null),
    ])) { ShownTotal = Total };

    /// <summary>The shop's own fields, in a form whose action posts to a third origin.</summary>
    public static CheckoutSnapshot PostsElsewhere() => new(Shop, new ValueList<CheckoutField>([
        Field("number", "cc-number", action: Third),
        Field("exp", "cc-exp", action: Third),
    ])) { ShownTotal = Total };

    /// <summary>The top frame moved after the owner approved: a redirect to another site.</summary>
    public static CheckoutSnapshot Redirected() => new(Third, new ValueList<CheckoutField>([
        Field("number", "cc-number", frame: Third, action: Third),
    ]));

    /// <summary>A hidden card number field beside the shown one: only the shown one is filled.</summary>
    public static CheckoutSnapshot HiddenTwin() => new(Shop, new ValueList<CheckoutField>([
        Field("shown", "cc-number"),
        Field("hidden", "cc-number", visible: false),
        Field("exp-month", "cc-exp-month"),
        Field("exp-year", "cc-exp-year"),
    ]));

    /// <summary>prepare_purchase's arguments for the fixture's shop.</summary>
    public static JSONValue Arguments(string checkout = Shop + "/checkout", string kind = "purchase", double total = 46.80, string currency = "USD") =>
        JSONValue.FromDictionaryLiteral(
            ("shop", "Shop Example"),
            ("checkout_url", checkout),
            ("items", JSONValue.FromArrayLiteral("Tamper, 58 mm: 24.00", "Milk jug, 350 ml: 14.50")),
            ("shipping", 4.90),
            ("tax", 3.40),
            ("total", total),
            ("currency", currency),
            ("ship_to", "Home (your saved address)"),
            ("kind", kind));
}
