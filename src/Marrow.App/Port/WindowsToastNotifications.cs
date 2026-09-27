// Source: DroppyCode/App/AppModel.swift (B3 52bc792), :1249-1270;
// DroppyCode/App/AppDelegate.swift (B3 52bc792), :15, :63-92;
// DroppyCode/App/UpdateChecker.swift (B3 52bc792), :245-261.
// PORT-ONLY: OD-0028 / ED-220. Windows calls are behind IWindowsToastPlatform; only Program binds production.
using Marrow.Core.Port;

namespace Marrow.App.Port;

public sealed record ToastBuildIdentity(string AppId, string Scheme)
{
    public static ToastBuildIdentity ForBuild(bool debug, bool dev) => throw new NotImplementedException("MARROW_STUB toasts identity");
}

public interface IWindowsToastPlatform
{
    void SetProcessAppId(string appId);
    string? ReadRegistry(string key, string name);
    void WriteRegistry(string key, string name, string value);
    void Show(string appId, string tag, string xml);
}

public static class WindowsToastRegistration
{
    public static void Register(IWindowsToastPlatform platform, ToastBuildIdentity identity, string executable, string icon, string? devRoot = null) =>
        throw new NotImplementedException("MARROW_STUB toasts registration");
}

public sealed record ToastRequest(string Identifier, string Title, string Body, bool Sound, IReadOnlyDictionary<string, object> UserInfo);

public interface IToastCenter
{
    void InstallDelegate(AppDelegate appDelegate);
    void RequestPermission();
    Task Add(ToastRequest request);
}

public static class ToastNotifications
{
    public static Ambient<IToastCenter> Center { get; } = new(DisabledToastCenter.Instance);
    public static void RequestPermissionOnce(AppModel model) => throw new NotImplementedException("MARROW_STUB toasts permission");
    public static bool IsOnScreen(AppModel model, Marrow.Core.Models.ChatThread thread) => throw new NotImplementedException("MARROW_STUB toasts screen gate");
}

public sealed class DisabledToastCenter : IToastCenter
{
    public static readonly DisabledToastCenter Instance = new();
    private DisabledToastCenter() { }
    public void InstallDelegate(AppDelegate appDelegate) { }
    public void RequestPermission() { }
    public Task Add(ToastRequest request) => Task.CompletedTask;
}

public sealed class WindowsToastCenter(IWindowsToastPlatform platform, ToastBuildIdentity identity) : IToastCenter, IUpdateToastCenter
{
    public void InstallDelegate(AppDelegate appDelegate) => throw new NotImplementedException("MARROW_STUB toasts delegate");
    public void RequestPermission() { } // ED-220 (4), PR-UPDATE-023: Windows has no runtime permission prompt.
    public Task Add(ToastRequest request)
    {
        _ = platform;
        _ = identity;
        throw new NotImplementedException("MARROW_STUB toasts post");
    }
    public void Show(string identifier, string title, string body, string version) => throw new NotImplementedException("MARROW_STUB toasts update");
    public static string Payload(ToastRequest request, ToastBuildIdentity identity) => throw new NotImplementedException("MARROW_STUB toasts XML");
}

public static class ToastActivation
{
    public static string Encode(ToastBuildIdentity identity, IReadOnlyDictionary<string, object> userInfo) => throw new NotImplementedException("MARROW_STUB toasts arguments");
    public static UNNotification? Decode(ToastBuildIdentity identity, string argument) => throw new NotImplementedException("MARROW_STUB toasts arguments");
}

public interface IToastActivationWindows
{
    void ShowMain();
    void Activate();
    void ShowAbout();
}

public static class ToastActivationWindows
{
    public static Ambient<IToastActivationWindows> Current { get; } = new(() => throw new NotImplementedException("MARROW_STUB toasts windows"));
}

public sealed class UNNotificationResponse(UNNotification notification)
{
    public UNNotification Notification { get; } = notification;
}

public interface IToastLaunchTransport
{
    bool Send(string name, string payload);
    IDisposable Listen(string name, Action<string> received);
}

public static class ToastSecondLaunch
{
    public static bool Forward(IToastLaunchTransport transport, string name, ToastBuildIdentity identity, IReadOnlyList<string> arguments) => throw new NotImplementedException("MARROW_STUB toasts second launch");
    public static Task<bool> Route(AppDelegate appDelegate, ToastBuildIdentity identity, string payload) => throw new NotImplementedException("MARROW_STUB toasts route");
}
