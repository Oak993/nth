// PORT-ONLY: T11 step 2. A pill drawn as the page draws it: the composer's capsule, with the pill's name and its steps inside as small account marks joined in order (a step on a new service has a quiet join; a step on an account that is off is dimmed).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.App.Port.Skills.Pages;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Settings;
using Marrow.UI.Common;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.MultiAccount.Settings;

internal sealed class PillShape : Decorator
{
    private readonly StackPanel marks = new() { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
    private readonly Control label;

    /// <param name="label">The name: text, or a box when the name is being edited.</param>
    internal PillShape(Control label)
    {
        this.label = label;
        label.VerticalAlignment = VerticalAlignment.Center;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { label, marks } };
        var capsule = new Border { Padding = new Thickness(12, 5, 8, 5), Child = row, HorizontalAlignment = HorizontalAlignment.Left }.RoundedRectangle(100, RoundedCornerStyle.Continuous);
        capsule.Background = ChromeTokens.Overlay(0.07).Brush(capsule);
        Child = capsule;
    }

    internal int MarkCount => marks.Children.Count;

    internal IReadOnlyList<(string Name, bool Skipped, double Opacity)> Marks =>
        [.. marks.Children.OfType<Control>().Select(c => (Avalonia.Automation.AutomationProperties.GetName(c) ?? "", c.Opacity < 1, c.Opacity))];

    internal void Apply(IReadOnlyList<StepMark> steps)
    {
        marks.Children.Clear();
        foreach (var step in steps)
        {
            var icon = new ProviderIcon { Provider = step.Service ?? ProviderKind.Claude, Size = 14, Width = 14, Height = 14 };
            icon.Opacity = step.Skipped ? 0.4 : 1;
            icon.Margin = new Thickness(step.NewService ? 4 : 0, 0, 0, 0);
            icon.Named(step.AccountName);
            marks.Children.Add(icon);
        }
    }
}
