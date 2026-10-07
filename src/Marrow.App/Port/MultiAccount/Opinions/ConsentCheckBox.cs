// PORT-ONLY: OD-0120 checkbox, not a switch. Minimal plain template, keyboard/pointer semantics retained.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Marrow.App.Port.Skills.Pages;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using Marrow.UI.Port;
namespace Marrow.App.Port.MultiAccount.Opinions;
internal sealed class ConsentCheckBox : CheckBox
{
    internal ConsentCheckBox(string label)
    {
        IsChecked = false; Focusable = true;
        Template = new FuncControlTemplate<CheckBox>((check, _) =>
        {
            var tick = LibraryUi.Text(check.IsChecked == true ? "✓" : "", 11);
            check.PropertyChanged += (_, e) => { if (e.Property == IsCheckedProperty) tick.Text = check.IsChecked == true ? "✓" : ""; };
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children =
            {
                new Border { Width = 14, Height = 14, BorderThickness = new Thickness(1), BorderBrush = ChromeTokens.SecondaryText.Brush(check), Child = tick }.RoundedRectangle(3, RoundedCornerStyle.Continuous),
                LibraryUi.Text(label, 12),
            } };
        });
        this.Named(label, "other-service-remember");
        // Raised keyboard events in an unthemed tree take the same single Space toggle.
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key != Key.Space || !IsEffectivelyEnabled) return; IsChecked = IsChecked != true; e.Handled = true; }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }
}
