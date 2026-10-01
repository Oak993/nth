using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Marrow.App.Runtime;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// One real HydraButton on a real lead thread of a throwaway AppModel (App/Views/Chat/HydraButton.swift:9-101). The model
/// is the real one; nothing reaches a provider: no session is started, and a head is only a thread record. Popovers are
/// recorded by the presenter seam (headless cannot open a Popup), frames and time are driven by hand.
/// </summary>
internal sealed class HydraButtonRig : IDisposable
{
    private readonly AppModelHarness harness = new();
    private readonly IDisposable frameScope;
    private readonly IDisposable motionScope;
    private readonly IDisposable presenterScope;
    private readonly ManualFrameSource frames = new();
    private readonly List<ChatThread> heads = [];

    public HydraButtonRig(
        bool hydraOn = true, bool introRead = true, bool hydraEnabledInSettings = true, bool attach = true,
        HydraPair? pair = null, ProviderKind? provider = null, bool reduceMotion = false)
    {
        Motion = new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = reduceMotion };
        motionScope = Chrome.ReduceMotionPlatform.Override(Motion);
        frameScope = FrameClock.Source.Override(frames);
        presenterScope = Presentation.Presenter.Override(Presenter);
        Model = harness.MakeModel();
        harness.Main.OnMain(() =>
        {
            Project = Model.AddProject(new Project("P", harness.NewFolder("project")).Url);
            Lead = Model.NewThread(Project)!;
            Model.Settings.HydraEnabled = hydraEnabledInSettings;
            Model.Settings.HasSeenHydraIntro = introRead;
            if (pair is not null) Model.Settings.AddHydraPair(pair);
            Model.UpdateThread(Lead.Id, thread => thread with
            {
                HydraEnabled = hydraOn,
                Provider = provider ?? thread.Provider,
                HydraPairID = pair?.Id,
            });
            Lead = Model.Thread(Lead.Id)!;
            Runtime = Model.Runtime(Lead.Id);
            Button = new HydraButton(Lead, Runtime);
            // Natural size at the top left, with room around for what overflows the mark (the badge sits 3 points outside it).
            Window = new Avalonia.Controls.Window
            {
                Content = new Border { Padding = new Thickness(20), Child = Content },
            };
            Content.Children.Add(Button);
        });
        if (attach) Show();
    }

    public AppModel Model { get; }
    public ContentRecordingPresenter Presenter { get; } = new();

    /// <summary>The Reduce Motion setting the button reads (NSWorkspace.accessibilityDisplayShouldReduceMotion), settable.</summary>
    public FakeGeniePlatform Motion { get; }
    public Project Project { get; private set; } = null!;
    public ChatThread Lead { get; private set; } = null!;
    public ThreadRuntime Runtime { get; private set; } = null!;
    public HydraButton Button { get; private set; } = null!;
    public Avalonia.Controls.Window Window { get; private set; } = null!;

    /// <summary>The row the mark sits in, as in the chrome row: natural size, nothing stretched.</summary>
    public StackPanel Content { get; } = new()
    {
        Orientation = Avalonia.Layout.Orientation.Horizontal,
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
    };

    private readonly List<Avalonia.Controls.Window> hosts = [];
    public TestRuntime Main => harness.Main;
    public ManualFrameSource Frames => frames;

    public void Show()
    {
        harness.Main.OnMain(Window.Show);
        Drain();
    }

    /// <summary>Everything posted so far: the main-actor loop and the dispatcher, alternating until neither has work.</summary>
    public void Drain()
    {
        harness.Main.RunUntilIdle();
        // A render pass can update the pointer-over element, whose hover handlers write main-actor state.
        harness.Main.OnMain(() => Dispatcher.UIThread.RunJobs());
        harness.Main.RunUntilIdle();
    }

    /// <summary>Moves SwiftClock and the frame clock on together, then runs what that made due.</summary>
    public void Tick(double seconds)
    {
        var span = TimeSpan.FromTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));
        harness.Main.Advance(span);
        frames.Advance(span);
        Drain();
    }

    public ChatThread AddHead(HydraHeadInfo.Status status)
    {
        ChatThread head = null!;
        harness.Main.OnMain(() =>
        {
            head = Model.NewThread(Project)!;
            var index = heads.Count;
            Model.UpdateThread(head.Id, thread => thread with
            {
                ParentThreadID = Lead.Id,
                IsInPanel = true,
                Hydra = new HydraHeadInfo(index, "Task", HydraHeadInfo.Kind.Native, HydraHeadInfo.Origin.Delegated)
                {
                    HydraHeadInfoStatus = status,
                },
            });
            heads.Add(Model.Thread(head.Id)!);
        });
        Drain();
        return Model.Thread(head.Id)!;
    }

    public void SetHeadStatus(ChatThread head, HydraHeadInfo.Status status)
    {
        harness.Main.OnMain(() => Model.UpdateThread(head.Id, thread => thread with
        {
            Hydra = thread.Hydra! with { HydraHeadInfoStatus = status },
        }));
        Drain();
    }

    /// <summary>The runtime's own private phase and timeline have no public writer (a real turn needs a provider).</summary>
    public void SetRunning(bool running)
    {
        harness.Main.OnMain(() => typeof(ThreadRuntime).GetProperty("Phase")!
            .SetValue(Runtime, running ? RuntimePhase.Running : RuntimePhase.Idle));
        Drain();
    }

    public void SetEntries(params TimelineEntry[] entries)
    {
        harness.Main.OnMain(() => typeof(ThreadRuntime).GetProperty("Entries")!
            .SetValue(Runtime, new ValueList<TimelineEntry>(entries)));
        Drain();
    }

    public static TimelineEntry Entry(string id, Guid? turn, TimelineItem.Content content) =>
        new(new TimelineItem(turn, content, id));

    public static TimelineEntry Reply(string id, Guid? turn, string text, bool streaming = false) =>
        Entry(id, turn, new TimelineItem.Content.Assistant(new AssistantMessage { Text = text, IsStreaming = streaming }));

    public static TimelineEntry Tool(string id, Guid? turn, ToolCall.Kind kind, ToolCall.Status status) =>
        Entry(id, turn, new TimelineItem.Content.Tool(new ToolCall { ToolCallKind = kind, ToolCallStatus = status, Title = "Task" }));

    public static TimelineEntry User(string id, Guid? turn, string text) =>
        Entry(id, turn, new TimelineItem.Content.User(new UserMessage { Text = text }));

    /// <summary>Replaces the item of an entry that is already on the timeline, as a streamed token does.</summary>
    public void ReplaceItem(TimelineEntry entry, TimelineItem.Content content)
    {
        harness.Main.OnMain(() => entry.Item = entry.Item with { TimelineItemContent = content });
        Drain();
    }

    // What the user sees.

    public Button Mark => LogicalQuery.SelfAndDescendants<Button>(Button).First();

    public HydraSendWorkingBadge? Badge => LogicalQuery.SelfAndDescendants<HydraSendWorkingBadge>(Button).SingleOrDefault();

    /// <summary>A real press and release of a mouse button at a point of the window.</summary>
    public void Press(Point at, MouseButton button, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        harness.Main.OnMain(() =>
        {
            Window.MouseDown(at, button, modifiers);
            Window.MouseUp(at, button, modifiers);
        });
        Drain();
    }

    public void Click()
    {
        harness.Main.OnMain(() => Mark.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)));
        Drain();
    }

    /// <summary>The content of the popups the presenter was last told to show and not to hide since.</summary>
    public IReadOnlyList<Control> Presented() => Presenter.Shown().Select(item => item.Content).OfType<Control>().ToArray();

    /// <summary>The mark's centre in window coordinates.</summary>
    public Point MarkCentre => Mark.TranslatePoint(new Point(Mark.Bounds.Width / 2, Mark.Bounds.Height / 2), Window)!.Value;

    public Point MarkAt(double x, double y) => Mark.TranslatePoint(new Point(x, y), Window)!.Value;

    /// <summary>The popup the presenter was last told to show whose content is a <typeparamref name="T"/>, if still shown.</summary>
    public Popup? PopupShowing<T>() where T : Control =>
        Presenter.Shown().FirstOrDefault(item => item.Content is T).Popup;

    /// <summary>The content of a presented popover laid out in a window of its own (headless cannot open a Popup).</summary>
    public T Open<T>() where T : Control
    {
        var (popup, content) = Presenter.Shown().FirstOrDefault(item => item.Content is T);
        if (popup is null) throw new InvalidOperationException(typeof(T).Name + " is not presented.");
        popup.Child = null;
        var host = new Avalonia.Controls.Window { Content = content };
        hosts.Add(host);
        harness.Main.OnMain(host.Show);
        Drain();
        return (T)content!;
    }

    public static PopoverItem Item(Control popover, string title) => LogicalQuery.SelfAndDescendants<PopoverItem>(popover)
        .Single(item => LogicalQuery.SelfAndDescendants<TextBlock>(item).Any(text => text.Text == title));

    public void Choose(PopoverItem item)
    {
        harness.Main.OnMain(() => LogicalQuery.SelfAndDescendants<Button>(item).First().RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)));
        Drain();
    }

    /// <summary>Whether a choice row shows its checkmark (the column keeps its place, at opacity 0 when not chosen).</summary>
    public static bool IsChecked(PopoverItem item) =>
        LogicalQuery.SelfAndDescendants<SwiftImage>(item).Single(image => image.SymbolName == "checkmark").Opacity == 1;

    public void Dispose()
    {
        try
        {
            harness.Main.OnMain(() =>
            {
                foreach (var host in hosts) host.Close();
                Window.Close();
            });
        }
        finally
        {
            Drain();
            presenterScope.Dispose();
            frameScope.Dispose();
            motionScope.Dispose();
            harness.Dispose();
        }
    }
}
