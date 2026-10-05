// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092; first-release voice typing, VT-13): a Settings row whose detail crossfades (customization-SPEC CZ-12.9).
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

/// <summary>
/// The Voice typing page's row: ChromeRow's measures (title in body, an optional detail in subheadline below it, the control at the trailing edge, padding 16 / 11 /
/// <see cref="Chrome.RowControlTrailingPadding"/> / 11, PR-WIN-204) with the detail in <see cref="CzCrossfadeText"/>, so a changed detail crossfades and its height eases (CZ-12.9,
/// OD-0076), where ChromeRow's is a fixed string. The row's control is named after the row (CZ-15) and carries the detail as its help text.
/// </summary>
internal sealed class VoiceSettingsRow : Decorator
{
    /// <summary>The row's title.</summary>
    public string TitleText { get; }

    /// <summary>The detail, or null for a row without one (the Microphone row).</summary>
    public CzCrossfadeText? Detail { get; }

    /// <summary>The control at the trailing edge.</summary>
    public Control Control { get; }

    public VoiceSettingsRow(string title, Control control, string? detail = null)
    {
        TitleText = title;
        Control = control;
        var titleText = new TextBlock { Text = title, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        SwiftFont.System(SwiftFont.TextStyle.Body).Apply(titleText);
        titleText.Foreground = Chrome.PrimaryText.Brush(titleText);
        var textStack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Stretch };
        textStack.Children.Add(titleText);
        if (!string.IsNullOrEmpty(detail))
        {
            var subheadline = SwiftFont.Subheadline;
            Detail = new CzCrossfadeText(detail) { Wrap = true, Secondary = true, FontSize = subheadline.PointSize };
            if (subheadline.LineBox is { } box) Detail.SetValue(SwiftFont.LineBoxProperty, box);
            textStack.Children.Add(Detail);
        }
        AutomationProperties.SetName(control, title);
        if (!string.IsNullOrEmpty(detail)) AutomationProperties.SetHelpText(control, detail);
        control.HorizontalAlignment = HorizontalAlignment.Right;
        control.VerticalAlignment = VerticalAlignment.Center;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,12,16,Auto"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(textStack, 0);
        Grid.SetColumn(control, 4);
        row.Children.Add(textStack);
        row.Children.Add(control);
        Child = new Border { Padding = new Thickness(16, 11, Chrome.RowControlTrailingPadding, 11), Child = row };
    }

    /// <summary>Shows a new detail: it crossfades in and the row's height eases; the control's help text follows.</summary>
    public void SetDetail(string text)
    {
        if (Detail is null) return;
        Detail.SetText(text);
        AutomationProperties.SetHelpText(Control, text);
    }
}
