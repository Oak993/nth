// PORT-ONLY: the platform surface UpdateChecker.NotifyOnce needs to show its banner
// (DroppyCode/App/UpdateChecker.swift:245-261). PR-UPDATE-023's windows_path: Windows 10/11 toasts need no
// runtime permission prompt, so upstream's notDetermined/requestAuthorization branch has no analogue here; the
// port sends the toast directly. No Windows toast delivery unit exists yet in this tree (a 4.3 platform unit),
// so the default binding is a safe no-op until it lands - matching the shipped behavior when a toast silently
// fails to add (UpdateChecker.swift:260 `try? await center.add(request)`; PR-UPDATE-023 failure_cases).
using Marrow.Core.Port;

namespace Marrow.App.Port;

/// <summary>One toast: an identifier, a title, a body and the version it is about.</summary>
public interface IUpdateToastCenter
{
    void Show(string identifier, string title, string body, string version);
}

/// <summary>The default binding until the Windows toast delivery unit exists: does nothing, silently.</summary>
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
