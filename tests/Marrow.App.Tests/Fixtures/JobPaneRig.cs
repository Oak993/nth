using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Views.Panes;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Fixtures;

/// <summary>A background-job source a test drives by hand: the jobs it lists, the actions it was asked for, and whether it accepts them.</summary>
public sealed class FakeJobSource(string name = "fake") : IBackgroundJobSource
{
    public List<BackgroundJob> Jobs { get; } = [];
    public List<string> Stopped { get; } = [];
    public List<string> Paused { get; } = [];
    public List<string> Resumed { get; } = [];
    public List<string> Dismissed { get; } = [];
    public bool Accept { get; set; } = true;

    public string Name => name;

    public IReadOnlyList<BackgroundJob> Snapshot(DateTimeOffset now) => Jobs.ToArray();

    public bool Stop(string jobId) { Stopped.Add(jobId); return Accept; }

    public bool Pause(string jobId)
    {
        Paused.Add(jobId);
        if (Accept) Replace(jobId, job => job with { State = BackgroundJobState.Paused });
        return Accept;
    }

    public bool Resume(string jobId)
    {
        Resumed.Add(jobId);
        if (Accept) Replace(jobId, job => job with { State = BackgroundJobState.Running });
        return Accept;
    }

    public void Dismiss(string jobId) => Dismissed.Add(jobId);

    public void Replace(string id, Func<BackgroundJob, BackgroundJob> change)
    {
        var at = Jobs.FindIndex(job => job.Id == id);
        Jobs[at] = change(Jobs[at]);
    }

    public static BackgroundJob Job(string id, BackgroundJobKind kind = BackgroundJobKind.HydraHead, DateTimeOffset? started = null, Guid? thread = null,
        BackgroundJobState state = BackgroundJobState.Running, string? title = null) => new()
        {
            Id = id,
            Kind = kind,
            Title = title ?? "Task " + id,
            KindLabel = kind == BackgroundJobKind.HydraHead ? "Hydra head" : kind.ToString(),
            ThreadId = thread,
            State = state,
            StartedAt = started ?? new DateTimeOffset(2026, 9, 22, 11, 50, 0, TimeSpan.Zero),
            FinishedAt = state.IsFinished() ? new DateTimeOffset(2026, 9, 22, 11, 56, 0, TimeSpan.Zero) : null,
            CanStop = state.IsLive(),
        };
}

/// <summary>
/// The Background tasks view (web W5) on a real headless window: a job list over fake sources on the test runtime's manual clock, a
/// manual frame source, Reduce Motion as the test sets it, and the card commands recorded.
/// </summary>
public sealed class JobPaneRig : IDisposable
{
    private readonly IDisposable frameScope;
    private readonly IDisposable motionScope;
    private readonly IDisposable actorScope;
    private readonly IDisposable scopeMemory;

    public JobPaneRig(double width = 360, double height = 640, bool reduceMotion = false, Guid? thread = null, BackgroundJobScope scope = BackgroundJobScope.AllThreads)
    {
        Runtime = new TestRuntime();
        actorScope = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        Frames = new ManualFrameSource();
        frameScope = FrameClock.Source.Override(Frames);
        Motion = new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = reduceMotion };
        motionScope = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(Motion);
        var previous = BackgroundTasksPane.Scope;
        scopeMemory = new Restore(() => BackgroundTasksPane.Scope = previous);
        Thread = thread;
        Source = new FakeJobSource();
        Jobs = new BackgroundJobs();
        Jobs.AddSource(Source);
        Commands = new JobCardCommands(
            Stop: job => { Calls.Add("stop " + job.Id); Jobs.Stop(job.Id); },
            Pause: job => { Calls.Add("pause " + job.Id); Jobs.Pause(job.Id); },
            Resume: job => { Calls.Add("resume " + job.Id); Jobs.Resume(job.Id); },
            ViewTranscript: job => Calls.Add("transcript " + job.Id),
            OpenPreview: job => Calls.Add("preview " + job.DevServerUrl));
        View = new BackgroundTasksView(Jobs, thread, Commands, scope);
        Window = new AvaloniaWindow { Width = width, Height = height, Content = View };
        Window.Show();
        Layout();
    }

    public TestRuntime Runtime { get; }

    public ManualFrameSource Frames { get; }

    public FakeGeniePlatform Motion { get; }

    public Guid? Thread { get; }

    public FakeJobSource Source { get; }

    public BackgroundJobs Jobs { get; }

    public JobCardCommands Commands { get; }

    public List<string> Calls { get; } = [];

    public BackgroundTasksView View { get; }

    public AvaloniaWindow Window { get; }

    public JobStack Stack => View.Stack;

    public void Layout()
    {
        Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
    }

    /// <summary>One 4 Hz tick: the manual clock moves 250 ms, the timer fires, the main actor runs the refresh, and the view lays out.</summary>
    public void Tick(int count = 1)
    {
        for (var i = 0; i < count; i++)
        {
            Runtime.Advance(TimeSpan.FromMilliseconds(250));
            Dispatcher.UIThread.RunJobs();
            Layout();
        }
    }

    /// <summary>A refresh of the list now, then the layout.</summary>
    public void Refresh()
    {
        Jobs.Refresh();
        Layout();
    }

    /// <summary>One animation frame of <paramref name="milliseconds"/>.</summary>
    public void Frame(double milliseconds = 1000.0 / 60)
    {
        Frames.Advance(TimeSpan.FromMilliseconds(milliseconds));
        Layout();
    }

    /// <summary>Frames until nothing moves (at most <paramref name="limit"/>); returns how many.</summary>
    public int Settle(int limit = 600)
    {
        Layout();
        var count = 0;
        while ((Stack.IsMoving || Frames.Pending > 0) && count < limit)
        {
            Frame();
            count++;
        }
        Layout();
        return count;
    }

    public JobCard Card(string id) => Stack.CardOf(id) ?? throw new InvalidOperationException(id + " has no card");

    public static void Click(Button button) => button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    /// <summary>A card's laid-out rectangle in the stack (without the render transform it is drawn through).</summary>
    public Rect BoundsOf(Control control) => control.Bounds;

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }

    public void Dispose()
    {
        Window.Close();
        scopeMemory.Dispose();
        motionScope.Dispose();
        frameScope.Dispose();
        actorScope.Dispose();
        Runtime.Dispose();
    }
}
