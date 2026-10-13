using Marrow.Setup.Platform;

namespace Marrow.Setup.Shell;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3, "Close"; unit U1, ED-NEW-inst-u1-1). Close works while nothing has started. From the
/// moment Velopack's Setup.exe starts until the install is complete it is paused, not hidden: the caption's X is drawn
/// disabled, the screen's Close-paused line shows, and every way of closing (the X, Alt+F4, the taskbar, the system menu,
/// sign-out and shutdown) is refused and brightens that line instead. Sign-out and shutdown are held with a reason for
/// that time. Velopack rolls back only if its Setup.exe finishes, so it is never killed; there is no Cancel.
/// </summary>
public sealed class CloseGuard
{
    private readonly IShutdownBlocker blocker;
    private HoldLine? line;

    public CloseGuard(IShutdownBlocker blocker) => this.blocker = blocker ?? throw new ArgumentNullException(nameof(blocker));

    public bool IsPaused { get; private set; }

    /// <summary>How many tries to close were refused (for tests and --ui-smoke).</summary>
    public int Refusals { get; private set; }

    /// <summary>Raised when the pause starts or ends.</summary>
    public event Action<bool>? PausedChanged;

    /// <summary>Raised for every refused try to close.</summary>
    public event Action? Refused;

    /// <summary>
    /// Pauses Close: <paramref name="hold"/> (the current screen's line) shows <paramref name="words"/>, and sign-out and
    /// shutdown are held with <paramref name="shutdownReason"/>. Calling it again moves the line or changes its words.
    /// </summary>
    public void Pause(HoldLine hold, string words, string shutdownReason)
    {
        ArgumentNullException.ThrowIfNull(hold);
        if (line is not null && !ReferenceEquals(line, hold)) line.Hide();
        line = hold;
        hold.Show(words);
        var was = IsPaused;
        IsPaused = true;
        blocker.Block(shutdownReason);
        if (!was) PausedChanged?.Invoke(true);
    }

    /// <summary>Ends the pause. The line leaves with its screen (it is not hidden here, so it fades out with the screen).</summary>
    public void Resume(bool hideLine = false)
    {
        if (!IsPaused) return;
        IsPaused = false;
        if (hideLine) line?.Hide();
        line = null;
        blocker.Release();
        PausedChanged?.Invoke(false);
    }

    /// <summary>A try to close. False (and the line brightens) while paused.</summary>
    public bool TryClose()
    {
        if (!IsPaused) return true;
        Refusals++;
        line?.Ping();
        Refused?.Invoke();
        return false;
    }
}
