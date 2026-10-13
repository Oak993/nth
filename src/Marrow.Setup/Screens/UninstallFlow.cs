using Avalonia.Threading;
using Marrow.Setup.Flow;
using Marrow.Setup.Support;
using Marrow.Setup.Text;

namespace Marrow.Setup.Screens;

/// <summary>
/// PORT-ONLY (no Swift twin; nth installer PLAN.md 3, 3b and 8, the canvas's Uninstall; unit U6, ED-NEW-inst-u6-1). Uninstall
/// mode in the window. Uninstall (clicked or tabbed to; Cancel keeps the focus, Enter and Esc cancel) shows Removing at once
/// (the row, the warning and the buttons leave, the bar and the paused line arrive; Close is paused and sign-out waits) and
/// runs the removal off the UI thread. When it is done, and not before Removing has been shown for
/// <see cref="MinimumRemoving"/> (so a fast removal never flashes its words), the window shows "nth was removed" with one
/// primary Close; Close works again. A removal that failed resumes Close and shows Problem, worded for removing ("nth couldn't
/// be removed", "Nothing was changed on your PC." when Velopack never ran), whose Retry comes back to this screen already
/// removing and runs again with the same choice. The removal's report is kept for the details.
/// </summary>
public sealed class UninstallFlow
{
    /// <summary>The shortest time Removing is shown (the canvas's Calm demo time; its title and subtitle settle well inside it).</summary>
    public const double MinimumRemoving = 1.2;

    private readonly SetupSession session;
    private readonly Func<bool, Task<UninstallReport>> remove;
    private readonly Action<Action> post;
    private UninstallReport? arrived;
    private bool shownLongEnough;
    private int attempt;

    private UninstallFlow(SetupSession session, UninstallScreen screen, Func<bool, Task<UninstallReport>> remove, Action<Action> post)
    {
        this.session = session;
        Screen = screen;
        this.remove = remove;
        this.post = post;
        screen.UninstallRequested += OnUninstall;
        session.UninstallRetryRequested += OnRetry;
    }

    /// <summary>
    /// Wires uninstall mode to a session whose first screen is Uninstall. <paramref name="remove"/> runs the removal with the
    /// switch's state (it is started on the UI thread and must not block it); <paramref name="post"/> brings its end back to
    /// the UI thread (the dispatcher by default).
    /// </summary>
    public static UninstallFlow Attach(SetupSession session, Func<bool, Task<UninstallReport>> remove, Action<Action>? post = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(remove);
        if (!session.Uninstalling) throw new InvalidOperationException("Uninstall mode needs a session started on the Uninstall screen.");
        var screen = (UninstallScreen)session.Get(SetupScreen.Uninstall);
        return new UninstallFlow(session, screen, remove, post ?? (action => Dispatcher.UIThread.Post(action)));
    }

    public UninstallScreen Screen { get; }

    /// <summary>The last removal's report (null until one has ended).</summary>
    public UninstallReport? Report { get; private set; }

    /// <summary>A removal is running (or its end is waiting for the shortest Removing time).</summary>
    public bool Busy { get; private set; }

    /// <summary>How many removals were started (the first and each Retry).</summary>
    public int Attempts => attempt;

    private void OnUninstall(bool wipe)
    {
        if (Busy) return;
        Screen.ShowRemoving();
        Begin(wipe);
    }

    private void OnRetry()
    {
        if (Busy) return;
        Screen.EnterRemoving();
        session.Go(SetupScreen.Uninstall);
        Begin(Screen.WipeData);
    }

    private void Begin(bool wipe)
    {
        Busy = true;
        arrived = null;
        shownLongEnough = false;
        var number = ++attempt;
        Screen.Cues.After(MinimumRemoving, () =>
        {
            if (number != attempt) return;
            shownLongEnough = true;
            TryFinish();
        });
        Task<UninstallReport> running;
        try { running = remove(wipe); }
        catch (Exception error) when (error is not OutOfMemoryException) { running = Task.FromResult(Unexpected()); }
        _ = running.ContinueWith(task =>
        {
            var report = task.IsCompletedSuccessfully ? task.Result : Unexpected();
            post(() =>
            {
                if (number != attempt) return;
                arrived = report;
                TryFinish();
            });
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void TryFinish()
    {
        if (!Busy || arrived is not { } report || !shownLongEnough) return;
        Busy = false;
        Report = report;
        if (report.Outcome == UninstallOutcome.Removed)
        {
            Screen.ShowRemoved();
            return;
        }
        session.ResumeClose();
        session.Fail(SetupStrings.UninstallProblemOther, safe: report.NothingChanged, details: report.Details());
    }

    /// <summary>A removal that threw: nothing says what it reached, so Problem says files may be left.</summary>
    private static UninstallReport Unexpected() => new(UninstallOutcome.VelopackFailed);
}
