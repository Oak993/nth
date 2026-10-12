// PORT-ONLY: web surfaces W11, the rules a model cannot talk its way around in Visus (E:/Droppy/work/design/web-surfaces/DESIGN.md
// 5.10 "Safety (Tactus parity)": the cross-origin-after-read rule, guarded actions, hard denies, the URL policy; 8.1, 8.4;
// ED-NEW-web-w11-1). No Swift counterpart. Pure functions over what the engine read from the page at the moment it acts (the
// element's accessible name, role and attributes, its form, its frame), so the guard runs whatever the model believes. The
// localised guarded words are Tactus's own lists (PcPolicy.GuardedWord), so both products ask for the same words.
using System.Text.RegularExpressions;
using Marrow.Core.Port.PcControl;

namespace Marrow.Core.Web;

/// <summary>What the engine read about one element just before acting on it (CDP: the AX node and the DOM element).</summary>
public sealed record WebElementFacts
{
    public string Role { get; init; } = "";
    public string Name { get; init; } = "";
    public string Tag { get; init; } = "";

    /// <summary>The input's or button's type attribute, lower case ("submit", "password", "file", "checkbox").</summary>
    public string? Type { get; init; }

    public string? AriaLabel { get; init; }
    public string? Id { get; init; }
    public string? NameAttribute { get; init; }

    /// <summary>A submit input's value (its visible label); never a typed field's value.</summary>
    public string? Label { get; init; }

    public string? Title { get; init; }
    public string? Autocomplete { get; init; }

    /// <summary>The element is inside a form (or is a form's submit control).</summary>
    public bool InForm { get; init; }

    /// <summary>A click or Enter here submits its form (a submit button, an image input, Enter in a single-line field).</summary>
    public bool Submits { get; init; }

    /// <summary>The form's resolved action address, when it has a form.</summary>
    public string? FormAction { get; init; }

    /// <summary>The form's method, lower case ("get", "post", "dialog").</summary>
    public string? FormMethod { get; init; }

    /// <summary>The link's resolved address when the element is (inside) a link.</summary>
    public string? Href { get; init; }

    /// <summary>The address of the document the element is in (an iframe's own address for an element inside one).</summary>
    public string? FrameUrl { get; init; }

    /// <summary>The element's class attribute (for CAPTCHA widgets).</summary>
    public string? ClassName { get; init; }

    public bool IsPassword => string.Equals(Type, "password", StringComparison.OrdinalIgnoreCase)
        || Autocomplete is { } a && (a.Contains("current-password", StringComparison.OrdinalIgnoreCase) || a.Contains("new-password", StringComparison.OrdinalIgnoreCase));

    public bool IsFileInput => string.Equals(Tag, "input", StringComparison.OrdinalIgnoreCase) && string.Equals(Type, "file", StringComparison.OrdinalIgnoreCase);
}

public enum WebGuardKind
{
    /// <summary>Nothing to ask: the step runs.</summary>
    None,

    /// <summary>The guarded-action card asks the user (Confirm / Cancel) before the step runs.</summary>
    Confirm,

    /// <summary>A hard deny: the step never runs, and the model gets the plain reason.</summary>
    Deny,
}

/// <summary>Which guarded action a card is about (its title and its "Asked because" line).</summary>
public enum WebGuardCategory
{
    None,
    Send,
    Post,
    Order,
    Delete,
    Submit,
    Install,
    SignOut,
    Upload,
    Download,
    NewSite,
}

/// <summary>What the guard says about one step.</summary>
public sealed record WebGuardVerdict(WebGuardKind Kind, WebGuardCategory Category, string? Word, string? Reason)
{
    public static WebGuardVerdict Pass { get; } = new(WebGuardKind.None, WebGuardCategory.None, null, null);

    public static WebGuardVerdict Denied(string reason) => new(WebGuardKind.Deny, WebGuardCategory.None, null, reason);

    public static WebGuardVerdict Ask(WebGuardCategory category, string word) => new(WebGuardKind.Confirm, category, word, null);
}

public static partial class WebAgentPolicy
{
    /// <summary>The refusal a password field gives (8.1 "values of password inputs never reach the model").</summary>
    public const string PasswordRefusal = "nth never types into password fields for a model. Ask the user to type it themselves.";

    public const string PaymentRefusal = "nth never completes a purchase or a payment for a model, and never types card details. Ask the user to do this step themselves.";

    public const string AccountRefusal = "nth never creates an account for a model. Ask the user to sign up themselves.";

    public const string CaptchaRefusal = "This is a CAPTCHA. nth never answers CAPTCHAs for a model; ask the user to do it.";

    public const string CodeRefusal = "nth never types one-time codes for a model. Ask the user to type it themselves.";

    // Card, payment and one-time-code fields by their autocomplete tokens (the HTML autofill names).
    private static readonly string[] paymentTokens = ["cc-number", "cc-csc", "cc-exp", "cc-exp-month", "cc-exp-year", "cc-name", "cc-type", "transaction-amount"];

    [GeneratedRegex(@"\b(sign\s*up|create\s+(an\s+|a\s+new\s+|your\s+)?account|register|join\s+now|open\s+an\s+account)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AccountWords();

    [GeneratedRegex(@"\b(place\s+(your\s+)?order|complete\s+(purchase|order|payment)|confirm\s+(and\s+)?pay|pay\s+now|buy\s+now|checkout\s+now)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PaymentPhrases();

    [GeneratedRegex(@"captcha|i'?m not a robot|verify (that )?you are (a )?human|are you a robot", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CaptchaWords();

    private static readonly string[] captchaHosts = ["recaptcha.net", "hcaptcha.com", "challenges.cloudflare.com", "arkoselabs.com", "funcaptcha.com", "captcha-delivery.com", "geetest.com"];

    /// <summary>
    /// A click or Enter on <paramref name="element"/> in a page of <paramref name="pageOrigin"/> (DESIGN 5.10): a hard deny for a
    /// purchase or payment, an account sign-up or a CAPTCHA; the guarded-action card for the guarded words (on the accessible
    /// name, aria-label, label, title or id), for a POST form whose action names one, for a submit to another site, and for a file
    /// upload; otherwise nothing.
    /// </summary>
    public static WebGuardVerdict CheckPress(WebElementFacts element, string? pageOrigin)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (IsCaptcha(element)) return WebGuardVerdict.Denied(CaptchaRefusal);
        var names = Names(element).ToList();
        foreach (var name in names)
        {
            if (PaymentPhrases().IsMatch(name)) return WebGuardVerdict.Denied(PaymentRefusal);
            if (AccountWords().IsMatch(name)) return WebGuardVerdict.Denied(AccountRefusal);
        }
        string? word = null;
        foreach (var name in names)
        {
            if (PcPolicy.GuardedWord(name) is { } found)
            {
                word = found;
                break;
            }
        }
        word ??= PcPolicy.GuardedWord(null, element.Id) ?? PcPolicy.GuardedWord(null, element.NameAttribute);
        // DESIGN 5.10 "Hard denies: purchases and payments completed by the model": the buy, pay and purchase words never run.
        if (word is "buy" or "pay" or "purchase") return WebGuardVerdict.Denied(PaymentRefusal);
        if (element.IsFileInput) return WebGuardVerdict.Ask(WebGuardCategory.Upload, "upload");
        if (word is not null) return WebGuardVerdict.Ask(CategoryOf(word), word);
        if (element.Submits) return CheckSubmit(element, pageOrigin);
        return WebGuardVerdict.Pass;
    }

    /// <summary>
    /// A form submit (a submit control's click, Enter in a form field): the card when the form posts to another site, or when a
    /// POST form's action names a guarded word; a POST to an address that names a payment is a hard deny. A GET form on its own site
    /// (a search, a filter) runs.
    /// </summary>
    public static WebGuardVerdict CheckSubmit(WebElementFacts element, string? pageOrigin)
    {
        ArgumentNullException.ThrowIfNull(element);
        var action = element.FormAction;
        var target = WebPolicy.OriginOf(action);
        if (target is not null && pageOrigin is not null && !string.Equals(target, pageOrigin, StringComparison.OrdinalIgnoreCase))
            return WebGuardVerdict.Ask(WebGuardCategory.NewSite, "submit");
        if (!string.Equals(element.FormMethod, "post", StringComparison.OrdinalIgnoreCase)) return WebGuardVerdict.Pass;
        var path = Uri.TryCreate(action, UriKind.Absolute, out var uri) ? uri.AbsolutePath : action ?? "";
        var word = PcPolicy.GuardedWord(null, path.Replace('/', ' ').Replace('-', ' ').Replace('_', ' ').Replace('.', ' '))
            ?? PcPolicy.GuardedWord(string.Join(' ', path.Split('/', '-', '_', '.')));
        if (word is "buy" or "pay" or "purchase") return WebGuardVerdict.Denied(PaymentRefusal);
        return word is not null ? WebGuardVerdict.Ask(CategoryOf(word), word) : WebGuardVerdict.Ask(WebGuardCategory.Submit, "submit");
    }

    /// <summary>
    /// Typing into <paramref name="element"/> (DESIGN 5.10 "typing into password fields", "account creation"; 8.1): refused for
    /// password fields, card and payment fields, one-time codes and CAPTCHAs; otherwise allowed.
    /// </summary>
    public static WebGuardVerdict CheckType(WebElementFacts element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (IsCaptcha(element)) return WebGuardVerdict.Denied(CaptchaRefusal);
        if (element.Autocomplete is { } auto && auto.Contains("new-password", StringComparison.OrdinalIgnoreCase)) return WebGuardVerdict.Denied(AccountRefusal);
        if (element.IsPassword) return WebGuardVerdict.Denied(PasswordRefusal);
        if (element.Autocomplete is { } tokens)
        {
            var parts = tokens.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Any(p => paymentTokens.Contains(p, StringComparer.OrdinalIgnoreCase))) return WebGuardVerdict.Denied(PaymentRefusal);
            if (parts.Any(p => string.Equals(p, "one-time-code", StringComparison.OrdinalIgnoreCase))) return WebGuardVerdict.Denied(CodeRefusal);
        }
        return WebGuardVerdict.Pass;
    }

    /// <summary>An element of a CAPTCHA: inside a CAPTCHA provider's frame, or named or classed as one.</summary>
    public static bool IsCaptcha(WebElementFacts element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (WebPolicy.IsWeb(element.FrameUrl) && Uri.TryCreate(element.FrameUrl, UriKind.Absolute, out var frame))
        {
            var host = frame.Host.ToLowerInvariant();
            if (captchaHosts.Any(h => host == h || host.EndsWith("." + h, StringComparison.Ordinal))) return true;
            if (host.EndsWith("google.com", StringComparison.Ordinal) && frame.AbsolutePath.Contains("/recaptcha", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return Names(element).Any(n => CaptchaWords().IsMatch(n)) || (element.ClassName is { } css && CaptchaWords().IsMatch(css));
    }

    /// <summary>The category of a guarded word (its card's title and reason).</summary>
    public static WebGuardCategory CategoryOf(string word) => word switch
    {
        "send" => WebGuardCategory.Send,
        "post" or "publish" => WebGuardCategory.Post,
        "order" or "transfer" or "buy" or "pay" or "purchase" => WebGuardCategory.Order,
        "delete" or "delete account" or "uninstall" => WebGuardCategory.Delete,
        "install" => WebGuardCategory.Install,
        "sign out" => WebGuardCategory.SignOut,
        _ => WebGuardCategory.Submit,
    };

    /// <summary>The card's foot: why nth asked ("Asked because this sends an email. Enter never confirms.").</summary>
    public static string AskedBecause(WebGuardCategory category) => "Asked because " + category switch
    {
        WebGuardCategory.Send => "this sends a message",
        WebGuardCategory.Post => "this posts in public",
        WebGuardCategory.Order => "this places an order or moves money",
        WebGuardCategory.Delete => "this deletes something",
        WebGuardCategory.Install => "this installs something",
        WebGuardCategory.SignOut => "this signs you out",
        WebGuardCategory.Upload => "this uploads a file from your PC",
        WebGuardCategory.Download => "this saves a file to your PC",
        WebGuardCategory.NewSite => "this sends the form to another site",
        _ => "this submits a form",
    } + ". Enter never confirms.";

    // The names a guard reads, in order: the accessible name, aria-label, a submit input's label, the title.
    private static IEnumerable<string> Names(WebElementFacts element)
    {
        foreach (var name in new[] { element.Name, element.AriaLabel, element.Label, element.Title })
            if (!string.IsNullOrWhiteSpace(name)) yield return name.Trim();
    }
}
