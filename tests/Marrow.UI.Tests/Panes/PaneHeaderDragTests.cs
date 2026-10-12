using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Marrow.Core.Models;
using Marrow.UI.Panes;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Panes;

// nth web surfaces, unit W3b: the pane header as the start of a re-dock (work/design/web-surfaces/DESIGN.md 3.6 "Press on a
// header's empty area and move 4 DIP: the pane lifts"). The header only reports the gesture; the workspace lifts and
// drops. A press on a button keeps its click, except on the chosen tab; a press that never moves 4 DIP is nothing.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w3b-2")]
public sealed class PaneHeaderDragTests(HeadlessSession headless)
{
    private static Point Empty(PaneHeaderRig rig) => rig.Header.TranslatePoint(new Point(19, 18), rig.Window) ?? default; // the kind glyph's cell

    private static void Down(PaneHeaderRig rig, Point at, MouseButton button = MouseButton.Left)
    {
        rig.Window.MouseDown(at, button, button == MouseButton.Left ? RawInputModifiers.LeftMouseButton : RawInputModifiers.RightMouseButton);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Move(PaneHeaderRig rig, Point to)
    {
        rig.Window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Up(PaneHeaderRig rig, Point at)
    {
        rig.Window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class Events
    {
        public Events(PaneHeader header)
        {
            header.DragLifted += _ => Log.Add("lift");
            header.DragMoved += _ => Log.Add("move");
            header.DragDropped += _ => Log.Add("drop");
            header.DragCanceled += () => Log.Add("cancel");
        }

        public List<string> Log { get; } = [];
    }

    [Fact]
    public void A_press_on_the_empty_area_that_moves_4_DIP_lifts_follows_and_drops() => headless.OnUiThread(() =>
    {
        using var rig = new PaneHeaderRig(PaneKind.Browser);
        rig.Header.CanDrag = true;
        var events = new Events(rig.Header);
        var press = Empty(rig);
        Down(rig, press);
        Move(rig, press + new Vector(3.9, 0));
        Assert.Empty(events.Log); // under 4 DIP is a press, not a drag
        Assert.Equal(new Point(19, 18), rig.Header.PressPoint);
        Move(rig, press + new Vector(4.5, 0));
        Assert.Equal(["lift", "move"], events.Log);
        Assert.True(rig.Header.IsLifted);
        Assert.NotNull(rig.Header.Cursor); // the move cursor while it is held
        Move(rig, press + new Vector(80, 40));
        Move(rig, press + new Vector(120, 60));
        Up(rig, press + new Vector(120, 60));
        Assert.Equal(["lift", "move", "move", "move", "drop"], events.Log);
        Assert.False(rig.Header.IsLifted);
        Assert.Null(rig.Header.Cursor);
        Move(rig, press + new Vector(130, 60)); // a released button drags nothing
        Assert.Equal(5, events.Log.Count);
    });

    [Fact]
    public void A_press_that_never_moves_4_DIP_raises_nothing_at_all() => headless.OnUiThread(() =>
    {
        using var rig = new PaneHeaderRig(PaneKind.Browser);
        rig.Header.CanDrag = true;
        var events = new Events(rig.Header);
        var press = Empty(rig);
        Down(rig, press);
        Move(rig, press + new Vector(2, 2));
        Up(rig, press + new Vector(2, 2));
        Assert.Empty(events.Log);
        Assert.False(rig.Header.IsLifted);
    });

    [Fact]
    public void Without_CanDrag_a_header_never_lifts() => headless.OnUiThread(() =>
    {
        using var rig = new PaneHeaderRig(PaneKind.Browser);
        var events = new Events(rig.Header);
        Assert.False(rig.Header.CanDrag);
        var press = Empty(rig);
        Down(rig, press);
        Move(rig, press + new Vector(60, 0));
        Up(rig, press + new Vector(60, 0));
        Assert.Empty(events.Log);
    });

    [Fact]
    public void Only_the_left_button_lifts() => headless.OnUiThread(() =>
    {
        using var rig = new PaneHeaderRig(PaneKind.Browser);
        rig.Header.CanDrag = true;
        var events = new Events(rig.Header);
        var press = Empty(rig);
        Down(rig, press, MouseButton.Right);
        rig.Window.MouseMove(press + new Vector(60, 0), RawInputModifiers.RightMouseButton);
        rig.Window.MouseUp(press + new Vector(60, 0), MouseButton.Right, RawInputModifiers.None);
        Assert.Empty(events.Log);
    });

    [Fact]
    public void A_press_on_a_button_keeps_its_click_and_does_not_lift() => headless.OnUiThread(() =>
    {
        using var rig = new PaneHeaderRig(PaneKind.Browser);
        rig.Header.CanDrag = true;
        var events = new Events(rig.Header);
        var close = rig.Header.CloseButton;
        var at = close.TranslatePoint(new Point(close.Bounds.Width / 2, close.Bounds.Height / 2), rig.Window)!.Value;
        var closed = 0;
        rig.Header.CloseRequested += () => closed++;
        Down(rig, at);
        Move(rig, at + new Vector(50, 20));
        Up(rig, at + new Vector(50, 20));
        Assert.Empty(events.Log);
        Assert.Equal(0, closed);
        Down(rig, at);
        Up(rig, at);
        Assert.Equal(1, closed);
        Assert.Empty(events.Log);
    });

    [Fact]
    public void The_chosen_tab_lifts_and_another_tab_is_a_click() => headless.OnUiThread(() =>
    {
        using var rig = new PaneHeaderRig(PaneKind.Browser);
        rig.Header.CanDrag = true;
        rig.Header.SetTabs([new PaneHeaderTab(PaneKind.BackgroundTasks, "Tasks", false), new PaneHeaderTab(PaneKind.Browser, "Visus", true)]);
        rig.Layout();
        var events = new Events(rig.Header);
        var chosenTabs = rig.Header.TabStrip.Children.OfType<Button>().ToList();
        var chosen = chosenTabs.Single(tab => tab.Classes.Contains("chosen"));
        var other = chosenTabs.Single(tab => !tab.Classes.Contains("chosen"));

        var otherAt = other.TranslatePoint(new Point(other.Bounds.Width / 2, other.Bounds.Height / 2), rig.Window)!.Value;
        Down(rig, otherAt);
        Move(rig, otherAt + new Vector(40, 0));
        Up(rig, otherAt + new Vector(40, 0));
        Assert.Empty(events.Log);

        var chosenAt = chosen.TranslatePoint(new Point(chosen.Bounds.Width / 2, chosen.Bounds.Height / 2), rig.Window)!.Value;
        var chosenClicks = 0;
        rig.Header.TabChosen += _ => chosenClicks++;
        Down(rig, chosenAt);
        Move(rig, chosenAt + new Vector(10, 0));
        Move(rig, chosenAt + new Vector(40, 0));
        Up(rig, chosenAt + new Vector(40, 0));
        Assert.Equal(["lift", "move", "move", "drop"], events.Log);
        Assert.Equal(0, chosenClicks); // lifting a tab is not choosing it
    });

    [Fact]
    public void CancelDrag_ends_a_lift_once_and_the_release_after_it_drops_nothing() => headless.OnUiThread(() =>
    {
        using var rig = new PaneHeaderRig(PaneKind.Browser);
        rig.Header.CanDrag = true;
        var events = new Events(rig.Header);
        var press = Empty(rig);
        Down(rig, press);
        Move(rig, press + new Vector(30, 0));
        rig.Header.CancelDrag();
        Assert.Equal(["lift", "move", "cancel"], events.Log);
        Assert.False(rig.Header.IsLifted);
        Up(rig, press + new Vector(30, 0));
        rig.Header.CancelDrag(); // nothing in flight: nothing raised
        Assert.Equal(["lift", "move", "cancel"], events.Log);
    });
}
