using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Marrow.Core.Models;

// PORT-ONLY: web surfaces W3b, where a key press lands for the shortcut scope rule (E:/Droppy/work/design/web-surfaces/DESIGN.md
// 3.7 "Content scopes: a web view (Visus page, Tabula preview, App, inline widget) and the terminal. Here the content gets
// every key first"; ED-NEW-web-w3b-1). No Swift counterpart. A control declares itself a content scope with
// <see cref="ShortcutScopeHost.ScopeProperty"/>; every control under it inherits the scope, and the scope of a key press is the
// scope of the control holding keyboard focus.

namespace Marrow.UI.Chrome;

/// <summary>The scope a control and everything under it answers keys in.</summary>
public static class ShortcutScopeHost
{
    /// <summary><see cref="ShortcutScope.Content"/> on a web view host or the terminal; nth's own controls are chrome (the default).</summary>
    public static readonly AttachedProperty<ShortcutScope> ScopeProperty =
        AvaloniaProperty.RegisterAttached<Control, ShortcutScope>("Scope", typeof(ShortcutScopeHost), ShortcutScope.Chrome, inherits: true);

    public static ShortcutScope GetScope(Control control) => control.GetValue(ScopeProperty);

    public static void SetScope(Control control, ShortcutScope value) => control.SetValue(ScopeProperty, value);

    /// <summary>The scope of the control holding keyboard focus in <paramref name="topLevel"/>; chrome when nothing has focus.</summary>
    public static ShortcutScope ScopeOfFocus(TopLevel topLevel) =>
        topLevel.FocusManager?.GetFocusedElement() is Control focused ? GetScope(focused) : ShortcutScope.Chrome;
}
