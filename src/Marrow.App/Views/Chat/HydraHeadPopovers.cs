using Avalonia.Controls;
using Marrow.Core.Models;
using Marrow.UI.Port;

// Source: DroppyCode/App/Views/Chat/HydraHeadPopovers.swift (B2 1fbf81e), :1-173. Skeleton (c100 group 1): the
// three views and their inputs are declared; every body (:14-47, :64-103, :149-172), the initializers that parse
// the markdown (:57-62, :141-147) and the static helpers (:105-129) are left to the implementing unit.
// Forms (ED-095): a SwiftUI View is a Decorator whose constructor builds `body`; AnyView is Control; Color is
// SwiftColor; Date is a UTC DateTimeOffset.

namespace Marrow.App.Views.Chat;

/// <summary>HydraPopoverHeader (:3-48).</summary>
public sealed class HydraPopoverHeader : Decorator
{
    // :5-12. `captionColor` defaults to Chrome.secondaryText (:8), not a compile-time constant: null here stands for
    // that default.
    public HydraPopoverHeader(
        HydraPersona persona,
        HydraHeadInfo.Status? status = null,
        string? caption = null,
        SwiftColor? captionColor = null,
        string? title = null,
        Control? accessory = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraHeadPopovers.swift:14");
}

/// <summary>HydraHeadReportPopover (:50-130).</summary>
public sealed class HydraHeadReportPopover : Decorator
{
    // :55. Swift: width; prefixed with the owner's name because Layoutable.Width is a member of every control.
    private const double HydraHeadReportPopoverWidth = 460;

    public HydraHeadInfo Head { get; } // :52

    // :57-62 and the body :64-103
    public HydraHeadReportPopover(HydraHeadInfo head) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraHeadPopovers.swift:58");

    // :105-114. Swift: caption(for:).
    public static string Caption(HydraHeadInfo head) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraHeadPopovers.swift:105");

    // :116-122. Swift: captionColor(for:).
    public static SwiftColor CaptionColor(HydraHeadInfo.Status status) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraHeadPopovers.swift:116");

    // :124-129. Swift: duration(from:to:).
    public static string Duration(DateTimeOffset from, DateTimeOffset to) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraHeadPopovers.swift:124");
}

/// <summary>HydraBriefPopover (:132-173).</summary>
public sealed class HydraBriefPopover : Decorator
{
    // :139. Swift: width; prefixed with the owner's name because Layoutable.Width is a member of every control.
    private const double HydraBriefPopoverWidth = 520;

    public HydraPersona Persona { get; } // :134
    public string? Task { get; }         // :135
    public string Text { get; }          // :136

    // :141-147 and the body :149-172
    public HydraBriefPopover(HydraPersona persona, string? task, string text) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/App/Views/Chat/HydraHeadPopovers.swift:142");
}
