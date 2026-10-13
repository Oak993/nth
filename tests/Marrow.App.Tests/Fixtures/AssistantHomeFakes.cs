using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Assistant;
using Marrow.App.Assistant.Views;
using Marrow.App.Safety;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Testing;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Dextra AS-8: a task list for the home's views, set by the test; every control call is recorded and answered.</summary>
public sealed class FakeMissionJobs : IMissionJobs
{
    public List<MissionJob> Jobs { get; } = [];

    public List<string> Calls { get; } = [];

    /// <summary>What a control call answers (null: taken).</summary>
    public string? Refusal { get; set; }

    /// <summary>What a state each control call moves the task to (applied to <see cref="Jobs"/>).</summary>
    public Func<MissionJob, SlotKind, MissionJob>? Apply { get; set; }

    public int Snapshots { get; private set; }

    public string Name => "fake";

    public event Action? Changed;

    public IReadOnlyList<MissionJob> Snapshot(DateTimeOffset now)
    {
        Snapshots++;
        return [.. Jobs];
    }

    public void Set(params MissionJob[] jobs)
    {
        Jobs.Clear();
        Jobs.AddRange(jobs);
        Changed?.Invoke();
    }

    public void Replace(MissionJob job)
    {
        var index = Jobs.FindIndex(j => j.Id == job.Id);
        if (index >= 0) Jobs[index] = job;
        else Jobs.Add(job);
        Changed?.Invoke();
    }

    public void Raise() => Changed?.Invoke();

    private string? Act(string call, Guid job, SlotKind kind)
    {
        Calls.Add(call + " " + job.ToString("N")[..4]);
        if (Refusal is not null) return Refusal;
        if (Apply is { } apply && Jobs.FindIndex(j => j.Id == job) is >= 0 and var index)
        {
            Jobs[index] = apply(Jobs[index], kind);
            Changed?.Invoke();
        }
        return null;
    }

    public string? Pause(Guid job) => Act("Pause", job, SlotKind.Pause);

    public string? Resume(Guid job) => Act("Resume", job, SlotKind.Resume);

    public string? Stop(Guid job) => Act("Stop", job, SlotKind.Stop);

    public string? Restart(Guid job) => Act("Restart", job, SlotKind.Restart);

    public Task<string?> Revert(Guid job) => Task.FromResult(Act("Revert", job, SlotKind.Revert));

    public string? PauseHead(Guid job, Guid head) => Act("PauseHead", job, SlotKind.Pause);

    public string? ResumeHead(Guid job, Guid head) => Act("ResumeHead", job, SlotKind.Resume);

    public string? StopHead(Guid job, Guid head) => Act("StopHead", job, SlotKind.Stop);

    public void Dismiss(Guid job) => Calls.Add("Dismiss " + job.ToString("N")[..4]);
}

/// <summary>Dextra AS-8: open cards for the Decisions tab; answers are recorded and close the card unless refused.</summary>
public sealed class FakeDecisions : IDecisionSource
{
    public List<DecisionItem> Items { get; } = [];

    public List<(Guid Id, DecisionAnswer Answer)> Answers { get; } = [];

    public string? Refusal { get; set; }

    public IReadOnlyList<DecisionItem> Waiting => [.. Items];

    public event Action? Changed;

    public void Add(params DecisionItem[] items)
    {
        Items.AddRange(items);
        Changed?.Invoke();
    }

    public string? Answer(Guid id, DecisionAnswer answer)
    {
        Answers.Add((id, answer));
        if (Refusal is not null) return Refusal;
        Items.RemoveAll(i => i.Id == id);
        Changed?.Invoke();
        return null;
    }
}

/// <summary>Dextra AS-8: the global controls, with a toggle the test sets and every call recorded.</summary>
public sealed class FakeGlobalControls : IGlobalControls
{
    public GlobalToggle Toggle { get; set; } = new(GlobalToggleKind.PauseAll, true, false);

    public StopReport? Stopped { get; set; }

    public List<string> Calls { get; } = [];

    public event Action? Changed;

    public void Raise() => Changed?.Invoke();

    public string PauseAll(string source)
    {
        Calls.Add("PauseAll " + source);
        return "Pausing 2 tasks.";
    }

    public string ResumeAll()
    {
        Calls.Add("ResumeAll");
        return "Resumed 2 tasks.";
    }

    public StopReport StopAll(StopSource from)
    {
        Calls.Add("StopAll " + from);
        Stopped = new StopReport(from, "Stop all", 12, 3, 3, TimeSpan.FromMilliseconds(400));
        Changed?.Invoke();
        return Stopped;
    }

    public bool ForceEnd(Guid thread)
    {
        Calls.Add("ForceEnd");
        return true;
    }
}

/// <summary>Dextra AS-8: tasks and cards in the shapes the canvas shows.</summary>
public static class HomeSamples
{
    public static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    public static MissionJob Job(string title, MissionState state, int minutesAgo = 5, params HeadJob[] heads) =>
        new(Guid.NewGuid(), title, state)
        {
            Created = Now.AddMinutes(-minutesAgo),
            Ended = state is MissionState.Done or MissionState.Stopped or MissionState.Failed ? Now.AddMinutes(-1) : null,
            Heads = new ValueList<HeadJob>(heads),
            Detail = "Lead at work · 2 heads",
            Activity = state == MissionState.Working ? "running the tests (12 of 40)" : null,
            Agents = 1 + heads.Length,
            Progress = .4,
            LeadThread = Guid.NewGuid(),
        };

    public static MissionJob Moved(MissionJob job, MissionState state) => job with
    {
        State = state,
        Slots = MissionStates.Slots(state, job.ChangedFiles > 0, job.Reverted),
        Group = MissionStates.Group(state),
        Dimmed = MissionStates.Dimmed(state),
        Ended = state is MissionState.Done or MissionState.Stopped or MissionState.Failed ? Now : null,
    };

    public static ValueList<MissionSpend> Spent(params MissionSpend[] spend) => new(spend);

    public static HeadJob Head(string name, HeadState state, string? activity = null) => new(Guid.NewGuid(), name, state) { Activity = activity };

    public static DecisionItem Reversible(string title, Guid? mission = null) =>
        new(Guid.NewGuid(), DecisionKind.Approval, DecisionWeight.Reversible, title)
        {
            Why = "It wants to sign in to the store's dashboard.",
            Class = ActionClass.Login,
            GrantTarget = GrantTarget.Site("https://shop.example"),
            AssistantId = "dextra",
            MissionId = mission,
            Raised = Now,
        };

    public static DecisionItem Irreversible(string title) =>
        new(Guid.NewGuid(), DecisionKind.Approval, DecisionWeight.Irreversible, title)
        {
            Why = "It drafted the reply and wants to send it.",
            Class = ActionClass.Outward,
            AssistantId = "dextra",
            Raised = Now,
        };
}

/// <summary>
/// Dextra AS-8: a view in a headless window with manual frame and wall clocks (nothing animates or ticks by itself). Made inside
/// <c>HeadlessSession.OnUiThread</c>.
/// </summary>
public sealed class HomeStage : IDisposable
{
    private readonly IDisposable frames = FrameClock.Source.Override(new ManualFrameSource());
    private readonly IDisposable clockScope;

    public HomeStage(Control content, double width = 1200, double height = 800, FlowDirection flow = FlowDirection.LeftToRight, ManualTimeProvider? clock = null)
    {
        Clock = clock ?? new ManualTimeProvider(HomeSamples.Now);
        clockScope = SwiftClock.Provider.Override(Clock);
        Window = new AvaloniaWindow { Content = content, Width = width, Height = height, FlowDirection = flow };
        Window.Show();
        Layout();
    }

    public ManualTimeProvider Clock { get; }

    public AvaloniaWindow Window { get; }

    public void Layout()
    {
        for (var i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Window.UpdateLayout();
        }
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Moves the wall clock on and lets every timer that came due run.</summary>
    public void Advance(TimeSpan by)
    {
        Clock.Advance(by, () => Dispatcher.UIThread.RunJobs());
        Layout();
    }

    public void Press(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Window.KeyPress(key, modifiers, PhysicalKey.None, null);
        Window.KeyRelease(key, modifiers, PhysicalKey.None, null);
        Layout();
    }

    public void Down(Key key)
    {
        Window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        Layout();
    }

    public void Up(Key key)
    {
        Window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        Layout();
    }

    public void Resize(double width, double height = 800)
    {
        Window.Width = width;
        Window.Height = height;
        Layout();
    }

    public Control? Focused => Window.FocusManager?.GetFocusedElement() as Control;

    /// <summary>A control's box in the window's coordinates.</summary>
    public Rect Box(Visual control) =>
        control.TranslatePoint(default, Window) is { } at ? new Rect(at, control.Bounds.Size) : throw new InvalidOperationException("Not in the window.");

    public void Dispose()
    {
        Window.Close();
        clockScope.Dispose();
        frames.Dispose();
    }
}

/// <summary>Dextra AS-8: a host whose starter makes no controller unless the test gives one.</summary>
public static class DextraHosts
{
    public static DextraHost Make(AppModel model, InMemorySettingsStore? store = null, DextraStarter? starter = null) =>
        new(model, new DextraSettings(store ?? new InMemorySettingsStore()), starter ?? ((_, _, _) => null));
}
