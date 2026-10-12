using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Port;

// PORT-ONLY: ED-NEW-inst-c7-5. The owner's closing notice belongs to the shell, including its empty page.
internal sealed class InstallerClosingNotice : Decorator
{
    private readonly AnimatedScalar shown;
    private bool held;
    private readonly TextBlock message = new()
    {
        Text = "nth is closing to update",
        MaxLines = 1,
        TextWrapping = TextWrapping.NoWrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    internal InstallerClosingNotice()
    {
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(12, Chrome.TrafficLightTop + Chrome.TrafficLightDiameter + 6, 12, 0);
        IsHitTestVisible = false;
        IsVisible = false;
        shown = new AnimatedScalar(this, 0, value =>
        {
            Opacity = value;
            if (!held && value == 0) IsVisible = false;
        });
        ZIndex = 100;
        SwiftFont.System(SwiftFont.TextStyle.Subheadline).Apply(message);
        message.Foreground = Chrome.Warning.Brush(message);
        Child = message;
        this.Track(() => AppEnvironment.GetAppModel(this).InstallerHoldsNewTurns, value =>
        {
            held = value;
            if (held) IsVisible = true;
            shown.Set(held ? 1 : 0, Animation.Smooth(0.15));
        });
    }

    internal string? VisibleMessage => held ? message.Text : null;
}
