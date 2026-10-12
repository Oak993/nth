using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Views.Panes;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Panes;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// A pane workspace (web W3a) on a real headless window: a chat column stand-in that records every width it is laid
/// out at, pane bodies with a focusable box each, a manual frame source, Reduce Motion as the test sets it, a window
/// live-resize model, and a compositor snapshot stand-in (a small bitmap) so the clip reflow's cross-fade can run.
/// </summary>
public sealed class PaneWorkspaceRig : IDisposable
{
    private readonly IDisposable frameScope;
    private readonly IDisposable motionScope;
    private readonly IDisposable cursorScope;
    private readonly IDisposable snapshotScope;
    private readonly IDisposable actorScope;

    public PaneWorkspaceRig(
        double width = 1400,
        double height = 900,
        ChatReflow reflow = ChatReflow.Live,
        bool reduceMotion = false,
        Func<PaneKind, PaneContent?>? contents = null,
        PaneWorkspaceState? state = null)
    {
        Runtime = new TestRuntime();
        // The window's models (WindowLiveResize) are main-actor isolated: the UI thread is the main actor here, as in the app.
        actorScope = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        Frames = new ManualFrameSource();
        frameScope = FrameClock.Source.Override(Frames);
        Motion = new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = reduceMotion };
        motionScope = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(Motion);
        cursorScope = DragHandleView.CursorPlatform.Override(new ChatNoCursor());
        snapshotScope = ChatSlot.Snapshot.Override((_, _) =>
        {
            Snapshots++;
            Bitmap? image = SnapshotsWork ? new WriteableBitmap(new PixelSize(4, 4), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul) : null;
            return Task.FromResult(image);
        });
        ChatColumn = new ChatColumnProbe();
        Workspace = new PaneWorkspace(ChatColumn, contents ?? Content) { Reflow = reflow };
        Workspace.StateChanged += changed => Changes.Add(changed);
        Window = new AvaloniaWindow { Width = width, Height = height, Content = Workspace };
        Resize = new WindowLiveResize(Window);
        AppEnvironment.SetWindowLiveResize(Window, Resize);
        if (state is not null) Workspace.SetState(state, animate: false);
        Window.Show();
        Layout();
    }

    public TestRuntime Runtime { get; }

    public ManualFrameSource Frames { get; }

    public FakeGeniePlatform Motion { get; }

    public ChatColumnProbe ChatColumn { get; }

    public PaneWorkspace Workspace { get; }

    public AvaloniaWindow Window { get; }

    public WindowLiveResize Resize { get; }

    /// <summary>Every state the workspace reported as the user's change, in order.</summary>
    public List<PaneWorkspaceState> Changes { get; } = [];

    /// <summary>IsWorkspaceResizing after each frame, in order.</summary>
    public List<bool> ResizingSeen { get; } = [];

    /// <summary>The pane bodies made, by kind.</summary>
    public Dictionary<PaneKind, TextBox> Bodies { get; } = [];

    public int Snapshots { get; private set; }

    public bool SnapshotsWork { get; set; } = true;

    /// <summary>The solver's area: the workspace less the title band and the 8 DIP margins.</summary>
    public Size SolverSize => new(Math.Max(0, Workspace.Bounds.Width - PaneWorkspace.EdgeMargin), Math.Max(0, Workspace.Bounds.Height - PaneWorkspace.TopBand - PaneWorkspace.EdgeMargin));

    /// <summary>The layout the workspace must land on for its state.</summary>
    public PaneLayoutResult Expected() => PaneLayout.Solve(Workspace.State, SolverSize.Width, SolverSize.Height);

    /// <summary>A pane's expected rectangle in the workspace.</summary>
    public Rect ExpectedBounds(PaneKind kind)
    {
        var rect = Expected().BoundsOf(kind) ?? throw new InvalidOperationException($"{kind} has no rectangle");
        return new Rect(rect.X, rect.Y + PaneWorkspace.TopBand, rect.Width, rect.Height);
    }

    /// <summary>The whole area an expanded pane fills.</summary>
    public Rect Area => new(0, PaneWorkspace.TopBand, SolverSize.Width, SolverSize.Height);

    private PaneContent Content(PaneKind kind)
    {
        var box = new TextBox { Text = PaneKinds.Title(kind) };
        Bodies[kind] = box;
        return new PaneContent(new Border { Child = box }, PaneKinds.Title(kind));
    }

    public void Layout()
    {
        Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
    }

    /// <summary>One frame of <paramref name="milliseconds"/>, then the layout it asked for.</summary>
    public void Frame(double milliseconds = 1000.0 / 60)
    {
        Runtime.Advance(TimeSpan.FromMilliseconds(milliseconds));
        Frames.Advance(TimeSpan.FromMilliseconds(milliseconds));
        Layout();
        ResizingSeen.Add(Resize.IsWorkspaceResizing);
    }

    /// <summary>Frames until nothing moves, the cross-fade included (at most <paramref name="limit"/>); returns how many.</summary>
    public int Settle(int limit = 600)
    {
        Layout(); // a change waits for the layout pass that starts its springs
        var count = 0;
        while ((Workspace.IsMoving || Workspace.Chat.IsEnding || Workspace.Chat.IsCrossFading || Frames.Pending > 0) && count < limit)
        {
            Frame();
            count++;
        }
        Layout();
        return count;
    }

    public PaneCard Card(PaneKind kind) => Workspace.CardOf(kind) ?? throw new InvalidOperationException($"{kind} has no card");

    public Rect BoundsOf(Control control) => new(control.TranslatePoint(default, Workspace) ?? default, control.Bounds.Size);

    public Point SplitterCentre(string key)
    {
        Layout();
        var splitter = Workspace.SplitterOf(key);
        return splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), Window) ?? default;
    }

    private void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        Runtime.RunUntilIdle();
        Layout();
    }

    public void Down(Point at)
    {
        Window.MouseDown(at, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        Pump();
    }

    public void Move(Point to)
    {
        Window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        Pump();
    }

    public void Up(Point at)
    {
        Window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
        Pump();
    }

    public void Key(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Window.KeyPress(key, modifiers, PhysicalKey.None, null);
        Window.KeyRelease(key, modifiers, PhysicalKey.None, null);
        Pump();
    }

    public static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    public IInputElement? Focused => TopLevel.GetTopLevel(Workspace)?.FocusManager?.GetFocusedElement();

    public void Dispose()
    {
        Window.Close();
        snapshotScope.Dispose();
        cursorScope.Dispose();
        motionScope.Dispose();
        frameScope.Dispose();
        actorScope.Dispose();
        Runtime.Dispose();
    }
}

/// <summary>The chat column's stand-in: a composer box, and every width the column is measured at.</summary>
public sealed class ChatColumnProbe : Border
{
    public ChatColumnProbe()
    {
        Background = Brushes.DimGray;
        Child = Composer;
    }

    public TextBox Composer { get; } = new() { Text = "composer", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom };

    public List<double> Widths { get; } = [];

    protected override Size MeasureOverride(Size availableSize)
    {
        Widths.Add(availableSize.Width);
        return base.MeasureOverride(availableSize);
    }
}
