// PORT-ONLY. SwiftUI's `.buttonStyle(.bordered)` and `.buttonStyle(.borderedProminent)` are AppKit's system push buttons
// (a bezel; the prominent one filled with the accent). The app carries no theme package, so an Avalonia Button without a
// template shows its label and nothing else (probe 2026-10-01).
//
// ENGINEERING DECISION (proposal ED-229 (4), E:\Droppy\work\reference-video\ED_PROPOSAL_system_controls.md): the Windows
// counterparts are the system's standard and accent buttons, from the Windows 11 design (WinUI Button / AccentButton:
// corner radius 4, padding 11,5,11,6, a translucent fill with a hairline border, or the accent fill with white text);
// recorded deviation from the macOS bezel. The label uses the port's body font. A disabled button keeps its shape at
// 40 % opacity (the macOS and Windows disabled look both dim the whole control).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Styling;

namespace Marrow.UI.Port;

/// <summary>PORT-ONLY. The bordered and bordered-prominent buttons (see the file note).</summary>
public static class SystemButtons
{
    private static readonly SwiftColor StandardFill = SwiftColor.Dynamic(new SwiftColor(white: 1, opacity: 0.7), new SwiftColor(white: 1, opacity: 0.06));
    private static readonly SwiftColor StandardStroke = SwiftColor.Dynamic(new SwiftColor(white: 0, opacity: 0.06), new SwiftColor(white: 1, opacity: 0.07));

    /// <summary><c>Button(title, action:).buttonStyle(.bordered)</c>.</summary>
    public static Button Bordered(string title, Action action) => Make(title, action, prominent: false);

    /// <summary><c>Button(title, action:).buttonStyle(.borderedProminent)</c>.</summary>
    public static Button BorderedProminent(string title, Action action) => Make(title, action, prominent: true);

    private static Button Make(string title, Action action, bool prominent)
    {
        var label = new TextBlock { Text = title, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        SwiftFont.Body.Apply(label);
        var button = new Button
        {
            Content = label,
            Template = new FuncControlTemplate<Button>((owner, scope) =>
            {
                var card = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(11, 5, 11, 6),
                    BorderThickness = new Thickness(prominent ? 0 : 1),
                    Child = new ContentPresenter
                    {
                        Name = "PART_ContentPresenter",
                        [!ContentPresenter.ContentProperty] = owner[!ContentControl.ContentProperty],
                    }.RegisterInNameScope(scope),
                };
                card.Background = (prominent ? Marrow.UI.Chrome.Chrome.Accent : StandardFill).Brush(card);
                if (!prominent) card.BorderBrush = StandardStroke.Brush(card);
                return card;
            }),
        };
        label.Foreground = (prominent ? SwiftColor.White : Marrow.UI.Chrome.Chrome.PrimaryText).Brush(label);
        button.PropertyChanged += (_, e) =>
        {
            if (e.Property == Avalonia.Input.InputElement.IsEnabledProperty) button.Opacity = button.IsEnabled ? 1 : 0.4;
        };
        button.Click += (_, _) => action();
        return button;
    }
}
