// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-runtime; work/ignis/PLAN.md unit U3). No Swift twin. The app's side of Ignis:
// a hidden keep-warm ping ends silently (no chime, notification, unread mark or auto-continue, and the chat keeps its
// place in the sidebar), every turn end reaches the controller, and the controller reads the app through
// AppIgnisEnvironment (lock state, archive, screen, draft, plan windows, account kind).
using System.Runtime.InteropServices;
using Marrow.App.Port.Ignis.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port.Ignis;

namespace Marrow.App;

public sealed partial class AppModel
{
    /// <summary>
    /// Called first in TurnFinished. Forwards the turn end to the controller; for a hidden ping it only settles the
    /// thread's status (and a session waiting to be released) and returns true: TurnFinished then returns at once.
    /// </summary>
    private bool IgnisTurnFinished(Guid id, TurnStatus status)
    {
        var runtime = ExistingRuntime(id);
        var ping = runtime?.IgnisFinishingPing == true;
        if (ping)
        {
            UpdateThread(id, current => current with { LastStatus = status });
            if (sessionsToRelease.Remove(id)) runtime!.StopSession();
        }
        (runtime is null ? IgnisSeams.Controller.Current() : runtime.IgnisAttach())?.TurnFinished(id);
        return ping;
    }

    internal bool IgnisIsOnScreen(ChatThread thread) => IsApplicationActive() && IsOnScreen(thread);
}

/// <summary>The controller's reading of the running app.</summary>
public sealed class AppIgnisEnvironment(AppModel app) : IIgnisEnvironment
{
    public bool IsLocked => WorkstationLock.IsLocked();

    public bool IsArchived(Guid thread) => app.Thread(thread)?.IsArchived ?? false;

    public bool IsOnScreen(Guid thread) => app.Thread(thread) is { } chat && app.IgnisIsOnScreen(chat);

    public bool HasDraft(Guid thread) => app.ExistingRuntime(thread) is { DraftIsEmpty: false };

    public double? FullestPlanWindow(ProviderKind provider) =>
        app.Providers.PlanLimits.TryGetValue(provider, out var limits) && limits.Windows.Count != 0
            ? limits.Windows.Max(window => window.Percent)
            : null;

    public IgnisAccountKind Account(ProviderKind provider) => provider.IsAPIKeyBased ? IgnisAccountKind.ApiKey : IgnisAccountKind.Plan;
}

/// <summary>
/// ENGINEERING DECISION: the PC counts as locked when the input desktop cannot be opened (it is the secure desktop
/// while locked). Read only when a timer fires, so it costs nothing in between.
/// </summary>
internal static partial class WorkstationLock
{
    private const uint DesktopSwitchDesktop = 0x0100;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint desiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(IntPtr desktop);

    public static bool IsLocked()
    {
        if (!OperatingSystem.IsWindows()) return false;
        var desktop = OpenInputDesktop(0, false, DesktopSwitchDesktop);
        if (desktop == IntPtr.Zero) return true;
        CloseDesktop(desktop);
        return false;
    }
}
