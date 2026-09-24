using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;

// Source: DroppyCode/UI/Markdown/LinkParagraphView.swift (B2 1fbf81e), :1-550.
// Skeleton (c100 group 5): every type the file declares, the stored inputs and the members the other cycle files use.
// Every member with behaviour is left to the implementing unit (MARROW_STUB).
// Forms (ED-095): an NSViewRepresentable is a Decorator whose constructor makes the hosted view (makeNSView :26-34);
// NSTextView is LinkTextView, a Decorator (a Control) the implementing unit fills with the selectable text; URL is
// SwiftURL; CGPoint/NSRect are Avalonia Point/Rect; NSRange is a (int Location, int Length) tuple; NSColor is
// SwiftColor and NSImage IImage. The `extension EnvironmentValues` (:266-271) and `extension NSAttributedString.Key`
// (:247-249) cannot be a static class named after the file (R-NAME-4): that name is the view's, so they live on
// LinkParagraphView; MergeRequestTargetKey (:262-264) holds the attached property's default.
// Not declared yet: updateNSView (:36-60), sizeThatFits (:62-65) and dismantleNSView (:73-77), the representable's
// SwiftUI plumbing, which the implementing unit maps onto the control; the private font and dragon caches
// (:108, :158) and LinkTextView's private measurement state (:276-277, :475-477) and its AppKit overrides
// (updateTrackingAreas :346-348, resetCursorRects :350, hitTest :436-442, mouseDown :444-451, rightMouseDown
// :453-456, menu(for:) :459-461, intrinsicContentSize :509-513, layout :515-537, viewDidEndLiveResize :539-549),
// which become Avalonia overrides and handlers with the bodies.
// NEEDS DECISION (group 5 report): Foundation's NSAttributedString / NSMutableAttributedString have no port type, so
// attributed(source:pointSize:dimmed:streaming:mentions:targets:) (:110-156), appendProse (:179-244) and
// LinkTextView.render(_:) (:481-496) are not declared yet.

namespace Marrow.UI.Markdown;

/// <summary>:9-245 <c>struct LinkParagraphView: NSViewRepresentable</c>: a selectable paragraph with real links.</summary>
public sealed class LinkParagraphView : Decorator
{
    // :248 static let hydraHead = NSAttributedString.Key("droppycode.hydraHead")
    public const string HydraHead = "droppycode.hydraHead";

    /// <param name="source">:10 <c>let source: String</c>.</param>
    /// <param name="pointSize">:11 <c>var pointSize: CGFloat = 13</c>.</param>
    /// <param name="dimmed">:12 <c>var dimmed: Bool = false</c>.</param>
    /// <param name="streaming">:14 <c>var streaming: Bool = false</c>.</param>
    /// <param name="revision">:16 <c>var revision: Int = 0</c>.</param>
    /// <param name="mentions">:17 <c>var mentions: [HydraPersona] = []</c>.</param>
    /// <param name="targets">:18 <c>var targets: [String: HydraMentionTarget] = [:]</c>.</param>
    /// <param name="onOpenHead">:19 <c>var onOpenHead: ((URL) -> Void)?</c>.</param>
    /// <param name="onHost">:24 <c>var onHost: ((LinkTextView) -> Void)?</c>.</param>
    // :26-34 makeNSView(context:)
    public LinkParagraphView(
        string source,
        double pointSize = 13,
        bool dimmed = false,
        bool streaming = false,
        int revision = 0,
        IReadOnlyList<HydraPersona>? mentions = null,
        IReadOnlyDictionary<string, HydraMentionTarget>? targets = null,
        Action<SwiftURL>? onOpenHead = null,
        Action<LinkTextView>? onHost = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/LinkParagraphView.swift:26");

    // :67-69 func makeCoordinator() -> Coordinator
    public Coordinator MakeCoordinator() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/LinkParagraphView.swift:67");

    /// <summary>:79-101 <c>final class Coordinator: NSObject, NSTextViewDelegate</c>: the last inputs built into a
    /// string, so repeat updates with nothing new skip it. Shared with ProseRunRepresentable.</summary>
    public sealed class Coordinator
    {
        public string? LastSource { get; set; } // :81
        public double LastPointSize { get; set; } // :82
        public bool LastDimmed { get; set; } // :83
        public bool LastStreaming { get; set; } // :84
        public int LastRevision { get; set; } // :85
        public bool LastVeiled { get; set; } // :86
        public ValueList<string> LastMentions { get; set; } = ValueList<string>.Empty; // :87
        public ValueMap<string, HydraMentionTarget> LastTargets { get; set; } = ValueMap<string, HydraMentionTarget>.Empty; // :88
        public ValueMap<string, SwiftURL> LastHeads { get; set; } = ValueMap<string, SwiftURL>.Empty; // :89

        // :90-100 func textView(_ textView: NSTextView, clickedOnLink link: Any, at charIndex: Int) -> Bool
        public bool TextView(LinkTextView textView, object clickedOnLink, int at) =>
            throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/LinkParagraphView.swift:90");
    }

    // :160-162 private static func nsColor(_ persona: HydraPersona) -> NSColor
    private static SwiftColor NsColor(HydraPersona persona) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/LinkParagraphView.swift:160");

    // :164-177 @MainActor private static func dragonImage(_ persona: HydraPersona, size: CGFloat) -> NSImage
    private static IImage DragonImage(HydraPersona persona, double size) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/LinkParagraphView.swift:164");

    // :266-271 extension EnvironmentValues { var mergeRequestTarget: MergeRequestTarget? }, an inherited environment
    // value (R-VIEW-6, ED-095).
    public static readonly AttachedProperty<MergeRequestTarget?> MergeRequestTargetProperty =
        AvaloniaProperty.RegisterAttached<Control, MergeRequestTarget?>(
            "MergeRequestTarget", typeof(LinkParagraphView), MergeRequestTargetKey.DefaultValue, inherits: true);

    // :268 get
    public static MergeRequestTarget? GetMergeRequestTarget(Control control) => control.GetValue(MergeRequestTargetProperty);

    // :269 set
    public static void SetMergeRequestTarget(Control control, MergeRequestTarget? value) =>
        control.SetValue(MergeRequestTargetProperty, value);
}

/// <summary>:253-260 <c>struct MergeRequestTarget: Equatable</c>: what a merge or pull request link in a chat hands
/// its request to. Compared by the chat alone (:257-259), a custom <c>==</c> ported as Equals (R-VAL-4).</summary>
/// <param name="ChatID">:254 <c>let chatID: UUID</c>.</param>
/// <param name="Merge">:255 <c>let merge: @MainActor (MergeRequestLink) -> Void</c>.</param>
public sealed record MergeRequestTarget(Guid ChatID, Action<MergeRequestLink> Merge)
{
    // :257-259 static func == (lhs: Self, rhs: Self) -> Bool
    public bool Equals(MergeRequestTarget? other) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/LinkParagraphView.swift:257");

    // :257-259, the hash that matches Equals (R-VAL-4)
    public override int GetHashCode() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/LinkParagraphView.swift:257");
}

/// <summary>:262-264 <c>private struct MergeRequestTargetKey: EnvironmentKey</c>: the default of
/// LinkParagraphView.MergeRequestTargetProperty.</summary>
file static class MergeRequestTargetKey
{
    public static MergeRequestTarget? DefaultValue => null; // :263
}

/// <summary>:275-550 <c>final class LinkTextView: NSTextView</c>: a non-editable selectable text view that sizes
/// itself to its content width.</summary>
public sealed class LinkTextView : Decorator
{
    // :281 var mergeTarget: MergeRequestTarget?
    public MergeRequestTarget? MergeTarget { get; set; }

    // :285 var passesProseThrough = false
    public bool PassesProseThrough { get; set; }

    // :287 var onOpenHead: ((URL) -> Void)?
    public Action<SwiftURL>? OnOpenHead { get; set; }

    // :290 let streamVeil = LinkTextVeil()
    public LinkTextVeil StreamVeil { get; } = new();

    // :299-332 init()
    public LinkTextView() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/LinkParagraphView.swift:299");

    // :355-357 func hasLink(at point: CGPoint) -> Bool
    public bool HasLink(Point at) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/LinkParagraphView.swift:355");

    // :360-362 func link(at point: CGPoint) -> URL?
    public SwiftURL? Link(Point at) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/LinkParagraphView.swift:360");

    // :365-378 func headMention(at point: CGPoint) -> (url: URL, range: NSRange)?
    public (SwiftURL Url, (int Location, int Length) Range)? HeadMention(Point at) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/LinkParagraphView.swift:365");

    // :381-412 func headMentionRect(at point: CGPoint) -> (name: String, url: URL, rect: NSRect)?
    public (string Name, SwiftURL Url, Rect Rect)? HeadMentionRect(Point at) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/LinkParagraphView.swift:381");

    // :415-428 private func linkAndRange(at point: CGPoint) -> (url: URL, range: NSRange)?
    private (SwiftURL Url, (int Location, int Length) Range)? LinkAndRange(Point at) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/LinkParagraphView.swift:415");

    // :463-471 private func presentLinkPopover(for event: NSEvent) -> Bool; the event's location in this view.
    private bool PresentLinkPopover(Point @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/LinkParagraphView.swift:463");

    // :498-507 func height(forWidth width: CGFloat) -> CGFloat. Swift: height(forWidth:); `Height` is Layoutable's
    // property, so the label joins the name.
    public double HeightForWidth(double width) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/LinkParagraphView.swift:498");
}
