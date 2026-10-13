// PORT-ONLY: web surfaces W8a (E:/Droppy/work/design/web-surfaces/DESIGN.md 2.3 profiles, 5.1 "the site chip (lock, 'Not
// secure', 'Preview', 'Private', or 'Agent')", 5.2 "Allowed schemes: https, http (with the 'Not secure' chip), about:blank.
// Blocked: javascript:, file:, data: at the top level, edge:, chrome: and other internal pages", 8.1 "NavigationStarting and
// FrameNavigationStarting enforce the scheme rules of 5.2", 8.2 the trust strip, 8.3 external protocols, 13.1 WebPolicyTests;
// ED-NEW-web-w8a-1). No Swift counterpart. The rules every Visus tab is held to, as pure functions, so the engine layer, the
// pane and the tests read one answer.
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Marrow.Core.Localisation;

namespace Marrow.Core.Web;

/// <summary>DESIGN 2.3, 5.1: the kinds of Visus tab. Each runs in its own profile.</summary>
public enum VisusTabKind
{
    /// <summary>A normal tab: the <c>browse</c> profile, where the user's logins live.</summary>
    Browse,

    /// <summary>A private tab: InPrivate, leaves nothing, never seen by the model.</summary>
    Private,

    /// <summary>A project preview (localhost, <c>*.localhost</c>, LAN dev hosts): the <c>preview</c> profile.</summary>
    Preview,

    /// <summary>A tab the model opened itself: the thread's <c>agent</c> profile.</summary>
    Agent,
}

/// <summary>DESIGN 5.1: what the site chip says.</summary>
public enum SiteChipKind
{
    /// <summary>A new tab: no chip.</summary>
    None,

    /// <summary>https: the lock.</summary>
    Secure,

    /// <summary>http on a public host: "Not secure".</summary>
    NotSecure,

    /// <summary>A dev host in a preview tab: "Preview".</summary>
    Preview,

    /// <summary>A private tab: the mask and "Private".</summary>
    Private,

    /// <summary>A tab the model opened: "Agent".</summary>
    Agent,
}

/// <summary>The site chip: its kind, and whether the page is also plain http (a private or agent tab on http).</summary>
public readonly record struct SiteChip(SiteChipKind Kind, bool NotSecure)
{
    /// <summary>The chip's words ("Preview", "Private · Not secure"); empty for the lock alone and for no chip.</summary>
    public string Label => Kind switch
    {
        SiteChipKind.NotSecure => UiText.Get("visus.site.chip_not_secure"),
        SiteChipKind.Preview => UiText.Get("visus.site.chip_preview"),
        SiteChipKind.Private => NotSecure ? UiText.Get("visus.site.chip_private_not_secure") : UiText.Get("visus.site.chip_private"),
        SiteChipKind.Agent => NotSecure ? UiText.Get("visus.site.chip_agent_not_secure") : UiText.Get("visus.site.chip_agent"),
        _ => "",
    };

    /// <summary>How the chip reads aloud (the trust strip's accessible name ends with it).</summary>
    public string Spoken => Kind switch
    {
        SiteChipKind.None => UiText.Get("visus.site.spoken_new_tab"),
        SiteChipKind.Secure => UiText.Get("visus.site.spoken_secure"),
        SiteChipKind.NotSecure => UiText.Get("visus.site.spoken_not_secure"),
        SiteChipKind.Preview => NotSecure ? UiText.Get("visus.site.spoken_preview") : UiText.Get("visus.site.spoken_preview_secure"),
        SiteChipKind.Private => NotSecure ? UiText.Get("visus.site.spoken_private_not_secure") : UiText.Get("visus.site.spoken_private"),
        SiteChipKind.Agent => NotSecure ? UiText.Get("visus.site.spoken_agent_not_secure") : UiText.Get("visus.site.spoken_agent"),
        _ => "",
    };
}

/// <summary>DESIGN 5.2, 8.1, 8.2: scheme rules, dev hosts, the site chip and the source name.</summary>
public static class WebPolicy
{
    /// <summary>The blank page a new tab shows.</summary>
    public const string Blank = "about:blank";

    // Internal pages and the schemes that are never a top-level page (5.2), plus every browser-internal scheme Chromium has.
    private static readonly HashSet<string> Internal = new(StringComparer.OrdinalIgnoreCase)
    {
        "edge", "chrome", "chrome-extension", "chrome-untrusted", "chrome-search", "chrome-error", "chrome-native", "edge-extension",
        "edge-untrusted", "ms-browser-extension", "devtools", "view-source", "about", "filesystem", "blob", "data", "file",
        "javascript", "vbscript", "intent", "android-app", "googlechrome", "microsoft-edge", "read", "edge-elevated",
    };

    /// <summary>The scheme of an address, lower case, or null when it has none.</summary>
    public static string? SchemeOf(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var colon = url.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0) return null;
        if (!char.IsAsciiLetter(url[0])) return null;
        for (var i = 1; i < colon; i++)
        {
            var c = url[i];
            if (!(char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.')) return null;
        }
        return url[..colon].ToLowerInvariant();
    }

    /// <summary>True for the blank page (with or without a fragment).</summary>
    public static bool IsBlank(string? url) =>
        url is not null && (url.Equals(Blank, StringComparison.OrdinalIgnoreCase) || url.StartsWith(Blank + "#", StringComparison.OrdinalIgnoreCase));

    /// <summary>http or https with a host.</summary>
    public static bool IsWeb(string? url) =>
        SchemeOf(url) is "http" or "https" && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.Length > 0;

    /// <summary>A browser-internal page or a scheme that is never a page of its own (5.2): refused at the top level.</summary>
    public static bool IsInternalScheme(string? scheme) => scheme is not null && Internal.Contains(scheme);

    /// <summary>
    /// DESIGN 5.2, 8.1: whether a Visus tab may load <paramref name="url"/> as its page: https, http and the blank page only.
    /// </summary>
    public static bool IsTopLevelAllowed(string? url) => IsWeb(url) || IsBlank(url);

    /// <summary>
    /// DESIGN 5.2, 8.1: whether a frame inside a Visus page may load <paramref name="url"/>. data: and blob: are refused only
    /// at the top level; file:, script schemes and internal pages never load, in any frame.
    /// </summary>
    public static bool IsFrameAllowed(string? url)
    {
        if (IsTopLevelAllowed(url)) return true;
        if (url is null) return false;
        if (url.Equals("about:srcdoc", StringComparison.OrdinalIgnoreCase)) return true;
        return SchemeOf(url) is "data" or "blob";
    }

    /// <summary>
    /// What a top-level navigation the engine reports should do: go ahead, be cancelled (the scheme rules), or be left to the
    /// external-protocol handler (an app's scheme: the engine raises its own launch request, which nth gates, 8.3).
    /// </summary>
    public static TopLevelVerdict CheckTopLevel(string? url)
    {
        if (IsTopLevelAllowed(url)) return TopLevelVerdict.Allow;
        var scheme = SchemeOf(url);
        if (scheme is null || IsInternalScheme(scheme) || ExternalProtocols.IsBlocked(scheme)) return TopLevelVerdict.Cancel;
        if (scheme is "http" or "https") return TopLevelVerdict.Cancel; // malformed (no host)
        return TopLevelVerdict.External;
    }

    /// <summary>
    /// DESIGN 2.3: a project preview host: localhost, <c>*.localhost</c>, loopback and private LAN addresses, and the reserved
    /// local names (<c>.local</c>, <c>.test</c>, <c>.internal</c>, <c>.lan</c>, <c>.home.arpa</c>). The host is a bare host
    /// (IPv6 with or without brackets), no port.
    /// </summary>
    public static bool IsDevHost(string? host)
    {
        if (string.IsNullOrEmpty(host)) return false;
        var name = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (name.StartsWith('[') && name.EndsWith(']')) name = name[1..^1];
        if (name.Length == 0) return false;
        if (name is "localhost" || name.EndsWith(".localhost", StringComparison.Ordinal)) return true;
        foreach (var suffix in new[] { ".local", ".test", ".internal", ".lan", ".home.arpa" })
            if (name.EndsWith(suffix, StringComparison.Ordinal)) return true;
        if (!IPAddress.TryParse(name, out var address)) return false;
        if (IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            var any = b[0] == 0 && b[1] == 0 && b[2] == 0 && b[3] == 0; // 0.0.0.0: a dev server's "all interfaces" (5.6)
            return b[0] == 10 || any || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
        }
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv4MappedToIPv6) return IsDevHost(address.MapToIPv4().ToString());
            var b = address.GetAddressBytes();
            return address.IsIPv6LinkLocal || (b[0] & 0xFE) == 0xFC; // fe80::/10, fc00::/7
        }
        return false;
    }

    /// <summary>Whether <paramref name="url"/>'s host is a dev host (5.6 preview origins).</summary>
    public static bool IsDevUrl(string? url) =>
        IsWeb(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri) && IsDevHost(uri.Host);

    /// <summary>
    /// DESIGN 8.2: the source a trust strip names: the host in its ASCII form (so a lookalike Unicode name shows as what it is),
    /// with the port when it is not the scheme's default ("localhost:5173"). "New tab" for the blank page.
    /// </summary>
    public static string SourceName(string? url)
    {
        if (url is null || IsBlank(url) || url.Length == 0) return UiText.Get("visus.tabs.title_new");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Host.Length == 0) return url;
        var host = uri.HostNameType == UriHostNameType.IPv6 ? "[" + uri.IdnHost.Trim('[', ']') + "]" : uri.IdnHost;
        return uri.IsDefaultPort ? host : host + ":" + uri.Port.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The site a choice is remembered for (scheme, host and port: the origin).</summary>
    public static string? OriginOf(string? url)
    {
        if (!IsWeb(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        return uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
    }

    /// <summary>DESIGN 5.1: the site chip of a tab showing <paramref name="url"/>.</summary>
    public static SiteChip ChipFor(VisusTabKind kind, string? url)
    {
        var web = IsWeb(url);
        var plain = web && SchemeOf(url) == "http" && !IsDevUrl(url);
        return kind switch
        {
            VisusTabKind.Private => new SiteChip(SiteChipKind.Private, plain),
            VisusTabKind.Agent => new SiteChip(SiteChipKind.Agent, plain),
            _ when !web => new SiteChip(SiteChipKind.None, false),
            _ when IsDevUrl(url) => new SiteChip(SiteChipKind.Preview, SchemeOf(url) == "http"),
            _ when plain => new SiteChip(SiteChipKind.NotSecure, true),
            _ => new SiteChip(SiteChipKind.Secure, false),
        };
    }
}

/// <summary>What happens to a top-level navigation (<see cref="WebPolicy.CheckTopLevel"/>).</summary>
public enum TopLevelVerdict
{
    Allow,
    Cancel,

    /// <summary>An app's scheme: not a page; the engine's launch request decides (8.3).</summary>
    External,
}

/// <summary>DESIGN 8.3: what nth does when a page asks to open another app.</summary>
public enum ExternalLaunchDecision
{
    /// <summary>Denied with no prompt.</summary>
    Deny,

    /// <summary>"example.com wants to open Mail (mailto:)" with Open and Cancel.</summary>
    Ask,

    /// <summary>The same prompt with "Remember for this site" (the harmless schemes only).</summary>
    AskRememberable,
}

/// <summary>DESIGN 8.3: external protocols (<c>LaunchingExternalUriScheme</c>).</summary>
public static class ExternalProtocols
{
    /// <summary>
    /// Never offered (8.3: <c>ms-msdt:</c>, <c>search-ms:</c>, <c>ms-officecmd:</c>, <c>ms-appinstaller:</c>, <c>javascript:</c>,
    /// <c>vbscript:</c>, <c>file:</c>), plus the other schemes known to run code or open documents from the web
    /// (ED-NEW-web-w8a-1): the Office document handlers, the help and archive viewers, the shell and Settings, the account
    /// setup host, and the Edge hand-off that would leave Visus's profiles.
    /// </summary>
    public static IReadOnlySet<string> BlockList { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ms-msdt", "search-ms", "search", "ms-officecmd", "ms-appinstaller", "javascript", "vbscript", "file",
        "ms-word", "ms-excel", "ms-powerpoint", "ms-visio", "ms-access", "ms-project", "ms-publisher", "ms-spd", "ms-infopath",
        "ms-officeapp", "ms-cxh", "ms-cxh-full", "hcp", "ms-help", "ms-its", "its", "mk", "mhtml", "res", "shell", "ms-settings",
        "microsoft-edge", "microsoft-edge-holographic", "ms-calculator", "ms-excel-x", "ms-word-x", "ldap", "jar", "telnet",
    };

    /// <summary>The harmless schemes a choice may be remembered for, per site (8.3).</summary>
    public static IReadOnlySet<string> Rememberable { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "mailto", "tel" };

    // Schemes that are external apps even when typed without "//" (the address field: "mailto:dana@example.com").
    private static readonly HashSet<string> KnownApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "mailto", "tel", "sms", "callto", "facetime", "skype", "msteams", "zoommtg", "zoomus", "slack", "spotify", "steam",
        "discord", "vscode", "vscode-insiders", "figma", "obsidian", "notion", "webcal", "magnet", "git", "ssh", "sftp", "ftp",
        "ms-windows-store", "ms-teams", "whatsapp", "tg", "signal", "linkedin", "zotero",
    };

    // The product names (they read the same in every language); the plain words (Mail, Phone, ...) are in AppName, read when asked.
    private static readonly Dictionary<string, string> BrandNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["msteams"] = "Microsoft Teams", ["ms-teams"] = "Microsoft Teams", ["zoommtg"] = "Zoom", ["zoomus"] = "Zoom",
        ["slack"] = "Slack", ["spotify"] = "Spotify", ["steam"] = "Steam", ["discord"] = "Discord",
        ["vscode"] = "Visual Studio Code", ["vscode-insiders"] = "Visual Studio Code Insiders", ["figma"] = "Figma",
        ["obsidian"] = "Obsidian", ["notion"] = "Notion", ["skype"] = "Skype", ["ms-windows-store"] = "Microsoft Store",
        ["whatsapp"] = "WhatsApp", ["tg"] = "Telegram", ["signal"] = "Signal", ["zotero"] = "Zotero",
    };

    /// <summary>Whether the scheme is on the block list (or is no app at all: an internal page).</summary>
    public static bool IsBlocked(string? scheme) => scheme is null || BlockList.Contains(scheme) || WebPolicy.IsInternalScheme(scheme);

    /// <summary>A scheme that names an app even without "//" after it.</summary>
    public static bool IsKnownApp(string? scheme) => scheme is not null && KnownApps.Contains(scheme);

    /// <summary>
    /// DESIGN 8.3: sandboxed views and agent tabs always deny with no prompt; browse and preview tabs deny silently unless the
    /// launch comes from a user gesture, then ask, rememberable for the harmless schemes; private tabs ask and never remember.
    /// </summary>
    public static ExternalLaunchDecision Decide(WebProfileKind profile, string? uri, bool isUserInitiated)
    {
        var scheme = WebPolicy.SchemeOf(uri);
        if (profile is WebProfileKind.Agent or WebProfileKind.Artifacts) return ExternalLaunchDecision.Deny;
        if (IsBlocked(scheme) || scheme is "http" or "https") return ExternalLaunchDecision.Deny;
        if (!isUserInitiated) return ExternalLaunchDecision.Deny;
        if (profile == WebProfileKind.Private) return ExternalLaunchDecision.Ask;
        return Rememberable.Contains(scheme!) ? ExternalLaunchDecision.AskRememberable : ExternalLaunchDecision.Ask;
    }

    /// <summary>The app a scheme opens, for the prompt: the system's own name when known, else nth's, else "another app".</summary>
    public static string AppName(string scheme, Func<string, string?>? systemName = null)
    {
        if (systemName?.Invoke(scheme) is { Length: > 0 } name) return name;
        if (BrandNames.TryGetValue(scheme, out var brand)) return brand;
        return scheme.ToLowerInvariant() switch
        {
            "mailto" => UiText.Get("visus.apps.mail"),
            "tel" or "callto" => UiText.Get("visus.apps.phone"),
            "sms" => UiText.Get("visus.apps.messages"),
            "webcal" => UiText.Get("visus.apps.calendar"),
            "magnet" => UiText.Get("visus.apps.torrent_app"),
            _ => UiText.Get("visus.apps.another_app"),
        };
    }

    /// <summary>"example.com wants to open Mail (mailto:)" (8.3).</summary>
    public static string PromptTitle(string source, string appName, string scheme) => UiText.Format("visus.prompt.wants_to_open", source, appName, scheme);
}

/// <summary>DESIGN 2.3, 5.10, 8.1: what each kind of tab may keep and who may see it (the private tab rules).</summary>
public static class VisusTabRules
{
    /// <summary>The profile the tab's web view is created in.</summary>
    public static WebProfileKind Profile(VisusTabKind kind) => kind switch
    {
        VisusTabKind.Private => WebProfileKind.Private,
        VisusTabKind.Preview => WebProfileKind.Preview,
        VisusTabKind.Agent => WebProfileKind.Agent,
        _ => WebProfileKind.Browse,
    };

    /// <summary>Visits feed the address field's suggestions (browse and preview tabs only; private tabs leave nothing).</summary>
    public static bool RecordsHistory(VisusTabKind kind) => kind is VisusTabKind.Browse or VisusTabKind.Preview;

    /// <summary>A per-site choice ("Remember", zoom, permissions) may be kept (never for private or agent tabs).</summary>
    public static bool MayRemember(VisusTabKind kind) => kind is VisusTabKind.Browse or VisusTabKind.Preview;

    /// <summary>The tab is saved with the thread and comes back after a restart (private tabs never are).</summary>
    public static bool IsPersisted(VisusTabKind kind) => kind != VisusTabKind.Private;

    /// <summary>Closed tabs can be reopened (Ctrl+Shift+T); a closed private tab is gone for good.</summary>
    public static bool MayReopen(VisusTabKind kind) => kind != VisusTabKind.Private;

    /// <summary>
    /// DESIGN 5.10: the model may read the tab: its own agent tabs; a browse or preview tab only once the user shared it; a private
    /// tab never.
    /// </summary>
    public static bool ModelMaySee(VisusTabKind kind, bool sharedWithModel) => kind switch
    {
        VisusTabKind.Private => false,
        VisusTabKind.Agent => true,
        _ => sharedWithModel,
    };

    /// <summary>
    /// The kind of tab a page opens from a tab of <paramref name="opener"/> (a link to a new tab, <c>window.open</c>): private and
    /// agent tabs keep their kind; otherwise a dev host opens as a preview and anything else as a normal tab (2.3: localhost
    /// cookies live in <c>preview</c>).
    /// </summary>
    public static VisusTabKind KindForOpened(VisusTabKind opener, string url) => opener switch
    {
        VisusTabKind.Private or VisusTabKind.Agent => opener,
        _ => WebPolicy.IsDevUrl(url) ? VisusTabKind.Preview : VisusTabKind.Browse,
    };

    /// <summary>
    /// The kind of tab an address typed into a tab of <paramref name="current"/> needs: private and agent tabs stay what they are;
    /// a browse tab sends a dev host to a preview tab and a preview tab sends a public host to a normal tab.
    /// </summary>
    public static VisusTabKind KindForTyped(VisusTabKind current, string url) => KindForOpened(current, url);
}
