// PORT-ONLY (ED-NEW-session-hotkeys): a chat column on its way out of a switch is gated as if hidden (not enabled, so no hot key,
// default button or focus reaches it) while it is still drawn for its short fade. Its controls keep the look they had: a
// disabled look appearing mid-fade (a glass button dimming to 40 %) would be a change on screen the switch never asked for.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Marrow.UI.Port;

/// <summary>
/// Whether a control draws its enabled look. <see cref="HeldProperty"/> (inherited) holds the look of a subtree while its gate is
/// closed for a moment: the controls become not <see cref="InputElement.IsEffectivelyEnabled"/>, so nothing acts on them, but
/// each keeps drawing the look it had when the hold began (enabled or, if it was disabled of its own, disabled). Controls that
/// draw a disabled look keep the state they draw in a local, read it through <see cref="Of"/> and repaint on <see cref="Changes"/>.
/// </summary>
public static class EnabledLook
{
    /// <summary>True on a subtree that keeps its enabled look while its gate is closed (inherited down the tree).</summary>
    public static readonly AttachedProperty<bool> HeldProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Held", typeof(EnabledLook), inherits: true);

    public static bool GetHeld(Control control) => control.GetValue(HeldProperty);

    public static void SetHeld(Control control, bool value) => control.SetValue(HeldProperty, value);

    /// <summary>
    /// Whether <paramref name="control"/> draws as enabled: its real state, kept in <paramref name="drawn"/>; while its look is
    /// held, the state it drew last.
    /// </summary>
    public static bool Of(Control control, ref bool drawn)
    {
        if (!control.GetValue(HeldProperty)) drawn = control.IsEffectivelyEnabled;
        return drawn;
    }

    /// <summary>Whether a property change can change <see cref="Of"/>.</summary>
    public static bool Changes(AvaloniaProperty property) =>
        property == InputElement.IsEffectivelyEnabledProperty || property == HeldProperty;
}
