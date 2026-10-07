namespace Marrow.UI.Theme.Wallpapers;

/// <summary>
/// What wakes an idle wallpaper: with Dynamic on, one timer at a time armed for the next minute boundary of the local clock (the injected
/// <see cref="TimeProvider"/> makes it, so a test's clock fires it), and the accent source's Changed event. It runs whether the window is hidden
/// or the animation is still under Reduce Motion; nothing is armed without Dynamic. The callbacks come on a timer thread or the raiser's thread.
/// </summary>
public sealed class WallpaperWakeSource : IDisposable
{
    private readonly WallpaperEnvironment env;
    private readonly Action wake;
    private readonly object gate = new();
    private ITimer? timer;
    private bool disposed;

    public WallpaperWakeSource(WallpaperEnvironment environment, Action wake)
    {
        env = environment; this.wake = wake; env.Accent.Changed += OnAccent;
    }

    /// <summary>Arms the minute timer while Dynamic is on and disarms it when it is off.</summary>
    public void Update(bool dynamicOn)
    {
        lock (gate)
        {
            if (disposed) return;
            if (!dynamicOn) { timer?.Dispose(); timer = null; return; }
            timer ??= Arm();
        }
    }

    private ITimer Arm()
    {
        var local = env.Clock.GetLocalNow();
        // the clock is read again at every firing, so a timer that fires a hair early arms itself for the boundary again
        var due = TimeSpan.FromTicks(TimeSpan.TicksPerMinute - local.Ticks % TimeSpan.TicksPerMinute);
        return env.Clock.CreateTimer(_ => Fire(), null, due, Timeout.InfiniteTimeSpan);
    }

    private void Fire()
    {
        lock (gate)
        {
            if (disposed || timer is null) return;
            timer.Dispose(); timer = Arm();
        }
        wake();
    }

    private void OnAccent()
    {
        if (!Volatile.Read(ref disposed)) wake();
    }

    public void Dispose()
    {
        lock (gate) { disposed = true; timer?.Dispose(); timer = null; }
        env.Accent.Changed -= OnAccent;
    }
}
