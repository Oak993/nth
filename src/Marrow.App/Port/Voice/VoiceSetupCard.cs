// PORT-ONLY: WV-1 / VT-15, OD-0098. The orb's PopoverCard uses the locked V2 words and the preparation's real progress.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Marrow.App.Port.FirstRun;
using Marrow.UI.Port.Settings;
using Marrow.UI.Port;
using Marrow.UI.Theme;

namespace Marrow.App.Port.Voice;

internal sealed class VoiceSetupCard : Decorator
{
    private readonly VoiceFirstRun flow;
    private readonly VoiceProgressBar bar = new();
    private readonly TextBlock progress = new();

    internal VoiceSetupCard(VoiceFirstRun flow)
    {
        this.flow = flow;
        Width = 330;
        var body = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8 };
        body.Children.Add(new WelcomeVoiceSection(flow.Question, atOrb: true));
        SwiftFont.System(SwiftFont.TextStyle.Caption).Apply(progress);
        progress.Margin = new Thickness(16, 0, 16, 8);
        body.Children.Add(bar);
        body.Children.Add(progress);
        Child = body;
        AttachedToVisualTree += (_, _) => { flow.Changed += Update; Update(); };
        DetachedFromVisualTree += (_, _) => flow.Changed -= Update;
        Update();
    }

    private void Update()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(Update); return; }
        var item = flow.Progress;
        var running = item?.State == VoicePrepState.Running;
        bar.SetActive(running && item?.Fraction is not null);
        bar.SetFraction(item?.Fraction ?? 0);
        progress.Text = running ? item!.Label + (item.Fraction is { } fraction
            ? " " + fraction.ToString("P0", System.Globalization.CultureInfo.InvariantCulture) : "") : "";
    }
}
