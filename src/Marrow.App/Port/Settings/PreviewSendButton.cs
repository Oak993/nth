// PORT-ONLY CZ-3 (customization-SPEC CZ-4.3, CZ-14.1; ComposerView.swift:815-866 SendDraftState): the preview composer's send button, drawn as the real one is, without
// a thread behind it. It is inert: nothing is sent from the Settings page. OD-0136 amendments 2-3 (ED-NEW-nth-motion, step 9): the real button is the 32 px nth send face
// (graphite disc, silver in the dark, the arrow or the stop square, half-dimmed while there is nothing to send), laid out 28 tall as before, so this preview is too.
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;

namespace Marrow.App.Port.Settings;

internal sealed class PreviewSendButton : Decorator
{
    private readonly NthSendFace face = new();
    private readonly AnimatedScalar dimmed;

    /// <summary>The button (the real one's disc).</summary>
    internal Button Disc { get; }

    /// <summary>The face drawn in it (tests).</summary>
    internal NthSendFace Face => face;

    /// <summary>The symbol now drawn: "arrow.up" or, while a turn runs, "stop.fill".</summary>
    internal string Symbol => SendDraftState.Symbol(face.IsRunning);

    internal PreviewSendButton()
    {
        var label = new Border { Width = NthSendFace.Size, Height = NthSendFace.Size, Child = face }; // ComposerView.swift:835-836
        Disc = PlainControls.Button(label, () => { });
        Disc.Focusable = false;
        Disc.IsHitTestVisible = false; // inert
        Disc.Margin = new Thickness(4, -2, 0, -2); // :841, and the 32 px face laid out 28 tall as the real button is
        AutomationProperties.SetName(Disc, "Send");
        Child = Disc;
        dimmed = new AnimatedScalar(label, face.Dim, value => face.Dim = value);
        Show(running: false, enabled: false, animated: false);
    }

    /// <summary>The preview's state: Typing has something to send; Running offers to stop. Rest has nothing to send.</summary>
    internal void Show(bool running, bool enabled, bool animated)
    {
        var animation = animated && FrameClock.IsBound ? Animation.EaseOut(duration: 0.12) : null; // the real button's dim motion (OD-0076)
        Disc.IsEnabled = enabled;
        AutomationProperties.SetName(Disc, running ? "Stop" : "Send");
        face.IsRunning = running; // the face animates the change once it is shown, as the real one does
        dimmed.Set(enabled ? 1 : SendDraftState.DisabledLabelOpacity, animation);
    }
}
