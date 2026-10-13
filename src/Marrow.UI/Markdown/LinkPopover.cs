using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.UI.Markdown;

// Source: DroppyCode/UI/Markdown/LinkPopover.swift (B3 52bc792), :1-108.
// Forms (ED-095): NSPopover is an Avalonia Popup and NSView is a Control; NSRect is an Avalonia Rect; URL is SwiftURL; a
// SwiftUI View is a Decorator whose constructor builds `body`; `static let shared` is `Shared`. Layering: this file is
// in Marrow.UI and never references Marrow.App; MergeRequestTarget is LinkParagraphView.swift's (UI.Markdown).
// ENGINEERING DECISION (windows_path, precedent AttachmentPreviewPanel.cs:160-171): the app-wide NSEvent local
// key-down monitor becomes an Avalonia Window-scoped KeyDown handler (Tunnel routing, handledEventsToo), added
// while the popover is shown and removed on close.

/// <summary>:8-66 <c>@MainActor final class LinkPopover: NSObject, NSPopoverDelegate</c>: the popover a link in the chat
/// opens on a right-click, in place of the system's text menu.</summary>
public sealed class LinkPopover
{
    // :10 static let shared = LinkPopover()
    public static LinkPopover Shared { get; } = new();

    private readonly Popup popover; // :12 private let popover = NSPopover()
    private readonly PopoverCloseBox closeBox; // :16 private lazy var closeBox = PopoverCloseBox { [weak self] in self?.close() }
    private Window? monitorWindow; // the window the escape monitor is attached to
    private Control? anchor; // the view it is shown relative to, while shown (ED-253)
    private bool escapeMonitorActive; // :13 private var escapeMonitor: Any?
    // ENGINEERING DECISION (precedent AttachmentPreviewPanel.cs :44, "isShown"): tracks :28/:45's `popover.isShown`
    // ourselves rather than reading Avalonia's `Popup.IsOpen`, which only the real presenter (not the test seam's
    // recorder, R-TEST-7) toggles.
    private bool isShown;

    // :18-23 private override init()
    private LinkPopover()
    {
        popover = new Popup
        {
            IsLightDismissEnabled = true, // :20-21 behavior = .transient, animates = true
        };
        // :22 popover.delegate = self, so popoverDidClose(_:) always runs on AppKit's own dismissal of a
        // .transient popover (an outside click), not only on our own performClose(nil). IsLightDismissEnabled
        // above means Avalonia can close this Popup itself the same way, so this resync is required here,
        // not merely defensive as it is at AttachmentPreviewPanel.cs :56-65 (whose popup never light-dismisses).
        popover.Closed += (_, _) => PopoverDidClose();
        closeBox = new PopoverCloseBox(Close); // :16
    }

    /// <summary>:35 the hosted view's intrinsic size. A seam so a test can make the measure come back empty, the case :36 answers.</summary>
    internal static Func<Control, Size> MeasureContent { get; set; } = content => StyledMeasure.Of(content, new Size(double.PositiveInfinity, double.PositiveInfinity));

    /// <summary>Test-only readback (InternalsVisibleTo, matching AttachmentPreviewCoordinator.cs's PopupForTests
    /// pattern): the Popup this class drives, so a test can inspect or directly close it.</summary>
    internal Popup PopoverForTests => popover;

    /// <summary>
    /// :27-41 func show(url:mergeTarget:relativeTo:of:). Opens under <paramref name="relativeTo"/> (in
    /// <paramref name="of"/>'s coordinates), the link's own bounds, so the arrow points at the words that were
    /// clicked.
    /// </summary>
    public void Show(SwiftURL url, MergeRequestTarget? mergeTarget, Rect relativeTo, Control of)
    {
        if (isShown) Close(); // :28
        var request = mergeTarget is null ? null : MergeRequestLink.Create(url); // :29
        var content = new LinkPopoverContent(url, request, r =>
        {
            Close(); // :31
            mergeTarget?.Merge(r); // :32
        }, of);
        Popovers.SetClosePopoverBox(content, closeBox); // :34 .environment(\.closePopoverBox, closeBox)
        // :35 NSHostingView(rootView: content).intrinsicContentSize: the size of a hosted view, so measured in a window, where
        // the rows' button templates are in effect (detached, the rows measured 12 pt tall instead of 64).
        var size = MeasureContent(content);
        if (size.Width <= 0 || size.Height <= 0) size = new Size(220, 110); // :36
        popover.SetFixedContent(content, size); // :37
        // :39 preferredEdge: .minY, "below the words in the flipped text view" (the source's own comment):
        // the popover is shown below the anchor rect.
        popover.PlacementTarget = of;
        popover.Placement = PlacementMode.AnchorAndGravity;
        popover.PlacementAnchor = PopupAnchor.Bottom;
        popover.PlacementGravity = PopupGravity.Bottom;
        popover.PlacementRect = relativeTo;
        // ED-253: an NSPopover is a window of its own whose Escape runs before the window's (a Settings window's own Escape
        // closes the window): it is presented as open over its anchor's window, so that window's Escape closes it first.
        anchor = of;
        Presentation.SetPresented(popover, true, of, Close);
        isShown = true;
        StartEscapeMonitor(of); // :40
    }

    // :43-46 func close()
    public void Close()
    {
        StopEscapeMonitor(); // :44
        if (!isShown) return;
        isShown = false;
        Presentation.SetPresented(popover, false, anchor ?? (Visual)popover, Close); // :45
        anchor = null;
    }

    // :48-50 func popoverDidClose(_:)
    public void PopoverDidClose()
    {
        isShown = false; // resync the shadow flag (:154-style guard) for a close this class did not itself drive
        WindowTransients.Hide(popover); // ED-253: no longer open over the window
        anchor = null;
        StopEscapeMonitor(); // :49
    }

    // :53-60 startEscapeMonitor(). Escape (key code 53) closes and swallows the key; the rows take no focus,
    // so the popover never sees the key itself.
    private void StartEscapeMonitor(Control of)
    {
        if (escapeMonitorActive) return; // :54
        var window = TopLevel.GetTopLevel(of) as Window;
        if (window is null) return;
        monitorWindow = window;
        window.AddHandler(InputElement.KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel); // a key PopoverHost already forwarded is handled and acts once (PopoverHost.Listen)
        escapeMonitorActive = true;
    }

    // :62-65 stopEscapeMonitor()
    private void StopEscapeMonitor()
    {
        if (!escapeMonitorActive) return;
        monitorWindow?.RemoveHandler(InputElement.KeyDownEvent, OnWindowKeyDown);
        monitorWindow = null;
        escapeMonitorActive = false;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return; // :56 event.keyCode == 53
        Close(); // :57
        e.Handled = true; // :58 return nil: the key does not reach anything else
    }
}

/// <summary>:68-108 <c>private struct LinkPopoverContent: View</c>: Merge, Open Link and Copy Link.</summary>
internal sealed class LinkPopoverContent : Decorator
{
    // :73 @State private var favicon: NSImage?
    private sealed class ViewState : MainActorModel
    {
        private IImage? favicon;

        public IImage? Favicon { get => Read(favicon); set => Set(ref favicon, value); }
    }

    private readonly ViewState state = new();

    // The view the popover opened from: the real presenter hosts this content in a popup's own root, so the pasteboard is taken from
    // the window the link is in (the port's other menus do the same, TimelineRows.CopyMessageText).
    private readonly Control? anchor;

    // :69-71 let url, let request, let merge; :75-107 body
    public LinkPopoverContent(SwiftURL url, MergeRequestLink? request, Action<MergeRequestLink> merge, Control? anchor = null)
    {
        this.anchor = anchor;
        Child = Build(url, request, merge, state.Favicon);
        this.Track(() => state.Favicon, favicon => Child = Build(url, request, merge, favicon)); // :73, :81 re-render on favicon

        // :98-106 the favicon task: runs once, since Swift's plain `.task` uses a constant id.
        this.Task(() => 0, async _ =>
        {
            if (request is null) return; // :100 guard request != nil
            if (url.Host is not { } host) return; // :100 let host = url.host?.lowercased()
            var lowercasedHost = SwiftString.Lowercased(host);
            if (FaviconCache.Cached(lowercasedHost) is { } cached) // :101-102
            {
                state.Favicon = cached;
                return;
            }
            state.Favicon = await FaviconCache.Image(lowercasedHost); // :103-104
        });
    }

    private Control Build(SwiftURL url, MergeRequestLink? request, Action<MergeRequestLink> merge, IImage? favicon)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 }; // :76 VStack(alignment: .leading, spacing: 0)

        if (request is not null) // :77-87
        {
            var mergeText = new StyledText("Merge").FontWeight(SwiftFont.Weight.Semibold) + new StyledText(" " + request.Label); // :80
            stack.Children.Add(new PopoverItem(
                request.Title,
                mergeText,
                () => merge(request),
                symbol: favicon is null ? "arrow.triangle.merge" : null, // :81
                image: favicon));
            stack.Children.Add(new PopoverDivider()); // :86
        }

        stack.Children.Add(new PopoverItem("Open Link", () => OpenLink(url), symbol: "safari")); // :88-90
        // PORT-ONLY (web surfaces W10): the chat's own rows for this link ("Open in Visus" for an HTML file of the project).
        if (anchor is not null && MarkdownView.GetChatLinkMenu(anchor) is { } more)
            foreach (var item in more(url)) stack.Children.Add(new PopoverItem(item.Title, item.Run, symbol: item.Symbol));
        stack.Children.Add(new PopoverItem("Copy Link", () => CopyLink(url), symbol: "link")); // :91-94

        var border = new Border
        {
            Child = stack,
            Padding = new Thickness(6), // :96 .padding(6)
            Width = 220, // :97 .frame(width: 220, alignment: .leading)
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        return border;
    }

    // :88-90 MarkdownLinkOpener.open(url). PORT-ONLY (web surfaces W10): in a chat the link goes through the chat's link hook, so it follows the
    // reader's "Open links from chats in" choice like a click does; anywhere else, or when the hook declines, the default.
    private void OpenLink(SwiftURL url)
    {
        if (anchor is not null && MarkdownView.GetChatLinkAction(anchor) is { } chat && chat(url, Avalonia.Input.KeyModifiers.None)) return;
        MarkdownLinkOpener.Open(url);
    }

    // :92-93 NSPasteboard.general.clearContents(); NSPasteboard.general.setString(url.absoluteString, forType: .string).
    // NSPasteboard has no Avalonia port type; TopLevel.Clipboard is the platform's own equivalent (precedent:
    // MarkdownView.cs's CopyButton, ComposerTextView.cs).
    private async void CopyLink(SwiftURL url)
    {
        var clipboard = (anchor is null ? null : TopLevel.GetTopLevel(anchor)?.Clipboard) ?? TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        try
        {
            await clipboard.ClearAsync(); // :92 clearContents()
            await clipboard.SetTextAsync(url.AbsoluteString); // :93 setString(url.absoluteString, forType: .string)
        }
        catch (Exception)
        {
            // a clipboard another process holds open: nothing to copy to, as a pasteboard write that fails is silent
        }
    }
}
