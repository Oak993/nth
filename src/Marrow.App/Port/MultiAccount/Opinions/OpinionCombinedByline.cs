// PORT-ONLY: page .by :344-345, :4869. The actual text is posted by E1; W associates its entry ID, and this annotation uses the round's source snapshot.
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.App.Port.Skills.Pages;
using Marrow.Core.Port.MultiAccount.Thread;
namespace Marrow.App.Port.MultiAccount.Opinions;
internal sealed class OpinionCombinedByline : StackPanel
{
    private readonly TextBlock text;
    internal OpinionCombinedByline(string value, Guid? source, ThreadAccount? account)
    {
        Orientation = Orientation.Horizontal; Spacing = 6; Margin = new Avalonia.Thickness(0, -6, 0, 0);
        var dot = new OpinionSurface(OpinionSurfaceKind.Dot, 3, account) { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center };
        text = LibraryUi.Text(value, 11); Children.Add(dot); Children.Add(text); this.Named(value, "opinion-byline-" + source?.ToString("N"));
        AttachedToVisualTree += (_, _) => Paint(); ActualThemeVariantChanged += (_, _) => Paint();
        void Paint() => text.Foreground = new SolidColorBrush(CapsuleTokens.For(ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark).Tx3);
    }
    internal void Apply(string value) { text.Text = value; Avalonia.Automation.AutomationProperties.SetName(this, value); }
}
