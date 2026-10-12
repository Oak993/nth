using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Panes;
using Marrow.UI.Port;

// PORT-ONLY: web surfaces W4, the Changes pane's controls (E:/Droppy/work/design/web-surfaces/DESIGN.md 3.12 "pane cards ...
// are tinted layers over [the window's one blur] ... Header controls are plain glyphs with a tinted hover fill. Real glass
// with its own blur is used only where something sits over content"; ED-NEW-web-w4-1). No Swift counterpart. The
// popover's chrome row is glass capsules (ChromeTextMenu, ChromeCapsule); inside a pane card that would be glass on glass,
// so the pane's row uses the pane header's plain look: a tinted pill for the selection menu and a glyph button for More,
// each opening the same popover menu the capsule opens.

namespace Marrow.App.Views.Diff;

/// <summary>The plain controls of the Changes pane's chrome row.</summary>
internal static class DiffPaneChrome
{
    private sealed class MenuState : MainActorModel
    {
        private bool isPresented;

        public bool IsPresented { get => Read(isPresented); set => Set(ref isPresented, value); }
    }

    /// <summary>The selection menu: a tinted pill with the choice and a chevron ("All changes ▾"), opening <paramref name="menu"/>.</summary>
    public static Control SelectionChip(string title, string help, Func<Control> menu)
    {
        var state = new MenuState();
        var text = new TextBlock
        {
            Text = title,
            FontSize = 11.5,
            FontWeight = FontWeight.Medium,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var chevron = new PaneGlyph(PaneGlyphKind.Chevron, 11) { VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { text, chevron } };
        var face = new Border
        {
            Height = 26,
            Padding = new Thickness(10, 0, 7, 0),
            CornerRadius = new CornerRadius(13),
            Child = row,
        };
        void Paint(bool hovering)
        {
            face.Background = (hovering ? PaneTokens.HoverFill : PaneTokens.HoverFill.Opacity(0.6)).Brush(face);
            var ink = (hovering ? PaneTokens.Text1 : PaneTokens.Text2).Brush(face);
            text.Foreground = ink;
            chevron.Foreground = ink;
        }
        Paint(false);
        var button = PlainControls.Button(face, () => state.IsPresented = !state.IsPresented).KeyboardFocus(13);
        button.OnHover(Paint);
        ToolTip.SetTip(button, help);
        AutomationProperties.SetName(button, help);
        var popover = PlainControls.Popover(button, new Binding<bool>(() => state.IsPresented, value => state.IsPresented = value), () => new PopoverMenu(menu));
        return new Panel { Children = { button, popover } };
    }

    /// <summary>The More button: the pane header's plain glyph button, opening <paramref name="menu"/>.</summary>
    public static Control MoreButton(string help, Func<Control> menu)
    {
        var state = new MenuState();
        var glyph = new PaneGlyph(PaneGlyphKind.More);
        var face = new Border
        {
            Width = PaneTokens.ButtonSize,
            Height = PaneTokens.ButtonSize,
            CornerRadius = new CornerRadius(8),
            Child = glyph,
        };
        void Paint(bool hovering)
        {
            face.Background = hovering ? PaneTokens.HoverFill.Brush(face) : Brushes.Transparent;
            glyph.Foreground = (hovering ? PaneTokens.Text1 : PaneTokens.Text2).Brush(face);
        }
        Paint(false);
        var button = PlainControls.Button(face, () => state.IsPresented = !state.IsPresented).KeyboardFocus(8);
        button.OnHover(Paint);
        ToolTip.SetTip(button, help);
        AutomationProperties.SetName(button, help);
        var popover = PlainControls.Popover(button, new Binding<bool>(() => state.IsPresented, value => state.IsPresented = value), () => new PopoverMenu(menu));
        return new Panel { Children = { button, popover } };
    }
}
