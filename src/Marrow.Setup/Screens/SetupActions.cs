using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Marrow.App.Port;
using Marrow.Core.Support;
using Marrow.Setup.Port;

namespace Marrow.Setup.Screens;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3 and 8; unit U2, ED-NEW-inst-u2-1). What the screens do outside the window: open
/// nth, copy Problem's details, show the licence, pick a folder. Tests use a fake; nothing here installs anything.
/// </summary>
public interface ISetupActions
{
    /// <summary>Starts nth from <paramref name="installRoot"/> (Open nth). False when it could not be started.</summary>
    bool OpenApp(string installRoot);

    /// <summary>Puts <paramref name="text"/> on the clipboard (Copy details).</summary>
    Task<bool> CopyAsync(TopLevel window, string text);

    /// <summary>Shows the GNU AGPL v3.0 text nth ships under (View license).</summary>
    bool ViewLicense();

    /// <summary>Lets the user pick a folder (Options' Change…); null when cancelled.</summary>
    Task<string?> PickFolderAsync(TopLevel window, string current);
}

/// <summary>The real actions: Windows' shell, the window's clipboard and folder picker.</summary>
public sealed class WindowsSetupActions : ISetupActions
{
    public const string LicenseResource = "SetupLicense.txt";

    /// <summary>The licence copy View license writes, in nth Setup's own folder under the temp folder.</summary>
    public const string LicenseCopyName = "LICENSE.txt";

    /// <summary>How long Open nth waits for Explorer to start nth for an elevated setup.</summary>
    public static readonly TimeSpan OpenPatience = TimeSpan.FromSeconds(5);

    private readonly string tempPath;

    public WindowsSetupActions() : this(Path.GetTempPath())
    {
    }

    internal WindowsSetupActions(string tempPath) => this.tempPath = tempPath;

    /// <summary>
    /// Open nth (PLAN.md 8a; ED-NEW-inst-fix-4): the launch target copied from Velopack's Start shortcut (unit U5's
    /// <see cref="DesktopShortcut.LaunchTarget(string, ShellLink?, Func{string, bool}, Func{string, string})"/>, the same one the
    /// desktop shortcut gets), started as the signed-in user: through Explorer when this setup runs elevated.
    /// </summary>
    public bool OpenApp(string installRoot) =>
        Open(ReadLaunchTarget(installRoot), Environment.IsPrivilegedProcess, File.Exists, StartDirect, launch => UserShellStart.Start(launch, OpenPatience));

    /// <summary>
    /// The decision Open nth makes: nothing to start, no start; an elevated setup starts nth only through Explorer (never with
    /// its own admin token, even when Explorer cannot be reached); otherwise it starts it itself.
    /// </summary>
    internal static bool Open(LaunchTarget launch, bool elevated, Func<string, bool> fileExists, Func<LaunchTarget, bool> startDirect, Func<LaunchTarget, bool> startAsUser)
    {
        ArgumentNullException.ThrowIfNull(launch);
        if (!fileExists(launch.Target)) return false;
        return elevated ? startAsUser(launch) : startDirect(launch);
    }

    /// <summary>Start's launch target when the shortcut can be read, else <c>&lt;root&gt;\current\nth.exe</c> (DesktopShortcut's own fallback).</summary>
    internal static LaunchTarget ReadLaunchTarget(string installRoot)
    {
        try
        {
            using var shell = new ShellLinks();
            return new DesktopShortcut(shell, installRoot).CurrentLaunchTarget(ShellLinks.CallTimeout);
        }
        catch (Exception error) when (DesktopShortcut.IsShellFailure(error))
        {
            return DesktopShortcut.LaunchTarget(installRoot, null, File.Exists, Environment.ExpandEnvironmentVariables);
        }
    }

    private static bool StartDirect(LaunchTarget launch)
    {
        try
        {
            var start = new ProcessStartInfo(launch.Target) { UseShellExecute = true, Arguments = launch.Arguments };
            if (launch.WorkingDirectory.Length > 0) start.WorkingDirectory = launch.WorkingDirectory;
            using var started = Process.Start(start);
            return started is not null;
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }

    public async Task<bool> CopyAsync(TopLevel window, string text)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.Clipboard is not { } clipboard) return false;
        try
        {
            await clipboard.SetTextAsync(text);
            return true;
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    /// <summary>
    /// Writes the embedded licence to <c>%TEMP%\nth Setup\LICENSE.txt</c> and opens it with the shell. The copy is removed again
    /// when the window closes (<see cref="RemoveLicenseCopy"/>, VELOPACK_CHECK.md finding 8; ED-NEW-inst-fix-3); one fixed name,
    /// so even a setup that ends without closing leaves at most this one file, which the next setup's close removes.
    /// </summary>
    public bool ViewLicense()
    {
        try
        {
            if (WriteLicenseCopy(tempPath) is not { } path) return false;
            using var opened = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary><c>&lt;temp&gt;\nth Setup\LICENSE.txt</c>.</summary>
    public static string LicenseCopyPath(string tempPath) => Path.Combine(tempPath, InstallOptions.SetupFolderName, LicenseCopyName);

    /// <summary>Writes the licence copy; null when this build carries no licence.</summary>
    internal static string? WriteLicenseCopy(string tempPath)
    {
        using var stream = typeof(WindowsSetupActions).Assembly.GetManifestResourceStream(LicenseResource);
        if (stream is null) return null;
        var path = LicenseCopyPath(tempPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var file = File.Create(path)) stream.CopyTo(file);
        return path;
    }

    /// <summary>
    /// Removes the licence copy, and nth Setup's temp folder when nothing else is in it. An editor has read the text by then
    /// (Notepad keeps no handle on the file); a copy that cannot be removed now is removed by the next setup's close.
    /// </summary>
    public static void RemoveLicenseCopy(string tempPath)
    {
        var path = LicenseCopyPath(tempPath);
        try
        {
            if (File.Exists(path)) File.Delete(path);
            var folder = Path.GetDirectoryName(path)!;
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // kept for the next close (ED-NEW-inst-fix-3): one small file in the user's own temp folder, never worth a message
        }
    }

    public async Task<string?> PickFolderAsync(TopLevel window, string current)
    {
        ArgumentNullException.ThrowIfNull(window);
        IStorageFolder? start = null;
        try
        {
            var parent = Directory.Exists(current) ? current : Path.GetDirectoryName(current);
            if (parent is not null && Directory.Exists(parent)) start = await window.StorageProvider.TryGetFolderFromPathAsync(parent);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            start = null;
        }
        var picked = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false, SuggestedStartLocation = start });
        return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
    }
}
