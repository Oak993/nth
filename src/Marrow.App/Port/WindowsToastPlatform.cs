// Source: DroppyCode/App/AppDelegate.swift (B3 52bc792), :15, :77-92;
// App/AppModel.swift :1261-1268; App/UpdateChecker.swift :249-261.
// PORT-ONLY: OD-0028, ED-220 (1)-(3), (6)-(7). The Windows SDK is used only in this production adapter.
using System.Runtime.InteropServices;
using Avalonia.Controls.ApplicationLifetimes;
using Marrow.App.Views.Settings;
using Marrow.App.Window;
using Marrow.Core.Support;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using Avalonia.Platform;
using Marrow.Services.Port;

namespace Marrow.App.Port;

public sealed class WindowsToastPlatform : IWindowsToastPlatform
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    public void SetProcessAppId(string appId) => Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(appId));

    public string? ReadRegistry(string key, string name)
    {
        using var registry = Registry.CurrentUser.OpenSubKey(key, writable: false);
        return registry?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    public void WriteRegistry(string key, string name, string value)
    {
        using var registry = Registry.CurrentUser.CreateSubKey(key, writable: true);
        registry.SetValue(name, value, RegistryValueKind.String);
    }

    public void DeleteRegistryTree(string key)
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
    }

    public void Show(string appId, string tag, string xml)
    {
        var document = new XmlDocument();
        document.LoadXml(xml);
        var toast = new ToastNotification(document) { Tag = tag };
        ToastNotificationManager.CreateToastNotifier(appId).Show(toast);
    }

    // PORT-ONLY (AS-9, ED-NEW-dextra-as9-6): a decision that was answered in the app takes its toast out of the action center, so no stale
    // button waits there. The group is empty because Show sets none. Best effort: a refusal leaves a stale toast, which nth's own check refuses.
    public void Remove(string appId, string tag)
    {
        try { ToastNotificationManager.History.Remove(tag, string.Empty, appId); }
        catch (Exception error) when (error is COMException or ArgumentException or InvalidOperationException or UnauthorizedAccessException) { }
    }
}

public sealed class AvaloniaToastActivationWindows : IToastActivationWindows
{
    public static readonly AvaloniaToastActivationWindows Instance = new();
    private AvaloniaToastActivationWindows() { }
    public void ShowMain() => WindowManager.Shared.ShowMain();
    public void Activate()
    {
        // AppDelegate.swift:89 NSApp.activate(). Do not show a hidden main window for an update (:85).
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            (desktop.Windows.FirstOrDefault(w => w.IsActive) ?? desktop.Windows.LastOrDefault(w => w.IsVisible))?.Activate();
    }
    public void ShowAbout() => WindowManager.Shared.ShowSettings(page: SettingsPage.About);
}

public static class WindowsToastStartup
{
    private static IReadOnlyList<string> launchArguments = [];
    public static ToastBuildIdentity Identity
    {
        get
        {
            var identity = ToastBuildIdentity.ForLaunch(IsDebug,
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(DevDataRoot.VariableName)), launchArguments);
            return DevDataRoot.IsRedirected ? identity with { DevRoot = DevDataRoot.LocalApplicationData() } : identity;
        }
    }
#if DEBUG
    private const bool IsDebug = true;
#else
    private const bool IsDebug = false;
#endif

    public static void RestoreDevRoot(IReadOnlyList<string> arguments)
    {
        launchArguments = arguments.ToArray(); // identity travels with a protocol launch on either build channel
#if DEBUG
        // A protocol launch is a fresh Explorer process. Restore ED-212's explicit, already registered root before
        // its validation and lock selection, never a fallback to real data. Release retains ED-212's no-redirect rule.
        for (var index = 0; index + 1 < arguments.Count; index++)
            if (string.Equals(arguments[index], "--notification-data-root", StringComparison.Ordinal))
                Environment.SetEnvironmentVariable(DevDataRoot.VariableName, arguments[index + 1]);
        // Prefer the notification's origin over a scheme command installed by another dev instance later.
        var devIdentity = ToastBuildIdentity.ForBuild(debug: true, dev: false);
        foreach (var argument in arguments)
            if (ToastActivation.DevRoot(devIdentity, argument) is { } root)
                Environment.SetEnvironmentVariable(DevDataRoot.VariableName, root);
#endif
    }

    public static void Bind() => Bind(BindPlatform);

    internal static void Bind(Action bindPlatform)
    {
        // T44: copied-profile diagnostics must never redirect the owner's installed protocol or post OS toasts.
        PerformanceProfileMaintenance.Run(bindPlatform);
    }

    private static void BindPlatform()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("No application executable path.");
        var platform = new WindowsToastPlatform();
        // IconUri is a shell-readable image path. Materialize the owner's existing embedded PNG in this app's
        // own settings directory; a fake file seam tests the copy/change decisions. No extra project-file edit.
        using var resource = new StandardAssetLoader().Open(new Uri("avares://Marrow.App/Assets/icon.png"));
        using var png = new MemoryStream();
        resource.CopyTo(png);
        var icon = ToastIcon.Install(new WindowsToastIconFiles(),
            Path.Combine(Path.GetDirectoryName(SettingsStore.ProductionPath(DevDataRoot.LocalApplicationData()))!, "notification-icon.png"), png.ToArray());
        var root = DevDataRoot.IsRedirected ? DevDataRoot.LocalApplicationData() : null;
        WindowsToastRegistration.Register(platform, Identity, executable, icon, root);
        var center = new WindowsToastCenter(platform, Identity);
        ToastNotifications.Center.SetProcessDefault(center);
        UpdateToastCenter.Ambient.SetProcessDefault(center);
        ToastLaunchTransport.Current.SetProcessDefault(new WindowsToastLaunchTransport());
    }
}
