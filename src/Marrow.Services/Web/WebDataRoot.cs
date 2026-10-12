using Marrow.Core.Support;

namespace Marrow.Services.Web;

/// <summary>
/// DESIGN 2.3 (W23): where all of Visus's browser data lives, <c>%LOCALAPPDATA%\nth\WebView2</c>: every profile's cookies, cache and
/// storage. One resolution for both users of the folder: <see cref="WebProfiles.DefaultUserDataFolder"/> (the engine) and the
/// Velopack uninstall hook (<see cref="WebDataUninstall"/>), which runs before any binding of the app exists. It goes through
/// <see cref="DevDataRoot"/> like the rest of the data root (ED-212), so a development run against a throwaway data root keeps
/// its web data there, and the owner's real folder is untouched.
/// </summary>
public static class WebDataRoot
{
    /// <summary>The folder's name under the data root; the uninstall hook refuses any other.</summary>
    public const string FolderName = "WebView2";

    public static string Resolve() => Path.Combine(DevDataRoot.LocalApplicationData(), AppInfo.DataFolderName, FolderName);
}
