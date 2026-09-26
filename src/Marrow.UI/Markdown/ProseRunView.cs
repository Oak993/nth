using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;

// Source: DroppyCode/UI/Markdown/ProseRunView.swift (B3 52bc792), :1-209.
// Forms (ED-095): the SwiftUI View and the NSViewRepresentable are Decorators; URL is SwiftURL. The one
// NSAttributedString the run builds is a ParagraphText drawn by LinkTextView.Render (ED-210). SwiftUI's
// makeNSView/updateNSView/dismantleNSView keep their names and run where SwiftUI would call them: make and a
// first update on construction, an update whenever the view's inputs or the environment values they read change,
// dismantle when the representable leaves the visual tree (a return makes a new text view, as SwiftUI would).

namespace Marrow.UI.Markdown;

/// <summary>:4-31 <c>struct ProseRunView: View</c>: a run of prose blocks as one selectable link text view.</summary>
public sealed class ProseRunView : Decorator
{
    // :14 @State private var faviconRevision = 0, read by body (R-VIEW-2).
    private sealed class ViewState : MainActorModel
    {
        private int faviconRevision;

        public int FaviconRevision { get => Read(faviconRevision); set => Set(ref faviconRevision, value); }
    }

    private readonly ViewState state = new();
    private readonly IReadOnlyList<MarkdownBlock> blocks; // :5
    private readonly ProseRunRepresentable representable;
    private readonly LinkHover.HoverState hover = new(); // :12-13 showsHand, hoveredHeadID
    private readonly Action<SwiftURL> openHead;
    private readonly Action<LinkTextView> host;
    private LinkTextView? linkView; // :11 @State private var linkView = WeakView()

    /// <param name="blocks">:5 <c>let blocks: [MarkdownBlock]</c>.</param>
    // :16-30 body
    public ProseRunView(IReadOnlyList<MarkdownBlock> blocks)
    {
        this.blocks = blocks;
        var sources = ProseRunRepresentable.JoinedSources(blocks); // :18
        var linkKey = RichLink.LinkAssetKey(sources); // :19
        openHead = url => InlineText.OpenUrl?.Invoke(url); // :22 { url in openURL(url) } (the App's openURL hook)
        host = view => linkView = view; // :22 { [weak linkBox] view in linkBox?.value = view }
        representable = new ProseRunRepresentable( // :22
            blocks,
            pointSize: Scaled(),
            dimmed: MarkdownView.GetMarkdownDimmed(this),
            revision: state.FaviconRevision,
            mentions: MarkdownView.GetHydraMentionPersonas(this),
            targets: MarkdownView.GetHydraMentionTargets(this),
            onOpenHead: openHead,
            onHost: host)
        {
            HorizontalAlignment = HorizontalAlignment.Stretch, // :23 .frame(maxWidth: .infinity, alignment: .leading)
        };

        // :24 .modifier(LinkHoverModifier(linkView:showsHand:hoveredHeadID:personas:targets:)) (LinkHover.swift:14-44),
        // with the personas and targets the environment holds when the pointer moves.
        representable.PointerMoved += (_, e) => LinkHover.Moved(representable, linkView, e.GetPosition(representable),
            MarkdownView.GetHydraMentionPersonas(this), MarkdownView.GetHydraMentionTargets(this), hover);
        representable.PointerExited += (_, _) => LinkHover.End(representable, hover); // LinkHover.swift:37-39
        representable.DetachedFromVisualTree += (_, _) => LinkHover.End(representable, hover); // LinkHover.swift:41-43

        // :25-29 .task(id: linkKey): favicons and video titles for the run's links, then a revision bump.
        this.Task(() => linkKey, async _ =>
        {
            if (linkKey.Length > 0) // :26
            {
                await InlineText.FetchLinkAssets(sources, streaming: false, // :27
                    revision: new Binding<int>(() => state.FaviconRevision, value => state.FaviconRevision = value));
            }
        });

        Child = representable;

        // body reads these environment values (:6-9) and faviconRevision (:22): a change re-evaluates it, and SwiftUI
        // hands the representable its new inputs (updateNSView).
        PropertyChanged += (_, e) =>
        {
            if (e.Property == MarkdownView.MarkdownPointSizeProperty || e.Property == MarkdownView.MarkdownDimmedProperty
                || e.Property == MarkdownView.HydraMentionPersonasProperty || e.Property == MarkdownView.HydraMentionTargetsProperty)
                Refresh();
        };
        this.Track(() => state.FaviconRevision, _ => Refresh());
    }

    // :17 let scaled = (pointSize * 2).rounded() / 2: the markdown point size to the nearest half point, no zoom.
    private double Scaled() => SwiftNumber.Rounded(MarkdownView.GetMarkdownPointSize(this) * 2) / 2;

    private void Refresh() =>
        representable.Update( // :22
            blocks,
            pointSize: Scaled(),
            dimmed: MarkdownView.GetMarkdownDimmed(this),
            revision: state.FaviconRevision,
            mentions: MarkdownView.GetHydraMentionPersonas(this),
            targets: MarkdownView.GetHydraMentionTargets(this),
            onOpenHead: openHead,
            onHost: host);
}

/// <summary>:33-209 <c>struct ProseRunRepresentable: NSViewRepresentable</c>: the run's one LinkTextView.</summary>
public sealed class ProseRunRepresentable : Decorator
{
    private LinkTextView view;
    private bool dismantled;

    /// <param name="blocks">:34 <c>let blocks: [MarkdownBlock]</c>.</param>
    /// <param name="pointSize">:35 <c>var pointSize: CGFloat = 13</c>.</param>
    /// <param name="dimmed">:36 <c>var dimmed: Bool = false</c>.</param>
    /// <param name="revision">:37 <c>var revision: Int = 0</c>.</param>
    /// <param name="mentions">:38 <c>var mentions: [HydraPersona] = []</c>.</param>
    /// <param name="targets">:39 <c>var targets: [String: HydraMentionTarget] = [:]</c>.</param>
    /// <param name="onOpenHead">:40 <c>var onOpenHead: ((URL) -> Void)?</c>.</param>
    /// <param name="onHost">:41 <c>var onHost: ((LinkTextView) -> Void)?</c>.</param>
    public ProseRunRepresentable(
        IReadOnlyList<MarkdownBlock> blocks,
        double pointSize = 13,
        bool dimmed = false,
        int revision = 0,
        IReadOnlyList<HydraPersona>? mentions = null,
        IReadOnlyDictionary<string, HydraMentionTarget>? targets = null,
        Action<SwiftURL>? onOpenHead = null,
        Action<LinkTextView>? onHost = null)
    {
        Blocks = blocks;
        Mentions = [];
        Targets = new Dictionary<string, HydraMentionTarget>();
        SetInputs(blocks, pointSize, dimmed, revision, mentions, targets, onOpenHead, onHost);
        Coordinator = MakeCoordinator();
        view = MakeNSView(LinkParagraphView.GetMergeRequestTarget(this));
        Child = view;
        UpdateNSView(view, LinkParagraphView.GetMergeRequestTarget(this));

        // updateNSView reads context.environment.mergeRequestTarget (:76): a change updates the view.
        PropertyChanged += (_, e) =>
        {
            if (e.Property == LinkParagraphView.MergeRequestTargetProperty && !dismantled)
                UpdateNSView(view, LinkParagraphView.GetMergeRequestTarget(this));
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (dismantled) return;
            DismantleNSView(view, Coordinator);
            dismantled = true;
        };
        AttachedToVisualTree += (_, _) =>
        {
            if (!dismantled) return;
            dismantled = false;
            Coordinator = MakeCoordinator();
            view = MakeNSView(LinkParagraphView.GetMergeRequestTarget(this));
            Child = view;
            UpdateNSView(view, LinkParagraphView.GetMergeRequestTarget(this));
        };
    }

    public IReadOnlyList<MarkdownBlock> Blocks { get; private set; }

    public double PointSize { get; private set; }

    public bool Dimmed { get; private set; }

    public int Revision { get; private set; }

    public IReadOnlyList<HydraPersona> Mentions { get; private set; }

    public IReadOnlyDictionary<string, HydraMentionTarget> Targets { get; private set; }

    public Action<SwiftURL>? OnOpenHead { get; private set; }

    public Action<LinkTextView>? OnHost { get; private set; }

    /// <summary><c>context.coordinator</c>.</summary>
    public LinkParagraphView.Coordinator Coordinator { get; private set; }

    /// <summary>SwiftUI re-creating the representable with new inputs, then calling updateNSView (:56-77).</summary>
    public void Update(
        IReadOnlyList<MarkdownBlock> blocks,
        double pointSize = 13,
        bool dimmed = false,
        int revision = 0,
        IReadOnlyList<HydraPersona>? mentions = null,
        IReadOnlyDictionary<string, HydraMentionTarget>? targets = null,
        Action<SwiftURL>? onOpenHead = null,
        Action<LinkTextView>? onHost = null)
    {
        SetInputs(blocks, pointSize, dimmed, revision, mentions, targets, onOpenHead, onHost);
        if (!dismantled) UpdateNSView(view, LinkParagraphView.GetMergeRequestTarget(this));
    }

    private void SetInputs(
        IReadOnlyList<MarkdownBlock> blocks,
        double pointSize,
        bool dimmed,
        int revision,
        IReadOnlyList<HydraPersona>? mentions,
        IReadOnlyDictionary<string, HydraMentionTarget>? targets,
        Action<SwiftURL>? onOpenHead,
        Action<LinkTextView>? onHost)
    {
        Blocks = blocks;
        PointSize = pointSize;
        Dimmed = dimmed;
        Revision = revision;
        Mentions = mentions ?? [];
        Targets = targets ?? new Dictionary<string, HydraMentionTarget>();
        OnOpenHead = onOpenHead;
        OnHost = onHost;
    }

    // :43-45 func makeCoordinator() -> LinkParagraphView.Coordinator
    public LinkParagraphView.Coordinator MakeCoordinator() => new();

    /// <summary>:47-54 <c>makeNSView(context:)</c>; <paramref name="mergeRequestTarget"/> is
    /// <c>context.environment.mergeRequestTarget</c>.</summary>
    public LinkTextView MakeNSView(MergeRequestTarget? mergeRequestTarget)
    {
        var made = new LinkTextView(); // :48
        made.Delegate = Coordinator; // :49
        made.PassesProseThrough = false; // :50
        OnHost?.Invoke(made); // :51
        made.MergeTarget = mergeRequestTarget; // :52
        return made; // :53
    }

    /// <summary>:56-77 <c>updateNSView(_:context:)</c>: the string is rebuilt only when the joined source, the point
    /// size, the dimming, the revision, the mention names or the head URLs changed; the head handler and the merge
    /// target land on every update (PR-THEME-150).</summary>
    public void UpdateNSView(LinkTextView view, MergeRequestTarget? mergeRequestTarget)
    {
        _ = Revision; // :57
        var coordinator = Coordinator; // :58
        var source = JoinedSources(Blocks); // :59
        var mentionNames = new ValueList<string>(Mentions.Select(persona => persona.Name)); // :60
        var headLinks = new ValueMap<string, SwiftURL>( // :61 targets.mapValues(\.url)
            Targets.Select(pair => KeyValuePair.Create(pair.Key, pair.Value.Url)), SwiftStringComparer.Instance);
        if (coordinator.LastSource is not { } lastSource || !SwiftString.Equal(lastSource, source) // :62
            || coordinator.LastPointSize != PointSize
            || coordinator.LastDimmed != Dimmed || coordinator.LastRevision != Revision // :63
            || !coordinator.LastMentions.SequenceEqual(mentionNames, SwiftStringComparer.Instance) // :64
            || !coordinator.LastHeads.Equals(headLinks))
        {
            coordinator.LastSource = source; // :65
            coordinator.LastPointSize = PointSize; // :66
            coordinator.LastDimmed = Dimmed; // :67
            coordinator.LastRevision = Revision; // :68
            coordinator.LastMentions = mentionNames; // :69
            coordinator.LastHeads = headLinks; // :70
            view.Render(Attributed(Blocks, PointSize, Dimmed, Mentions, Targets)); // :71
        }
        // :73-74 Outside the render check: the click handler is the environment's, and a new one must land even on
        // an update that drew nothing.
        view.OnOpenHead = OnOpenHead; // :75
        view.MergeTarget = mergeRequestTarget; // :76
    }

    /// <summary>:79-82 <c>sizeThatFits(_:nsView:context:)</c>: no answer without a proposed width (Avalonia's
    /// unconstrained width), else the width and the text's height at it.</summary>
    public Size? SizeThatFits(double proposalWidth, LinkTextView nsView)
    {
        if (double.IsInfinity(proposalWidth) || double.IsNaN(proposalWidth)) return null; // :80
        return new Size(proposalWidth, nsView.HeightForWidth(proposalWidth)); // :81
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var measured = base.MeasureOverride(availableSize);
        return SizeThatFits(availableSize.Width, view) ?? measured;
    }

    /// <summary>:84-88 <c>static func dismantleNSView(_:coordinator:)</c>: the delegate, head handler and merge target
    /// are dropped. UPSTREAM DEFECT 55, reproduced: unlike LinkParagraphView.swift:73-77 it does not detach the
    /// stream veil (latent: this path never activates the veil).</summary>
    public static void DismantleNSView(LinkTextView view, LinkParagraphView.Coordinator coordinator)
    {
        _ = coordinator;
        view.Delegate = null; // :85
        view.OnOpenHead = null; // :86
        view.MergeTarget = null; // :87
    }

    // :90-94 static func joinedSources(_ blocks: [MarkdownBlock]) -> String
    public static string JoinedSources(IReadOnlyList<MarkdownBlock> blocks)
    {
        var parts = new List<string>(); // :91
        Collect(blocks, parts); // :92
        return string.Join("\n", parts); // :93
    }

    // :96-112 private static func collect(_ blocks: [MarkdownBlock], into parts: inout [String])
    private static void Collect(IReadOnlyList<MarkdownBlock> blocks, List<string> into)
    {
        foreach (var block in blocks) // :97
        {
            switch (block) // :98
            {
                case MarkdownBlock.Paragraph(var text): // :99-100
                    into.Add(text);
                    break;
                case MarkdownBlock.Heading(_, var text): // :99-100
                    into.Add(text);
                    break;
                case MarkdownBlock.List(_, _, var items): // :101-105
                    foreach (var item in items)
                    {
                        into.Add(item.Text); // :103
                        Collect(item.Children, into); // :104
                    }
                    break;
                case MarkdownBlock.Quote(var inner): // :106-107
                    Collect(inner, into);
                    break;
                case MarkdownBlock.Code or MarkdownBlock.Table or MarkdownBlock.Rule: // :108-109
                    break;
                default:
                    throw new UnreachableException();
            }
        }
    }

    /// <summary>:114-121 <c>static func attributed(blocks:pointSize:dimmed:mentions:targets:) -> NSAttributedString</c>:
    /// every block at depth 0, indent 0, the run's dimming and 12 points after.</summary>
    public static ParagraphText Attributed(
        IReadOnlyList<MarkdownBlock> blocks,
        double pointSize,
        bool dimmed,
        IReadOnlyList<HydraPersona> mentions,
        IReadOnlyDictionary<string, HydraMentionTarget>? targets = null)
    {
        targets ??= new Dictionary<string, HydraMentionTarget>();
        var @out = new ParagraphText(); // :116
        foreach (var block in blocks) // :117-119
            Append(block, depth: 0, indent: 0, dimmed: dimmed, spacing: 12, pointSize: pointSize, mentions: mentions, targets: targets, to: @out);
        return @out; // :120
    }

    // :123-199 private static func append(_:depth:indent:dimmed:spacing:pointSize:mentions:targets:to:)
    private static void Append(
        MarkdownBlock block,
        int depth,
        double indent,
        bool dimmed,
        double spacing,
        double pointSize,
        IReadOnlyList<HydraPersona> mentions,
        IReadOnlyDictionary<string, HydraMentionTarget> targets,
        ParagraphText to)
    {
        var @base = SwiftFont.SystemFont(pointSize); // :125
        var color = dimmed ? LinkParagraphView.DimmedLabelTextColor : LinkParagraphView.LabelTextColor; // :126
        switch (block) // :127
        {
            case MarkdownBlock.Paragraph(var text): // :128-136
            {
                Separate(to, @base, color); // :129
                var line = new ParagraphText(); // :130
                line.Append(LinkParagraphView.Attributed(text, pointSize, dimmed, mentions: mentions, targets: targets));
                var style = new ParagraphStyle // :131-134
                {
                    FirstLineHeadIndent = indent,
                    HeadIndent = indent,
                    ParagraphSpacing = spacing,
                };
                line.AddParagraphStyle(style, 0, line.Length); // :135
                to.Append(line); // :136
                break;
            }
            case MarkdownBlock.Heading(var level, var text): // :137-161
            {
                Separate(to, @base, color); // :138
                var size = level switch // :139-145
                {
                    1 => SwiftNumber.Rounded(pointSize * 1.35), // :141
                    2 => SwiftNumber.Rounded(pointSize * 1.2), // :142
                    3 => SwiftNumber.Rounded(pointSize * 1.08), // :143
                    _ => pointSize, // :144
                };
                var line = new ParagraphText(); // :146
                line.Append(LinkParagraphView.Attributed(text, size, dimmed, mentions: mentions, targets: targets));
                var semibold = SwiftFont.SystemFont(size, SwiftFont.Weight.Semibold); // :147
                foreach (var (font, location, length) in line.FontRuns()) // :148-154
                {
                    if (font is null) continue; // :149
                    if (!font.HasBoldTrait && !font.HasMonoSpaceTrait) // :150-151
                        line.AddFont(semibold, location, length); // :152
                }
                var style = new ParagraphStyle // :155-159
                {
                    FirstLineHeadIndent = indent,
                    HeadIndent = indent,
                    ParagraphSpacingBefore = level <= 2 ? 6 : 2,
                    ParagraphSpacing = 12,
                };
                line.AddParagraphStyle(style, 0, line.Length); // :160
                to.Append(line); // :161
                break;
            }
            case MarkdownBlock.List(var ordered, var start, var items): // :162-191
            {
                for (var index = 0; index < items.Count; index++) // :163
                {
                    var item = items[index];
                    Separate(to, @base, color); // :164
                    string marker; // :165
                    if (item.Checked is { } isChecked) // :166-167
                        marker = isChecked ? "\U00002611" : "\U00002610"; // U+2611 BALLOT BOX WITH CHECK, U+2610 BALLOT BOX
                    else if (ordered) // :168-169
                        marker = checked(start + index).ToString(System.Globalization.CultureInfo.InvariantCulture) + ".";
                    else // :170-171
                        marker = depth == 0 ? "\U00002022" : "\U000025E6"; // U+2022 BULLET, U+25E6 WHITE BULLET
                    var line = new ParagraphText(); // :173-176
                    line.Append(marker + "\t", font: @base, foregroundColor: color);
                    if (item.Text.Length > 0) // :177-179
                        line.Append(LinkParagraphView.Attributed(item.Text, pointSize, dimmed, mentions: mentions, targets: targets));
                    var style = new ParagraphStyle // :180-185
                    {
                        FirstLineHeadIndent = indent,
                        HeadIndent = indent + 18,
                        TabStops = new ValueList<double>([indent + 18]),
                        DefaultTabInterval = 18,
                        ParagraphSpacing = depth == 0 && index == items.Count - 1 ? 12 : 6,
                    };
                    line.AddParagraphStyle(style, 0, line.Length); // :186
                    to.Append(line); // :187
                    foreach (var child in item.Children) // :188-190
                        Append(child, depth + 1, indent + 18, dimmed, spacing: 6, pointSize, mentions, targets, to);
                }
                break;
            }
            case MarkdownBlock.Quote(var inner): // :192-195
                foreach (var child in inner)
                    Append(child, depth, indent + 14, dimmed: true, spacing: spacing, pointSize, mentions, targets, to);
                break;
            case MarkdownBlock.Code or MarkdownBlock.Table or MarkdownBlock.Rule: // :196-197
                break;
            default:
                throw new UnreachableException();
        }
    }

    // :201-208 private static func separate(to:font:color:): a newline before every block after the first.
    private static void Separate(ParagraphText to, SwiftFont font, SwiftColor color)
    {
        if (to.Length == 0) return; // :203
        to.Append("\n", font: font, foregroundColor: color); // :204-207
    }
}
