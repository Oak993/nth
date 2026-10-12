using Marrow.Core.Support;
using Marrow.Setup.Platform;

namespace Marrow.Setup.Flow;

/// <summary>The setup window's screens (nth installer PLAN.md 3, "Screens"; the canvas's list).</summary>
public enum SetupScreen
{
    Welcome,
    Options,
    Installing,
    Ready,
    Update,
    UpToDate,
    NewerInstalled,
    Problem,
    Uninstall,
}

/// <summary>Where the window starts and what it would install over.</summary>
/// <param name="Screen">The first screen.</param>
/// <param name="Action">InstallerGate's decision (OD-0072).</param>
/// <param name="Installed">The installed root and version, as found.</param>
/// <param name="TargetRoot">Where Setup.exe installs: the existing root for an update, else --installto or %LOCALAPPDATA%\Programs\nth.</param>
public sealed record SetupRoute(SetupScreen Screen, InstallerAction Action, InstalledRoot Installed, string TargetRoot)
{
    /// <summary>
    /// The exit code when the window is closed on its first screen without anything having run: 0 for Welcome, Update and
    /// Up to date (nothing was asked of the setup that failed), 1 for Newer installed (OD-0072's refusal) and for Problem.
    /// </summary>
    public int ExitCodeWhenClosed => SetupFlow.ExitCodeWhenClosed(Screen);
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3, steps 3 and 5; unit U1, ED-NEW-inst-u1-1). Chooses the first screen from what is
/// installed, with InstallerGate's unchanged decision: nothing installed is Welcome, older is Update, the same version is
/// Up to date (exit 0), newer is Newer installed (exit 1). The installed root comes from the uninstall entry, so an install
/// at %LOCALAPPDATA%\NthApp (1.0.0) is updated where it is and never duplicated at the new root. A setup whose own
/// signature does not hold goes to Problem instead and installs nothing.
/// </summary>
public static class SetupFlow
{
    /// <summary>A fresh install's folder under %LOCALAPPDATA% (PLAN.md 2, override 10): Windows' usual per-user program folder.</summary>
    public const string FreshRoot = @"Programs\nth";

    public static SetupRoute Route(
        string installerVersion,
        string packageId,
        IReadOnlyList<string> arguments,
        string launchDirectory,
        string localAppData,
        IUninstallKeys keys)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var installTo = InstallerGate.ReadInstallTo(arguments, launchDirectory);
        var installed = InstalledRoot.Find(keys, packageId, Path.Combine(localAppData, packageId), installTo);
        var action = InstallerGate.Decide(installed.Version, installerVersion);
        var screen = IsUninstall(arguments) ? SetupScreen.Uninstall : action switch
        {
            InstallerAction.Install => SetupScreen.Welcome,
            InstallerAction.Upgrade => SetupScreen.Update,
            InstallerAction.AlreadyUpToDate => SetupScreen.UpToDate,
            InstallerAction.RefuseNewerInstalled => SetupScreen.NewerInstalled,
            _ => throw new InvalidOperationException("Unknown installer action."),
        };
        var target = action == InstallerAction.Install ? installTo ?? Path.Combine(localAppData, FreshRoot) : installed.Root;
        return new SetupRoute(screen, action, installed, target);
    }

    /// <summary>
    /// <c>--uninstall</c> (before any <c>--</c>): uninstall mode, which <c>nth Uninstall.exe</c> is started with (PLAN.md 7; the
    /// caption says "Uninstall nth"). Unit U6 builds the mode itself.
    /// </summary>
    public static bool IsUninstall(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        foreach (var argument in arguments)
        {
            if (argument == "--") return false;
            if (string.Equals(argument, "--uninstall", StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>The route when what is installed cannot be read or the setup's own version is not valid: Problem, nothing to install over.</summary>
    public static SetupRoute Problem(string localAppData)
    {
        var root = Path.Combine(localAppData, FreshRoot);
        return new SetupRoute(SetupScreen.Problem, InstallerAction.Install, new InstalledRoot(root, null, false), root);
    }

    /// <summary>A setup whose signature is there but does not hold shows Problem ("has been changed") and installs nothing.</summary>
    public static bool BlocksInstall(SignatureResult signature) => signature.State == SignatureState.Invalid;

    public static int ExitCodeWhenClosed(SetupScreen screen) => screen switch
    {
        SetupScreen.NewerInstalled => InstallerGate.RefusedExitCode,
        SetupScreen.Problem => 1,
        _ => 0,
    };
}
