// PORT-ONLY CZ-3 (customization-SPEC CZ-4.3, CZ-14.1; ComposerView.swift:815-866 SendDraftState): the preview composer's send button, drawn as the real one is (28 across, the
// white arrow or stop glyph on the accent disc, half-dimmed and on the quaternary fill while there is nothing to send, the glyph replaced in place when it changes),
// without a thread behind it. It is inert: nothing is sent from the Settings page.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Port.Settings;

internal sealed class PreviewSendButton : Decorator
{
    private readonly Border label;
    private readonly Panel face = new();
    private readonly AnimatedColor fill;
    private Control? image;
    private AnimatedColor? ink; // the current glyph's ink: follows the enabled state as the real button's does (SendDraftState.Apply, :833)
    private string? shown;

    /// <summary>The 28 across button (the real one's disc).</summary>
    internal Button Disc { get; }

    /// <summary>The symbol now drawn: "arrow.up" or, while a turn runs, "stop.fill".</summary>
    internal string Symbol => shown ?? SendDraftState.Symbol(false);

    internal PreviewSendButton()
    {
        label = new Border { Width = 28, Height = 28, Child = face }.Capsule(RoundedCornerStyle.Circular); // ComposerView.swift:835-836
        fill = new AnimatedColor(label);
        label.Background = fill.Brush;
        Disc = PlainControls.Button(label, () => { });
        Disc.Focusable = false;
        Disc.IsHitTestVisible = false; // inert
        Disc.Margin = new Thickness(4, 0, 0, 0); // :841
        AutomationProperties.SetName(Disc, "Send");
        Child = Disc;
        Show(running: false, enabled: false, animated: false);
    }

    /// <summary>The preview's state: Typing has something to send; Running offers to stop. Rest has nothing to send.</summary>
    internal void Show(bool running, bool enabled, bool animated)
    {
        var animation = animated && FrameClock.IsBound ? Animation.EaseOut(duration: 0.12) : null; // the real button's hover-fill motion (OD-0076)
        var symbol = SendDraftState.Symbol(running);
        var dim = enabled ? 1 : SendDraftState.DisabledLabelOpacity;
        Disc.IsEnabled = enabled;
        AutomationProperties.SetName(Disc, running ? "Stop" : "Send");
        fill.Set((enabled ? Popovers.GetTint(this) ?? SwiftColor.AccentColor : SwiftColor.Quaternary).Opacity(dim), animation); // :836
        if (symbol != shown)
        {
            var next = SwiftImage.SystemName(symbol).Font(Chrome.IconFont).ForegroundStyle(SwiftColor.White); // :831-833
            var nextInk = new AnimatedColor(next);
            next.Foreground = nextInk.Brush;
            nextInk.Set(SwiftColor.White.Opacity(dim), null);
            ink = nextInk;
            var previous = image;
            image = next;
            if (previous is null || !animated)
            {
                face.Children.Clear();
                face.Children.Add(next);
            }
            else
            {
                face.Children.Add(next);
                SymbolReplace.Play(previous, next, () => face.Children.Remove(previous)); // :834 the symbol's replace effect
            }
            shown = symbol;
        }
        else ink!.Set(SwiftColor.White.Opacity(dim), animation); // :833, :838 every state change sets the ink again
    }
}
