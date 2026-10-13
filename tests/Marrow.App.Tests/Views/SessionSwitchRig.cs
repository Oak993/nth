using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views;
using Marrow.App.Views.Panes;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.App.Views.Sidebar;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views;

// ED-NEW-session-hotkeys: SessionSwitchTests' shown window with a sidebar and a detail, stepped on the manual frame clock.
internal sealed class SessionSwitchRig(AppModelHarness h, AppModel model, ThreadWindow window, SidebarView sidebar, SidebarView? floating, DetailView detail, ManualFrameSource frames, Guid[] ids, Guid[] second)
{
    public const double Frame = 1000.0 / 60;

    public AppModelHarness H { get; } = h;
    public AppModel Model { get; } = model;
    public ThreadWindow Window { get; } = window;
    public SidebarView Sidebar { get; } = sidebar;
    public SidebarView? Floating { get; } = floating;
    public DetailView Detail { get; } = detail;
    public ManualFrameSource Frames { get; } = frames;
    public Guid[] Ids { get; } = ids;
    public Guid[] Second { get; } = second;
    public bool ControlDown { get; set; }

    /// <summary>The numbered order the sidebar published (a convenience: the tests that prove the order read the rows).</summary>
    public IReadOnlyList<Guid> Order => Model.SessionNumbers.Order ?? throw new Xunit.Sdk.XunitException("no published order");

    public void Step(double milliseconds = Frame) => Step(TimeSpan.FromMilliseconds(milliseconds));

    public void Step(TimeSpan by)
    {
        // The real dispatcher runs work posted by input (a spawned hold, a posted refresh) before time moves on.
        Dispatcher.UIThread.RunJobs();
        H.Main.Time.Advance(by, () => Dispatcher.UIThread.RunJobs());
        Frames.Advance(by);
    }

    public void Settle(int frames = 40)
    {
        for (var i = 0; i < frames; i++)
        {
            Step();
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
        }
    }

    public void Press(Key key, PhysicalKey physical)
    {
        Window.KeyPress(key, RawInputModifiers.Control, physical, null);
        Dispatcher.UIThread.RunJobs();
    }

    public void Release(Key key, PhysicalKey physical) => Window.KeyRelease(key, RawInputModifiers.Control, physical, null);

    public void Key(Key key, PhysicalKey physical)
    {
        Window.KeyPress(key, RawInputModifiers.None, physical, null);
        Window.KeyRelease(key, RawInputModifiers.None, physical, null);
        Dispatcher.UIThread.RunJobs();
    }

    // ED-NEW-finalint-17 / web-w3a-1: the approved workspace wraps the unchanged session columns.
    public ThreadColumns Columns
    {
        get
        {
            var workspace = Assert.IsType<PaneWorkspace>(Detail.Child);
            Assert.Same(Detail.Workspace, workspace);
            return Assert.IsType<ThreadColumns>(workspace.Chat.Content);
        }
    }

    public Dictionary<Guid, FrameHostingView> ColumnMap
    {
        get
        {
            var coordinator = typeof(ThreadColumns).GetField("coordinator", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Columns)!;
            return (Dictionary<Guid, FrameHostingView>)coordinator.GetType().GetField("columns", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(coordinator)!;
        }
    }

    public FrameHostingView Column(Guid id) => ColumnMap[id];

    public ChatView Chat(Guid id) => ChatSwitchMotion.ChatOf(Column(id))!;

    public SidebarThreadRow Row(Guid id) =>
        LogicalQuery.SelfAndDescendants<SidebarThreadRow>(Sidebar).Single(row => ((IGlideRow)row).ThreadID == id);

    /// <summary>The row's fill shape in the sidebar's space.</summary>
    public Rect RowFrame(Guid id)
    {
        var shape = ((IGlideRow)Row(id)).FillShape;
        return new Rect(shape.TranslatePoint(default, Sidebar.Glide)!.Value, shape.Bounds.Size);
    }

    /// <summary>The pointer arrives over the row (its hover look settles in its own 0.1 s, before any click).</summary>
    public void Hover(Guid id)
    {
        Window.MouseMove(Center(id));
        Dispatcher.UIThread.RunJobs();
    }

    private Point Center(Guid id)
    {
        var shape = ((IGlideRow)Row(id)).FillShape;
        return shape.TranslatePoint(new Point(shape.Bounds.Width / 2, shape.Bounds.Height / 2), Window)!.Value;
    }

    public void Click(Guid id)
    {
        var center = Center(id);
        Window.MouseMove(center);
        Window.MouseDown(center, MouseButton.Left);
        Window.MouseUp(center, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    public TextBox SearchBox => LogicalQuery.SelfAndDescendants<TextBox>(Sidebar).First();

    public object? Focused => Window.FocusManager?.GetFocusedElement();
}
