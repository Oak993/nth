// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 12.6b "Moves", "Heads", "Layout, controls and access"; the canvas's click handler,
// openAsk, closeAsk, focusAfter, toggleOpen and holdOf, work/design/assistant/index.html lines 2027, 2695-2745; ED-NEW-dextra-as8-4).
// No Swift twin.
// What a task's controls do, shared by the Result list's rows and the Board's cards so the two never differ: a slot press pauses,
// resumes, restarts or stops at once, except a Stop that would waste something (a long turn in flight; for a head, also heads that
// wait on it), which opens the inline ask, and Revert changes, which always asks. An answer closes the ask, acts, and gives focus
// back to the control that opened it, or, when that one is now disabled or gone, to the first live control of the same row, then
// the row's expander, never the page. Only one ask is open per task surface; an ask that stops applying (the task stopped by
// another route) folds. The slots are held shown while the task is pausing or stopping, asking, or expanded.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Assistant.Views;

/// <summary>A button that discloses a panel, read as expanded or collapsed (the canvas's aria-expanded).</summary>
public sealed class DisclosureButton : Button
{
    public static readonly StyledProperty<bool> IsExpandedProperty = AvaloniaProperty.Register<DisclosureButton, bool>(nameof(IsExpanded));

    protected override Type StyleKeyOverride => typeof(Button);

    public bool IsExpanded { get => GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsExpandedProperty && Avalonia.Automation.Peers.ControlAutomationPeer.FromElement(this) is { } peer)
        {
            peer.RaisePropertyChangedEvent(Avalonia.Automation.ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
                change.GetOldValue<bool>() ? Avalonia.Automation.ExpandCollapseState.Expanded : Avalonia.Automation.ExpandCollapseState.Collapsed,
                change.GetNewValue<bool>() ? Avalonia.Automation.ExpandCollapseState.Expanded : Avalonia.Automation.ExpandCollapseState.Collapsed);
        }
    }

    protected override Avalonia.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(DisclosureButton owner) : Avalonia.Automation.Peers.ButtonAutomationPeer(owner), Avalonia.Automation.Provider.IExpandCollapseProvider
    {
        public Avalonia.Automation.ExpandCollapseState ExpandCollapseState =>
            owner.IsExpanded ? Avalonia.Automation.ExpandCollapseState.Expanded : Avalonia.Automation.ExpandCollapseState.Collapsed;

        public bool ShowsMenu => false;

        public void Expand()
        {
            if (!owner.IsExpanded) owner.RaiseClick();
        }

        public void Collapse()
        {
            if (owner.IsExpanded) owner.RaiseClick();
        }
    }

    private void RaiseClick() => OnClick();
}

/// <summary>The controls of one task on one surface (a Result row or a Board card): slots, the inline ask and the heads panel.</summary>
public sealed class TaskSurface
{
    private readonly IMissionJobs jobs;
    private readonly TaskSlots slots;
    private readonly TaskAskPanel ask;
    private readonly HeadsPanel heads;
    private readonly Func<Control?> expander;
    private readonly Action refresh;
    private (AskKind Kind, Guid? Head, Control? Back) asked;
    private bool focusAsk;
    private bool narrow;

    public TaskSurface(IMissionJobs jobs, TaskSlots slots, TaskAskPanel ask, HeadsPanel heads, Func<Control?> expander, Action refresh)
    {
        this.jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        this.slots = slots;
        this.ask = ask;
        this.heads = heads;
        this.expander = expander;
        this.refresh = refresh;
        slots.A.Clicked += (b, kind) => Press(b.Button, kind, null);
        slots.B.Clicked += (b, kind) => Press(b.Button, kind, null);
        ask.Answered += (_, answer) => Answer(answer, null);
        heads.HeadSlot += (line, kind) => Press(kind == SlotKind.Stop || kind == SlotKind.Revert ? line.Slots.B.Button : line.Slots.A.Button, kind, line);
        heads.HeadAnswered += (line, answer) => Answer(answer, line);
        heads.RevertAsked += () => Open(AskKind.Revert, null, heads.RevertButton);
    }

    public MissionJob? Job { get; private set; }

    /// <summary>Whether the heads panel is open (set by the surface's expander).</summary>
    public bool Expanded { get; private set; }

    /// <summary>The ask open now, and on which head (null: the task's own).</summary>
    public (AskKind Kind, Guid? Head) Asked => (asked.Kind, asked.Head);

    /// <summary>Slots stay shown while the task is pausing or stopping, asking, or expanded (the canvas's holdOf).</summary>
    public bool Hold => Job is { State: MissionState.Pausing or MissionState.Stopping } || asked.Kind != AskKind.None || ask.Kind != AskKind.None || Expanded;

    /// <summary>Raised when the hold may have changed.</summary>
    public event Action? HoldChanged;

    /// <summary>What the polite live region should say: an ask that opened, or a control's refusal.</summary>
    public event Action<string>? Said;

    public void Expand(bool open)
    {
        if (Expanded == open) return;
        Expanded = open;
        heads.Open(open);
        if (Job is { } job && open) heads.Show(job, narrow);
        HoldChanged?.Invoke();
    }

    /// <summary>Paints the task's slots, its ask and its heads for <paramref name="job"/>.</summary>
    public void Show(MissionJob job, bool narrowNow)
    {
        Job = job ?? throw new ArgumentNullException(nameof(job));
        narrow = narrowNow;
        var still = TaskLook.Still(slots);
        slots.Set(job.Slots, job.Title, still);
        if (Expanded || heads.IsOpen) heads.Show(job, narrow);

        // the task's own ask: the owner's, checked against the state now, or the 5 s still-pausing note
        var mine = asked.Head is null ? asked.Kind : AskKind.None;
        var shown = TaskWords.Shown(mine, job);
        if (mine != AskKind.None && shown == AskKind.None) Fold();
        var focusThis = focusAsk && asked.Head is null && shown == asked.Kind;
        ask.Show(shown, shown == AskKind.None ? null : TaskWords.Ask(shown, job), focusThis);

        // a head's ask, under that head's line
        foreach (var line in heads.Lines)
        {
            var head = job.Heads.FirstOrDefault(h => h.Thread == line.Head);
            var kind = head is not null && asked.Head == line.Head ? TaskWords.Shown(asked.Kind, head) : AskKind.None;
            if (head is not null && asked.Head == line.Head && asked.Kind != AskKind.None && kind == AskKind.None) Fold();
            var focusLine = focusAsk && asked.Head == line.Head && kind != AskKind.None;
            line.Ask.Show(kind, kind == AskKind.None || head is null ? null : TaskWords.Ask(kind, job, head), focusLine);
        }
        if (asked.Head is { } gone && job.Heads.All(h => h.Thread != gone)) Fold();
        focusAsk = false;
        HoldChanged?.Invoke();
    }

    private void Fold()
    {
        var hadFocus = FocusInside();
        var back = asked.Back;
        var line = asked.Head is { } h ? heads.Line(h) : null;
        asked = default;
        if (hadFocus) Dispatcher.UIThread.Post(() => FocusAfter(back, line));
    }

    private bool FocusInside()
    {
        var focused = TopLevel.GetTopLevel(slots)?.FocusManager?.GetFocusedElement() as Visual;
        if (focused is null) return false;
        if (ask.IsVisualAncestorOf(focused)) return true;
        return heads.Lines.Any(l => l.Ask.IsVisualAncestorOf(focused));
    }

    private void Press(Button from, SlotKind kind, HeadLine? line)
    {
        if (Job is not { } job) return;
        if (line is not null)
        {
            var head = job.Heads.FirstOrDefault(h => h.Thread == line.Head);
            if (head is null) return;
            if (kind == SlotKind.Stop && TaskWords.StopAsks(head)) { Open(AskKind.Stop, line.Head, from); return; }
            Act(kind switch
            {
                SlotKind.Pause => () => jobs.PauseHead(job.Id, head.Thread),
                SlotKind.Resume => () => jobs.ResumeHead(job.Id, head.Thread),
                SlotKind.Stop => () => jobs.StopHead(job.Id, head.Thread),
                _ => null,
            });
            return;
        }
        switch (kind)
        {
            case SlotKind.Pause: Act(() => jobs.Pause(job.Id)); break;
            case SlotKind.Resume: Act(() => jobs.Resume(job.Id)); break;
            case SlotKind.Restart: Act(() => jobs.Restart(job.Id)); break;
            case SlotKind.Stop:
                if (TaskWords.StopAsks(job)) Open(AskKind.Stop, null, from);
                else Act(() => jobs.Stop(job.Id));
                break;
            case SlotKind.Revert: Open(AskKind.Revert, null, from); break;
        }
    }

    /// <summary>Opens an ask (Revert only on a stopped task with kept changes, r4 B1); focus goes into it.</summary>
    public void Open(AskKind kind, Guid? head, Control? back)
    {
        if (Job is not { } job) return;
        if (kind == AskKind.Revert && TaskWords.Shown(AskKind.Revert, job) == AskKind.None) return;
        asked = (kind, head, back);
        focusAsk = true;
        Show(job, narrow);
        var text = head is { } h && job.Heads.FirstOrDefault(x => x.Thread == h) is { } hj ? TaskWords.Ask(kind, job, hj) : TaskWords.Ask(kind, job);
        Said?.Invoke(text.Lead + (text.Body.Length > 0 ? " " + text.Body.Trim() : ""));
    }

    private void Answer(AskAnswer answer, HeadLine? line)
    {
        if (Job is not { } job) return;
        var back = asked.Back;
        var headId = line?.Head ?? asked.Head;
        asked = default;
        Show(job, narrow);
        var head = headId is { } id ? job.Heads.FirstOrDefault(h => h.Thread == id) : null;
        switch (answer)
        {
            case AskAnswer.StopNow when head is not null: Act(() => jobs.StopHead(job.Id, head.Thread)); break;
            case AskAnswer.PauseInstead when head is not null: Act(() => jobs.PauseHead(job.Id, head.Thread)); break;
            case AskAnswer.StopNow or AskAnswer.StopInstead: Act(() => jobs.Stop(job.Id)); break;
            case AskAnswer.PauseInstead: Act(() => jobs.Pause(job.Id)); break;
            case AskAnswer.Revert:
                _ = RevertAsync(job.Id, back, line);
                return;
        }
        FocusAfter(back, line);
    }

    private async Task RevertAsync(Guid job, Control? back, HeadLine? line)
    {
        string? note;
        try { note = await jobs.Revert(job).ConfigureAwait(true); }
        catch (InvalidOperationException e) { note = e.Message; }
        catch (IOException e) { note = e.Message; }
        if (note is not null) Said?.Invoke(note);
        refresh();
        FocusAfter(back, line);
    }

    private void Act(Func<string?>? action)
    {
        if (action is null) return;
        var note = action();
        if (note is not null) Said?.Invoke(note);
        refresh();
    }

    /// <summary>The canvas's focusAfter: the opener if it is still live, else the first live control of its row, else the expander.</summary>
    public void FocusAfter(Control? back, HeadLine? line)
    {
        static bool Live(Control? c) => c is not null && c.IsAttachedToVisualTree() && c.IsEffectivelyVisible && c.IsEffectivelyEnabled && c.Focusable
            && KeyboardNavigation.GetIsTabStop(c);
        if (Live(back)) { back!.Focus(NavigationMethod.Tab); return; }
        var row = line is not null && line.IsAttachedToVisualTree() ? line.Slots : slots;
        if (row.FirstLive is { } first && Live(first)) { first.Focus(NavigationMethod.Tab); return; }
        expander()?.Focus(NavigationMethod.Tab);
    }
}
