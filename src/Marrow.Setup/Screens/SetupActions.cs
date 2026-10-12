using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;

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

    /// <summary>
    /// STUB(U5): Open nth starts <c>&lt;root&gt;\current\nth.exe</c> (PLAN.md 8a's second default) until unit U5's resolved
    /// launch target (copied from Velopack's Start shortcut) is shared with the setup.
    /// </summary>
    public static string LaunchTarget(string installRoot) => Path.Combine(installRoot, "current", "nth.exe");

    public bool OpenApp(string installRoot)
    {
        var target = LaunchTarget(installRoot);
        if (!File.Exists(target)) return false;
        try
        {
            using var started = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(target) ?? installRoot });
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

    /// <summary>Writes the embedded licence to <c>%TEMP%\nth Setup\LICENSE.txt</c> and opens it with the shell.</summary>
    public bool ViewLicense()
    {
        try
        {
            using var stream = typeof(WindowsSetupActions).Assembly.GetManifestResourceStream(LicenseResource);
            if (stream is null) return false;
            var folder = Path.Combine(Path.GetTempPath(), "nth Setup");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "LICENSE.txt");
            using (var file = File.Create(path)) stream.CopyTo(file);
            using var opened = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            return false;
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
