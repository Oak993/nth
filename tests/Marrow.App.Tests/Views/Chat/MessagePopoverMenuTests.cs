using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

// Tests for App/Views/Chat/MessagePopoverMenu.swift (B2 1fbf81e), :1-83, record PR-CHAT-087. Expected values are
// transcribed from the Swift: RightClickPopoverModifier :4-21 (anchor state, the 1 by 1 attachment rect at the
// recorded point, PopoverMenu { RowActionItems(actions: actions()) }), rightClickPopover :23-28, RightClickCatcher
// :30-40 and RightClickCatcherView :42-83 (hitTest claims only a secondary mouse down inside its bounds and returns
// nil for every other event; rightMouseDown records the point in the flipped view; rightMouseUp is swallowed).
// The view is driven through real Avalonia headless input (window.MouseDown/MouseMove/MouseUp), so a left click, a
// drag and a text selection must reach the content through the real hit test and routing.
// ED-183 (after git merge impl/b2): RightClickCatcher is implemented as a tunnel-phase PointerPressed handler on
// the wrapped content, not as an overlay. The handler calls onRightClick and sets e.Handled = true only for right-
// button or Ctrl+left-button presses inside content bounds; all other presses pass through unhandled.
// Not covered here, reported as STOP items (see the unit report): the context-menu key itself (no Swift line, no
// anchor point); arrowEdge: .top :16 (no recorded mapping from a SwiftUI arrow edge to an Avalonia Popup placement);
// dismissal clearing the anchor :14 (the R-VIEW-9 seam cannot simulate a user dismissal while a headless Popup
// cannot open); acceptsFirstMouse :60 and mouseDownCanMoveWindow :58 (AppKit window behaviour with no headless
// equivalent).
[Collection("Headless")]
public sealed class MessagePopoverMenuTests(HeadlessSession headless)
{
    // The message sits away from the window origin, so a point in the view's own space and a window point differ.
    private static readonly Thickness Inset = new(10, 20, 0, 0);

    private static Avalonia.Controls.Window Show(Control content)
    {
        var window = new Avalonia.Controls.Window { Width = 400, Height = 300, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    // A message row stand-in: a 200 by 60 surface; `child` sits at its top left.
    private static Border Message(Control? child = null) => new()
    {
        Width = 200,
        Height = 60,
        Background = Brushes.Transparent,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        Child = child,
    };

    // A clickable element inside the message (a link, a button): 100 by 40 at the message's top left.
    private static Border Link() => new()
    {
        Width = 100,
        Height = 40,
        Background = Brushes.Transparent,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
    };

    // The modified message, then a sibling of the same size under it, inset from the window origin.
    private static StackPanel Column(Control message, Control? sibling = null)
    {
        var column = new StackPanel
        {
            Margin = Inset,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        column.Children.Add(message);
        if (sibling is not null) column.Children.Add(sibling);
        return column;
    }

    private static void RightClick(Avalonia.Controls.Window window, Point at)
    {
        window.MouseDown(at, MouseButton.Right, RawInputModifiers.None);
        window.MouseUp(at, MouseButton.Right, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static void LeftClick(Avalonia.Controls.Window window, Point at)
    {
        window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static void ControlLeftClick(Avalonia.Controls.Window window, Point at)
    {
        window.MouseDown(at, MouseButton.Left, RawInputModifiers.Control);
        window.MouseUp(at, MouseButton.Left, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    [Trait("Req", "PR-CHAT-087")] // App/Views/Chat/MessagePopoverMenu.swift:9-19,25-27,63-66,69-71; UI/Chrome/Popovers.swift:354-363
    public void A_right_click_on_the_message_presents_a_PopoverMenu_holding_RowActionItems_of_the_actions() => headless.OnUiThread(() =>
    {
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var ran = new List<string>();
        var view = Message().RightClickPopover(actions: () => MessageActionsSample.Make(canRevert: false, ran));
        var window = Show(Column(view));
        Assert.DoesNotContain(presenter.Calls, call => call.Presented);

        RightClick(window, new Point(40, 45));

        var shown = Assert.Single(presenter.Calls, call => call.Presented);
        Assert.Equal((shown.Popup, true), presenter.Calls[^1]);
        Assert.NotNull(shown.Popup.Child);
        var menu = Assert.Single(LogicalQuery.SelfAndDescendants<PopoverMenu>(shown.Popup.Child));
        var items = Assert.Single(LogicalQuery.SelfAndDescendants<RowActionItems>(menu));
        // Popovers.swift:357-361: one PopoverItem per action, a PopoverDivider only before a startsGroup action.
        Assert.Equal(2, LogicalQuery.SelfAndDescendants<PopoverItem>(items).Count());
        Assert.Empty(LogicalQuery.SelfAndDescendants<PopoverDivider>(items));
        // Presenting the menu runs no action.
        Assert.Empty(ran);
        window.Close();
    });

    [Theory]
    [Trait("Req", "PR-CHAT-087")] // App/Views/Chat/MessagePopoverMenu.swift:11-15,56,69-71
    [InlineData(40, 45)]
    [InlineData(209, 79)]
    [InlineData(150, 30)]
    public void The_popover_is_anchored_to_a_one_by_one_rect_at_the_pointer(double x, double y) => headless.OnUiThread(() =>
    {
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var view = Message(Link()).RightClickPopover(actions: () => MessageActionsSample.Make(canRevert: true, []));
        var window = Show(Column(view));

        RightClick(window, new Point(x, y));

        var popup = Assert.Single(presenter.Calls, call => call.Presented).Popup;
        Assert.NotNull(popup.PlacementRect);
        var rect = popup.PlacementRect.Value;
        // :15 CGSize(width: 1, height: 1)
        Assert.Equal(new Size(1, 1), rect.Size);
        // :15 origin: anchor, the pointer in the view's own flipped space (:56, :70); PlacementRect is relative to
        // the placement target (the popup's parent when no target is set), so in window space it is the click point.
        var target = popup.PlacementTarget ?? popup.Parent as Visual;
        Assert.NotNull(target);
        Assert.Equal(new Point(x, y), target.TranslatePoint(rect.Position, window));
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-CHAT-087")] // App/Views/Chat/MessagePopoverMenu.swift:9-19,69-71
    public void The_actions_are_read_when_the_popover_presents_so_the_menu_shows_what_they_return_at_the_click() => headless.OnUiThread(() =>
    {
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var canRevert = false;
        var reads = 0;
        var view = Message().RightClickPopover(actions: () =>
        {
            reads += 1;
            return MessageActionsSample.Make(canRevert, []);
        });
        var window = Show(Column(view));
        canRevert = true;

        RightClick(window, new Point(40, 45));

        Assert.True(reads >= 1);
        var popup = Assert.Single(presenter.Calls, call => call.Presented).Popup;
        Assert.NotNull(popup.Child);
        var items = Assert.Single(LogicalQuery.SelfAndDescendants<RowActionItems>(popup.Child));
        // TimelineRows.swift:219-226 with canRevert: three actions, the third startsGroup (Popovers.swift:358).
        Assert.Equal(3, LogicalQuery.SelfAndDescendants<PopoverItem>(items).Count());
        Assert.Single(LogicalQuery.SelfAndDescendants<PopoverDivider>(items));
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-CHAT-087")] // App/Views/Chat/MessagePopoverMenu.swift:42-46,62-67
    public void A_left_click_on_a_link_inside_the_message_still_reaches_the_link_and_presents_nothing() => headless.OnUiThread(() =>
    {
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var link = Link();
        var log = new List<string>();
        link.PointerMoved += (_, _) => log.Add("moved");
        link.PointerPressed += (_, e) => log.Add("pressed " + e.GetCurrentPoint(link).Properties.PointerUpdateKind);
        link.PointerReleased += (_, e) => log.Add("released " + e.InitialPressMouseButton);
        link.Tapped += (_, _) => log.Add("tapped");
        var view = Message(link).RightClickPopover(actions: () => MessageActionsSample.Make(canRevert: false, []));
        var window = Show(Column(view));

        window.MouseMove(new Point(40, 45), RawInputModifiers.None);
        LeftClick(window, new Point(40, 45));

        Assert.Contains("moved", log);
        Assert.Contains("pressed LeftButtonPressed", log);
        Assert.Contains("released Left", log);
        Assert.Contains("tapped", log);
        Assert.DoesNotContain(presenter.Calls, call => call.Presented);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-CHAT-087")] // App/Views/Chat/MessagePopoverMenu.swift:42-46,62-67
    public void A_left_drag_across_message_text_still_selects_the_text_and_presents_nothing() => headless.OnUiThread(() =>
    {
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var text = new SelectableTextBlock
        {
            Text = "Selectable message text",
            FontSize = 14,
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var view = Message(text).RightClickPopover(actions: () => MessageActionsSample.Make(canRevert: false, []));
        var window = Show(Column(view));
        Assert.Equal(0, text.SelectionEnd - text.SelectionStart);

        window.MouseDown(new Point(12, 25), MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(new Point(90, 26), RawInputModifiers.LeftMouseButton);
        window.MouseUp(new Point(90, 26), MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, text.SelectionStart);
        Assert.True(text.SelectionEnd > text.SelectionStart);
        Assert.DoesNotContain(presenter.Calls, call => call.Presented);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-CHAT-087")] // App/Views/Chat/MessagePopoverMenu.swift:62-67,69-71,81
    public void The_right_press_and_its_release_are_claimed_and_never_reach_the_content_underneath() => headless.OnUiThread(() =>
    {
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var link = Link();
        var log = new List<string>();
        link.PointerPressed += (_, _) => log.Add("pressed");
        link.PointerReleased += (_, _) => log.Add("released");
        link.Tapped += (_, _) => log.Add("tapped");
        var view = Message(link).RightClickPopover(actions: () => MessageActionsSample.Make(canRevert: false, []));
        var window = Show(Column(view));

        RightClick(window, new Point(40, 45));

        Assert.Empty(log);
        Assert.Single(presenter.Calls, call => call.Presented);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-CHAT-087")] // App/Views/Chat/MessagePopoverMenu.swift:64
    public void A_right_click_outside_the_message_presents_nothing() => headless.OnUiThread(() =>
    {
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var sibling = Message();
        var siblingPresses = 0;
        sibling.PointerPressed += (_, _) => siblingPresses += 1;
        var view = Message().RightClickPopover(actions: () => MessageActionsSample.Make(canRevert: false, []));
        var window = Show(Column(view, sibling));

        // The message spans window y 20 to 80; the sibling spans 80 to 140.
        RightClick(window, new Point(40, 100));

        Assert.DoesNotContain(presenter.Calls, call => call.Presented);
        Assert.Equal(1, siblingPresses);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-CHAT-087")] // App/Views/Chat/MessagePopoverMenu.swift:62-67,69-71,74-76; ED-183: Ctrl+left presents the popover
    public void A_control_left_click_on_the_message_presents_a_PopoverMenu_at_the_pointer() => headless.OnUiThread(() =>
    {
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var view = Message().RightClickPopover(actions: () => MessageActionsSample.Make(canRevert: false, []));
        var window = Show(Column(view));

        ControlLeftClick(window, new Point(40, 45));

        var popup = Assert.Single(presenter.Calls, call => call.Presented).Popup;
        Assert.NotNull(popup.PlacementRect);
        var rect = popup.PlacementRect.Value;
        Assert.Equal(new Size(1, 1), rect.Size);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-CHAT-087")] // App/Views/Chat/MessagePopoverMenu.swift:62-67,74-76; ED-183: Ctrl+left press is claimed
    public void A_control_left_press_is_claimed_and_never_reaches_the_content() => headless.OnUiThread(() =>
    {
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var link = Link();
        var log = new List<string>();
        link.PointerPressed += (_, _) => log.Add("pressed");
        var view = Message(link).RightClickPopover(actions: () => MessageActionsSample.Make(canRevert: false, []));
        var window = Show(Column(view));

        ControlLeftClick(window, new Point(40, 45));

        Assert.Empty(log);
        Assert.Single(presenter.Calls, call => call.Presented);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-CHAT-087")] // App/Views/Chat/MessagePopoverMenu.swift:62-67,90-95; ED-183: plain left press reaches content
    public void A_plain_left_click_reaches_content_and_presents_nothing() => headless.OnUiThread(() =>
    {
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var link = Link();
        var log = new List<string>();
        link.PointerPressed += (_, _) => log.Add("pressed");
        var view = Message(link).RightClickPopover(actions: () => MessageActionsSample.Make(canRevert: false, []));
        var window = Show(Column(view));

        LeftClick(window, new Point(40, 45));

        Assert.Contains("pressed", log);
        Assert.DoesNotContain(presenter.Calls, call => call.Presented);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-CHAT-087")] // App/Views/Chat/MessagePopoverMenu.swift:62-67; ED-183: shift+left passes through
    public void A_shift_left_click_reaches_content_and_presents_nothing() => headless.OnUiThread(() =>
    {
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var link = Link();
        var log = new List<string>();
        link.PointerPressed += (_, _) => log.Add("pressed");
        var view = Message(link).RightClickPopover(actions: () => MessageActionsSample.Make(canRevert: false, []));
        var window = Show(Column(view));

        window.MouseDown(new Point(40, 45), MouseButton.Left, RawInputModifiers.Shift);
        window.MouseUp(new Point(40, 45), MouseButton.Left, RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();

        Assert.Contains("pressed", log);
        Assert.DoesNotContain(presenter.Calls, call => call.Presented);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-CHAT-087")] // App/Views/Chat/MessagePopoverMenu.swift:62-67; ED-183: middle click passes through
    public void A_middle_click_reaches_content_and_presents_nothing() => headless.OnUiThread(() =>
    {
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var link = Link();
        var log = new List<string>();
        link.PointerPressed += (_, _) => log.Add("pressed");
        var view = Message(link).RightClickPopover(actions: () => MessageActionsSample.Make(canRevert: false, []));
        var window = Show(Column(view));

        window.MouseDown(new Point(40, 45), MouseButton.Middle, RawInputModifiers.None);
        window.MouseUp(new Point(40, 45), MouseButton.Middle, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Contains("pressed", log);
        Assert.DoesNotContain(presenter.Calls, call => call.Presented);
        window.Close();
    });
}
