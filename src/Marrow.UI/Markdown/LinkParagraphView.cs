using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;

// Source: DroppyCode/UI/Markdown/LinkParagraphView.swift (B2 1fbf81e), :1-550.

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
        Action<LinkTextView>? onHost = null)
    {
        _ = revision;
        var view = new LinkTextView
        {
            OnOpenHead = onOpenHead,
        };
        view.RenderText(source, pointSize, dimmed, streaming, mentions ?? [], targets ?? new Dictionary<string, HydraMentionTarget>());
        Child = view;
        onHost?.Invoke(view);
    }

    // :67-69 func makeCoordinator() -> Coordinator
    public Coordinator MakeCoordinator() => new();

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
        public bool TextView(LinkTextView textView, object clickedOnLink, int at)
        {
            _ = textView;
            _ = at;
            if (clickedOnLink is SwiftURL url)
            {
                InlineText.OpenUrl?.Invoke(url);
                return true;
            }

            if (clickedOnLink is string text && SwiftURL.Create(text) is { } parsed)
            {
                InlineText.OpenUrl?.Invoke(parsed);
                return true;
            }

            return false;
        }
    }

    // :160-162 private static func nsColor(_ persona: HydraPersona) -> NSColor
    private static SwiftColor NsColor(HydraPersona persona) =>
        new((persona.Hex >> 16 & 0xFF) / 255.0, (persona.Hex >> 8 & 0xFF) / 255.0, (persona.Hex & 0xFF) / 255.0);

    // :164-177 @MainActor private static func dragonImage(_ persona: HydraPersona, size: CGFloat) -> NSImage
    private static readonly Dictionary<string, IImage> dragonCache = new(StringComparer.Ordinal);

    private static IImage DragonImage(HydraPersona persona, double size)
    {
        var key = persona.Asset + "@" + persona.Hex.ToString("X6", System.Globalization.CultureInfo.InvariantCulture) + "@" +
            SwiftNumber.Description(size);
        if (dragonCache.TryGetValue(key, out var cached)) return cached;

        var source = SwiftImage.Named(persona.Asset);
        source.Width = size;
        source.Height = size;
        source.Measure(new Size(size, size));
        source.Arrange(new Rect(0, 0, size, size));

        var pixelWidth = Math.Max(1, (int)Math.Ceiling(size));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(size));
        var rendered = new RenderTargetBitmap(new PixelSize(pixelWidth, pixelHeight), new Vector(96, 96));
        rendered.Render(source);

        var tinted = new WriteableBitmap(new PixelSize(pixelWidth, pixelHeight), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (var buffer = tinted.Lock())
        {
            var stride = buffer.RowBytes;
            var pixels = new byte[stride * pixelHeight];
            rendered.CopyPixels(new PixelRect(0, 0, pixelWidth, pixelHeight), buffer.Address, pixels.Length, stride);
            Marshal.Copy(buffer.Address, pixels, 0, pixels.Length);
            var red = (byte)((persona.Hex >> 16) & 0xFF);
            var green = (byte)((persona.Hex >> 8) & 0xFF);
            var blue = (byte)(persona.Hex & 0xFF);
            for (var y = 0; y < pixelHeight; y++)
            {
                for (var x = 0; x < pixelWidth; x++)
                {
                    var index = y * stride + x * 4;
                    pixels[index] = blue;
                    pixels[index + 1] = green;
                    pixels[index + 2] = red;
                }
            }
            Marshal.Copy(pixels, 0, buffer.Address, pixels.Length);
        }

        dragonCache[key] = tinted;
        return tinted;
    }

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
    public bool Equals(MergeRequestTarget? other) => other is not null && ChatID == other.ChatID;

    // :257-259, the hash that matches Equals (R-VAL-4)
    public override int GetHashCode() => ChatID.GetHashCode();
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
    private sealed record TextRange(int Location, int Length, SwiftURL Url);
    private sealed record LinkHitRange(int Location, int Length, TextRange Link);
    private sealed record HeadRange(int Location, int Length, int NameLocation, int NameLength, SwiftURL Url, string Name);

    private readonly List<TextRange> links = [];
    private readonly List<LinkHitRange> linkHits = [];
    private readonly List<HeadRange> heads = [];
    private SelectableTextBlock? textBlock;
    private int textLength;
    private double measuredWidth;
    private double measuredHeight;

    private static readonly Cursor textCursor = new(StandardCursorType.Ibeam);
    private static readonly Cursor linkCursor = new(StandardCursorType.Hand);
    private static readonly Cursor arrowCursor = new(StandardCursorType.Arrow);

    // :281 var mergeTarget: MergeRequestTarget?
    public MergeRequestTarget? MergeTarget { get; set; }

    // :285 var passesProseThrough = false
    public bool PassesProseThrough { get; set; }

    // :287 var onOpenHead: ((URL) -> Void)?
    public Action<SwiftURL>? OnOpenHead { get; set; }

    // :290 let streamVeil = LinkTextVeil()
    public LinkTextVeil StreamVeil { get; } = new();

    // :299-332 init()
    public LinkTextView()
    {
        textBlock = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Padding = new Thickness(0),
            MinHeight = 0,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
        };
        Child = textBlock;
        textBlock.PointerPressed += OnPointerPressed;
        textBlock.PointerMoved += OnPointerMoved;
        textBlock.PointerExited += OnPointerExited;
        ActualThemeVariantChanged += (_, _) => ReapplyLinkAttributes();
    }

    // :355-357 func hasLink(at point: CGPoint) -> Bool
    public bool HasLink(Point at) => LinkAndRange(at) is not null;

    // :360-362 func link(at point: CGPoint) -> URL?
    public SwiftURL? Link(Point at) => LinkAndRange(at)?.Url;

    // :365-378 func headMention(at point: CGPoint) -> (url: URL, range: NSRange)?
    public (SwiftURL Url, (int Location, int Length) Range)? HeadMention(Point at)
    {
        if (CharacterIndexAt(at) is not { } index) return null;
        var match = heads.FirstOrDefault(head => index >= head.Location && index < head.Location + head.Length);
        return match is null ? null : (match.Url, (match.Location, match.Length));
    }

    // :381-412 func headMentionRect(at point: CGPoint) -> (name: String, url: URL, rect: NSRect)?
    public (string Name, SwiftURL Url, Rect Rect)? HeadMentionRect(Point at)
    {
        if (HeadMention(at) is not { } mention) return null;
        var head = heads.FirstOrDefault(candidate =>
            candidate.Location == mention.Range.Location && candidate.Length == mention.Range.Length);
        var block = textBlock;
        if (head is null || block is null || block.TextLayout is not { } layout) return null;

        var rects = layout.HitTestTextRange(head.NameLocation, head.NameLength).ToArray();
        var rect = rects.Length > 0 ? rects[0] : new Rect();
        foreach (var next in rects.Skip(1)) rect = rect.Union(next);
        if (rect.Width <= 0 || rect.Height <= 0) rect = new Rect(at.X, at.Y, 1, 1);
        return (head.Name, head.Url, rect);
    }

    // :415-428 private func linkAndRange(at point: CGPoint) -> (url: URL, range: NSRange)?
    private (SwiftURL Url, (int Location, int Length) Range)? LinkAndRange(Point at)
    {
        if (CharacterIndexAt(at) is not { } index) return null;
        var match = linkHits.FirstOrDefault(link => index >= link.Location && index < link.Location + link.Length);
        return match is null ? null : (match.Link.Url, (match.Link.Location, match.Link.Length));
    }

    // :463-471 private func presentLinkPopover(for event: NSEvent) -> Bool; the event's location in this view.
    private bool PresentLinkPopover(Point @for)
    {
        if (LinkAndRange(@for) is not { } link || textBlock is not { TextLayout: { } layout } block) return false;
        var rects = layout.HitTestTextRange(link.Range.Location, link.Range.Length).ToArray();
        var rect = rects.Length > 0 ? rects[0] : new Rect();
        foreach (var next in rects.Skip(1)) rect = rect.Union(next);
        if (rect.Width <= 0 || rect.Height <= 0) rect = new Rect(@for.X, @for.Y, 1, 1);
        LinkPopover.Shared.Show(link.Url, MergeTarget, rect, this);
        return true;
    }

    /// <summary>:498-507 func height(forWidth width: CGFloat).</summary>
    public double HeightForWidth(double width)
    {
        var block = textBlock;
        if (width <= 0 || block is null) return 0;
        if (width == measuredWidth) return measuredHeight;
        block.Measure(new Size(width, double.PositiveInfinity));
        measuredWidth = width;
        measuredHeight = Math.Ceiling(block.DesiredSize.Height);
        return measuredHeight;
    }

    private int? CharacterIndexAt(Point point)
    {
        var block = textBlock;
        if (block is null || textLength == 0 || block.TextLayout is not { } layout) return null;
        var hit = layout.HitTestPoint(point);
        if (!hit.IsInside || hit.TextPosition < 0 || hit.TextPosition >= textLength) return null;
        return hit.TextPosition;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (textBlock is not { } block) return;
        var point = e.GetPosition(block);
        block.Cursor = HeadMention(point) is not null
            ? arrowCursor
            : Link(point) is not null
                ? linkCursor
                : textCursor;
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        _ = e;
        if (textBlock is { } block) block.Cursor = null;
    }

    private void OnPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (textBlock is not { } block) return;
        var point = e.GetPosition(block);
        if (HeadMention(point) is { } head)
        {
            OnOpenHead?.Invoke(head.Url);
            e.Handled = true;
            return;
        }

        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed ||
            (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control)))
        {
            if (PresentLinkPopover(e.GetPosition(this))) e.Handled = true;
            return;
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && Link(point) is { } link)
        {
            InlineText.OpenUrl?.Invoke(link);
            e.Handled = true;
        }
    }

    // :481-496 render(_:)
    public void RenderText(
        string source,
        double pointSize,
        bool dimmed,
        bool streaming,
        IReadOnlyList<HydraPersona> mentions,
        IReadOnlyDictionary<string, HydraMentionTarget> targets)
    {
        var block = textBlock;
        if (block is null) return;
        links.Clear();
        linkHits.Clear();
        heads.Clear();
        textLength = 0;
        block.Inlines!.Clear();

        var pretty = RichLink.PrettyAttributed(source, streaming);
        var faces = Fonts(pointSize);
        var textColor = dimmed ? SwiftColor.LabelColor.Opacity(0.7) : SwiftColor.LabelColor;
        foreach (var run in pretty.Runs)
        {
            if (run.Text.Length == 0) continue;
            var font = run.IsCode ? faces.Mono : run.IsBold ? faces.Bold : run.IsItalic ? faces.Italic : faces.Base;
            if (run.Link is { } url)
            {
                var linkStart = textLength;
                var host = url.Host is { } value ? SwiftString.Lowercased(value) : null;
                if (host is not null && FaviconCache.Cached(host) is { } icon)
                {
                    block.Inlines!.Add(new InlineUIContainer
                    {
                        Child = new Image { Source = icon, Width = 14, Height = 14 },
                    });
                    textLength++;
                    block.Inlines!.Add(new Run(" ") { FontSize = pointSize, Foreground = textColor.Brush(block) });
                    textLength++;
                }

                var resolved = font.Resolve();
                var linkRun = new Run(run.Text)
                {
                    FontSize = font.PointSize,
                    FontFamily = resolved.FontFamily,
                    FontWeight = resolved.FontWeight,
                    FontStyle = resolved.FontStyle,
                    Foreground = Chrome.Chrome.AccentNSColor.Brush(block),
                };
                block.Inlines!.Add(linkRun);
                var linkText = new TextRange(textLength, run.Text.Length, url);
                links.Add(linkText);
                textLength += run.Text.Length;
                linkHits.Add(new LinkHitRange(linkStart, textLength - linkStart, linkText));
                continue;
            }

            AppendProse(run.Text, font, pointSize, textColor, faces.Bold, mentions, targets, block);
        }

        ReapplyLinkAttributes();
        block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
    }

    private void AppendProse(
        string value,
        SwiftFont font,
        double pointSize,
        SwiftColor color,
        SwiftFont bold,
        IReadOnlyList<HydraPersona> mentions,
        IReadOnlyDictionary<string, HydraMentionTarget> targets,
        SelectableTextBlock block)
    {
        if (mentions.Count == 0)
        {
            AddRun(value, font, color, block);
            return;
        }

        var sorted = mentions.OrderByDescending(persona => SwiftString.Count(persona.Name)).ToArray();
        var graphemes = SwiftString.Characters(value).ToArray();
        var boundaries = new HashSet<int> { 0, value.Length };
        var offset = 0;
        foreach (var grapheme in graphemes)
        {
            offset += grapheme.Length;
            boundaries.Add(offset);
        }

        var cursor = 0;
        var runStart = 0;
        while (cursor < value.Length)
        {
            HydraPersona? match = null;
            foreach (var persona in sorted)
            {
                if (persona.Name.Length == 0 || cursor + persona.Name.Length > value.Length) continue;
                if (!boundaries.Contains(cursor + persona.Name.Length)) continue;
                if (!string.Equals(value.Substring(cursor, persona.Name.Length), persona.Name, StringComparison.Ordinal)) continue;

                var beforeOkay = cursor == 0 || !IsWordCharacter(PreviousCharacter(graphemes, boundaries, cursor));
                var afterOffset = cursor + persona.Name.Length;
                var afterOkay = afterOffset == value.Length || !IsWordCharacter(NextCharacter(graphemes, boundaries, afterOffset));
                if (!beforeOkay || !afterOkay) continue;
                match = persona;
                break;
            }

            if (match is null)
            {
                cursor = NextBoundary(boundaries, cursor);
                continue;
            }

            var personaMatch = match;
            var start = cursor;
            var end = start + personaMatch.Name.Length;
            AddRun(value[runStart..start], font, color, block);

            var glyphSize = SwiftNumber.Rounded(pointSize * 1.25);
            block.Inlines!.Add(new InlineUIContainer
            {
                Child = new Image
                {
                    Source = DragonImageForText(personaMatch, glyphSize),
                    Width = glyphSize,
                    Height = glyphSize,
                },
            });
            var attachmentLocation = textLength;
            textLength++;
            block.Inlines!.Add(new Run(" ") { FontSize = pointSize, Foreground = color.Brush(block) });
            textLength++;

            var resolvedBold = bold.Resolve();
            var nameLocation = textLength;
            var nameRun = new Run(personaMatch.Name)
            {
                FontSize = bold.PointSize,
                FontFamily = resolvedBold.FontFamily,
                FontWeight = resolvedBold.FontWeight,
                Foreground = PersonaColorForText(personaMatch).Brush(block),
            };
            block.Inlines!.Add(nameRun);
            if (targets.TryGetValue(personaMatch.Name, out var target))
                heads.Add(new HeadRange(attachmentLocation, personaMatch.Name.Length + 2, nameLocation,
                    personaMatch.Name.Length, target.Url, personaMatch.Name));
            textLength += personaMatch.Name.Length;
            cursor = end;
            runStart = end;
        }

        if (runStart < value.Length) AddRun(value[runStart..], font, color, block);
    }

    private static IImage DragonImageForText(HydraPersona persona, double size)
    {
        var key = persona.Asset + "@" + persona.Hex.ToString("X6", System.Globalization.CultureInfo.InvariantCulture) + "@" +
            SwiftNumber.Description(size);
        if (textDragonCache.TryGetValue(key, out var cached)) return cached;

        var source = SwiftImage.Named(persona.Asset);
        source.Width = size;
        source.Height = size;
        source.Measure(new Size(size, size));
        source.Arrange(new Rect(0, 0, size, size));

        var pixelWidth = Math.Max(1, (int)Math.Ceiling(size));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(size));
        var rendered = new RenderTargetBitmap(new PixelSize(pixelWidth, pixelHeight), new Vector(96, 96));
        rendered.Render(source);

        var tinted = new WriteableBitmap(new PixelSize(pixelWidth, pixelHeight), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (var buffer = tinted.Lock())
        {
            var stride = buffer.RowBytes;
            var pixels = new byte[stride * pixelHeight];
            rendered.CopyPixels(new PixelRect(0, 0, pixelWidth, pixelHeight), buffer.Address, pixels.Length, stride);
            Marshal.Copy(buffer.Address, pixels, 0, pixels.Length);
            var red = (byte)((persona.Hex >> 16) & 0xFF);
            var green = (byte)((persona.Hex >> 8) & 0xFF);
            var blue = (byte)(persona.Hex & 0xFF);
            for (var y = 0; y < pixelHeight; y++)
            {
                for (var x = 0; x < pixelWidth; x++)
                {
                    var index = y * stride + x * 4;
                    pixels[index] = blue;
                    pixels[index + 1] = green;
                    pixels[index + 2] = red;
                }
            }
            Marshal.Copy(pixels, 0, buffer.Address, pixels.Length);
        }

        textDragonCache[key] = tinted;
        return tinted;
    }

    private static readonly Dictionary<string, IImage> textDragonCache = new(StringComparer.Ordinal);

    private static SwiftColor PersonaColorForText(HydraPersona persona) =>
        new((persona.Hex >> 16 & 0xFF) / 255.0, (persona.Hex >> 8 & 0xFF) / 255.0, (persona.Hex & 0xFF) / 255.0);

    private static int NextBoundary(HashSet<int> boundaries, int offset)
    {
        foreach (var boundary in boundaries)
            if (boundary > offset) return boundary;
        return offset + 1;
    }

    private static string PreviousCharacter(IReadOnlyList<string> graphemes, HashSet<int> boundaries, int offset)
    {
        _ = boundaries;
        var position = 0;
        foreach (var grapheme in graphemes)
        {
            var next = position + grapheme.Length;
            if (next >= offset) return next == offset ? grapheme : "";
            position = next;
        }
        return "";
    }

    private static string NextCharacter(IReadOnlyList<string> graphemes, HashSet<int> boundaries, int offset)
    {
        var position = 0;
        foreach (var grapheme in graphemes)
        {
            var next = position + grapheme.Length;
            if (position == offset) return grapheme;
            if (position < offset && offset < next) return "";
            position = next;
        }
        return "";
    }

    private void AddRun(string value, SwiftFont font, SwiftColor color, SelectableTextBlock block)
    {
        if (value.Length == 0) return;
        var resolved = font.Resolve();
        block.Inlines!.Add(new Run(value)
        {
            FontSize = font.PointSize,
            FontFamily = resolved.FontFamily,
            FontWeight = resolved.FontWeight,
            FontStyle = resolved.FontStyle,
            Foreground = color.Brush(block),
        });
        textLength += value.Length;
    }

    // :481-496 linkTextAttributes are reapplied when the theme changes.
    private void ReapplyLinkAttributes()
    {
        var block = textBlock;
        if (block?.Inlines is not { } inlines) return;
        var offset = 0;
        foreach (var inline in inlines)
        {
            if (inline is Run run)
            {
                var length = run.Text?.Length ?? 0;
                if (links.Any(link => link.Location < offset + length && link.Location + link.Length > offset))
                    run.Foreground = Chrome.Chrome.AccentNSColor.Brush(block);
                offset += length;
            }
            else if (inline is InlineUIContainer)
            {
                offset++;
            }
        }
    }

    private static bool IsWordCharacter(string value) =>
        SwiftCharacter.IsLetter(value) || SwiftCharacter.IsNumber(value);

    private static (SwiftFont Base, SwiftFont Bold, SwiftFont Italic, SwiftFont Mono) Fonts(double pointSize)
    {
        if (!fonts.TryGetValue(pointSize, out var result))
        {
            var baseFont = SwiftFont.SystemFont(pointSize);
            result = (
                baseFont,
                SwiftFont.BoldSystemFont(pointSize),
                baseFont.Italic(),
                SwiftFont.MonospacedSystemFont(Math.Max(9, pointSize - 1), SwiftFont.Weight.Regular));
            fonts[pointSize] = result;
        }

        return result;
    }

    private static readonly Dictionary<double, (SwiftFont Base, SwiftFont Bold, SwiftFont Italic, SwiftFont Mono)> fonts = [];

    // :346-348 override func updateTrackingAreas()
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        StreamVeil.Update(this, active: false);
    }

    // :350 override func resetCursorRects()
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        StreamVeil.Detach(this);
        base.OnDetachedFromVisualTree(e);
    }
}
