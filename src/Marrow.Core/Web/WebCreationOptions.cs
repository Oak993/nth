namespace Marrow.Core.Web;

// Web surfaces W7 (DESIGN 2.1, 2.3; ED-NEW-web-w7-1). The contracts the UI host (Marrow.UI/Web/WebViewHost) and the web
// engine layer (Marrow.Services/Web) share. They live in Core because the layer table (ED-064) keeps UI and Services
// apart: the host may not reference the engine, and the engine may not reference Avalonia.

/// <summary>DESIGN 2.3: the five profiles. Each keeps its own cookies, cache and storage.</summary>
public enum WebProfileKind
{
    /// <summary>Normal Visus tabs; persistent (the user's logins live here).</summary>
    Browse,

    /// <summary>Project previews; persistent, apart from Browse because localhost cookies are not port-isolated.</summary>
    Preview,

    /// <summary>Tabs the model opens itself; persistent per thread folder.</summary>
    Agent,

    /// <summary>Tabula, inline HTML widgets and MCP Apps; InPrivate, ephemeral.</summary>
    Artifacts,

    /// <summary>The user's private tabs; InPrivate, ephemeral.</summary>
    Private,
}

/// <summary>
/// DESIGN 2.1: everything that is fixed when the WebView2 environment and controller are created, before
/// <c>AdapterCreated</c>. <see cref="UserDataFolder"/>, <see cref="Language"/> and <see cref="AdditionalBrowserArguments"/>
/// are the environment's options and are identical for every view (2.3: "all environments use identical options, or
/// the second one fails"); <see cref="ProfileName"/> and <see cref="IsInPrivate"/> are the controller's options.
/// </summary>
public sealed record WebCreationOptions(
    WebProfileKind Kind,
    string ProfileName,
    bool IsInPrivate,
    string UserDataFolder,
    string Language,
    string? AdditionalBrowserArguments,
    bool EnableDevTools)
{
    /// <summary>The environment half of the options: two views share one browser process group only when these match.</summary>
    public (string UserDataFolder, string Language, string? AdditionalBrowserArguments) EnvironmentKey =>
        (UserDataFolder, Language, AdditionalBrowserArguments);
}
