using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Marrow.Core.Web;

namespace Marrow.Services.Web;

/// <summary>DESIGN 2.3: who a web view is for. Each kind maps to one profile.</summary>
public enum WebSurfaceKind
{
    /// <summary>A normal Visus tab: <see cref="WebProfileKind.Browse"/>.</summary>
    VisusTab,

    /// <summary>A private Visus tab: <see cref="WebProfileKind.Private"/>, InPrivate.</summary>
    VisusPrivateTab,

    /// <summary>A project preview tab: <see cref="WebProfileKind.Preview"/>.</summary>
    PreviewTab,

    /// <summary>A tab the model opened itself: <see cref="WebProfileKind.Agent"/>, per thread folder.</summary>
    AgentTab,

    /// <summary>A Tabula artifact: <see cref="WebProfileKind.Artifacts"/>, InPrivate.</summary>
    Artifact,

    /// <summary>An inline HTML widget in the thread: <see cref="WebProfileKind.Artifacts"/>, InPrivate.</summary>
    InlineWidget,

    /// <summary>An MCP App: <see cref="WebProfileKind.Artifacts"/>, InPrivate.</summary>
    McpApp,
}

/// <summary>
/// DESIGN 2.3 (ED-NEW-web-w7-2): one user data folder (<c>%LOCALAPPDATA%\nth\WebView2</c>), one browser process group,
/// identical environment options for every view, and a profile per surface kind. The model never inherits
/// <c>browse</c>: agent tabs get their own persistent profile per thread folder, and sandboxed content and private tabs
/// are InPrivate. ED-NEW-web-sec-res-1: the sandboxed views (artifacts, inline widgets, MCP Apps) are the one exception to
/// the shared environment: a second browser process group on its own folder inside that one, whose arguments block WebRTC.
/// </summary>
public sealed class WebProfiles
{
    public const string BrowseProfile = "browse";
    public const string PreviewProfile = "preview";
    public const string AgentProfilePrefix = "agent-";
    public const string ArtifactsProfile = "artifacts";
    public const string PrivateProfile = "private";

    /// <summary>
    /// The browser arguments every environment shares (DESIGN 6.5, ED-NEW-web-w13-1): site isolation kept on for every
    /// site, so each sandboxed surface (its own site under <c>.invalid</c>) gets its own renderer process; and WebRTC
    /// never sends UDP outside a proxy, which closes the WebRTC channel CSP cannot (harmless for browsing). Nothing else:
    /// SmartScreen and the defaults stay on (DESIGN 5.4). ED-NEW-web-sec-rev-4: the UDP block in both spellings WebView2 runtimes read,
    /// with the same value: runtime 154 reads only the command-line preference <c>--webrtc-ip-handling-policy</c>, older runtimes
    /// <c>--force-webrtc-ip-handling-policy</c>, and each ignores the one it does not know.
    /// </summary>
    public const string BrowserArguments =
        "--site-per-process --force-webrtc-ip-handling-policy=disable_non_proxied_udp --webrtc-ip-handling-policy=disable_non_proxied_udp";

    /// <summary>
    /// ED-NEW-web-sec-res-1: the sandboxed views' own environment folder, under the one user data folder (so the uninstall hook
    /// and "Clear browsing data" reach it with the rest): <c>%LOCALAPPDATA%\nth\WebView2\Sandbox</c>.
    /// </summary>
    public const string SandboxFolderName = "Sandbox";

    /// <summary>
    /// ED-NEW-web-sec-res-1: the browser arguments of the sandboxed views' environment (artifacts, inline widgets, MCP Apps).
    /// The page-script hooks cannot catch every way a frame reaches WebRTC (the indexed setters <c>select[i] = option</c> and
    /// <c>select.options[i] = option</c>; a script inside the insertion that adds a frame with a src), so WebRTC is blocked at the
    /// engine too: no WebRTC UDP outside a proxy (no host, server-reflexive or UDP relay candidates, no STUN). WebView2 has no switch
    /// that turns WebRTC off; this policy is the strongest one its runtimes read. Runtime 154 reads it only as the command-line
    /// preference <c>--webrtc-ip-handling-policy</c> (the string <c>force-webrtc-ip-handling-policy</c> is not in its msedge.dll);
    /// older runtimes read <c>--force-webrtc-ip-handling-policy</c>; each ignores the spelling it does not know, so both are passed.
    /// Visus keeps <see cref="BrowserArguments"/>, which carry the same switches since ED-NEW-web-sec-rev-4; the two environments
    /// stay apart by their folders.
    /// </summary>
    public const string SandboxBrowserArguments =
        "--site-per-process --force-webrtc-ip-handling-policy=disable_non_proxied_udp --webrtc-ip-handling-policy=disable_non_proxied_udp";

    /// <param name="userDataFolder">The one user data folder; <see cref="DefaultUserDataFolder"/> in the app.</param>
    /// <param name="language">The UI language every environment is created with (one value for the whole process).</param>
    public WebProfiles(string userDataFolder, string language)
    {
        ArgumentException.ThrowIfNullOrEmpty(userDataFolder);
        ArgumentException.ThrowIfNullOrEmpty(language);
        UserDataFolder = userDataFolder;
        Language = language;
    }

    /// <summary><c>%LOCALAPPDATA%\nth\WebView2</c> (DESIGN 2.3; the Velopack uninstall hook removes it, W23: <see cref="WebDataRoot"/>).</summary>
    public static string DefaultUserDataFolder => WebDataRoot.Resolve();

    public string UserDataFolder { get; }

    /// <summary>ED-NEW-web-sec-res-1: the sandboxed views' environment folder (<see cref="SandboxFolderName"/> in <see cref="UserDataFolder"/>).</summary>
    public string SandboxUserDataFolder => Path.Combine(UserDataFolder, SandboxFolderName);

    public string Language { get; }

    /// <summary>The profile a surface kind uses.</summary>
    public static WebProfileKind ProfileFor(WebSurfaceKind kind) => kind switch
    {
        WebSurfaceKind.VisusTab => WebProfileKind.Browse,
        WebSurfaceKind.VisusPrivateTab => WebProfileKind.Private,
        WebSurfaceKind.PreviewTab => WebProfileKind.Preview,
        WebSurfaceKind.AgentTab => WebProfileKind.Agent,
        WebSurfaceKind.Artifact or WebSurfaceKind.InlineWidget or WebSurfaceKind.McpApp => WebProfileKind.Artifacts,
    };

    /// <summary>
    /// The creation options for a surface. Visus, preview and agent tabs share <see cref="UserDataFolder"/>,
    /// <see cref="Language"/> and <see cref="BrowserArguments"/>; only the profile name, InPrivate and DevTools differ.
    /// ED-NEW-web-sec-res-1: the sandboxed kinds share an environment of their own instead: <see cref="SandboxUserDataFolder"/>
    /// and <see cref="SandboxBrowserArguments"/>, in the same language.
    /// </summary>
    /// <param name="kind">The surface.</param>
    /// <param name="threadFolder">For agent tabs: the thread's folder, which names the agent profile.</param>
    public WebCreationOptions For(WebSurfaceKind kind, string? threadFolder = null)
    {
        var profile = ProfileFor(kind);
        var (name, inPrivate) = profile switch
        {
            WebProfileKind.Browse => (BrowseProfile, false),
            WebProfileKind.Preview => (PreviewProfile, false),
            WebProfileKind.Agent => (AgentProfileName(threadFolder ?? throw new ArgumentNullException(nameof(threadFolder), "An agent tab needs its thread folder.")), false),
            WebProfileKind.Artifacts => (ArtifactsProfile, true),
            WebProfileKind.Private => (PrivateProfile, true),
        };

        // DESIGN 5.8: "Open DevTools" is for preview tabs only.
        if (profile == WebProfileKind.Artifacts)
            return new WebCreationOptions(profile, name, inPrivate, SandboxUserDataFolder, Language, SandboxBrowserArguments, EnableDevTools: false);
        return new WebCreationOptions(profile, name, inPrivate, UserDataFolder, Language, BrowserArguments, EnableDevTools: kind == WebSurfaceKind.PreviewTab);
    }

    /// <summary>
    /// The agent profile for a thread folder: "agent-" and 16 hex digits of the SHA-256 of the folder's full path,
    /// lower-cased (Windows paths are case-insensitive), so the same folder always reaches the same cookies and no
    /// path text lands on disk.
    /// </summary>
    public static string AgentProfileName(string threadFolder)
    {
        ArgumentException.ThrowIfNullOrEmpty(threadFolder);
        var full = Path.GetFullPath(threadFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(full));
        return AgentProfilePrefix + Convert.ToHexString(hash, 0, 8).ToLower(CultureInfo.InvariantCulture);
    }

    /// <summary>True for any agent profile name ("Forget agent data" clears all of them).</summary>
    public static bool IsAgentProfile(string profileName) => profileName.StartsWith(AgentProfilePrefix, StringComparison.Ordinal);
}
