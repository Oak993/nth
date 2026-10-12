using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Marrow.Setup.Flow;
using Marrow.Setup.Shell;
using Marrow.Setup.Text;
using Marrow.Setup.Theme;
using Marrow.Setup.Ui;

namespace Marrow.Setup.Screens;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3 and 4, the canvas's Installing; unit U2, ED-NEW-inst-u2-1). No Cancel: the still mark
/// (56 DIP), "Installing nth", the subtitle (a polite live region, reserved for the stall line so nothing moves when it
/// changes), the four steps, bar B with the real percentage, and the Close-paused line at the bottom. Unit U3's progress
/// engine drives the steps, the bar, the stall line and the pause through <see cref="Steps"/>, <see cref="Bar"/>,
/// <see cref="ShowSlow"/> and the session.
/// </summary>
public sealed class InstallingScreen : CanvasScreen, IProgressScreen
{
    private readonly SetupMark mark;
    private readonly TextBlock subtitle;

    public InstallingScreen(SetupSession session) : base(SetupScreen.Installing, session, SetupStrings.ScreenInstalling)
    {
        mark = Mark(56);
        var title = ScreenText.Title(SetupStrings.InstallingTitle);
        subtitle = Reveal.LiveRegion(ScreenText.Subtitle(SetupStrings.InstallingSubtitle));
        var subtitleBox = Reveal.Reserve(subtitle, TextRole.Body, SetupStrings.InstallingSubtitle, SetupStrings.InstallingSlow);
        Steps = new StepList(Calm);
        Steps.Attach(Frames);
        Bar = new ProgressBarB(Calm);
        Bar.Attach(Frames);
        Hold = new HoldLine(Calm);
        Column.With(mark, title, subtitleBox, Steps, Bar, Space(), Hold);
        Progress = new CanvasProgress(Steps, Bar, Steps.SetSteps, ShowSlow);
    }

    /// <summary>What unit U3's progress engine draws into (the rows, the bar and the stall line above).</summary>
    public IProgressView Progress { get; }

    public StepList Steps { get; }

    public ProgressBarB Bar { get; }

    public TextBlock Subtitle => subtitle;

    /// <summary>The steps of an install: checking, copying (with its byte count), adding the shortcuts, finishing.</summary>
    public static IReadOnlyList<StepSpec> InstallSteps(bool desktop, long? sizeMegabytes)
    {
        var total = sizeMegabytes ?? 9999;
        return
        [
            new StepSpec(SetupStrings.StepChecking),
            new StepSpec(SetupStrings.StepCopying, SetupStrings.ProgressBytes(total, total)),
            new StepSpec(desktop ? SetupStrings.StepAddingBoth : SetupStrings.StepAddingStart),
            new StepSpec(SetupStrings.StepFinishing),
        ];
    }

    protected override void Setup(SetupScreen? from)
    {
        mark.Show();
        subtitle.Text = SetupStrings.InstallingSubtitle;
        Steps.SetSteps(InstallSteps(Session.DesktopShortcut, Session.SizeMegabytes));
        Bar.Reset();
    }

    protected override void AfterSetup(SetupScreen? from) => Bar.Run();

    /// <summary>The stall line: "Slower than usual. Windows may be checking the files." while nothing moves (words cross-fade).</summary>
    public void ShowSlow(bool slow) => Reveal.SetText(subtitle, slow ? SetupStrings.InstallingSlow : SetupStrings.InstallingSubtitle);
}

/// <summary>Which run Update starts (the canvas's <c>UPDATE</c> lists).</summary>
public enum UpdateRun
{
    /// <summary>nth is closed.</summary>
    Closed,

    /// <summary>nth is open and agrees to save and quit.</summary>
    Open,

    /// <summary>nth's threads are stopped first.</summary>
    Stop,

    /// <summary>The update waits for nth's threads to finish.</summary>
    Wait,
}

/// <summary>Update's states: asked, nth busy, waiting for it, updating.</summary>
public enum UpdateState
{
    Ask,
    Busy,
    Waiting,
    Updating,
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3 and 3a, the canvas's Update; unit U2, ED-NEW-inst-u2-1). Replaces Velopack's stock
/// "already installed, update?" box (OD-0072): the mark (64 DIP, its pop-in: the first screen of an update), the title and
/// subtitle (the new version in semibold), what's new (from the package's release notes, hidden when there are none), the
/// threads running in nth when it is busy, the steps and bar while it updates, the footnote, the Close-paused line, and
/// two buttons whose words follow the state. Every change is a Reveal: what leaves or changes its words fades first, the
/// rest glides; a button whose label changes fades out and back while the other glides to its new place. Unit U4 drives
/// Busy, Waiting and Updating (<see cref="ShowBusy"/>, <see cref="ShowWaiting"/>, <see cref="ShowUpdating"/>) and answers
/// the buttons through <see cref="UpdateRequested"/>, <see cref="StopRequested"/> and <see cref="WaitRequested"/>.
/// </summary>
public sealed class UpdateScreen : CanvasScreen, IProgressScreen
{
    private readonly SetupMark mark;
    private readonly TextBlock title;
    private readonly TextBlock subtitle;
    private readonly NotesCard notes;
    private readonly NotesCard busy;
    private readonly TextBlock fine;

    public UpdateScreen(SetupSession session) : base(SetupScreen.Update, session, SetupStrings.ScreenUpdate)
    {
        mark = Mark(64);
        title = ScreenText.Title(SetupStrings.UpdateTitle);
        subtitle = Reveal.LiveRegion(ScreenText.Subtitle(""));
        notes = new NotesCard(SetupStrings.UpdateWhatsNew(session.Version), session.ReleaseNotes);
        busy = new NotesCard(SetupStrings.UpdateBusyRunning, []) { IsVisible = false };
        Steps = new StepList(Calm) { IsVisible = false };
        Steps.Attach(Frames);
        Bar = new ProgressBarB(Calm) { IsVisible = false };
        Bar.Attach(Frames);
        fine = ScreenText.Fine(SetupStrings.UpdateKeep);
        Hold = new HoldLine(Calm);
        Later = Button(SetupStrings.UpdateLater, ButtonKind.Secondary, OnLater);
        Go = Button(SetupStrings.UpdateGo, ButtonKind.Primary, OnGo);
        DefaultButton = Go;
        Column.With(mark, title, subtitle, notes, busy, Steps, Bar, fine, Space(ButtonGap), Hold, Buttons(Later, Go));
        Progress = new CanvasProgress(Steps, Bar, steps => ShowUpdating(UpdateRun.Closed, 0, steps), slow => SetSubtitle(slow ? SetupStrings.InstallingSlow : SetupStrings.InstallingSubtitle));
    }

    /// <summary>What unit U3's progress engine draws into once the update runs (the rows, the bar and the stall line).</summary>
    public IProgressView Progress { get; }

    public UpdateState State { get; private set; }

    /// <summary>nth is open now (the footnote says it saves and closes for a moment); unit U4 sets it.</summary>
    public bool NthOpen { get; set; }

    public int Threads { get; private set; }

    public StepList Steps { get; }

    public ProgressBarB Bar { get; }

    public SetupButton Later { get; }

    public SetupButton Go { get; }

    public NotesCard Notes => notes;

    public NotesCard Busy => busy;

    public TextBlock TitleBlock => title;

    public TextBlock SubtitleBlock => subtitle;

    public TextBlock Footnote => fine;

    /// <summary>Update was chosen in Ask (STUB(U4): with no handler, the session hands the update to the unchanged gate).</summary>
    public event Action? UpdateRequested;

    /// <summary>"Stop them and update" in Busy (unit U4 stops the threads, then calls <see cref="ShowUpdating"/>).</summary>
    public event Action? StopRequested;

    /// <summary>"Update when they finish" in Busy (unit U4 waits, then calls <see cref="ShowUpdating"/>).</summary>
    public event Action? WaitRequested;

    /// <summary>The steps of an update run.</summary>
    public static IReadOnlyList<StepSpec> UpdateSteps(UpdateRun run, int threads, long? sizeMegabytes)
    {
        var total = sizeMegabytes ?? 9999;
        var copy = new StepSpec(SetupStrings.StepCopyingNew, SetupStrings.ProgressBytes(total, total));
        var finish = new StepSpec(SetupStrings.StepFinishing);
        return run switch
        {
            UpdateRun.Open => [new StepSpec(SetupStrings.StepClosing), copy, finish],
            UpdateRun.Stop => [new StepSpec(SetupStrings.StepStopping(threads)), copy, finish],
            UpdateRun.Wait => [new StepSpec(SetupStrings.StepWaiting(threads)), new StepSpec(SetupStrings.StepClosing), copy, finish],
            _ => [new StepSpec(SetupStrings.StepChecking), copy, finish],
        };
    }

    protected override void Setup(SetupScreen? from)
    {
        labels.Clear();
        ShowAsk();
    }

    protected override void AfterSetup(SetupScreen? from)
    {
        if (Session.TakeFirstPop()) mark.Pop();
        else mark.Show();
    }

    /// <summary>Ask: what's new, the footnote, Not now and Update.</summary>
    public void ShowAsk()
    {
        State = UpdateState.Ask;
        Reveal.SetText(title, SetupStrings.UpdateTitle);
        Reveal.Apply(subtitle, AskSubtitle, () => subtitle.Text = "", fade: subtitle.Inlines is not { Count: > 0 });
        notes.SetLines(SetupStrings.UpdateWhatsNew(Session.Version), Session.ReleaseNotes);
        Reveal.Set(notes, Session.ReleaseNotes.Count > 0);
        Reveal.Hide(busy);
        Reveal.Hide(Steps);
        Reveal.Hide(Bar);
        Reveal.Show(fine);
        Reveal.SetText(fine, NthOpen ? SetupStrings.UpdateKeepOpen : SetupStrings.UpdateKeep);
        Labels(SetupStrings.UpdateLater, SetupStrings.UpdateGo);
        // Close works again (the canvas's setClose(true)); only this screen's own pause is ended
        if (Hold is { IsWanted: true })
        {
            Session.ResumeClose();
            Hold.Hide();
        }
    }

    /// <summary>Busy: nth is still working; the running threads and the choice.</summary>
    public void ShowBusy(IReadOnlyList<(string Thread, int Minutes)> running)
    {
        ArgumentNullException.ThrowIfNull(running);
        State = UpdateState.Busy;
        Threads = running.Count;
        Reveal.SetText(title, SetupStrings.UpdateBusyTitle);
        SetSubtitle(SetupStrings.UpdateBusySubtitle(Threads));
        Reveal.Hide(notes);
        busy.SetLines(SetupStrings.UpdateBusyRunning, [.. running.Select(r => SetupStrings.UpdateBusyRow(r.Thread, r.Minutes))]);
        Reveal.Show(busy);
        Reveal.Show(fine);
        Reveal.SetText(fine, SetupStrings.UpdateBusyNote);
        Labels(SetupStrings.UpdateBusyStop(Threads), SetupStrings.UpdateBusyWait(Threads));
    }

    /// <summary>Waiting: the update starts by itself when nth's threads finish; Stop waiting goes back to Ask.</summary>
    public void ShowWaiting(int threads)
    {
        State = UpdateState.Waiting;
        Threads = threads;
        Reveal.SetText(title, SetupStrings.UpdateWaitTitle);
        SetSubtitle(SetupStrings.UpdateWaitSubtitle(threads));
        Running(UpdateRun.Wait, threads);
        Labels(SetupStrings.UpdateWaitStop, null);
    }

    /// <summary>
    /// Updating: the steps and the bar; no buttons (Close pauses from copying on). The progress engine passes the rows of
    /// its run in <paramref name="steps"/>; without them the canvas's list for <paramref name="run"/> is used.
    /// </summary>
    public void ShowUpdating(UpdateRun run, int threads = 0, IReadOnlyList<StepSpec>? steps = null)
    {
        var fromWaiting = State == UpdateState.Waiting;
        State = UpdateState.Updating;
        Reveal.SetText(title, SetupStrings.UpdateRunningTitle(Session.Version));
        SetSubtitle(SetupStrings.InstallingSubtitle);
        if (!fromWaiting || steps is not null) Running(run, threads, steps);
        Labels(null, null);
    }

    private void Running(UpdateRun run, int threads, IReadOnlyList<StepSpec>? steps = null)
    {
        Reveal.Hide(notes);
        Reveal.Hide(busy);
        Reveal.Hide(fine);
        if (steps is not null) Steps.SetSteps(steps);
        else if (!Steps.IsVisible) Steps.SetSteps(UpdateSteps(run, threads, Session.SizeMegabytes));
        Reveal.Show(Steps);
        Reveal.Show(Bar);
        if (!Bar.IsVisible) Bar.Reset();
        Bar.Run();
    }

    private void Labels(string? later, string? go)
    {
        Label(Later, later);
        Label(Go, go);
    }

    private void Label(SetupButton button, string? words)
    {
        if (words is null)
        {
            Reveal.Hide(button);
            return;
        }
        Reveal.Show(button);
        // the words it is about to show (a change still waiting for its commit counts), so a reverse takes them back
        var before = labels.GetValueOrDefault(button) ?? button.Label;
        if (string.Equals(before, words, StringComparison.Ordinal)) return;
        labels[button] = words;
        Reveal.Apply(button, () => button.SetLabelNow(words), () => button.SetLabelNow(before), fade: true);
    }

    private readonly Dictionary<SetupButton, string> labels = [];

    private void SetSubtitle(string words)
    {
        if (subtitle.Inlines is not { Count: > 0 })
        {
            Reveal.SetText(subtitle, words);
            AutomationProperties.SetName(subtitle, words);
            return;
        }
        Reveal.Apply(subtitle, () =>
        {
            subtitle.Inlines.Clear();
            subtitle.Text = words;
            AutomationProperties.SetName(subtitle, words);
        }, AskSubtitle, fade: true);
    }

    private void AskSubtitle()
    {
        var installed = Session.Route.Installed.Version ?? "";
        var next = Session.Version;
        var full = SetupStrings.UpdateSubtitle(installed, next);
        var at = full.LastIndexOf(next, StringComparison.Ordinal);
        var inlines = ScreenText.Inlines(subtitle);
        if (at < 0 || next.Length == 0)
        {
            inlines.Add(new Run(full));
        }
        else
        {
            inlines.Add(new Run(full[..at]));
            inlines.Add(ScreenText.Run(subtitle, next, TextRole.Body, SetupTheme.Ink1, FontWeight.SemiBold));
            inlines.Add(new Run(full[(at + next.Length)..]));
        }
        AutomationProperties.SetName(subtitle, full);
    }

    private void OnGo()
    {
        switch (State)
        {
            case UpdateState.Ask:
                if (UpdateRequested is { } update) update();
                else Session.ChooseInstall();
                break;
            case UpdateState.Busy:
                WaitRequested?.Invoke();
                break;
        }
    }

    private void OnLater()
    {
        switch (State)
        {
            case UpdateState.Ask:
                Session.Close();
                break;
            case UpdateState.Busy:
                StopRequested?.Invoke();
                break;
            case UpdateState.Waiting:
                ShowAsk();
                break;
        }
    }
}
