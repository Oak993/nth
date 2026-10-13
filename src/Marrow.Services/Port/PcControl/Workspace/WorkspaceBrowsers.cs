// PORT-ONLY: Tactus, the separate workspace (unit P18, preview; ED-NEW-pcws-9; DESIGN.md 2.13). Browsers in the workspace
// use a Tactus profile (`--user-data-dir` under nth's data). The user's own profile is locked by their browser on the
// desk, so a fresh copy inside would hand the start back to it; a profile of Tactus's own opens a real browser in the
// workspace, and its DevTools port (R0) is Tactus's to use. Only the Chromium browsers nth knows get it, by exe or by the
// AppsFolder id they register; every other app starts as on the desk, with no arguments.
namespace Marrow.Services.Port.PcControl.Workspace;

public static class WorkspaceBrowserProfile
{
    // The folder under the profiles root, per browser exe and per AppsFolder id (the ids are checked in NP-6).
    private static readonly Dictionary<string, string> Exes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome.exe"] = "Chrome",
        ["msedge.exe"] = "Edge",
        ["brave.exe"] = "Brave",
    };

    private static readonly Dictionary<string, string> AppIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Chrome"] = "Chrome",
        ["MSEdge"] = "Edge",
        ["Brave"] = "Brave",
    };

    /// <summary>The arguments that start this target with Tactus's own profile; null for anything but a known browser.</summary>
    public static string? Arguments(string target, string root)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root) || root.IndexOfAny(['"', '\r', '\n', '\0']) >= 0) return null;
        var text = target.Trim();
        var name = Path.IsPathFullyQualified(text)
            ? Exes.GetValueOrDefault(Path.GetFileName(text))
            : AppIds.GetValueOrDefault(text);
        return name is null ? null : "--user-data-dir=\"" + Path.Combine(root, name) + "\"";
    }
}

/// <summary>The agent's launcher: every app as on the desk, a known browser with Tactus's own profile.</summary>
public sealed class WorkspaceLauncher(IAppLauncher inner, string profiles) : IAppLauncher
{
    public IReadOnlyList<PcInstalledApp> Installed() => inner.Installed();

    public PcLaunch Launch(string appIdOrPath) => inner.Launch(appIdOrPath, WorkspaceBrowserProfile.Arguments(appIdOrPath, profiles));
}
