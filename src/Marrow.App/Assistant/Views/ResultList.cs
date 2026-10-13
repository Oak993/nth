// PORT-ONLY: Dextra AS-8 (DESIGN.md rev 5, 12.6 "Afterwards" (the strip after Stop all: the two times kept apart, Still stopping
// with Force end, Resume all), 12.6b "Global controls" (lists with room show both Pause all and Resume all, each disabled when it
// has nothing to do), "Layout, controls and access" (a heading never holds its buttons: "Result"), "Motion" (a finished row shows
// Done, then folds after 1.4 s; a new row grows in); the canvas's #homeStrip, .phd and renderMirror, work/design/assistant/
// index.html lines 744, 917-920, 2096-2105; ED-NEW-dextra-as8-7). No Swift twin.
// The stop strip and the Result list sit under the home header. The strip shows while a stop is in effect; the Result list shows
// with it, and after Pause all or Resume all, so the owner sees each task land and can act on one of them; it folds when nothing
// is left in it.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Marrow.App.Safety;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.UI.Port;
using Marrow.UI.Port.Armilla;
using Marrow.UI.Chrome;

namespace Marrow.App.Assistant.Views;

public sealed class StopStrip : Decorator
{
    private readonly IGlobalControls controls;
    private readonly RevealBox box = new();
    private readonly LiveStatusText text = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly StackPanel stillStopping = new() { Orientation = Orientation.Vertical, Spacing = 4 };

    public StopStrip(IGlobalControls controls)
    {
        this.controls = controls ?? throw new ArgumentNullException(nameof(controls));
        SwiftFont.System(12.5).Apply(text);
        text.Foreground = Inverse.Brush(text);
        var (resume, resumeShown) = OnInk("Resume all", () => Said?.Invoke(controls.ResumeAll()), "Resume all");
        ResumeButton = resume;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        Grid.SetColumn(resumeShown, 1);
        resumeShown.VerticalAlignment = VerticalAlignment.Top;
        row.Children.Add(text);
        row.Children.Add(resumeShown);
        // the canvas's strip: the ink colour as its fill, the page's colour as its text (about 15:1 in both themes)
        var face = new Border { Padding = new Thickness(12, 10), Child = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8, Children = { row, stillStopping } } };
        face.RoundedRectangle(12, RoundedCornerStyle.Continuous);
        face.Background = Chrome.PrimaryText.Brush(face);
        box.Child = new Border { Padding = new Thickness(0, 0, 0, 10), Child = face };
        Child = box;
        AutomationProperties.SetName(face, "Stopped");
        AutomationProperties.SetControlTypeOverride(face, Avalonia.Automation.Peers.AutomationControlType.Group);
    }

    private static SwiftColor Inverse => SwiftColor.Dynamic(new SwiftColor(red: .97, green: .97, blue: .98), new SwiftColor(red: .08, green: .08, blue: .09));

    private static (Button Button, Control Shown) OnInk(string title, Action action, string name)
    {
        var label = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center };
        SwiftFont.System(12, SwiftFont.Weight.Medium).Apply(label);
        label.Foreground = Inverse.Brush(label);
        var face = new Border { Padding = new Thickness(10, 0), Height = 26, Child = label };
        face.Capsule(RoundedCornerStyle.Continuous);
        var rest = new SwiftColor(red: .5, green: .5, blue: .5, opacity: .22).Brush(face);
        var hot = new SwiftColor(red: .5, green: .5, blue: .5, opacity: .34).Brush(face);
        face.Background = rest;
        var button = PlainControls.Button(face, action);
        button.KeyboardFocus(13);
        button.PointerEntered += (_, _) => face.Background = hot;
        button.PointerExited += (_, _) => face.Background = rest;
        AutomationProperties.SetName(button, name);
        return (button, button);
    }

    public bool IsOpen => box.IsOpen;

    public string Text => text.Text ?? "";

    public Button ResumeButton { get; }

    public IReadOnlyList<Button> ForceEndButtons => [.. stillStopping.Children.OfType<Grid>().SelectMany(g => g.Children.OfType<Button>())];

    public event Action<string>? Said;

    public void Show()
    {
        var report = controls.Stopped;
        box.Open(report is not null, TaskLook.Still(this));
        if (report is null) return;
        text.Text = report.Strip;
        stillStopping.Children.Clear();
        foreach (var line in report.StillStopping.Where(l => !l.Settled))
        {
            var name = new TextBlock { Text = $"Still stopping: {line.Name}", VerticalAlignment = VerticalAlignment.Center, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            SwiftFont.System(12).Apply(name);
            name.Foreground = Inverse.Brush(name);
            var thread = line.Thread;
            var (force, forceShown) = OnInk("Force end", () => controls.ForceEnd(thread), "Force end " + line.Name);
            _ = force;
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
            Grid.SetColumn(forceShown, 1);
            g.Children.Add(name);
            g.Children.Add(forceShown);
            stillStopping.Children.Add(g);
        }
    }
}

public sealed class ResultList : Decorator
{
    public static readonly TimeSpan FoldAfter = TimeSpan.FromMilliseconds(1400);

    private readonly IMissionJobs jobs;
    private readonly IGlobalControls controls;
    private readonly RevealBox box = new();
    private readonly StackPanel rows = new() { Orientation = Orientation.Vertical, Spacing = 6 };
    private readonly TextBlock empty = TaskLook.Text("No running missions.", 12, SwiftFont.Weight.Regular, TaskLook.Ink2);
    private readonly Dictionary<Guid, (RevealBox Box, TaskRow Row)> byJob = [];
    private readonly Dictionary<Guid, ITimer> folding = [];
    private MotionScope.Entry? budget;
    private bool calm;
    private bool narrow;
    private bool ready;

    public ResultList(IMissionJobs jobs, IGlobalControls controls)
    {
        this.jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        this.controls = controls ?? throw new ArgumentNullException(nameof(controls));
        // the heading holds no buttons: "Result" alone, with Pause all and Resume all beside it
        Heading = TaskLook.Text("Result", 14, SwiftFont.Weight.Semibold, TaskLook.Ink);
        AutomationProperties.SetHeadingLevel(Heading, 2);
        var (pause, pauseShown) = TaskLook.Small("Pause all", () => Said?.Invoke(controls.PauseAll("Pause all in the Result list")));
        var (resume, resumeShown) = TaskLook.Small("Resume all", () => Said?.Invoke(controls.ResumeAll()));
        PauseAllButton = pause;
        ResumeAllButton = resume;
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 6 };
        Heading.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(pauseShown, 1);
        Grid.SetColumn(resumeShown, 2);
        head.Children.Add(Heading);
        head.Children.Add(pauseShown);
        head.Children.Add(resumeShown);
        var face = new Border { Padding = new Thickness(12, 10), Child = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8, Children = { head, rows, empty } } };
        SwiftGlass.SetEffect(face, GlassEffect.Regular(GlassShape.RoundedRectangle(14, RoundedCornerStyle.Continuous)));
        box.Child = new Border { Padding = new Thickness(0, 0, 0, 10), Child = face };
        Child = box;
        AutomationProperties.SetName(this, "Result");
        SizeChanged += (_, e) => Narrow = e.NewSize.Width < TaskMetrics.NarrowWidth;
    }

    public TextBlock Heading { get; }

    public Button PauseAllButton { get; }

    public Button ResumeAllButton { get; }

    public bool IsOpen => box.IsOpen;

    public IReadOnlyList<TaskRow> Rows => [.. rows.Children.OfType<RevealBox>().Select(b => b.Child).OfType<TaskRow>()];

    public TaskRow? Row(Guid job) => byJob.TryGetValue(job, out var r) ? r.Row : null;

    public TextBlock Empty => empty;

    public event Action<string>? Said;

    public bool Narrow
    {
        get => narrow;
        set
        {
            if (narrow == value) return;
            narrow = value;
            foreach (var (_, row) in byJob.Values) row.Narrow = value;
        }
    }

    public bool Calm
    {
        get => calm;
        set
        {
            calm = value;
            foreach (var (_, row) in byJob.Values) row.Mark.Calm = value;
        }
    }

    public int Moving => box.IsOpen ? byJob.Values.Count(r => r.Row.Mark.Moves) : 0;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        jobs.Changed += Refresh;
        if (TopLevel.GetTopLevel(this) is { } top)
            budget = MotionScope.For(top).Join(ArmillaListRank.Result, () => Moving, () => box.IsOpen && IsEffectivelyVisible, c => Calm = c);
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        jobs.Changed -= Refresh;
        budget?.Leave();
        budget = null;
        foreach (var t in folding.Values) t.Dispose();
        folding.Clear();
    }

    /// <summary>Shows the list (after a global action) or folds it.</summary>
    public void Open(bool open)
    {
        box.Open(open, TaskLook.Still(this));
        budget?.Changed();
        if (open) Refresh();
    }

    public void Refresh()
    {
        var toggle = controls.Toggle;
        PauseAllButton.IsEnabled = toggle.CanPauseAll;
        ResumeAllButton.IsEnabled = toggle.CanResumeAll;
        var now = SwiftClock.Provider.Current.GetUtcNow();
        var list = jobs.Snapshot(now);
        var live = list.ToDictionary(j => j.Id);
        var still = TaskLook.Still(this);
        // a row whose task is gone folds at once; a Done row shows Done, then folds after 1.4 s
        foreach (var (id, (rowBox, row)) in byJob.ToList())
        {
            if (!live.TryGetValue(id, out var job)) { Fold(id, TimeSpan.Zero); continue; }
            row.Show(job);
            if (job.State == MissionState.Done && !folding.ContainsKey(id)) Fold(id, FoldAfter);
        }
        foreach (var job in list)
        {
            if (byJob.ContainsKey(job.Id) || job.State == MissionState.Done) continue;
            var row = new TaskRow(jobs, Refresh) { Narrow = narrow };
            row.Mark.Calm = calm;
            row.Mark.MovesChanged += _ => budget?.Changed();
            row.Surface.Said += text => Said?.Invoke(text);
            row.Show(job);
            var rowBox = new RevealBox { Child = row };
            byJob[job.Id] = (rowBox, row);
            rows.Children.Add(rowBox);
            rowBox.Open(true, still || !ready);
        }
        ready = true;
        empty.IsVisible = byJob.Count == 0;
        budget?.Changed();
    }

    private void Fold(Guid id, TimeSpan after)
    {
        if (!byJob.TryGetValue(id, out var entry)) return;
        void Go()
        {
            if (folding.Remove(id, out var t)) t.Dispose();
            if (!byJob.Remove(id)) return;
            entry.Box.Open(false, TaskLook.Still(this));
            var gone = SwiftClock.Provider.Current.CreateTimer(_ => Dispatcher.UIThread.Post(() => rows.Children.Remove(entry.Box)), null, RevealBox.Out, Timeout.InfiniteTimeSpan);
            entry.Box.DetachedFromVisualTree += (_, _) => gone.Dispose();
            empty.IsVisible = byJob.Count == 0;
            budget?.Changed();
        }
        if (after <= TimeSpan.Zero) { Go(); return; }
        folding[id] = SwiftClock.Provider.Current.CreateTimer(_ => Dispatcher.UIThread.Post(Go), null, after, Timeout.InfiniteTimeSpan);
    }
}
