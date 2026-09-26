using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;

// Source: DroppyCode/UI/Markdown/LinkParagraphView.swift (B3 52bc792), :1-550. Link clicks and mention scanning
// now go through MarkdownLinkOpener and UI.Markdown.MentionScanner (N10); see each member's own citation.

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

        // :90-100 func textView(_ textView: NSTextView, clickedOnLink link: Any, at charIndex: Int) -> Bool. B3
        // :92, :96 route through MarkdownLinkOpener.open rather than the direct NSWorkspace call this pre-dated
        // (PR-THEME-145; the plain InlineText.OpenUrl hook was a pre-existing divergence).
        public bool TextView(LinkTextView textView, object clickedOnLink, int at)
        {
            _ = textView;
            _ = at;
            if (clickedOnLink is SwiftURL url)
            {
                MarkdownLinkOpener.Open(url);
                return true;
            }

            if (clickedOnLink is string text && SwiftURL.Create(text) is { } parsed)
            {
                MarkdownLinkOpener.Open(parsed);
                return true;
            }

            return false;
        }
    }

    // :121 and ProseRunView.swift:126: `NSColor.labelColor` and `NSColor.labelColor.withAlphaComponent(0.7)`, one
    // value each so two builds of the same text compare equal (NSAttributedString.isEqual, :490).
    internal static readonly SwiftColor LabelTextColor = SwiftColor.LabelColor;
    internal static readonly SwiftColor DimmedLabelTextColor = SwiftColor.LabelColor.Opacity(0.7);

    // :108 @MainActor private static var fonts: [CGFloat: (base: NSFont, bold: NSFont, italic: NSFont, mono: NSFont)];
    // the base face's descender rides along, measured once per size (:139, :202 `base.descender`).
    private static readonly Dictionary<double, (SwiftFont Base, SwiftFont Bold, SwiftFont Italic, SwiftFont Mono, double Descender)> attributedFonts = [];

    /// <summary>
    /// :110-156 <c>static func attributed(source:pointSize:dimmed:streaming:mentions:targets:) -> NSAttributedString</c>:
    /// the same pretty string the Text path renders (short titles, bold links), on explicit fonts, adaptive colours
    /// and favicon attachments (:103-105). ProseRunView.swift:130, :146, :178 build a run from it (ED-210); this unit's own
    /// paragraph path still draws through <see cref="LinkTextView.RenderText"/>.
    /// </summary>
    public static ParagraphText Attributed(
        string source,
        double pointSize,
        bool dimmed,
        bool streaming = false,
        IReadOnlyList<HydraPersona>? mentions = null,
        IReadOnlyDictionary<string, HydraMentionTarget>? targets = null)
    {
        mentions ??= [];
        targets ??= new Dictionary<string, HydraMentionTarget>();
        var pretty = RichLink.PrettyAttributed(source, streaming); // :112
        var (@base, bold, italic, mono, descender) = AttributedFonts(pointSize); // :113-120
        var textColor = dimmed ? DimmedLabelTextColor : LabelTextColor; // :121
        var @out = new ParagraphText(); // :122
        foreach (var run in pretty.Runs) // :123
        {
            var text = run.Text; // :124
            if (text.Length == 0) continue; // :125
            var font = @base; // :126
            if (run.IsCode) // :127-129
            {
                font = mono;
            }
            else
            {
                if (run.IsItalic) font = italic; // :131
                if (run.IsBold) font = bold; // :132
            }
            if (run.Link is { } url) // :134
            {
                if (url.Host is { } host && FaviconCache.Cached(SwiftString.Lowercased(host)) is { } icon) // :135-136
                {
                    @out.Append(new ParagraphTextAttachment(icon, 0, descender + 1, 14, 14)); // :137-140
                    @out.Append(" ", font: @base, foregroundColor: textColor); // :141-144
                }
                // :146-150 colour and underline come from the view's linkTextAttributes
                @out.Append(text, font: font, link: url);
            }
            else
            {
                AppendProse(text, font, textColor, @base, descender, bold, mentions, targets, @out); // :152
            }
        }
        return @out; // :155
    }

    private static (SwiftFont Base, SwiftFont Bold, SwiftFont Italic, SwiftFont Mono, double Descender) AttributedFonts(double pointSize)
    {
        if (attributedFonts.TryGetValue(pointSize, out var known)) return known;
        var @base = SwiftFont.SystemFont(pointSize); // :114
        var set = (@base, SwiftFont.BoldSystemFont(pointSize), // :115
            @base.Italic(), // :116 NSFontManager.shared.convert(base, toHaveTrait: .italicFontMask)
            SwiftFont.MonospacedSystemFont(Math.Max(9, pointSize - 1), SwiftFont.Weight.Regular), // :117
            Descender(@base));
        attributedFonts[pointSize] = set; // :118
        return set;
    }

    // NSFont.descender: the base face's descent below the baseline, negative, in points. Avalonia's glyph metrics
    // store Descent positive below the baseline, in design units (as InlineText.Metrics reads them).
    private static double Descender(SwiftFont font)
    {
        var resolved = font.Resolve();
        var typeface = new Typeface(resolved.FontFamily, resolved.FontStyle, resolved.FontWeight);
        if (!FontManager.Current.TryGetGlyphTypeface(typeface, out var glyph))
            throw new InvalidOperationException("No glyph typeface for the markdown base font (ED-107 registers Inter).");
        return -glyph.Metrics.Descent * resolved.FontSize / glyph.Metrics.DesignEmHeight;
    }

    // :179-223 @MainActor private static func appendProse(_:font:color:base:bold:mentions:targets:into:)
    private static void AppendProse(
        string value,
        SwiftFont font,
        SwiftColor color,
        SwiftFont @base,
        double descender,
        SwiftFont bold,
        IReadOnlyList<HydraPersona> mentions,
        IReadOnlyDictionary<string, HydraMentionTarget> targets,
        ParagraphText @out)
    {
        if (mentions.Count == 0) // :180-186
        {
            @out.Append(value, font: font, foregroundColor: color);
            return;
        }
        var glyphSize = SwiftNumber.Rounded(@base.PointSize * 1.25); // :187
        var runStart = 0; // :188
        foreach (var match in MentionScanner.Matches(value, mentions, persona => persona.Name)) // :197
        {
            var persona = match.Value; // :198
            @out.Append(value[runStart..match.LowerBound], font: font, foregroundColor: color); // :199 flush, :189-196
            var attachment = new ParagraphTextAttachment(DragonImage(persona, glyphSize), 0, descender + 1, glyphSize, glyphSize); // :200-202
            if (targets.TryGetValue(persona.Name, out var target)) // :203-210
                @out.Append(attachment, hydraHead: target.Url);
            else // :211-213
                @out.Append(attachment);
            @out.Append(" ", font: @base, foregroundColor: color); // :214
            if (targets.TryGetValue(persona.Name, out var head)) // :215-216
                @out.Append(persona.Name, font: bold, foregroundColor: NsColor(persona), hydraHead: head.Url);
            else // :217-218
                @out.Append(persona.Name, font: bold, foregroundColor: NsColor(persona));
            runStart = match.UpperBound; // :220
        }
        @out.Append(value[runStart..], font: font, foregroundColor: color); // :222 flush(from: runStart, to: endIndex)
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

/// <summary>
/// :226-245 <c>enum MarkdownLinkOpener</c>. On the source's own platform, a URL with no scheme, host, query or
/// fragment whose path starts with "/" opens as a local file (stripping a trailing ":&lt;positive int&gt;" line
/// suffix first, when the full path does not exist but the stripped one does and is not a directory); anything
/// else goes to <c>NSWorkspace.shared.open</c>. OD-0026 (owner ruling on unknowns.md UNK-0027; PA-0020) extends
/// this for Windows: a drive-letter path (<c>C:\...</c>, <c>C:/...</c>, or the file-URL form <c>/C:/...</c> or
/// <c>file:C:/...</c>) takes the same local-file branch, with no query/fragment exemption (review A29: unlike the
/// source's own POSIX guard, a "?"/"#" here is exactly as attacker-controllable as the rest of a chat-authored
/// link, and letting it skip the branch would defeat the reason OD-0026's Windows-only shapes exist); a UNC
/// target (<c>\\host\...</c>, <c>//host/...</c>, <c>/\host\...</c>, <c>file://host/...</c>) is never opened or
/// revealed (named safety deviation); and a local target of a type Windows runs directly is revealed, selected,
/// in File Explorer rather than launched (ED-204, another named safety deviation). ED-203 hardens the generic
/// opener this reaches when neither branch matches (<see cref="ShellWorkspace.OpenWithDefaultApp"/>'s own gate)
/// against a relative or scheme-less-host string.
/// </summary>
public static class MarkdownLinkOpener
{
    // :227 static func open(_ url: URL)
    public static void Open(SwiftURL url)
    {
        if (IsUnc(url)) return; // ED-204: never opened or revealed

        if (LocalFilePath(url) is { } rawPath)
        {
            var path = ApplyLineSuffixRule(rawPath); // :234-242
            if (ShellWorkspace.ShouldReveal(path)) ShellWorkspace.RevealInFileExplorer(path); // ED-204
            else ShellWorkspace.OpenWithDefaultApp(path);
            return;
        }

        ShellWorkspace.OpenWithDefaultApp(url.AbsoluteString); // :230, :243 NSWorkspace.shared.open(url)
    }

    /// <summary>
    /// :228-229 the source's own gate (no scheme, host, query or fragment; an absolute "/"-rooted path), extended
    /// by OD-0026 with two Windows-only shapes: a drive-letter path (whose drive letter parses as a one-letter
    /// <see cref="SwiftURL.Scheme"/>) and the file-URL form of one. Returns the candidate local-file path, or
    /// null when the URL does not qualify.
    /// </summary>
    internal static string? LocalFilePath(SwiftURL url)
    {
        if (url.Scheme is null && url.Host is null && url.Query is null && url.Fragment is null
            && url.Path.Length > 0 && url.Path[0] == '/')
            return url.Path; // :228-229

        if (url.Scheme is { Length: 1 } drive && IsAsciiLetter(drive[0]))
        {
            // review A29: a doubled separator right after the drive letter ("C://Windows/...") is exactly what
            // Windows treats as "C:/Windows/..." (it collapses a repeated path separator), but RFC 3986 parses
            // the doubled slash as an authority (a Host) rather than folding it into Path; both shapes must be
            // recognised, or "C://..." reaches ShellWorkspace.OpenWithDefaultApp raw and skips ShouldReveal.
            if (url.Host is { Length: > 0 } host && (url.Path.Length == 0 || url.Path[0] == '/'))
                return drive + ":/" + host + url.Path;
            if (url.Host is null && url.Path.Length > 0 && (url.Path[0] == '/' || url.Path[0] == '\\'))
                return drive + ":" + url.Path; // OD-0026: C:\... or C:/...
        }

        if (url.Scheme is { } scheme && SwiftString.Lowercased(scheme) == "file" && string.IsNullOrEmpty(url.Host))
        {
            var path = url.Path;
            if (path.Length >= 4 && path[0] == '/' && IsAsciiLetter(path[1]) && path[2] == ':'
                && (path[3] == '/' || path[3] == '\\'))
                return path[1..]; // OD-0026: /C:/... from file:///C:/...
            // review A29: "file:C:/..." (no "//" before the drive letter) is the same drive-letter path with one
            // fewer separator; with no authority to consume, SwiftURL's Path already starts at the drive letter.
            if (path.Length >= 3 && IsAsciiLetter(path[0]) && path[1] == ':' && (path[2] == '/' || path[2] == '\\'))
                return path;
        }

        return null;
    }

    /// <summary>
    /// ED-204: a UNC target is never opened or revealed. <c>\\host\...</c> and <c>file://host/...</c> are the two
    /// shapes OD-0026 names; review A29 adds the shapes Windows treats identically but RFC 3986 does not parse the
    /// same way: <c>//host/...</c> (a doubled forward slash, which .NET's own File.Exists/Directory.Exists resolve
    /// as a UNC path exactly like <c>\\host\...</c>) and a mixed <c>/\host\...</c> or <c>\/host\...</c>, plus the
    /// <c>file:</c> form of a doubled forward slash (<c>file:////host/...</c>, an empty authority followed by a
    /// path that still starts with two separators). A drive-letter path that happens to map to a network share is
    /// not inspected.
    /// </summary>
    internal static bool IsUnc(SwiftURL url)
    {
        if (url.Scheme is null && HasUncPrefix(url.Path)) return true; // \host\..., //host/..., /\host\...
        if (url.Scheme is { } scheme && SwiftString.Lowercased(scheme) == "file")
        {
            if (!string.IsNullOrEmpty(url.Host)) return true; // file://host/...
            if (HasUncPrefix(url.Path)) return true; // file:////host/... (an empty authority, then //host/...)
        }
        return false;
    }

    private static bool HasUncPrefix(string path) => path.Length >= 2 && IsSeparator(path[0]) && IsSeparator(path[1]);

    private static bool IsSeparator(char c) => c is '/' or '\\';

    /// <summary>:234-242: a trailing ":&lt;positive integer&gt;" is stripped when the full path does not exist but
    /// the stripped path does, and is not a directory.</summary>
    internal static string ApplyLineSuffixRule(string path)
    {
        if (File.Exists(path) || Directory.Exists(path)) return path; // :235 fileExists(atPath:)
        var colon = path.LastIndexOf(':'); // :236 lastIndex(of:)
        if (colon < 0) return path;
        if (SwiftNumber.ParseInt(path[(colon + 1)..]) is not { } line || line <= 0) return path; // :237
        var stripped = path[..colon]; // :238
        return File.Exists(stripped) ? stripped : path; // :239-241, exists and is not a directory
    }

    private static bool IsAsciiLetter(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
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
    // ED-210: set by Render(ParagraphText) (the ProseRunView path) in place of textBlock; the text it draws is the
    // view's text storage.
    private ParagraphTextPresenter? presenter;
    private ParagraphText? storage;
    private int renderCount;
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

    // NSTextView.delegate: set to the coordinator by ProseRunView.swift:49 (and LinkParagraphView.swift:28, which this
    // unit's port has not wired); a link click asks it first (NSTextViewDelegate.textView(_:clickedOnLink:at:)).
    public LinkParagraphView.Coordinator? Delegate { get; set; }

    // How many times render(_:) ran, drawn or not (PR-THEME-150 tests).
    internal int RenderCountForTests => renderCount;

    // The text storage render(_:) last set (ED-210).
    internal ParagraphText? TextStorageForTests => storage;

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
        if (head is null || head.NameLength <= 0 || head.Name.Length == 0) return null; // :401, :403, :406 no name run

        var rects = RangeRects(head.NameLocation, head.NameLength);
        if (rects is null) return null;
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
        if (LinkAndRange(@for) is not { } link || RangeRects(link.Range.Location, link.Range.Length) is not { } rects) return false;
        var rect = rects.Length > 0 ? rects[0] : new Rect();
        foreach (var next in rects.Skip(1)) rect = rect.Union(next);
        if (rect.Width <= 0 || rect.Height <= 0) rect = new Rect(@for.X, @for.Y, 1, 1);
        LinkPopover.Shared.Show(link.Url, MergeTarget, rect, this);
        return true;
    }

    /// <summary>:498-507 func height(forWidth width: CGFloat).</summary>
    public double HeightForWidth(double width)
    {
        if (presenter is { } laidOut) // ED-210: the ProseRunView path
        {
            if (width <= 0) return 0;
            if (width == measuredWidth) return measuredHeight;
            measuredHeight = laidOut.HeightForWidth(width);
            measuredWidth = width;
            return measuredHeight;
        }
        var block = textBlock;
        if (width <= 0 || block is null) return 0;
        if (width == measuredWidth) return measuredHeight;
        block.Measure(new Size(width, double.PositiveInfinity));
        measuredWidth = width;
        measuredHeight = Math.Ceiling(block.DesiredSize.Height);
        return measuredHeight;
    }

    // NSLayoutManager.boundingRect(forGlyphRange:in:) over the range's glyphs, one box per line; null with no text.
    private Rect[]? RangeRects(int location, int length)
    {
        if (presenter is { } laidOut) return [.. laidOut.RangeRects(location, length)]; // ED-210
        if (textBlock is not { TextLayout: { } layout }) return null;
        return layout.HitTestTextRange(location, length).ToArray();
    }

    /// <summary>
    /// :481-496 <c>func render(_ text: NSAttributedString)</c>, the path ProseRunView.swift:71 draws through (ED-210):
    /// links wear the theme's accent with no underline (:485-489, set on every render so a theme change recolours
    /// links already on screen); the storage is replaced, and the cached measurement dropped, only when the new
    /// text differs (:490-494); the size is invalidated either way (:495). The text is drawn by one
    /// <see cref="ParagraphTextPresenter"/>, which takes this view's place of the SelectableTextBlock that
    /// <see cref="RenderText"/> (the LinkParagraphView path) draws into.
    /// </summary>
    public void Render(ParagraphText text)
    {
        renderCount++;
        if (presenter is null)
        {
            presenter = new ParagraphTextPresenter { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            // Tunnel: a head or link press is the view's (mouseDown :444-451, clickedOnLink) before the presenter's
            // own press starts a selection.
            presenter.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
            // Tunnel: suppresses the presenter's own ContextMenu (ParagraphTextPresenter's Windows counterpart of
            // AppKit's text menu, :459-461) when the press this release pairs with already showed LinkPopover
            // (:453-456 `rightMouseDown`: presentLinkPopover-or-super, never both). Avalonia opens a control's
            // ContextMenu from a released, still-unhandled right button (Control.OnPointerReleased), a separate
            // event from the press PresentLinkPopover already handled, so it needs its own veto here.
            presenter.AddHandler(PointerReleasedEvent, OnPointerReleasedOverLink, RoutingStrategies.Tunnel);
            presenter.PointerMoved += OnPointerMoved;
            presenter.PointerExited += OnPointerExited;
            if (textBlock is { } old)
            {
                old.PointerPressed -= OnPointerPressed;
                old.PointerMoved -= OnPointerMoved;
                old.PointerExited -= OnPointerExited;
            }
            textBlock = null;
            Child = presenter;
        }
        presenter.LinkColor = Chrome.Chrome.AccentNSColor; // :485-489 linkTextAttributes
        if (storage?.IsEqual(text) != true) // :490
        {
            storage = text; // :491 textStorage?.setAttributedString(text)
            presenter.SetText(text);
            IndexRanges(text);
            measuredHeight = 0; // :492 lastMeasuredHeight = 0
            measuredWidth = 0; // :493
        }
        InvalidateMeasure(); // :495 invalidateIntrinsicContentSize()
    }

    // The .link and .hydraHead runs of the storage, as the lookups at :365-428 find them: a link is the longest
    // effective range of one URL (:423); a head mention the longest effective range of one .hydraHead value
    // (:372-373), whose name is its own text without U+FFFC, trimmed (:388-393), or else the next .hydraHead range
    // after it (:394-406), since the glyph is an attachment run of its own.
    private void IndexRanges(ParagraphText text)
    {
        links.Clear();
        linkHits.Clear();
        heads.Clear();
        textLength = text.Length;
        var linkRanges = new List<(int Location, int Length, SwiftURL Url)>();
        var headRanges = new List<(int Location, int Length, SwiftURL Url)>();
        foreach (var (run, location) in text.RunsWithLocations())
        {
            Merge(linkRanges, run.Link, location, run.Text.Length);
            Merge(headRanges, run.HydraHead, location, run.Text.Length);
        }
        foreach (var (location, length, url) in linkRanges)
        {
            var range = new TextRange(location, length, url);
            links.Add(range);
            linkHits.Add(new LinkHitRange(location, length, range));
        }
        var whole = text.String;
        for (var index = 0; index < headRanges.Count; index++)
        {
            var (location, length, url) = headRanges[index];
            var nameLocation = location;
            var nameLength = length;
            var name = RunName(whole, location, length);
            if (name.Length == 0)
            {
                if (index + 1 < headRanges.Count)
                {
                    (nameLocation, nameLength, _) = headRanges[index + 1];
                    name = RunName(whole, nameLocation, nameLength);
                }
                else
                {
                    nameLength = 0; // :401 no head run after it: no rect
                }
            }
            heads.Add(new HeadRange(location, length, nameLocation, nameLength, url, name));
        }

        static void Merge(List<(int Location, int Length, SwiftURL Url)> ranges, SwiftURL? value, int location, int length)
        {
            if (value is null) return;
            if (ranges.Count > 0 && ranges[^1].Location + ranges[^1].Length == location && ranges[^1].Url == value)
                ranges[^1] = (ranges[^1].Location, ranges[^1].Length + length, value);
            else
                ranges.Add((location, length, value));
        }

        // :388-392 runName: the substring without U+FFFC, trimmed of whitespace and newlines.
        static string RunName(string whole, int location, int length) =>
            FoundationText.Trim(whole.Substring(location, length).Replace(ParagraphText.AttachmentCharacter, "", StringComparison.Ordinal), includeNewlines: true);
    }

    private int? CharacterIndexAt(Point point)
    {
        if (presenter is { } laidOut) return textLength == 0 ? null : laidOut.GlyphIndexAt(point); // ED-210
        var block = textBlock;
        if (block is null || textLength == 0 || block.TextLayout is not { } layout) return null;
        var hit = layout.HitTestPoint(point);
        if (!hit.IsInside || hit.TextPosition < 0 || hit.TextPosition >= textLength) return null;
        return hit.TextPosition;
    }

    // The control the text is drawn by: the paragraph's SelectableTextBlock, or the run's presenter (ED-210).
    private Control? TextHost => (Control?)presenter ?? textBlock;

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (TextHost is not { } block) return;
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
        if (TextHost is { } block) block.Cursor = null;
    }

    private void OnPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (TextHost is not { } block) return;
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

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && LinkAndRange(point) is { } hit)
        {
            // NSTextView asks its delegate first (textView(_:clickedOnLink:at:), whose coordinator opens through
            // MarkdownLinkOpener, :90-100); with no delegate, or one that declines, the link still opens (B3 :337-341,
            // was InlineText.OpenUrl, a pre-existing divergence).
            if (Delegate?.TextView(this, hit.Url, hit.Range.Location) != true) MarkdownLinkOpener.Open(hit.Url);
            e.Handled = true;
        }
    }

    // See the wiring comment at the AddHandler call in Render(_:): a right-button release over a link would
    // otherwise still open the presenter's own ContextMenu (Avalonia's Control.OnPointerReleased opens it from any
    // released, unhandled right button, independently of the press PresentLinkPopover already handled). Checked at
    // the release point, not carried over from the press, the same way LinkAndRange itself is always a fresh
    // lookup; the two agree except across a drag, which :453-461 has no counterpart for either.
    private void OnPointerReleasedOverLink(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Right) return;
        if (LinkAndRange(e.GetPosition(this)) is not null) e.Handled = true;
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

        var runStart = 0;
        foreach (var match in MentionScanner.Matches(value, mentions, persona => persona.Name)) // :197
        {
            var persona = match.Value;
            AddRun(value[runStart..match.LowerBound], font, color, block);

            var glyphSize = SwiftNumber.Rounded(pointSize * 1.25);
            block.Inlines!.Add(new InlineUIContainer
            {
                Child = new Image
                {
                    Source = DragonImageForText(persona, glyphSize),
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
            var nameRun = new Run(persona.Name)
            {
                FontSize = bold.PointSize,
                FontFamily = resolvedBold.FontFamily,
                FontWeight = resolvedBold.FontWeight,
                Foreground = PersonaColorForText(persona).Brush(block),
            };
            block.Inlines!.Add(nameRun);
            if (targets.TryGetValue(persona.Name, out var target))
                heads.Add(new HeadRange(attachmentLocation, persona.Name.Length + 2, nameLocation,
                    persona.Name.Length, target.Url, persona.Name));
            textLength += persona.Name.Length;
            runStart = match.UpperBound;
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
