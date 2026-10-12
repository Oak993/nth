// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.6 "It holds the scheduler", 12.6b "Pause all ... is a latch"; unit AS-6;
// ED-NEW-dextra-as6-4). No Swift twin.
// The two holds that keep new work from running on its own: Stop all holds the scheduler (no new runs until the owner taps
// Resume all), and Pause all is a latch (a duty that fires while it is on starts paused). The scheduler (AS-10) reads it;
// the mission runner starts what it is told to hold paused, so Resume (or Resume all) launches it.
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Assistant;

public sealed class SchedulerHold
{
    private readonly object gate = new();
    private bool stopHeld;
    private bool latched;

    /// <summary>Raised after either hold changes.</summary>
    public event Action? Changed;

    /// <summary>Stop all is in effect: duties do not run, and nothing new starts until Resume all.</summary>
    public bool StopHeld
    {
        get
        {
            lock (gate) return stopHeld;
        }
    }

    /// <summary>Pause all is on: a duty that fires starts paused.</summary>
    public bool PauseLatched
    {
        get
        {
            lock (gate) return latched;
        }
    }

    /// <summary>Whether a duty may fire now (a Stop all holds the scheduler).</summary>
    public bool DutiesMayFire => !StopHeld;

    /// <summary>Whether work of this origin starts paused: everything under a Stop all, a duty under Pause all.</summary>
    public bool StartsPaused(MissionOrigin origin)
    {
        lock (gate) return stopHeld || (latched && origin == MissionOrigin.Duty);
    }

    public void HoldForStop()
    {
        lock (gate)
        {
            if (stopHeld) return;
            stopHeld = true;
            latched = false;
        }
        Changed?.Invoke();
    }

    public void LatchPause()
    {
        lock (gate)
        {
            if (latched) return;
            latched = true;
        }
        Changed?.Invoke();
    }

    /// <summary>Releases both holds (Resume all). Returns whether a Stop all had held the scheduler.</summary>
    public bool ReleaseAll()
    {
        bool hadStop;
        bool any;
        lock (gate)
        {
            hadStop = stopHeld;
            any = stopHeld || latched;
            stopHeld = false;
            latched = false;
        }
        if (any) Changed?.Invoke();
        return hadStop;
    }
}
