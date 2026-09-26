using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;

// Source: DroppyCode/UI/Markdown/ProseRunView.swift (B3 52bc792), :1-209.
// Skeleton (c100 group 5): both types the file declares, their inputs and the members the other cycle files use.
// Every member with behaviour is left to the implementing unit (MARROW_STUB).

namespace Marrow.UI.Markdown;

/// <summary>:4-31 <c>struct ProseRunView: View</c>: a run of prose blocks as one selectable link text view.</summary>
public sealed class ProseRunView : Decorator
{
    // :16-30 body
    public ProseRunView(IReadOnlyList<MarkdownBlock> blocks) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/ProseRunView.swift:16");
}

/// <summary>:33-209 <c>struct ProseRunRepresentable: NSViewRepresentable</c>.</summary>
public sealed class ProseRunRepresentable : Decorator
{
    // :47-54 makeNSView(context:)
    public ProseRunRepresentable(
        IReadOnlyList<MarkdownBlock> blocks,
        double pointSize = 13,
        bool dimmed = false,
        int revision = 0,
        IReadOnlyList<HydraPersona>? mentions = null,
        IReadOnlyDictionary<string, HydraMentionTarget>? targets = null,
        Action<SwiftURL>? onOpenHead = null,
        Action<LinkTextView>? onHost = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/ProseRunView.swift:47");

    public double PointSize => throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/ProseRunView.swift:35");

    public LinkParagraphView.Coordinator Coordinator =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/ProseRunView.swift:43");

    public void Update(
        IReadOnlyList<MarkdownBlock> blocks,
        double pointSize = 13,
        bool dimmed = false,
        int revision = 0,
        IReadOnlyList<HydraPersona>? mentions = null,
        IReadOnlyDictionary<string, HydraMentionTarget>? targets = null,
        Action<SwiftURL>? onOpenHead = null,
        Action<LinkTextView>? onHost = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/ProseRunView.swift:56");

    // :84-88
    public static void DismantleNSView(LinkTextView view, LinkParagraphView.Coordinator coordinator) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/ProseRunView.swift:84");

    // :90-94 static func joinedSources(_ blocks: [MarkdownBlock]) -> String
    public static string JoinedSources(IReadOnlyList<MarkdownBlock> blocks) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/ProseRunView.swift:90");

    // :114-121
    public static ParagraphText Attributed(
        IReadOnlyList<MarkdownBlock> blocks,
        double pointSize,
        bool dimmed,
        IReadOnlyList<HydraPersona> mentions,
        IReadOnlyDictionary<string, HydraMentionTarget>? targets = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/UI/Markdown/ProseRunView.swift:114");
}
