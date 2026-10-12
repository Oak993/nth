// PORT-ONLY: Tactus pill v2, where a click may activate the no-activate overlay (OD-0144, OD-0145, ED-NEW-pcctl-20;
// DESIGN.md 4.1, 10.4 "It never steals focus", 10.6). The pill and its card never take the keyboard on their own: a click
// on a button fires without activation, and only a click on a field marked here (the mini composer's text field) lets
// Windows activate the overlay, so typing reaches the card only after the user clicked into it. The overlay's
// WM_MOUSEACTIVATE handler asks Hit for the point under the cursor; a disabled or hidden field never activates.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Marrow.UI.Port.PcControl;

public static class OverlayActivation
{
    /// <summary>True on the one control (and its parts) whose click may activate the overlay: the composer's field.</summary>
    public static readonly AttachedProperty<bool> ActivatesProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Activates", typeof(OverlayActivation));

    public static bool GetActivates(Control control) => control.GetValue(ActivatesProperty);

    public static void SetActivates(Control control, bool value) => control.SetValue(ActivatesProperty, value);

    /// <summary>
    /// True when <paramref name="point"/> (in <paramref name="root"/>'s DIP) hits an enabled, visible control marked
    /// <see cref="ActivatesProperty"/>, or a part inside one.
    /// </summary>
    public static bool Hit(Visual root, Point point)
    {
        if (root is not IInputElement element || element.InputHitTest(point) is not Visual hit) return false;
        foreach (var visual in hit.GetSelfAndVisualAncestors())
        {
            if (visual is Control control && GetActivates(control)) return control.IsEffectivelyEnabled && control.IsEffectivelyVisible;
            if (ReferenceEquals(visual, root)) break;
        }
        return false;
    }
}
