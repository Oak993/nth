// PORT-ONLY: Dextra AS-20 (DESIGN.md rev 5, 12.7 "Every purchase shows the confirmation card", "Never allowed", "The fill
// is bound to the merchant"; ED-NEW-dextra-as20-1, -3). No Swift twin.
// What an agent asks nth to buy, from prepare_purchase's arguments: the shop and its origin (scheme, host and port), the
// items, shipping, tax, the total in a currency, and where it ships. Everything an agent writes here is untrusted text:
// it is bounded, cleaned of control characters, and the total must add up. Subscriptions, transfers and money to people
// are refused here as well as in the policy engine. The card's inputs (the binding) come from this record. Pure.
using System.Globalization;
using System.Text;
using Marrow.Core.Support;

namespace Marrow.Core.Port.Assistant;

/// <summary>One line of the cart: what it is and what it costs.</summary>
public sealed record PurchaseItem(string Name, decimal Amount);

/// <summary>A purchase an agent prepared (12.7): what the confirmation card states and the fill is bound to.</summary>
public sealed record PurchaseRequest(string Shop, string Origin, string CheckoutUrl, ValueList<PurchaseItem> Items, decimal Shipping, decimal Tax,
    decimal Total, string Currency, string ShipTo)
{
    public const int MaxItems = 20;
    public const int MaxName = 120;
    public const decimal Most = PurchaseLimits.Most;

    /// <summary>The price of the items alone.</summary>
    public decimal Subtotal => Items.Sum(i => i.Amount);
}

/// <summary>The rules of a request: parsing the tool's arguments, refusing what nth never pays, and the card's inputs.</summary>
public static class PurchaseRequests
{
    /// <summary>The kinds of money movement the tool takes; only "purchase" can go on.</summary>
    public static IReadOnlyList<string> Kinds { get; } = ["purchase", "subscription", "transfer", "to_person"];

    /// <summary>
    /// The request from prepare_purchase's arguments (already checked against its schema), or null with
    /// <paramref name="refusal"/> saying why. <paramref name="allowLoopbackHttp"/> lets a plain-http shop on this PC
    /// through (the checkout fixtures only; a real shop is https).
    /// </summary>
    public static PurchaseRequest? Parse(JSONValue? arguments, out string refusal, bool allowLoopbackHttp = false)
    {
        refusal = "";
        string? Text(string name) => arguments?[name]?.JSONValueString;
        decimal? Money(string name) => arguments?[name]?.JSONValueDouble is { } d && !double.IsNaN(d) && !double.IsInfinity(d)
            && Math.Abs(d) <= (double)PurchaseRequest.Most ? decimal.Round((decimal)d, 2, MidpointRounding.AwayFromZero) : null;

        var kind = Text("kind") ?? "purchase";
        if (Refused(kind) is { } never) { refusal = never; return null; }
        var shop = Clean(Text("shop"), MaxNameLength);
        if (shop.Length == 0) { refusal = "Not prepared: name the shop."; return null; }
        var url = Text("checkout_url") ?? "";
        if (CheckoutOrigins.Of(url, allowLoopbackHttp) is not { } origin)
        {
            refusal = "Not prepared: the checkout's address must be a full https address of the shop's own site.";
            return null;
        }
        var items = new List<PurchaseItem>();
        foreach (var line in arguments?["items"]?.JSONValueArray ?? [])
        {
            if (ParseItem(line.JSONValueString) is not { } item)
            {
                refusal = "Not prepared: each item is \"name: price\", for example \"Milk jug, 350 ml: 14.50\".";
                return null;
            }
            items.Add(item);
        }
        if (items.Count == 0) { refusal = "Not prepared: list what is in the cart."; return null; }
        if (items.Count > PurchaseRequest.MaxItems) { refusal = $"Not prepared: a purchase card holds at most {PurchaseRequest.MaxItems} items."; return null; }
        var shipping = Money("shipping") ?? 0m;
        var tax = Money("tax") ?? 0m;
        if (Money("total") is not { } total || total <= 0) { refusal = "Not prepared: give the total, with shipping and tax."; return null; }
        if (shipping < 0 || tax < 0) { refusal = "Not prepared: shipping and tax cannot be below 0."; return null; }
        if (Math.Abs(items.Sum(i => i.Amount) + shipping + tax - total) > 0.005m)
        {
            refusal = "Not prepared: the items, shipping and tax do not add up to the total. Read them again from the checkout.";
            return null;
        }
        var currency = (Text("currency") ?? "USD").Trim().ToUpperInvariant();
        if (currency.Length != 3 || !currency.All(c => c is >= 'A' and <= 'Z')) { refusal = "Not prepared: the currency is a three-letter code, for example USD."; return null; }
        var shipTo = Clean(Text("ship_to"), MaxNameLength);
        if (shipTo.Length == 0) { refusal = "Not prepared: say where it ships, as the checkout shows it."; return null; }
        var request = new PurchaseRequest(shop, origin, url, new ValueList<PurchaseItem>(items), shipping, tax, total, currency, shipTo);
        if (Recurring(request) is { } recurring) { refusal = recurring; return null; }
        return request;
    }

    private const int MaxNameLength = PurchaseRequest.MaxName;

    /// <summary>The refusal for a kind nth never pays (12.7 "Never allowed"), or null for a purchase.</summary>
    public static string? Refused(string kind) => kind switch
    {
        "purchase" => null,
        "subscription" => NeverAllowed,
        "transfer" or "to_person" => NeverAllowed,
        _ => "Not prepared: say what kind of payment it is.",
    };

    public const string NeverAllowed = "Never allowed: subscriptions, transfers and money to people stay with the owner.";

    // Words that make a "purchase" a subscription or money to a person whatever the agent called it.
    private static readonly string[] RecurringWords =
    [
        "subscription", "subscribe", "recurring", "auto-renew", "autorenew", "auto renew", "renews", "per month", "/month", "/mo",
        "monthly plan", "per year", "/year", "/yr", "annual plan", "membership", "free trial", "trial then",
        "wire transfer", "bank transfer", "money transfer", "send money", "gift to", "tip for", "donation to",
    ];

    /// <summary>A request whose words say it repeats or sends money to someone: refused like the kind itself.</summary>
    public static string? Recurring(PurchaseRequest request)
    {
        var text = string.Join(" ", request.Items.Select(i => i.Name).Append(request.Shop)).ToLowerInvariant();
        return RecurringWords.Any(w => text.Contains(w, StringComparison.Ordinal)) ? NeverAllowed : null;
    }

    /// <summary>"Milk jug, 350 ml: 14.50" as an item; the price is after the last colon. Null when it is not one.</summary>
    public static PurchaseItem? ParseItem(string? line)
    {
        if (line is null) return null;
        var colon = line.LastIndexOf(':');
        if (colon <= 0) return null;
        var name = Clean(line[..colon], MaxNameLength);
        var price = line[(colon + 1)..].Trim().TrimStart('$', '€', '£').Trim();
        if (name.Length == 0 || !decimal.TryParse(price, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)) return null;
        if (amount < 0 || amount > PurchaseRequest.Most || decimal.Round(amount, 2) != amount) return null;
        return new PurchaseItem(name, amount);
    }

    /// <summary>Untrusted text made safe to show on a card: control and format characters gone, spaces folded, cut to a length.</summary>
    public static string Clean(string? text, int max)
    {
        if (text is null) return "";
        var builder = new StringBuilder(Math.Min(text.Length, max));
        var space = false;
        foreach (var rune in text.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned || Rune.IsWhiteSpace(rune))
            {
                space = builder.Length > 0;
                continue;
            }
            if (space) builder.Append(' ');
            space = false;
            if (builder.Length + rune.Utf16SequenceLength > max) break;
            builder.Append(rune.ToString());
        }
        return builder.ToString();
    }
}

/// <summary>Amounts as the card shows them, in the card's currency.</summary>
public static class PurchaseMoney
{
    /// <summary>"$46.80", "€12.00", "£3.50", or "46.80 CHF" for any other currency.</summary>
    public static string Format(decimal amount, string currency)
    {
        var figure = decimal.Round(amount, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);
        return currency switch
        {
            "USD" => "$" + figure,
            "EUR" => "€" + figure,
            "GBP" => "£" + figure,
            _ => figure + " " + currency,
        };
    }
}
