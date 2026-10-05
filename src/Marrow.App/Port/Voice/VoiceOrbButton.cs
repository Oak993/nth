// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-10, voice-SPEC V-14.2): the orb's hit target.
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Marrow.App.Port.Voice;

/// <summary>
/// The button over the orb: nothing drawn (the orb draws itself), a rectangle that takes the pointer, focusable and a tab stop, Enter and Space as any button
/// (V-14.2). To a screen reader it is a toggle, pressed while the session listens.
/// </summary>
internal sealed class VoiceOrbButton : Button
{
    private static readonly FuncControlTemplate<Button> HitTemplate = new((owner, scope) => new Border { Background = Brushes.Transparent });

    public VoiceOrbButton()
    {
        Template = HitTemplate;
        Background = Brushes.Transparent;
        BorderThickness = default;
        Padding = default;
        Focusable = true;
        IsTabStop = true;
    }

    /// <summary>The toggle state a screen reader reads: on while listening.</summary>
    public bool On { get; set; }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(VoiceOrbButton owner) : ButtonAutomationPeer(owner), IToggleProvider
    {
        public ToggleState ToggleState => owner.On ? ToggleState.On : ToggleState.Off;

        public void Toggle() => owner.RaiseEvent(new RoutedEventArgs(ClickEvent));
    }
}
