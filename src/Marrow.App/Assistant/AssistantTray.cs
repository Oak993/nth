// PORT-ONLY: Dextra (DESIGN.md rev 5, 7.4, 12.6, 12.6b "The tray item and voice flip with it"; unit AS-6; ED-NEW-dextra-as6-10).
// No Swift twin. What the Assistant puts in nth's one tray icon: "Stop Assistant", and "Pause all" that flips to "Resume all" as
// soon as every live task is pausing or paused. The icon wears the busiest state of the Assistant's work and shows while any
// task is live, so Stop is one tap away with the window closed. The items belong to the controller, not to a window.
using Marrow.App.Tray;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Assistant;

public sealed class AssistantTray : IDisposable
{
    public const string Section = "Assistant";

    private readonly AppTray tray;
    private readonly AssistantController controller;
    private readonly IDisposable contribution;

    private AssistantTray(AppTray tray, AssistantController controller)
    {
        this.tray = tray;
        this.controller = controller;
        contribution = tray.Contribute(Section, Contribute);
        controller.Runner.Changed += OnChanged;
        controller.Hold.Changed += Refresh;
        controller.Queue.Changed += Refresh;
    }

    /// <summary>Adds the Assistant's items to the tray, and keeps them current as tasks change.</summary>
    public static AssistantTray Bind(AppTray tray, AssistantController controller)
    {
        ArgumentNullException.ThrowIfNull(tray);
        ArgumentNullException.ThrowIfNull(controller);
        return new AssistantTray(tray, controller);
    }

    public void Dispose()
    {
        controller.Runner.Changed -= OnChanged;
        controller.Hold.Changed -= Refresh;
        controller.Queue.Changed -= Refresh;
        contribution.Dispose();
    }

    private TrayContribution Contribute()
    {
        var live = controller.Runner.Missions.Select(r => r.Mission).Where(m => m.State is not (MissionState.Done or MissionState.Failed)).ToList();
        var state = live.Any(m => m.State == MissionState.NeedsYou) || controller.Queue.Waiting.Any(c => c.State == DecisionState.Open)
            ? TrayState.NeedsYou
            : live.Any(m => MissionStates.IsRunning(m.State) || m.State is MissionState.Pausing or MissionState.Stopping)
                ? TrayState.Working
                : live.Count > 0 && live.All(m => m.State is MissionState.Paused or MissionState.Stopped) ? TrayState.Paused : TrayState.Ready;
        return new TrayContribution(controller.Controls.TrayItems(), Live: live.Count > 0 || controller.Hold.StopHeld || controller.Hold.PauseLatched, State: state);
    }

    private void OnChanged(Marrow.Services.Assistant.MissionRecord record) => Refresh();

    private void Refresh() => tray.Refresh();
}
