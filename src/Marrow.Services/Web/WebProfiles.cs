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
/// DESIGN 2.3 (ED-NEW-web-W7-2): one user data folder (<c>%LOCALAPPDATA%\nth\WebView2</c>), one browser process group,
/// identical environment options for every view, and a profile per surface kind. The model never inherits
/// <c>browse</c>: agent tabs get their own persistent profile per thread folder, and sandboxed content and private tabs
/// are InPrivate.
/// </summary>
public sealed class WebProfiles
{
    public const string BrowseProfile = "browse";
    public const string PreviewProfile = "preview";
    public const string AgentProfilePrefix = "agent-";
    public const string ArtifactsProfile = "artifacts";
    public const string PrivateProfile = "private";

    /// <summary>The browser arguments every environment shares (none: SmartScreen and the defaults stay on, DESIGN 5.4).</summary>
    public const string? BrowserArguments = null;

    /// <param name="userDataFolder">The one user data folder; <see cref="DefaultUserDataFolder"/> in the app.</param>
    /// <param name="language">The UI language every environment is created with (one value for the whole process).</param>
    public WebProfiles(string userDataFolder, string language)
    {
        ArgumentException.ThrowIfNullOrEmpty(userDataFolder);
        ArgumentException.ThrowIfNullOrEmpty(language);
        UserDataFolder = userDataFolder;
        Language = language;
    }

    /// <summary><c>%LOCALAPPDATA%\nth\WebView2</c> (DESIGN 2.3; the Velopack uninstall hook removes it, W23).</summary>
    public static string DefaultUserDataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "nth", "WebView2");

    public string UserDataFolder { get; }

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
    /// The creation options for a surface. Every result shares <see cref="UserDataFolder"/>, <see cref="Language"/> and
    /// <see cref="BrowserArguments"/>; only the profile name, InPrivate and DevTools differ.
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
