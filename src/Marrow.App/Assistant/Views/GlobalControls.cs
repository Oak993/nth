// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 12.6 "One-button stop" (Stop all never asks; the strip afterwards), 12.6b "Global
// controls" (Pause all, Resume all, one toggle that flips as soon as every live task is pausing, paused or stopped);
// ED-NEW-dextra-as8-7). No Swift twin.
// What the home header, the stop strip, the Result list and the sidebar row's menu call for the global controls: the one
// MissionControls service, read through a small interface so the views are tested with a fake.
using Avalonia.Threading;
using Marrow.App.Safety;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant.Views;

public interface IGlobalControls
{
    /// <summary>Pause all or Resume all, and whether each has anything to do.</summary>
    GlobalToggle Toggle { get; }

    /// <summary>The report of the stop still in effect (the strip), null when none.</summary>
    StopReport? Stopped { get; }

    /// <summary>Pause all; the line says what it did.</summary>
    string PauseAll(string source);

    /// <summary>Resume all; the line names what it picked up and what it left alone.</summary>
    string ResumeAll();

    /// <summary>Stop all: never asks.</summary>
    StopReport StopAll(StopSource from);

    /// <summary>Force end for an agent still stopping after 5 s.</summary>
    bool ForceEnd(Guid thread);

    /// <summary>Something the toggle or the strip shows changed (raised on the UI thread).</summary>
    event Action? Changed;
}

public sealed class ControllerGlobalControls : IGlobalControls, IDisposable
{
    private readonly MissionControls controls;
    private readonly Action<Action> post;
    private int queued;

    public ControllerGlobalControls(MissionControls controls, Action<Action>? post = null)
    {
        this.controls = controls ?? throw new ArgumentNullException(nameof(controls));
        this.post = post ?? (a => Dispatcher.UIThread.Post(a));
        controls.Runner.Changed += OnRunner;
        controls.Stopper.Changed += OnStop;
        controls.Hold.Changed += Queue;
    }

    public GlobalToggle Toggle => controls.Toggle;

    public StopReport? Stopped => controls.Stopper.Report;

    public event Action? Changed;

    public string PauseAll(string source) => controls.PauseAll(source).Line;

    public string ResumeAll() => controls.ResumeAll().Line;

    public StopReport StopAll(StopSource from) => controls.StopAll(from);

    public bool ForceEnd(Guid thread) => controls.Stopper.ForceEnd(thread);

    private void OnRunner(MissionRecord _) => Queue();

    private void OnStop(StopReport? _) => Queue();

    private void Queue()
    {
        if (Interlocked.Exchange(ref queued, 1) == 1) return;
        post(() => { Volatile.Write(ref queued, 0); Changed?.Invoke(); });
    }

    public void Dispose()
    {
        controls.Runner.Changed -= OnRunner;
        controls.Stopper.Changed -= OnStop;
        controls.Hold.Changed -= Queue;
    }
}
