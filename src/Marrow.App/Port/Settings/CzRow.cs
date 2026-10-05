// PORT-ONLY CZ-4 (customization-SPEC CZ-3.1, CZ-12.2.7, CZ-12.9, CZ-14.1; DESIGN gen.py ROW_BODY, cz.js row()): one row of the Customization page's cards, in the port's own ChromeRow metrics (padding 16 / 11 / 10 / 11, a body title over a
// subheadline detail, the control at the trailing edge, CZ-3.1 gives the port's component the last word), with the detail a crossfading text (CZ-12.9) so a change of setting crossfades the sentence and eases the row's height, and a Dimmed
// state (the design's `na`): the text at 0.5 over 0.3 s, the control inert and out of the focus chain (CZ-12.2.7).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

internal sealed class CzRow : Decorator
{
    private readonly CzNumber textOpacity;
    private readonly Action<bool>? dimControl;
    private readonly StackPanel texts;
    private bool dimmed;

    internal TextBlock Title { get; }
    internal CzCrossfadeText Detail { get; }
    internal Control Control { get; }

    /// <param name="dimControl">How the control becomes inert and unfocusable (and fades itself); null for a control that has no such state.</param>
    internal CzRow(string title, string detail, Control control, Action<bool>? dimControl = null, bool stacked = false, bool topAligned = false)
    {
        Control = control;
        this.dimControl = dimControl;
        Title = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap };
        SwiftFont.System(SwiftFont.TextStyle.Body).Apply(Title);
        Title.Foreground = Chrome.PrimaryText.Brush(Title);
        // the detail is ChromeRow's subheadline in the secondary colour, in its 16 point line
        Detail = new CzCrossfadeText(detail) { Wrap = true, Secondary = true, FontSize = SwiftFont.Subheadline.Resolve().FontSize, HorizontalAlignment = HorizontalAlignment.Stretch };
        Detail.SetValue(SwiftFont.LineBoxProperty, 16.0);
        texts = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Stretch, Children = { Title, Detail } };
        control.HorizontalAlignment = HorizontalAlignment.Right;
        control.VerticalAlignment = topAligned ? VerticalAlignment.Top : VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,12,16,Auto"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(texts, 0);
        Grid.SetColumn(control, 4);
        grid.Children.Add(texts);
        grid.Children.Add(control);
        Control body = grid;
        if (stacked)
        {
            grid.Children.Clear();
            control.HorizontalAlignment = HorizontalAlignment.Left;
            body = new StackPanel { Spacing = 8, Children = { texts, control } };
        }
        Child = new Border { Padding = new Thickness(16, 11, Chrome.RowControlTrailingPadding, 11), Child = body };
        textOpacity = new CzNumber(new CzMotion(this), 1, v => texts.Opacity = v);
        Avalonia.Automation.AutomationProperties.SetName(this, title);
    }

    /// <summary>Whether the row is dimmed and inert (CZ-12.2.7): its text at 0.5 over 0.3 s, its control inert and not focusable.</summary>
    internal bool Dimmed
    {
        get => dimmed;
        set
        {
            if (dimmed == value) return;
            dimmed = value;
            textOpacity.To(value ? .5 : 1, CzMotion.Ease(.3));
            dimControl?.Invoke(value);
        }
    }

    /// <summary>How opaque the title and detail are now: 1, or 0.5 when dimmed (mid-fade while it changes).</summary>
    internal double TextOpacity => texts.Opacity;

    /// <summary>The detail sentence now: it crossfades (CZ-12.9) when it is not the one shown.</summary>
    internal void SetDetail(string text) => Detail.SetText(text);
}
