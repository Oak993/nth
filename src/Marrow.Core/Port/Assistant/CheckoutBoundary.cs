// PORT-ONLY: Dextra AS-20 (DESIGN.md rev 5, 12.7 "The fill is bound to the merchant" and "Cross-origin payment frames",
// 19 row 9; ED-NEW-dextra-as20-4). No Swift twin.
// The merchant binding of the fill. The approved card names the shop's origin (scheme, host and port). At the moment of
// filling, the real number goes in only if the top frame is still on that origin, the card field's own frame is that
// origin, the form the field belongs to posts there (or, script-submitted, the field's frame is the shop's), and the field
// is a visible card field by its autocomplete token. Anything else fills nothing and hands the step to the owner: a
// payment provider's frame from another origin is never filled across (this release has no allowlist). Pure: the page is
// read by the fill path (Visus or Tactus) into a snapshot.
namespace Marrow.Core.Port.Assistant;

/// <summary>Origins as the binding compares them: scheme, lowercase punycode host, and the port only when it is not the default.</summary>
public static class CheckoutOrigins
{
    /// <summary>
    /// The origin of an address, or null when it is not one nth fills on: https only, no user name or password in it; plain
    /// http only on this PC (localhost, 127.0.0.1, ::1, *.localhost, *.test) and only when <paramref name="allowLoopbackHttp"/>
    /// is set (the checkout fixtures).
    /// </summary>
    public static string? Of(string? address, bool allowLoopbackHttp = false)
    {
        if (string.IsNullOrWhiteSpace(address) || !Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.UserInfo.Length > 0 || uri.IdnHost.Length == 0) return null;
        var scheme = uri.Scheme.ToLowerInvariant();
        if (scheme == Uri.UriSchemeHttp ? !(allowLoopbackHttp && IsThisPc(uri)) : scheme != Uri.UriSchemeHttps) return null;
        var host = uri.IdnHost.ToLowerInvariant().TrimEnd('.');
        if (host.Length == 0) return null;
        if (uri.HostNameType == UriHostNameType.IPv6) host = "[" + host.Trim('[', ']') + "]";
        return scheme + "://" + host + (uri.IsDefaultPort ? "" : ":" + uri.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>The host of an origin, for the card's words ("shop.example").</summary>
    public static string Host(string origin) => Uri.TryCreate(origin, UriKind.Absolute, out var uri) ? uri.Host : origin;

    private static bool IsThisPc(Uri uri) =>
        uri.IsLoopback || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".test", StringComparison.OrdinalIgnoreCase)
        || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
}

/// <summary>What a card field takes, from its autocomplete token.</summary>
public enum CardFieldKind { Number, Expiry, ExpiryMonth, ExpiryYear, SecurityCode }

/// <summary>One input of the checkout page as the fill path reads it at the moment of filling.</summary>
/// <param name="Id">The fill path's own handle for the field.</param>
/// <param name="FrameOrigin">The origin of the frame that owns the field (the top frame's for a field in the page itself).</param>
/// <param name="FormActionOrigin">The origin the field's form posts to; null when the form has no action (script-submitted).</param>
/// <param name="Autocomplete">The field's autocomplete attribute as written ("billing cc-number").</param>
/// <param name="Visible">Whether the field is shown (not hidden, not zero-sized, not off screen).</param>
public sealed record CheckoutField(string Id, string FrameOrigin, string? FormActionOrigin, string? Autocomplete, bool Visible)
{
    /// <summary>The card field kind of its last autocomplete token, or null when it is no card field.</summary>
    public CardFieldKind? CardKind => (Autocomplete ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .LastOrDefault()?.ToLowerInvariant() switch
    {
        "cc-number" => CardFieldKind.Number,
        "cc-exp" => CardFieldKind.Expiry,
        "cc-exp-month" => CardFieldKind.ExpiryMonth,
        "cc-exp-year" => CardFieldKind.ExpiryYear,
        "cc-csc" => CardFieldKind.SecurityCode,
        _ => null,
    };
}

/// <summary>The checkout page at the moment of filling.</summary>
public sealed record CheckoutSnapshot(string TopOrigin, ValueList<CheckoutField> Fields)
{
    /// <summary>The total the page shows, when the fill path can read it; a different total is a changed purchase.</summary>
    public decimal? ShownTotal { get; init; }
}

/// <summary>Why nothing is filled.</summary>
public enum FillRefusalKind { TopFrameMoved, CrossOriginFrame, FormPostsElsewhere, NoCardField, TotalChanged }

/// <summary>A refused fill: what to tell the owner, and the provider's origin when the card sits in another site's frame.</summary>
public sealed record FillRefusal(FillRefusalKind Kind, string Words, string? Elsewhere);

/// <summary>The fields to fill, in page order, each with what goes in it.</summary>
public sealed record FillPlan(ValueList<(string FieldId, CardFieldKind Kind)> Fields);

public static class CheckoutBoundary
{
    /// <summary>
    /// The fill plan for <paramref name="page"/> against the approved origin and total, or the refusal. Every card field is
    /// checked, the hidden ones too: a card field in another origin's frame, or in a form that posts elsewhere, refuses the
    /// whole fill. Hidden card fields are never filled.
    /// </summary>
    public static (FillPlan? Plan, FillRefusal? Refusal) Check(string approvedOrigin, decimal approvedTotal, CheckoutSnapshot page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var host = CheckoutOrigins.Host(approvedOrigin);
        var top = Same(page.TopOrigin);
        if (top is null || top != approvedOrigin)
            return (null, new FillRefusal(FillRefusalKind.TopFrameMoved,
                $"The page moved to {Shown(page.TopOrigin)} after you approved {host}. Nothing was filled. Tap \"I'll pay\" to finish it yourself.", top ?? page.TopOrigin));
        var cardFields = page.Fields.Where(f => f.CardKind is not null).ToList();
        foreach (var field in cardFields)
        {
            var frame = Same(field.FrameOrigin);
            if (frame != approvedOrigin)
                return (null, new FillRefusal(FillRefusalKind.CrossOriginFrame,
                    $"This checkout takes the card in a frame from {Shown(field.FrameOrigin)}. Tap \"I'll pay\" to finish it yourself.", frame ?? field.FrameOrigin));
            if (field.FormActionOrigin is { } action && Same(action) != approvedOrigin)
                return (null, new FillRefusal(FillRefusalKind.FormPostsElsewhere,
                    $"The card form on {host} sends the card to {Shown(action)}. Nothing was filled. Tap \"I'll pay\" to finish it yourself.", Same(action) ?? action));
        }
        if (page.ShownTotal is { } shown && Math.Abs(shown - approvedTotal) > 0.005m)
            return (null, new FillRefusal(FillRefusalKind.TotalChanged, "The checkout's total changed since you approved it. Nothing was filled; a new card asks you again.", null));
        var visible = cardFields.Where(f => f.Visible).ToList();
        if (!visible.Any(f => f.CardKind == CardFieldKind.Number))
            return (null, new FillRefusal(FillRefusalKind.NoCardField,
                $"nth found no card number field it may fill on {host}. Tap \"I'll pay\" to finish it yourself.", null));
        return (new FillPlan(new ValueList<(string, CardFieldKind)>(visible.Select(f => (f.Id, f.CardKind!.Value)))), null);
    }

    // An origin as read from the page, normalised the same way as the approved one. A plain-http origin on this PC is read
    // too, only to be compared: it never equals an https shop's.
    private static string? Same(string? origin) => CheckoutOrigins.Of(origin, allowLoopbackHttp: true);

    private static string Shown(string origin) => PurchaseRequests.Clean(origin, 200) is { Length: > 0 } text ? text : "another site";
}
