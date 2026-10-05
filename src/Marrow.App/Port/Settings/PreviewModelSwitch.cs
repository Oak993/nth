// PORT-ONLY CZ-3 (customization-SPEC CZ-4.5; OWNER OD-0089 amendment, OD-0095; DESIGN gen.py MS(), cz.js:155-190): the preview model switch at the left of the preview's foot:
// a previous stepper, the chip naming the maker, a next stepper, and the menu of every maker. The menu opens ABOVE the chip, as the composer's own model popover does,
// as a popover with the Mac's arrow tip (Q-CZ-5 is settled by OD-0095): the port's presenter wraps it in a PopoverCard and grows it in from the tip. The switch changes the
// preview only (CZ-4.5.3): the PreviewSession's maker.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

internal sealed class PreviewModelSwitch : Decorator
{
    private sealed class MenuPanel : StackPanel
    {
        private sealed class Peer(MenuPanel owner) : ControlAutomationPeer(owner)
        {
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Menu;
        }
        protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);
    }

    /// <summary>CZ-4.5.2: the menu is 270 wide, padding 6.</summary>
    internal const double CardWidth = 270;
    internal const double ItemHeight = PreviewMenuItem.Height28;
    private const double ChipToTopBar = 46 + 44; // cz.js:150, the room the window's top bar takes above the chip

    private readonly PreviewSession session;
    private readonly Popup popup;
    private readonly Border card;
    private TopLevel? watched;
    private bool refreshing;

    internal PreviewStepper Previous { get; } = new("Previous model", forward: false);
    internal PreviewStepper Next { get; } = new("Next model", forward: true);
    internal PreviewSwitchChip Chip { get; }
    internal TextBlock Heading { get; }
    internal Panel Menu { get; }
    internal ScrollViewer List { get; }
    internal IReadOnlyList<PreviewMenuItem> Items { get; }
    internal double MenuWidth => card.Width;
    internal bool IsOpen { get; private set; }

    internal PreviewModelSwitch(PreviewSession session)
    {
        this.session = session;
        Chip = new PreviewSwitchChip(session.Maker);
        Heading = new TextBlock { Text = "Preview with", FontFamily = new FontFamily("fonts:Inter#Inter"), FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = CzTheme.Text(this, secondary: true), Margin = new Thickness(8, 6, 8, 4) };
        Items = PreviewMakers.All.Select(m => new PreviewMenuItem(m, Pick)).ToArray();
        var list = new StackPanel();
        foreach (var item in Items) list.Children.Add(item);
        List = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = 236 };
        Menu = new MenuPanel { Children = { Heading, List } };
        AutomationProperties.SetName(Menu, "Preview model");
        card = new Border { Width = CardWidth, Padding = new Thickness(6), Child = Menu };
        popup = new Popup { Child = card, PlacementTarget = Chip, Placement = PlacementMode.Top, IsLightDismissEnabled = true };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Previous);
        row.Children.Add(Chip);
        row.Children.Add(Next);
        Child = new Grid { Children = { row, popup } };
        Previous.Invoked += () => session.Step(-1);
        Next.Invoked += () => session.Step(1);
        Chip.Invoked += () => { if (IsOpen) CloseMenu(); else OpenMenu(); };
        Chip.Toggled = open => { if (open) OpenMenu(); else CloseMenu(); };
        popup.Closed += (_, _) => { if (IsOpen) CloseMenu(refocus: false); };
        session.Changed += Refresh;
        DetachedFromVisualTree += (_, _) => { CloseMenu(refocus: false); session.Changed -= Refresh; };
        AttachedToVisualTree += (_, _) => { session.Changed -= Refresh; session.Changed += Refresh; Refresh(); };
        Refresh();
    }

    /// <summary>CZ-4.5.2: <c>clamp(room, 112, 236)</c> with room = the chip's top in the window less the top bar's 46 + 44.</summary>
    internal static double ListMaxHeight(double chipTop) => Math.Clamp(chipTop - ChipToTopBar, 112, 236);

    private void Refresh()
    {
        if (refreshing) return;
        refreshing = true;
        try
        {
            Chip.Show(session.Maker);
            foreach (var item in Items) item.SetChecked(item.Maker.Id == session.Maker);
        }
        finally { refreshing = false; }
    }

    private void Pick(PreviewMaker maker)
    {
        session.Pick(maker.Id);
        CloseMenu();
    }

    internal void OpenMenu()
    {
        if (IsOpen) return;
        var top = TopLevel.GetTopLevel(this);
        var chipTop = top is null ? double.MaxValue : Chip.TranslatePoint(default, top)?.Y ?? double.MaxValue;
        List.MaxHeight = ListMaxHeight(chipTop);
        IsOpen = true;
        Chip.SetOpen(true);
        Presentation.SetPresented(popup, true, Chip, () => CloseMenu());
        watched = top;
        watched?.AddHandler(InputElement.KeyDownEvent, MenuKey, RoutingStrategies.Tunnel, handledEventsToo: true);
        card.AddHandler(InputElement.KeyDownEvent, MenuKey, RoutingStrategies.Tunnel, handledEventsToo: true);
        var current = Items.First(i => i.Maker.Id == session.Maker);
        current.Focus(NavigationMethod.Directional);
        CenterOnCurrent(current);
    }

    // the current item scrolls to the middle of the list (cz.js:183: scrollTop = offsetTop - (clientHeight - itemHeight) / 2), once the list has its extent
    private void CenterOnCurrent(PreviewMenuItem current)
    {
        void Place()
        {
            var viewport = List.Viewport.Height > 0 ? List.Viewport.Height : Math.Min(List.MaxHeight, Items.Count * ItemHeight);
            var offset = Math.Max(0, Items.ToList().IndexOf(current) * ItemHeight - (viewport - ItemHeight) / 2);
            List.Offset = new Vector(0, offset);
        }
        Place();
        void Once(object? sender, EventArgs e)
        {
            List.LayoutUpdated -= Once;
            if (IsOpen) { Place(); current.Focus(NavigationMethod.Directional); }
        }
        List.LayoutUpdated += Once;
    }

    internal void CloseMenu(bool refocus = true)
    {
        if (!IsOpen) return;
        IsOpen = false;
        watched?.RemoveHandler(InputElement.KeyDownEvent, MenuKey);
        watched = null;
        card.RemoveHandler(InputElement.KeyDownEvent, MenuKey);
        Chip.SetOpen(false);
        Presentation.SetPresented(popup, false, Chip, () => { });
        if (refocus) Chip.Focus(NavigationMethod.Directional);
    }

    /// <summary>A click outside the menu: the popover closed itself.</summary>
    internal void LightDismiss() => CloseMenu(refocus: false);

    private void MenuKey(object? sender, KeyEventArgs e)
    {
        if (!IsOpen || e.Handled) return;
        var index = Items.ToList().FindIndex(i => i.IsFocused);
        switch (e.Key)
        {
            case Key.Up: index = (index <= 0 ? Items.Count : index) - 1; break;
            case Key.Down: index = (index + 1) % Items.Count; break;
            case Key.Home: index = 0; break;
            case Key.End: index = Items.Count - 1; break;
            case Key.Enter or Key.Space:
                if (index >= 0) Items[index].Activate();
                e.Handled = true;
                return;
            case Key.Escape:
                CloseMenu(); // the menu only: the window never sees this Escape (CZ-15)
                e.Handled = true;
                return;
            default: return;
        }
        Items[index].Focus(NavigationMethod.Directional);
        Items[index].BringIntoView();
        e.Handled = true;
    }
}
