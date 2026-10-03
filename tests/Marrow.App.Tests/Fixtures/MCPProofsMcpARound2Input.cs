using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Views.Settings;

namespace Marrow.App.Tests.Fixtures;

// Source: MCPSettingsPage.swift:164,316,430,493; :169-179,923-961.
public static class MCPProofsMcpARound2Input
{
    // Real scene hit testing and routed native pointer input, never a direct ClickEvent or PointerEntered event.
    public static void PointerClick(Button button)
    {
        Avalonia.Controls.Window? temporary = null;
        if (TopLevel.GetTopLevel(button) is not Avalonia.Controls.Window)
        {
            Control root = button;
            while (root.Parent is Control parent && parent is not Avalonia.Controls.Primitives.Popup && parent is not Avalonia.Controls.Window) root = parent;
            if (root.Parent is Avalonia.Controls.Primitives.Popup popup) popup.Child = null;
            temporary = new Avalonia.Controls.Window { Width = 800, Height = 800, Content = root };
            temporary.Show(); Dispatcher.UIThread.RunJobs();
        }
        try
        {
            var window = (Avalonia.Controls.Window)TopLevel.GetTopLevel(button)!;
            button.BringIntoView(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            // A plain Swift button can stretch beyond its label; activate the rendered label rather than an unspecified empty area.
            var label = button.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Bounds.Width > 0 && t.Bounds.Height > 0);
            var target = (Control?)label ?? button;
            var at = target.TranslatePoint(new Point(label is null ? target.Bounds.Width / 2 : label.TextLayout.Width / 2, label is null ? target.Bounds.Height / 2 : label.TextLayout.Height / 2), window)!.Value;
            using var rendered = window.CaptureRenderedFrame(); var candidate = window.InputHitTest(at); Assert.True(candidate is Visual, $"No hit: at={at}, button={button.Bounds}, window={window.IsVisible}/{window.Bounds}, root={window.Content}");
            var hit = (Visual)candidate!;
            Assert.True(hit == button || button.IsVisualAncestorOf(hit), $"hit={hit.GetType().Name}, button={button.Content}, at={at}, bounds={button.Bounds}, hitBounds={hit.Bounds}, root={window.Content}, path={string.Join("/", hit.GetVisualAncestors().Select(v => v.GetType().Name))}, buttonPath={string.Join("/",button.GetVisualAncestors().Select(v => v.GetType().Name))}, label={label?.Bounds}, labelHit={label?.IsHitTestVisible}, enabled={button.IsEffectivelyEnabled}");
            window.MouseMove(at, RawInputModifiers.None);
            window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
            window.MouseMove(new Point(window.Width - 2, window.Height - 2), RawInputModifiers.None); // clear pointer-over while still on the model actor, before window disposal
        }
        finally { temporary?.Close(); }
    }

    public static void PointerAt(Control control, Point local, bool press = true)
    {
        var window = (Avalonia.Controls.Window)TopLevel.GetTopLevel(control)!;
        control.BringIntoView(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        var at = control.TranslatePoint(local, window)!.Value;
        var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(at));
        Assert.True(hit == control || control.IsVisualAncestorOf(hit), $"{control.GetType().Name} was not hit at {local}: {hit.GetType().Name}");
        window.MouseMove(at, RawInputModifiers.None);
        if (press) { window.MouseDown(at, MouseButton.Left, RawInputModifiers.None); window.MouseUp(at, MouseButton.Left, RawInputModifiers.None); }
    }
    public static void MoveAway(Control control)
    {
        var window = (Avalonia.Controls.Window)TopLevel.GetTopLevel(control)!;
        window.MouseMove(new Point(window.Width-2, window.Height-2), RawInputModifiers.None);
    }
    public static void PointerHover(Control control, bool entered)
    {
        // Popover contents are the actual production factory output, hosted on a real headless surface.
        if (TopLevel.GetTopLevel(control) is not Avalonia.Controls.Window)
        {
            if (control.Parent is Avalonia.Controls.Primitives.Popup popup) popup.Child = null;
            PopoverCardContent.Lift(control); // a popover's view comes off the card the presenter hung it on
            new Avalonia.Controls.Window { Width = 800, Height = 800, Content = control }.Show();
        }
        if (!entered) { MoveAway(control); return; }
        var label = control.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.TextLayout.Height > 0);
        var target = (Control?)label ?? control;
        PointerAt(target, label is null ? new Point(1,1) : new Point(label.TextLayout.Width/2,label.TextLayout.Height/2), false);
    }
    public static void KeyboardSpace(Control control)
    {
        var window = (Avalonia.Controls.Window)TopLevel.GetTopLevel(control)!;
        window.Activate(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame(); Assert.True(control.Focus());
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " "); window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
    }

    public static void SpacerOrder(Grid grid, Control name, Control action, double minimum)
    {
        ((Avalonia.Controls.Window)TopLevel.GetTopLevel(grid)!).UpdateLayout();
        var spacer = Assert.Single(grid.Children.OfType<Border>(), b => b.MinWidth == minimum);
        Assert.True(Grid.GetColumn(name) < Grid.GetColumn(spacer));
        Assert.True(Grid.GetColumn(spacer) < Grid.GetColumn(action));
        Assert.True(spacer.Bounds.Width >= minimum);
        Assert.True(name.Bounds.Right <= spacer.Bounds.X);
        Assert.True(spacer.Bounds.Right <= action.Bounds.X);
        Assert.True(action.Bounds.X - name.Bounds.Right >= minimum);
    }
}
