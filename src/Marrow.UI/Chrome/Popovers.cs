using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Marrow.UI.Port;

namespace Marrow.UI.Chrome;

// Source: DroppyCode/UI/Chrome/Popovers.swift (B2 1fbf81e), :1-401.
// SKELETON (cycle c35). Every member with behaviour throws MARROW_STUB until its unit ports it.
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; NSPopover is an Avalonia Popup,
// NSSize is Avalonia.Size, NSImage is Avalonia.Media.IImage, AnyView is Control, a styled Text is StyledText
// (ED-092), a @ViewBuilder parameter is a Func<Control>, a `@MainActor () -> Void` is an Action. A parameter without
// a default that follows one with a default moves before it (ED-095). The static class Popovers keeps the Swift
// file's name (R-NAME-4); the port's presenter seam is Marrow.UI.Port.Presentation (ED-095).
// Not declared yet, private in Swift and read by no other file: the @State and @Environment storage
// (PopoverScroll :59, :64; PopoverItem :208-211), which the implementing unit adds with the bodies.

/// <summary>The file's extensions of library types (R-NAME-4): <c>extension View</c> (:4-13),
/// <c>extension NSPopover</c> (:15-41) and <c>extension EnvironmentValues</c> (:176-187).</summary>
public static class Popovers
{
    // :8-12 func presentedChrome()
    public static Control PresentedChrome(this Control view) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Popovers.swift:8");

    // :28-40 @MainActor func setFixedContent(_:size:) on NSPopover
    public static void SetFixedContent(this Popup popover, Control content, Size size) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Popovers.swift:29");

    // :179 @Entry var closePopoverBox: PopoverCloseBox?, an inherited environment value (R-VIEW-6, ED-095).
    public static readonly AttachedProperty<PopoverCloseBox?> ClosePopoverBoxProperty =
        AvaloniaProperty.RegisterAttached<Control, PopoverCloseBox?>("ClosePopoverBox", typeof(Popovers), null, inherits: true);

    public static PopoverCloseBox? GetClosePopoverBox(Control control) => control.GetValue(ClosePopoverBoxProperty);

    public static void SetClosePopoverBox(Control control, PopoverCloseBox? value) => control.SetValue(ClosePopoverBoxProperty, value);

    // :183-184 var closePopover: (@MainActor () -> Void)? { get }
    public static Action? GetClosePopover(Control control) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Popovers.swift:184");

    // :185 var closePopover { set }
    public static void SetClosePopover(Control control, Action? value) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Popovers.swift:185");
}

// :43-92 struct PopoverScroll<Content: View>: View
public sealed class PopoverScroll : Decorator
{
    /// <summary>:66-73 <c>@MainActor init(maxHeight:minHeight:width:content:)</c>; content has no default, so it
    /// moves first (ED-095) and callers name the labels.</summary>
    // :82-91 body
    public PopoverScroll(Func<Control> content, double maxHeight = 460, double minHeight = 80, double? width = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Popovers.swift:67");

    // :77-80 @MainActor static func measuredHeight(of:width:)
    public static double MeasuredHeight(Control of, double width) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Popovers.swift:78");
}

// :94-115 struct PopoverMenu<Content: View>: View
public sealed class PopoverMenu : Decorator
{
    /// <param name="content">:100 <c>@ViewBuilder var content: Content</c>.</param>
    /// <param name="maxHeight">:96 <c>var maxHeight: CGFloat = 460</c>.</param>
    /// <param name="idealHeight">:99 <c>var idealHeight: CGFloat?</c>.</param>
    // :102-114 body
    public PopoverMenu(Func<Control> content, double maxHeight = 460, double? idealHeight = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Popovers.swift:102");
}

// :117-133 struct PopoverSectionHeader: View
public sealed class PopoverSectionHeader : Decorator
{
    // :120-122 init(_ title: String); :124-132 body
    public PopoverSectionHeader(string title) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Popovers.swift:120");
}

// :135-150 struct PopoverNote: View
public sealed class PopoverNote : Decorator
{
    // :138-140 init(_ text: String); :142-149 body
    public PopoverNote(string text) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Popovers.swift:138");
}

// :152-158 struct PopoverDivider: View
public sealed class PopoverDivider : Decorator
{
    // :153-157 body
    public PopoverDivider() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Popovers.swift:153");
}

/// <summary>:160-174 <c>final class PopoverCloseBox: Equatable</c>. Swift's <c>==</c> is identity (<c>===</c>,
/// :171-173), which is a C# class's default equality.</summary>
public sealed class PopoverCloseBox
{
    // :167-169 init(_ close:)
    public PopoverCloseBox(Action close)
    {
        Close = close;
    }

    // :165 let close: @MainActor () -> Void
    public Action Close { get; }
}

// :189-337 struct PopoverItem: View
public sealed class PopoverItem : Decorator
{
    // :214 private static let confirmationHold: Duration = .milliseconds(650)
    private static TimeSpan ConfirmationHold => TimeSpan.FromMilliseconds(650);

    /// <summary>:216-240 <c>init(_:detail:symbol:image:asset:leading:isChecked:isEnabled:isDestructive:confirmation:action:)</c>;
    /// action has no default, so it moves after the title (ED-095) and callers name the labels.</summary>
    // :257-336 body
    public PopoverItem(
        string title,
        Action action,
        string? detail = null,
        string? symbol = null,
        IImage? image = null,
        string? asset = null,
        Control? leading = null,
        bool? isChecked = null,
        bool isEnabled = true,
        bool isDestructive = false,
        string? confirmation = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Popovers.swift:216");

    /// <summary>:242-255 <c>init(_:text:symbol:image:action:)</c>, a row whose title is styled text (<c>titleText</c>,
    /// :193); action has no default, so it moves before the defaulted ones (ED-095).</summary>
    public PopoverItem(string title, StyledText text, Action action, string? symbol = null, IImage? image = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Popovers.swift:243");
}

// MARK: - Row actions

/// <summary>:341-352 <c>struct RowAction: Identifiable</c> (R-VAL-1). The memberwise init's labels are the
/// property names.</summary>
public sealed record RowAction
{
    public required string Title { get; init; } // :343
    public string? Symbol { get; init; } // :344
    public bool IsDestructive { get; init; } = false; // :345
    public bool StartsGroup { get; init; } = false; // :346
    public string? Confirms { get; init; } // :348
    public required Action Action { get; init; } // :349

    // :351 var id: String
    public string Id => throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Popovers.swift:351");
}

// :354-363 struct RowActionItems: View
public sealed class RowActionItems : Decorator
{
    /// <param name="actions">:355 <c>let actions: [RowAction]</c>.</param>
    // :357-362 body
    public RowActionItems(IReadOnlyList<RowAction> actions) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Popovers.swift:357");
}

// :365-382 struct RowActionMenuButtons: View
public sealed class RowActionMenuButtons : Decorator
{
    /// <param name="actions">:366 <c>let actions: [RowAction]</c>.</param>
    // :368-381 body
    public RowActionMenuButtons(IReadOnlyList<RowAction> actions) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Popovers.swift:368");
}

// :384-401 struct RowActionsButton: View
public sealed class RowActionsButton : Decorator
{
    /// <param name="actions">:386 <c>let actions: [RowAction]</c>.</param>
    /// <param name="isPresented">:387 <c>@Binding var isPresented: Bool</c>.</param>
    // :389-400 body
    public RowActionsButton(IReadOnlyList<RowAction> actions, Binding<bool> isPresented) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Chrome/Popovers.swift:389");
}
