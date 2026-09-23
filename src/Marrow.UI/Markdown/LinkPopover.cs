using System;
using Avalonia;
using Avalonia.Controls;
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.UI.Markdown;

// Source: DroppyCode/UI/Markdown/LinkPopover.swift (B2 1fbf81e), :1-108.
// Skeleton (c100 group 4): the popover and its content view are declared; the popover's init (:18-23), show (:27-41),
// close (:43-46), popoverDidClose (:48-50), the escape monitor (:53-65) and the content's body (:75-107) are left to
// the implementing unit (MARROW_STUB).
// Forms (ED-095): NSPopover is an Avalonia Popup and NSView is a Control; NSRect is an Avalonia Rect; URL is SwiftURL; a
// SwiftUI View is a Decorator whose constructor builds `body`; `static let shared` is `Shared`. Layering: this file is
// in Marrow.UI and never references Marrow.App; MergeRequestTarget is LinkParagraphView.swift's (UI.Markdown).

/// <summary>:8-66 <c>@MainActor final class LinkPopover: NSObject, NSPopoverDelegate</c>: the popover a link in the chat
/// opens on a right-click.</summary>
public sealed class LinkPopover
{
    // :10 static let shared = LinkPopover()
    public static LinkPopover Shared { get; } = new();

    // :18-23 private override init()
    private LinkPopover() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/LinkPopover.swift:18");

    // :27-41 func show(url:mergeTarget:relativeTo:of:)
    public void Show(SwiftURL url, MergeRequestTarget? mergeTarget, Rect relativeTo, Control of) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/LinkPopover.swift:27");

    // :43-46 func close()
    public void Close() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/LinkPopover.swift:43");

    // :48-50 func popoverDidClose(_:)
    public void PopoverDidClose() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/LinkPopover.swift:48");
}

/// <summary>:68-108 <c>private struct LinkPopoverContent: View</c>: Merge, Open Link and Copy Link.</summary>
internal sealed class LinkPopoverContent : Decorator
{
    // :69-71 let url, let request, let merge; :75-107 body
    public LinkPopoverContent(SwiftURL url, MergeRequestLink? request, Action<MergeRequestLink> merge) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/LinkPopover.swift:75");
}
