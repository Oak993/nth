// PORT-ONLY, first-release UE-4 (BUILD-PLAN.md 2.4; OD-0100 place A). The Updates page's logic without a control: it holds what the page itself knows (a check is going, Restart was pressed, Later was
// pressed, when the last check was, which run is on show), asks the runner (UE-2's orchestrator) to act, and projects the rows into the view the page draws (UpdatesProjector).
// ENGINEERING DECISIONS (ED-343): opening a page where nothing was ever checked runs one Check now (the same listings the button reads); changes from the runner reach the page as one refresh
// per turn of the main loop however many arrive; a press while anything runs does nothing; Restart Marrow hands over to the runner's restart and, should the process still be here after the update
// engine's give-up time (UE-1's 25 s), gives the button back.
using Marrow.Core.Port;
using Marrow.Core.Port.Updates;

namespace Marrow.App.Port.Settings;

internal sealed class UpdatesController
{
    /// <summary>Seconds after which a Restart Marrow that did not end the process gives the button back (the update engine's own give-up, ED-319 (4)).</summary>
    public const double RestartGiveUpSeconds = 25;

    private readonly IUpdatesRunner runner;
    private readonly Action changed;
    private bool attached, checking, restarting, later;
    private int refreshQueued;
    private ContinuousInstant? checkedAt;
    private HashSet<string>? runIds;
    private WorkItem? giveUp;
    private UpdatesPageView view;

    public UpdatesController(IUpdatesRunner runner)
    {
        this.runner = runner;
        changed = OnRunnerChanged;
        view = Compose();
    }

    /// <summary>What the page shows now.</summary>
    public UpdatesPageView View => view;

    /// <summary>Raised on the main loop after the view changed.</summary>
    public event Action? ViewChanged;

    /// <summary>The page appeared: listens to the runner and, when nothing was ever checked, checks once. The task ends with that check.</summary>
    public Task Attach()
    {
        if (!attached)
        {
            attached = true;
            runner.Changed += changed;
        }
        Refresh();
        var rows = runner.Rows;
        return rows.Count > 0 && rows.All(r => r.State == UpdateRowState.Unchecked) && !runner.IsBusy ? Check() : Task.CompletedTask;
    }

    public void Detach()
    {
        if (!attached) return;
        attached = false;
        runner.Changed -= changed;
        giveUp?.Cancel();
        giveUp = null;
    }

    /// <summary>The main button, whatever it says now: Check for updates, Update everything or Restart nth (ED-NEW-providers-updates-1); nothing while it is busy.</summary>
    public Task Press()
    {
        switch (Compose().Header.Action)
        {
            case UpdatesAction.Check: return Check();
            case UpdatesAction.UpdateAll: return UpdateAll();
            case UpdatesAction.Retry: return RetryFailed();
            case UpdatesAction.Restart: Restart(); return Task.CompletedTask;
            default: return Task.CompletedTask;
        }
    }

    /// <summary>A row's own Update. Ignored for a row that has no update due and while anything runs.</summary>
    public Task UpdateRow(string id)
    {
        if (!Idle() || runner.Rows.FirstOrDefault(r => r.Id == id) is not { State: UpdateRowState.UpdateAvailable or UpdateRowState.ChecksWhenUpdated or UpdateRowState.Failed }) return Task.CompletedTask;
        return Run(() => runner.UpdateOne(id), [id]);
    }

    /// <summary>A failed row's own Retry. Ignored for any other row and while anything runs.</summary>
    public Task RetryRow(string id)
    {
        if (!Idle() || runner.Rows.FirstOrDefault(r => r.Id == id) is not { State: UpdateRowState.Failed }) return Task.CompletedTask;
        return Run(() => runner.Retry(id), [id]);
    }

    /// <summary>U1 "Later": the restart banner is put away until the next run.</summary>
    public void DismissBanner()
    {
        later = true;
        Refresh();
    }

    /// <summary>Recomputes the view, so the "Checked ... ago" sentence keeps up with the clock.</summary>
    public void Tick() => Refresh();

    private bool Idle() => !checking && !restarting && !runner.IsBusy;

    private Task Check()
    {
        if (!Idle()) return Task.CompletedTask;
        checking = true;
        later = false;
        runIds = null;
        Refresh();
        return Run(async () =>
        {
            await runner.CheckNow();
            checkedAt = SwiftClock.Now;
        }, null);
    }

    private Task UpdateAll()
    {
        if (!Idle()) return Task.CompletedTask;
        var rows = runner.Rows;
        // With a row never checked, the run checks first and the set it will run is not known yet: the page then counts the rows it sees queued.
        var due = rows.Any(r => r.State == UpdateRowState.Unchecked) ? null : rows.Where(r => r.State is UpdateRowState.UpdateAvailable or UpdateRowState.ChecksWhenUpdated or UpdateRowState.Failed).Select(r => r.Id).ToHashSet();
        return Run(runner.UpdateEverything, due);
    }

    private Task RetryFailed()
    {
        if (!Idle()) return Task.CompletedTask;
        var failed = runner.Rows.Where(r => r.State == UpdateRowState.Failed).Select(r => r.Id).ToList();
        return Run(async () =>
        {
            foreach (var id in failed) await runner.Retry(id);
        }, failed.ToHashSet());
    }

    private async Task Run(Func<Task> body, HashSet<string>? ids)
    {
        later = false;
        runIds = ids;
        try { await body(); }
        catch (InvalidOperationException) { } // a run began between the check that nothing was running and the start
        catch (OperationCanceledException) { }
        finally
        {
            checking = false;
            Refresh();
        }
    }

    private void Restart()
    {
        restarting = true;
        Refresh();
        giveUp = MainActor.AsyncAfter(TimeSpan.FromSeconds(RestartGiveUpSeconds), () =>
        {
            restarting = false;
            giveUp = null;
            Refresh();
        });
        try { runner.Restart(); }
        catch
        {
            giveUp.Cancel();
            giveUp = null;
            restarting = false;
            Refresh();
        }
    }

    private void OnRunnerChanged()
    {
        if (Interlocked.Exchange(ref refreshQueued, 1) != 0) return;
        MainActor.Post(() =>
        {
            Volatile.Write(ref refreshQueued, 0);
            if (attached) Refresh();
        });
    }

    private void Refresh()
    {
        view = Compose();
        ViewChanged?.Invoke();
    }

    private UpdatesPageView Compose()
    {
        var age = checkedAt is { } at ? UpdatesProjector.Age(SwiftClock.Now - at) : null;
        return UpdatesProjector.Project(runner.Rows, new UpdatesFlags(checking, restarting, later, checking || runner.IsBusy, age, runIds));
    }
}
