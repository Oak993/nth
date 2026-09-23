using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Marrow.Core.Models;
using Marrow.Core.Port;

// Source: DroppyCode/UI/Markdown/ProseRunView.swift (B2 1fbf81e), :1-244.
// Skeleton (c100 group 5): both types the file declares, their inputs and the members the other cycle files use.
// Every member with behaviour is left to the implementing unit (MARROW_STUB).
// Forms (ED-095): a SwiftUI View and an NSViewRepresentable are Decorators whose constructor builds `body` or makes
// the hosted view; URL is SwiftURL; an `inout [String]` parameter of a helper returns the new list (R-VAL-1).
// Not declared yet: ProseRunView's @State/@Environment storage (:6-14), read by its body, and the representable's
// updateNSView (:91-112), sizeThatFits (:114-117) and dismantleNSView (:119-123) SwiftUI plumbing, which the
// implementing unit maps onto the control.
// NEEDS DECISION (group 5 report): Foundation's NSAttributedString / NSMutableAttributedString have no port type, so
// attributed(blocks:pointSize:dimmed:mentions:targets:) (:149-156), append(_:depth:indent:...) (:158-234) and
// separate(to:font:color:) (:236-243) are not declared yet.

namespace Marrow.UI.Markdown;

/// <summary>:4-66 <c>struct ProseRunView: View</c>: a run of prose blocks as one selectable link text view.</summary>
public sealed class ProseRunView : Decorator
{
    /// <param name="blocks">:5 <c>let blocks: [MarkdownBlock]</c>.</param>
    // :16-65 body
    public ProseRunView(IReadOnlyList<MarkdownBlock> blocks) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/ProseRunView.swift:16");
}

/// <summary>:68-244 <c>struct ProseRunRepresentable: NSViewRepresentable</c>.</summary>
public sealed class ProseRunRepresentable : Decorator
{
    /// <param name="blocks">:69 <c>let blocks: [MarkdownBlock]</c>.</param>
    /// <param name="pointSize">:70 <c>var pointSize: CGFloat = 13</c>.</param>
    /// <param name="dimmed">:71 <c>var dimmed: Bool = false</c>.</param>
    /// <param name="revision">:72 <c>var revision: Int = 0</c>.</param>
    /// <param name="mentions">:73 <c>var mentions: [HydraPersona] = []</c>.</param>
    /// <param name="targets">:74 <c>var targets: [String: HydraMentionTarget] = [:]</c>.</param>
    /// <param name="onOpenHead">:75 <c>var onOpenHead: ((URL) -> Void)?</c>.</param>
    /// <param name="onHost">:76 <c>var onHost: ((LinkTextView) -> Void)?</c>.</param>
    // :82-89 makeNSView(context:)
    public ProseRunRepresentable(
        IReadOnlyList<MarkdownBlock> blocks,
        double pointSize = 13,
        bool dimmed = false,
        int revision = 0,
        IReadOnlyList<HydraPersona>? mentions = null,
        IReadOnlyDictionary<string, HydraMentionTarget>? targets = null,
        Action<SwiftURL>? onOpenHead = null,
        Action<LinkTextView>? onHost = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/ProseRunView.swift:82");

    // :78-80 func makeCoordinator() -> LinkParagraphView.Coordinator
    public LinkParagraphView.Coordinator MakeCoordinator() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/ProseRunView.swift:78");

    // :125-129 static func joinedSources(_ blocks: [MarkdownBlock]) -> String
    public static string JoinedSources(IReadOnlyList<MarkdownBlock> blocks) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/ProseRunView.swift:125");

    // :131-147 private static func collect(_ blocks: [MarkdownBlock], into parts: inout [String])
    private static ValueList<string> Collect(IReadOnlyList<MarkdownBlock> blocks, ValueList<string> into) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/ProseRunView.swift:131");
}
