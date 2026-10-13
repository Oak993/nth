// PORT-ONLY: Dextra (DESIGN.md rev 5, 10 step 2, 12.1, 12.1a, 12.3 point 4; unit AS-19; ED-NEW-dextra-as19-3). No Swift twin.
// Which recorded steps ask the owner. Decided in code, never by a model: a button or a menu item whose label names sending,
// paying, deleting, installing or agreeing (in English, Hindi and Urdu), a command typed into a terminal that the action
// classifier does not let run alone, a shortcut that sends or deletes, and a step in a window nth never records. A model's draft
// can add to these marks and can never remove one (TeachDraftBuilder). Pure: no I/O, no clock, no model.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Marrow.Core.Port.Assistant;

/// <summary>A step that asks: the class it would be in a real run, and the reason in the owner's words.</summary>
public sealed record TeachGuardFinding(ActionClass Class, string Reason)
{
    /// <summary>The short word the draft shows ("sends", "pays", "deletes").</summary>
    public string Word => TeachGuards.WordOf(Class);

    /// <summary>The step is one nth never does for the owner (a password, a private window): he does it himself.</summary>
    public bool OwnerDoesIt => Class == ActionClass.HardDeny;
}

public static class TeachGuards
{
    private const RegexOptions Flags = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;
    private const int TimeoutMs = 250;
    private const string Before = @"(?<![\p{L}\p{N}\p{M}])";
    private const string After = @"(?![\p{L}\p{N}\p{M}])";

    // Latin phrases, then Hindi, then Urdu. A phrase may not sit inside a longer word (the lookarounds), and a label that holds
    // one anywhere ("Send feedback", "Yes, delete it") is guarded: asking too often is the safe side.
    private static readonly (ActionClass Class, string Reason, string[] Phrases)[] Table =
    [
        (ActionClass.Money, "pays or orders",
        [
            "pay", "pay now", "pay with", "buy", "buy now", "purchase", "place order", "place your order", "order now", "complete order", "complete purchase",
            "confirm order", "confirm purchase", "checkout", "check out", "subscribe", "start trial", "start free trial", "upgrade", "donate", "transfer", "send money",
            "भुगतान", "भुगतान करें", "खरीदें", "अभी खरीदें", "ऑर्डर करें", "ऑर्डर दें", "सदस्यता लें", "दान करें", "पैसे भेजें",
            "ادائیگی", "ادائیگی کریں", "ادا کریں", "خریدیں", "ابھی خریدیں", "آرڈر کریں", "آرڈر دیں", "سبسکرائب", "عطیہ", "رقم بھیجیں",
        ]),
        (ActionClass.Outward, "sends or posts",
        [
            "send", "send now", "send message", "send email", "reply", "reply all", "forward", "post", "publish", "share", "submit", "tweet", "upload", "comment", "invite", "broadcast",
            "भेजें", "भेजे", "भेज दें", "भेजिए", "पोस्ट करें", "प्रकाशित करें", "साझा करें", "जवाब दें", "सबमिट", "जमा करें", "अपलोड करें", "आमंत्रित करें",
            "بھیجیں", "بھیج دیں", "بھیجئے", "پوسٹ کریں", "شائع کریں", "شیئر کریں", "جواب دیں", "جمع کروائیں", "اپ لوڈ کریں", "مدعو کریں",
        ]),
        (ActionClass.Destructive, "deletes",
        [
            "delete", "remove", "erase", "wipe", "discard", "empty recycle bin", "empty trash", "format drive", "format disk", "quick format", "uninstall", "clear all", "clear history", "reset", "permanently delete",
            "shred", "recycle", "overwrite", "replace file", "replace files", "replace the file in the destination",
            "हटाएं", "हटाएँ", "हटाइए", "मिटाएं", "मिटाएँ", "डिलीट", "हटा दें", "अनइंस्टॉल", "फ़ॉर्मेट", "रीसेट",
            "حذف کریں", "حذف", "مٹائیں", "ہٹائیں", "ان انسٹال", "فارمیٹ", "ری سیٹ",
        ]),
        (ActionClass.Install, "installs",
        [
            "install", "add to chrome", "add to edge", "add extension", "run installer", "download and install",
            "इंस्टॉल", "इंस्टॉल करें", "स्थापित करें",
            "انسٹال", "انسٹال کریں",
        ]),
        (ActionClass.Commitment, "books or agrees",
        [
            "book", "book now", "reserve", "accept", "accept all", "i agree", "i accept", "agree", "agree and continue", "rsvp", "confirm booking", "sign document", "place bid", "bid", "create account", "sign up",
            "बुक करें", "आरक्षित करें", "स्वीकार करें", "सहमत", "मैं सहमत हूं", "खाता बनाएं", "साइन अप",
            "بک کریں", "ریزرو کریں", "قبول کریں", "اتفاق", "میں متفق ہوں", "اکاؤنٹ بنائیں", "سائن اپ",
        ]),
        (ActionClass.Login, "signs in",
        [
            "sign in", "log in", "login", "log on", "sign on",
            "साइन इन", "लॉग इन", "लॉगिन",
            "سائن ان", "لاگ ان", "لاگ اِن",
        ]),
    ];

    private static readonly Regex[] Matchers = [.. Table.Select(entry =>
        new Regex(Before + "(?:" + string.Join("|", entry.Phrases.Select(Fold).OrderByDescending(p => p.Length).Select(Regex.Escape)) + ")" + After, Flags, TimeSpan.FromMilliseconds(TimeoutMs)))];

    // The buttons that only say yes. They carry no meaning of their own, so after a guarded step they take its guard.
    private static readonly Regex Confirm = new(
        Before + @"(?:ok|okay|yes|yep|confirm|continue|proceed|apply|allow|done|got it|sure|accept|हाँ|हां|ठीक है|जारी रखें|पुष्टि करें|ہاں|ٹھیک ہے|جاری رکھیں|تصدیق کریں)" + After,
        Flags, TimeSpan.FromMilliseconds(TimeoutMs));

    private static readonly string[] PrivateApps =
    [
        "1password", "bitwarden", "keepass", "keepassxc", "lastpass", "dashlane", "nordpass", "roboform", "enpass", "keeper", "proton pass", "protonpass",
        "consent", "credwiz", "credentialuibroker", "logonui", "winlogon", "lockapp", "securityhealthui", "windowsdefender",
        "marrow.app", "marrow", "nth",
    ];

    private static readonly string[] PrivateWindowMarks =
    [
        "incognito", "inprivate", "private browsing", "private window", "user account control", "windows security", "credential manager", "password manager",
        "windows hello", "nth settings", "निजी ब्राउज़िंग", "پرائیویٹ براؤزنگ",
    ];

    private static readonly string[] Terminals =
    [
        "cmd", "powershell", "pwsh", "windowsterminal", "wt", "conhost", "bash", "wsl", "wslhost", "mintty", "alacritty", "wezterm", "git-bash",
    ];

    /// <summary>The word a draft shows beside a step that asks.</summary>
    public static string WordOf(ActionClass actionClass) => actionClass switch
    {
        ActionClass.Money => "pays",
        ActionClass.Outward => "sends",
        ActionClass.Destructive => "deletes",
        ActionClass.Install => "installs",
        ActionClass.Commitment => "agrees",
        ActionClass.Login => "signs in",
        ActionClass.HardDeny => "you do it yourself",
        ActionClass.PersonalDataOut => "shares personal data",
        _ => "needs a check",
    };

    /// <summary>The label with its menu marks (&amp;Send, _Send) and decoration taken off, folded to one form for matching.</summary>
    public static string Fold(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return "";
        var text = label.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is '&' or '_' or '…' or '​' or '‌' or '‍' or '﻿') continue;
            builder.Append(char.IsControl(c) ? ' ' : c);
        }
        return builder.ToString().Trim().ToLower(CultureInfo.InvariantCulture);
    }

    /// <summary>The guard a button's or menu item's label gives, or null: the strictest class that names the label.</summary>
    public static TeachGuardFinding? ForLabel(string? label)
    {
        var folded = Fold(label);
        if (folded.Length == 0) return null;
        TeachGuardFinding? best = null;
        for (var i = 0; i < Table.Length; i++)
        {
            bool hit;
            try { hit = Matchers[i].IsMatch(folded); }
            catch (RegexMatchTimeoutException) { hit = true; } // a label the patterns cannot finish on asks
            if (!hit) continue;
            var found = new TeachGuardFinding(Table[i].Class, $"“{Excerpt(label)}” {Table[i].Reason}.");
            if (best is null || ActionClasses.Strictness(found.Class) > ActionClasses.Strictness(best.Class)) best = found;
        }
        return best;
    }

    /// <summary>A button that only says yes (OK, Yes, Confirm): it takes the guard of the guarded step just before it.</summary>
    public static bool IsConfirmLabel(string? label)
    {
        var folded = Fold(label);
        if (folded.Length == 0 || folded.Length > 40) return false;
        try { return Confirm.IsMatch(folded); }
        catch (RegexMatchTimeoutException) { return false; }
    }

    /// <summary>The guard a shortcut gives (Ctrl+Enter often sends; Shift+Delete deletes for good), or null.</summary>
    public static TeachGuardFinding? ForShortcut(string? chord)
    {
        var folded = (chord ?? "").Replace(" ", "", StringComparison.Ordinal).ToLower(CultureInfo.InvariantCulture);
        return folded switch
        {
            "ctrl+enter" or "ctrl+shift+enter" or "alt+s" => new TeachGuardFinding(ActionClass.Outward, $"{chord} often sends what is open."),
            "shift+delete" or "ctrl+shift+delete" => new TeachGuardFinding(ActionClass.Destructive, $"{chord} deletes for good."),
            _ => null,
        };
    }

    /// <summary>
    /// The guard a command typed into a terminal gives: anything the classifier does not let run alone, else null. The shell is read from the
    /// terminal's app (PowerShell, cmd, a POSIX shell); a terminal that can hold any of them (Windows Terminal) is read with all three, and
    /// then only a command every grammar lets run alone passes, which in practice means it asks.
    /// </summary>
    public static TeachGuardFinding? ForCommand(string? command, string? app = null)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var shell = Path.GetFileNameWithoutExtension((app ?? "").Trim()).ToLower(CultureInfo.InvariantCulture) switch
        {
            "powershell" or "pwsh" => ShellKind.PowerShell,
            "cmd" or "conhost" => ShellKind.Cmd,
            "bash" or "wsl" or "wslhost" or "mintty" or "git-bash" => ShellKind.Posix,
            _ => ShellKind.Auto,
        };
        var classification = ActionClassifier.Classify(new ActionRequest.ShellCommand(command.Trim(), shell), new ClassifierContext());
        var worst = classification.Leaves.OrderByDescending(leaf => ActionClasses.Strictness(leaf.Class)).FirstOrDefault() ?? classification;
        return ActionClasses.DefaultOf(worst.Class) == ClassDefault.Alone
            ? null
            : new TeachGuardFinding(worst.Class, $"The command “{Excerpt(command)}” is not one that runs alone: {worst.Reason}.");
    }

    /// <summary>Whether an app is one nth never records the inside of (a password manager, the UAC prompt, nth itself).</summary>
    public static bool IsPrivateApp(string? app)
    {
        var name = Path.GetFileNameWithoutExtension((app ?? "").Trim()).ToLower(CultureInfo.InvariantCulture);
        return name.Length > 0 && PrivateApps.Contains(name, StringComparer.Ordinal);
    }

    /// <summary>Whether a window title marks a private or protected window (a private browser window, the UAC prompt).</summary>
    public static bool IsPrivateWindow(string? title)
    {
        var folded = Fold(title);
        return folded.Length > 0 && PrivateWindowMarks.Any(mark => folded.Contains(mark, StringComparison.Ordinal));
    }

    public static bool IsTerminal(string? app) =>
        Terminals.Contains(Path.GetFileNameWithoutExtension((app ?? "").Trim()).ToLower(CultureInfo.InvariantCulture), StringComparer.Ordinal);

    /// <summary>Whether a recorded element is private: its app or its window is (the control of a password field is handled by the recorder).</summary>
    public static bool IsPrivate(TeachElement? element) =>
        element is not null && (IsPrivateApp(element.App) || IsPrivateWindow(element.Window));

    /// <summary>
    /// The guard of one recorded event, or null. <paramref name="previous"/> is the guard of the step just before it, so a bare "Yes"
    /// after "Delete" asks too.
    /// </summary>
    public static TeachGuardFinding? ForEvent(TeachEvent e, TeachGuardFinding? previous = null)
    {
        ArgumentNullException.ThrowIfNull(e);
        switch (e.Kind)
        {
            case TeachEventKind.Private:
                return new TeachGuardFinding(ActionClass.HardDeny, "It happened in a window nth does not record (a password manager, a private window or a security prompt). You do it yourself.");
            case TeachEventKind.Type:
                if (e.Secret || e.Element?.IsPassword == true)
                    return new TeachGuardFinding(ActionClass.HardDeny, "It is a password or another secret. nth never types one for you; you do it yourself.");
                return IsTerminal(e.Element?.App) ? ForCommand(e.Text, e.Element?.App) : null;
            case TeachEventKind.Shortcut:
                return ForShortcut(e.Text);
            case TeachEventKind.Browser:
                if (string.Equals(e.Verb, "submit", StringComparison.OrdinalIgnoreCase))
                    return new TeachGuardFinding(ActionClass.Outward, "It submits a form.");
                return ForLabel(e.Element?.Name) ?? ForLabel(e.Text);
            default:
            {
                if (IsPrivate(e.Element))
                    return new TeachGuardFinding(ActionClass.HardDeny, "It happened in a window nth does not record. You do it yourself.");
                var label = ForLabel(e.Element?.Name);
                if (label is not null) return label;
                return previous is not null && IsConfirmLabel(e.Element?.Name)
                    ? previous with { Reason = "It confirms the step before: " + previous.Reason }
                    : null;
            }
        }
    }

    private static string Excerpt(string? text)
    {
        var folded = (text ?? "").Trim();
        return folded.Length <= 60 ? folded : folded[..60] + "…";
    }
}
