using Avalonia;
using Avalonia.Controls;
using Marrow.UI.Port;

// Source: DroppyCode/App/Views/Chat/ChatBadge.swift (B2 1fbf81e), :1-200. Skeleton (c100 group 1): every type the
// file declares and every member another file reaches are declared; the view body (:22-80), the modifier body
// (:98-110), the coordinator (:116-199) and the extension (:88-90) are left to the implementing unit.
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; a ViewModifier is a sealed record
// with `Control Body(Control content)`; NSPopover is an Avalonia Popup; CGRect is Avalonia.Rect; a @ViewBuilder
// parameter is a Func<Control>; a `@Binding` is a Binding<T> (R-VIEW-2, R-VIEW-7); a parameter without a default
// that follows one with a default moves before it (ED-095). The generic parameters Glyph and Detail (:9) are
// erased to Control, as every port view does.

namespace Marrow.App.Views.Chat;

/// <summary>ChatBadge (:4-81).</summary>
public sealed class ChatBadge : Decorator
{
    // :10-17. `isPresented`, `glyph` and `detail` have no default and move before the defaulted ones (ED-095).
    public ChatBadge(
        string title,
        Binding<bool> isPresented,
        Func<Control> glyph,
        Func<Control> detail,
        string? caption = null,
        bool showsChevron = true,
        bool needsAttention = false,
        bool isEnabled = true) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ChatBadge.swift:22");
}

/// <summary>`extension View` (:83-91). A static class named after the Swift file would clash with the view type
/// ChatBadge, so it takes the Extensions suffix R-NAME-4 gives enum extensions.</summary>
public static class ChatBadgeExtensions
{
    // :88-90 badgePopover(isPresented:detail:)
    public static Control BadgePopover(this Control view, Binding<bool> isPresented, Func<Control> detail) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ChatBadge.swift:88");
}

/// <summary>BadgePopoverModifier (:93-111), `private struct` at file scope -> internal (R-NAME-6).</summary>
internal sealed record BadgePopoverModifier(Binding<bool> IsPresented, Func<Control> Detail) // :94-95
{
    // :98-110
    public Control Body(Control content) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ChatBadge.swift:98");
}

/// <summary>BadgePopoverCoordinator (:113-200). @MainActor final class, NSObject and NSPopoverDelegate: a plain
/// class; the delegate's popoverDidClose is a method the Popup's Closed event calls.</summary>
public sealed class BadgePopoverCoordinator
{
    // :123. A `(() -> Void)?` is an Action?.
    public Action? OnClose { get; set; }

    // :125-130
    public BadgePopoverCoordinator() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ChatBadge.swift:125");

    // :132-134. Swift: setAnchor(windowRect:).
    public void SetAnchor(Rect windowRect) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ChatBadge.swift:132");

    // :136
    public bool IsShown =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ChatBadge.swift:136");

    // :138-152. Swift: show(_:), generic over Content: View.
    public void Show(Control content) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ChatBadge.swift:138");

    // :156-160
    public void Close() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ChatBadge.swift:156");

    // :162-167. Swift: popoverDidClose(_:), nonisolated.
    public void PopoverDidClose() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/ChatBadge.swift:162");
}
