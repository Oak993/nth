using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Core.Models;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.UI.Tests.Fixtures;

/// <summary>
/// A shared <see cref="Splitter"/> on a real headless window (web W1): a pane whose size is the splitter's value, the
/// splitter centred on the pane's edge, a deterministic runtime (manual clock and main loop) and a manual frame source,
/// so hover delays, springs and frames are stepped by hand. The window has overlay layers, so the chips can open.
/// </summary>
public sealed class SplitterRig : IDisposable
{
    /// <summary>The sidebar's numbers (DESIGN 4.3): 210 · 268 · 440, released under 110 it hides.</summary>
    public static SplitterLimits Sidebar { get; } = new(210, 440, 268)
    {
        Collapse = SplitterCollapse.BelowMinimum,
        CollapsedValue = 0,
        CollapsedText = "Sidebar hidden",
    };

    private readonly IDisposable frameScope;
    private readonly IDisposable motionScope;
    private readonly IDisposable cursorScope;

    public SplitterRig(
        SplitterOrientation orientation = SplitterOrientation.Vertical,
        SplitterLimits? limits = null,
        double? start = null,
        bool reduceMotion = false,
        FlowDirection flow = FlowDirection.LeftToRight,
        SplitterSide collapseSide = SplitterSide.Leading,
        bool bindFrames = true)
    {
        Runtime = new TestRuntime();
        Frames = new ManualFrameSource();
        frameScope = bindFrames ? FrameClock.Source.Override(Frames) : new Nothing();
        motionScope = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform(reduceMotion));
        cursorScope = DragHandleView.CursorPlatform.Override(new FakeCursorPlatform(null));
        Orientation = orientation;
        Limits = limits ?? Sidebar;
        Root = new Panel { Background = Brushes.Transparent, FlowDirection = flow };
        Pane = new Border
        {
            Background = Brushes.Gray,
            HorizontalAlignment = orientation == SplitterOrientation.Horizontal ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
            VerticalAlignment = orientation == SplitterOrientation.Horizontal ? VerticalAlignment.Top : VerticalAlignment.Stretch,
        };
        Value = new SplitterValue(Root, orientation, Limits, new SplitterState(start ?? Limits.Default), Place, state => Commits.Add(state));
        Splitter = new Splitter(orientation, Value)
        {
            Label = "Resize test pane",
            CollapseSide = collapseSide,
            HorizontalAlignment = orientation == SplitterOrientation.Horizontal ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
            VerticalAlignment = orientation == SplitterOrientation.Horizontal ? VerticalAlignment.Top : VerticalAlignment.Stretch,
        };
        if (orientation == SplitterOrientation.Horizontal) Splitter.Height = Splitter.DividerThickness;
        else Splitter.Width = Splitter.DividerThickness;
        Root.Children.Add(Pane);
        Root.Children.Add(Splitter);
        Place(Value.Model.Shown.Value);
        Drawn.Clear();
        Window = HeadlessPopupHost.OverlayWindow(Root);
        Window.Width = 900;
        Window.Height = 700;
        Window.Show();
        Dispatcher.UIThread.RunJobs();
    }

    public TestRuntime Runtime { get; }

    public ManualFrameSource Frames { get; }

    public SplitterOrientation Orientation { get; }

    public SplitterLimits Limits { get; }

    public Panel Root { get; }

    public Border Pane { get; }

    public SplitterValue Value { get; }

    public Splitter Splitter { get; }

    public AvaloniaWindow Window { get; }

    /// <summary>Every value the splitter drew, in order.</summary>
    public List<double> Drawn { get; } = [];

    /// <summary>Every committed state, in order.</summary>
    public List<SplitterState> Commits { get; } = [];

    /// <summary>The pane's size now (its width, or its height for a horizontal splitter).</summary>
    public double Size => Orientation == SplitterOrientation.Horizontal ? Pane.Height : Pane.Width;

    /// <summary>The splitter's centre, in window coordinates.</summary>
    public Point Centre
    {
        get
        {
            Dispatcher.UIThread.RunJobs();
            return Splitter.TranslatePoint(new Point(Splitter.Bounds.Width / 2, Splitter.Bounds.Height / 2), Window)!.Value;
        }
    }

    /// <summary>A travel along the splitter's axis, as a window vector.</summary>
    public Vector Along(double travel) => Orientation == SplitterOrientation.Horizontal ? new Vector(0, travel) : new Vector(travel, 0);

    /// <summary>One frame: the clock moves <paramref name="milliseconds"/> and the frame source delivers.</summary>
    public void Frame(double milliseconds = 1000.0 / 60)
    {
        Runtime.Advance(TimeSpan.FromMilliseconds(milliseconds));
        Frames.Advance(TimeSpan.FromMilliseconds(milliseconds));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Frames until nothing is animating (at most <paramref name="limit"/>).</summary>
    public int Settle(int limit = 600)
    {
        var count = 0;
        while ((Value.IsAnimating || Splitter.Grip.IsEasing || Frames.Pending > 0) && count < limit)
        {
            Frame();
            count++;
        }
        return count;
    }

    public void Wait(double milliseconds)
    {
        Runtime.Advance(TimeSpan.FromMilliseconds(milliseconds));
        Dispatcher.UIThread.RunJobs();
    }

    // Input runs on Avalonia's dispatcher; a task it spawns on the main actor (the hover and tooltip delays) starts on
    // the test runtime's loop, as it starts at once on the app's dispatcher.
    private void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        Runtime.RunUntilIdle();
        Dispatcher.UIThread.RunJobs();
    }

    public void Move(Point to, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Window.MouseMove(to, modifiers);
        Pump();
    }

    public void Down(Point at)
    {
        Window.MouseDown(at, MouseButton.Left, RawInputModifiers.LeftMouseButton);
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

    /// <summary>Gives the splitter keyboard focus, as Tab would.</summary>
    public void FocusByKeyboard()
    {
        Splitter.Focus(NavigationMethod.Tab);
        Dispatcher.UIThread.RunJobs();
    }

    private void Place(double value)
    {
        Drawn.Add(value);
        var edge = Math.Max(0, value);
        if (Orientation == SplitterOrientation.Horizontal)
        {
            Pane.Height = edge;
            Splitter.Margin = new Thickness(0, edge - Splitter.DividerThickness / 2, 0, 0);
        }
        else
        {
            Pane.Width = edge;
            Splitter.Margin = new Thickness(edge - Splitter.DividerThickness / 2, 0, 0, 0);
        }
    }

    public void Dispose()
    {
        Window.Close();
        cursorScope.Dispose();
        motionScope.Dispose();
        frameScope.Dispose();
        Runtime.Dispose();
    }

    private sealed class Nothing : IDisposable
    {
        public void Dispose() { }
    }
}
