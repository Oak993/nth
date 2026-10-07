// PORT-ONLY: locked page :4811, :886-888. Separate colored marks inside the compose box, changes crossfade.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Port.MultiAccount.Capsule;
using Marrow.App.Port.MultiAccount.Shared;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Views.Settings;
using Marrow.Core.Port.MultiAccount.Thread;
using Marrow.UI.Port;
namespace Marrow.App.Port.MultiAccount.Opinions;
internal sealed class AskOthersChips : Decorator
{
    private readonly CrossfadeSlot slot = new();
    private string key = "";
    internal string TextShown { get; private set; } = "";
    internal AskOthersChips() { Child = slot; IsVisible = false; }
    internal void Apply(string text, IReadOnlyList<ThreadAccount> picked)
    {
        var next = text + string.Join("|", picked.Select(a => a.Id + ":" + a.Number + ":" + a.Service));
        if (key == next) return; key = next; TextShown = text;
        Control content = new Border();
        if (text.Length > 0)
        {
            var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4 };
            row.Children.Add(LibraryUi.Text("Ask also: ", 11, secondary: true));
            foreach (var account in picked)
            {
                if (row.Children.Count > 1) row.Children.Add(LibraryUi.Text("·", 11, secondary: true));
                var hue = AccountMarkColors.Of(account);
                row.Children.Add(new Border { Padding = new Thickness(5, 0), Background = new SolidColorBrush(Color.FromArgb(61, hue.R, hue.G, hue.B)), BorderBrush = new SolidColorBrush(Color.FromArgb(153, hue.R, hue.G, hue.B)), BorderThickness = new Thickness(1), Child = LibraryUi.Text(account.Mono, 9.5, weight: SwiftFont.Weight.Bold) }.RoundedRectangle(8, RoundedCornerStyle.Continuous));
            }
            content = LibraryUi.Panel(row, 10, .06, new Thickness(7, 2, 6, 2));
        }
        this.Named(text);
        slot.Show(content, FrameClock.IsBound ? Animation.EaseOut(duration: CapsuleMotion.Reduce(this) ? .16 : .24) : null);
        IsVisible = true; // outgoing chips finish their fade; the empty live slot has no extent.
    }
}
