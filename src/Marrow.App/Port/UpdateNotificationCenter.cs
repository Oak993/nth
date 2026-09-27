// PORT-ONLY: the platform surface UpdateChecker.NotifyOnce needs to show its banner
// (DroppyCode/App/UpdateChecker.swift:245-261). PR-UPDATE-023's windows_path: Windows 10/11 toasts need no
// runtime permission prompt, so upstream's notDetermined/requestAuthorization branch has no analogue here; the
// port sends the toast directly. WindowsToastStartup binds WindowsToastCenter in production (OD-0028, ED-220),
// shared with AppModel.Notify. The unbound default remains a safe no-op for isolated model tests.
using Marrow.Core.Port;

namespace Marrow.App.Port;

/// <summary>One toast: an identifier, a title, a body and the version it is about.</summary>
public interface IUpdateToastCenter
{
    void Show(string identifier, string title, string body, string version);
}

/// <summary>The unbound/test default: does nothing, silently. Program binds the Windows poster at startup.</summary>
public sealed class NoUpdateToastCenter : IUpdateToastCenter
{
    public static readonly NoUpdateToastCenter Instance = new();
    private NoUpdateToastCenter() { }
    public void Show(string identifier, string title, string body, string version) { }
}

public static class UpdateToastCenter
{
    public static Ambient<IUpdateToastCenter> Ambient { get; } = new(NoUpdateToastCenter.Instance);
}
