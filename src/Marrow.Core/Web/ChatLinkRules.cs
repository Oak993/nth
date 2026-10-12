using System.Globalization;

// PORT-ONLY: web surfaces W10, links from chats (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.11 "Setting: Settings > Visus >
// 'Open links from chats in': Ask the first time (default), Visus, Default browser", "Ctrl+click always opens the other one",
// "Always the system browser: MCP OAuth, nth's own account sign-ins, update and store links, mailto: and other non-web schemes",
// "Local file links keep MarkdownLinkOpener"; ED-NEW-web-w10-2). No Swift counterpart. The rule as a pure function of the reader's
// choice and the address, so the router, the settings page and the tests share one table.

namespace Marrow.Core.Web;

/// <summary>Where a link from a chat goes. <see cref="Local"/> is never a setting: it is where a path or file link goes.</summary>
public enum ChatLinkTarget
{
    /// <summary>Ask the first time (the default): nothing opens until the reader chooses.</summary>
    Ask,

    /// <summary>Visus, the in-app browser.</summary>
    Visus,

    /// <summary>The PC's default browser (or the app that handles a non-web scheme).</summary>
    DefaultBrowser,

    /// <summary>A file on this PC: MarkdownLinkOpener reveals it or opens it in the editor.</summary>
    Local,
}

/// <summary>Settings › Tabula › "Artifacts may use your model" (DESIGN 9).</summary>
public enum ArtifactModelChoice
{
    /// <summary>Ask each artifact (the default): the first call shows a consent card.</summary>
    AskEachArtifact,

    /// <summary>Never: an artifact's call to the chat's model is refused.</summary>
    Never,
}

/// <summary>DESIGN 5.11: which browser a link from a chat opens in.</summary>
public static class ChatLinkRules
{
    // A sign-in page keeps the PC's own browser, where the reader's passwords and passkeys are. The path words are whole path
    // segments only ("/login", "/oauth/authorize", never "/guides/login-flow"), so a page about signing in is an ordinary link.
    private static readonly HashSet<string> SignInSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "login", "log-in", "signin", "sign-in", "sso", "oauth", "oauth2", "authorize", "authenticate",
    };

    // The well-known identity hosts: a link to one of them is a sign-in whatever its path says.
    private static readonly HashSet<string> SignInHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "accounts.google.com", "login.microsoftonline.com", "login.live.com", "appleid.apple.com", "id.apple.com",
    };

    /// <summary>
    /// What a link is, for the routing: a web page (http or https with a host), a file or path on this PC (no scheme, a drive letter or
    /// <c>file:</c>), or anything else (<c>mailto:</c>, <c>tel:</c> and the other apps' schemes).
    /// </summary>
    public static ChatLinkKind KindOf(string? link)
    {
        if (WebPolicy.IsWeb(link)) return ChatLinkKind.Web;
        var scheme = WebPolicy.SchemeOf(link);
        if (scheme is null || scheme.Length == 1 || scheme == "file") return ChatLinkKind.Local;
        return ChatLinkKind.Other;
    }

    /// <summary>
    /// Whether the address is a sign-in: an OAuth authorization request (a client id with a response type, a redirect or a scope), a
    /// path segment that names signing in, or a well-known identity host. These stay in the default browser whatever the setting says,
    /// and Ctrl+click does not move them.
    /// </summary>
    public static bool IsSignIn(string? link)
    {
        if (!WebPolicy.IsWeb(link) || !Uri.TryCreate(link, UriKind.Absolute, out var uri)) return false;
        if (SignInHosts.Contains(uri.IdnHost)) return true;
        foreach (var segment in uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
            if (SignInSegments.Contains(Uri.UnescapeDataString(segment))) return true;
        var query = QueryNames(uri.Query);
        return query.Contains("client_id") && (query.Contains("response_type") || query.Contains("redirect_uri") || query.Contains("scope"));
    }

    /// <summary>
    /// Where a link goes. A caller that knows the link is a sign-in (<paramref name="forcedExternal"/>) always gets the default browser;
    /// a path or file goes to <see cref="ChatLinkTarget.Local"/>; another scheme to the default browser (the system's handler); a
    /// web page follows the reader's <paramref name="preference"/>, and Ctrl+click (<paramref name="control"/>) takes the other
    /// browser for this one link. Ask stays Ask: nothing opens until the reader chooses.
    /// </summary>
    public static ChatLinkTarget Destination(ChatLinkTarget preference, string? link, bool control, bool forcedExternal)
    {
        if (forcedExternal) return ChatLinkTarget.DefaultBrowser;
        switch (KindOf(link))
        {
            case ChatLinkKind.Local: return ChatLinkTarget.Local;
            case ChatLinkKind.Other: return ChatLinkTarget.DefaultBrowser;
        }
        if (IsSignIn(link)) return ChatLinkTarget.DefaultBrowser;
        var chosen = preference == ChatLinkTarget.Local ? ChatLinkTarget.Ask : preference;
        return control ? Other(chosen) : chosen;
    }

    /// <summary>The other browser: Visus for the default browser and the default browser for Visus. Ask has none.</summary>
    public static ChatLinkTarget Other(ChatLinkTarget target) => target switch
    {
        ChatLinkTarget.Visus => ChatLinkTarget.DefaultBrowser,
        ChatLinkTarget.DefaultBrowser => ChatLinkTarget.Visus,
        _ => target,
    };

    /// <summary>Parses a stored choice; anything that is not one of the three settings reads as Ask.</summary>
    public static ChatLinkTarget ParseSetting(string? text) =>
        Enum.TryParse<ChatLinkTarget>(text, ignoreCase: false, out var value) && value is ChatLinkTarget.Visus or ChatLinkTarget.DefaultBrowser
            ? value : ChatLinkTarget.Ask;

    private static HashSet<string> QueryNames(string query)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = pair.Split('=', 2)[0];
            if (name.Length > 0) names.Add(Uri.UnescapeDataString(name).ToLower(CultureInfo.InvariantCulture));
        }
        return names;
    }
}

/// <summary>What a link points at.</summary>
public enum ChatLinkKind
{
    /// <summary>An http or https page.</summary>
    Web,

    /// <summary>A path, a drive-letter path or a <c>file:</c> address.</summary>
    Local,

    /// <summary>Another scheme: <c>mailto:</c>, <c>tel:</c> and the like.</summary>
    Other,
}
