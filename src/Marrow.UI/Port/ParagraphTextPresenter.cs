// PORT-ONLY: the NSTextView / NSLayoutManager half of ProseRunView's one attributed string (ED-210, the ProseRunView
// representation decision): one ParagraphText laid out with its per-paragraph styles, drawn as one text object with
// one selection and one character index space. Avalonia's TextLayout takes one TextParagraphProperties for all of
// its text, so it cannot hang a paragraph, space paragraphs apart or place a tab at a stop; this control formats
// each line itself with TextFormatter.FormatLine, at the x and width the paragraph style gives that line.
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Marrow.Core.Port;

namespace Marrow.UI.Port;

/// <summary>
/// Lays out and draws a <see cref="ParagraphText"/> the way an NSTextView with a zero container inset and zero line
/// fragment padding does (LinkParagraphView.swift:299-332): paragraphs end at U+000A, U+000D, CRLF, U+0085 and U+2029
/// and take the paragraph style of their first character (the "\n" ProseRunView.swift:201-208 inserts carries none
/// and ends the paragraph before it); a paragraph's first line starts at <c>firstLineHeadIndent</c>, its later lines at
/// <c>headIndent</c>; <c>paragraphSpacingBefore</c> goes above its first line and <c>paragraphSpacing</c> below its
/// last (the Swift's literal paragraph style, every paragraph alike; whether TextKit drops either at the edges of
/// the text is UNVERIFIED under OD-0001, oracle O-71, porting/IMPLEMENTING.md); a tab moves the pen to the first tab stop right of it, else to the next
/// multiple of <c>defaultTabInterval</c>, both measured from the container's leading edge (NSTextTab), and with
/// neither it does not move the pen (UNVERIFIED); an attachment is a box of its bounds on the baseline.
/// </summary>
public sealed class ParagraphTextPresenter : Control
{
    /// <summary>The selection highlight: the same property as SelectableTextBlock's, which draws a LinkParagraphView
    /// paragraph, but not its default (SelectableTextBlock.SelectionBrushProperty's own AddOwner default is null in
    /// Avalonia 12.1.2 with no Fluent/Simple theme loaded to override it; the constructor gives this control its
    /// own, below).</summary>
    public static readonly StyledProperty<IBrush?> SelectionBrushProperty =
        SelectableTextBlock.SelectionBrushProperty.AddOwner<ParagraphTextPresenter>();

    private sealed record LineBox(TextLine Line, double X, double Top, int Start, int End, int ParagraphStart, int ParagraphEnd);

    private readonly List<LineBox> lines = [];
    private readonly ConditionalWeakTable<SwiftColor, IBrush> brushes = new();
    private double laidOutWidth = double.NaN;
    private double laidOutHeight;
    private int anchor;
    private int active;
    private bool selecting;

    public ParagraphTextPresenter()
    {
        Focusable = true;
        // The colours' brushes follow the theme (SwiftColor.Brush); the laid-out lines draw with them, so a theme
        // change only needs a redraw (the adaptive NSColors of LinkParagraphView.swift:121, :485-489).
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        // SelectableTextBlock.SelectionBrushProperty's own default is null (Avalonia 12.1.2, no Fluent/Simple theme
        // loaded to override it), so with nothing set here a selection dragged across this control never painted:
        // Ctrl+C copied it, GlyphIndexAt still hit-tested it, but Render's `SelectionBrush is { } highlight` guard
        // (below) always failed. NSTextView never has that gap: AppKit always resolves selectedTextAttributes'
        // backgroundColor default (SwiftColor.SelectedTextBackgroundColor). Set once here rather than through the
        // styled-property default so the brush is the presenter's own theme-following one (SwiftColor.Brush(this),
        // the same helper the run text uses below).
        SelectionBrush = Brush(SwiftColor.SelectedTextBackgroundColor);
        // LinkParagraphView.swift:453-461: a right-click (or control-click) that misses a link falls through to
        // `super.menu(for:)` / `super.rightMouseDown(with:)`, AppKit's own text menu. This is the Windows
        // counterpart the presenter draws for itself (LinkTextView.PresentLinkPopover, :523-531, still wins first
        // when the click is on a link; see its own citation for how the two never both show).
        ContextMenu = BuildContextMenu();
    }

    private ContextMenu BuildContextMenu()
    {
        var copy = new MenuItem { Header = "Copy" };
        copy.Click += (_, _) => Copy();
        var selectAll = new MenuItem { Header = "Select All" };
        selectAll.Click += (_, _) => SelectAll();
        return new ContextMenu { Items = { copy, selectAll } };
    }

    // :275-332, :453-461: a run's automation exposure. The port's own paragraph path (a SelectableTextBlock) gets
    // Avalonia's TextBlockAutomationPeer for free (Owner.Inlines?.Text ?? Owner.Text, AutomationControlType.Text);
    // this hand-rolled Control has no such default (Control.OnCreateAutomationPeer returns NoneAutomationPeer,
    // IsControlElement/IsContentElement both false), so Narrator never reads a finished message's prose.
    protected override AutomationPeer OnCreateAutomationPeer() => new TextPeer(this);

    private sealed class TextPeer(ParagraphTextPresenter owner) : ControlAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

        protected override string? GetNameCore() => owner.Text.String;

        protected override bool IsControlElementCore() => true;

        protected override bool IsContentElementCore() => true;
    }

    /// <summary>The text storage's contents.</summary>
    public ParagraphText Text { get; private set; } = new();

    /// <summary><c>linkTextAttributes[.foregroundColor]</c> (LinkParagraphView.swift:485-489): the colour a run
    /// carrying <c>.link</c> is drawn in, whatever its own foreground; no underline.</summary>
    public SwiftColor? LinkColor { get; set; }

    public IBrush? SelectionBrush
    {
        get => GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    public int SelectionStart => Math.Min(anchor, active);

    public int SelectionEnd => Math.Max(anchor, active);

    /// <summary>The selected characters of the one string, paragraph separators and U+FFFC included.</summary>
    public string SelectedText => Text.String.Substring(SelectionStart, SelectionEnd - SelectionStart);

    /// <summary><c>textStorage.setAttributedString(_:)</c>: new contents, the selection collapsed to the start.</summary>
    public void SetText(ParagraphText text)
    {
        Text = text;
        anchor = 0;
        active = 0;
        ClearLines();
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>Selects [start, end) of the string (NSTextView.setSelectedRange).</summary>
    public void Select(int start, int end)
    {
        anchor = Math.Clamp(start, 0, Text.Length);
        active = Math.Clamp(end, 0, Text.Length);
        InvalidateVisual();
    }

    /// <summary>NSText.selectAll(_:).</summary>
    public void SelectAll() => Select(0, Text.Length);

    /// <summary>NSText.copy(_:): the selected characters as plain text; nothing when the selection is empty.</summary>
    public void Copy()
    {
        if (SelectionEnd == SelectionStart) return;
        _ = TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(SelectedText);
    }

    /// <summary>Lays the text out at <paramref name="width"/> and returns the used height, unrounded.</summary>
    public double Layout(double width)
    {
        if (width.Equals(laidOutWidth)) return laidOutHeight;
        ClearLines();
        laidOutHeight = Build(width);
        laidOutWidth = width;
        return laidOutHeight;
    }

    /// <summary>LinkParagraphView.swift:498-507 <c>height(forWidth:)</c>: 0 for a width that is not positive, else the
    /// laid-out height rounded up.</summary>
    public double HeightForWidth(double width) => width <= 0 ? 0 : Math.Ceiling(Layout(width));

    /// <summary>NSLayoutManager.glyphIndex(for:in:) checked against the glyph's own box (LinkParagraphView.swift:367-369,
    /// :418-420): the character whose box contains <paramref name="point"/>, or null.</summary>
    public int? GlyphIndexAt(Point point)
    {
        EnsureLayout();
        foreach (var box in lines)
        {
            if (point.Y < box.Top || point.Y >= box.Top + box.Line.Height) continue;
            if (box.End <= box.Start) return null;
            var hit = box.Line.GetCharacterHitFromDistance(point.X - box.X);
            var index = Math.Clamp(hit.FirstCharacterIndex, box.Start, box.End - 1);
            foreach (var rect in RangeRects(index, 1))
                if (rect.Contains(point)) return index;
            return null;
        }
        return null;
    }

    /// <summary>NSLayoutManager.boundingRect(forGlyphRange:in:) per line: the boxes the characters in
    /// [location, location + length) occupy, one or more per line, in this control's coordinates.</summary>
    public IReadOnlyList<Rect> RangeRects(int location, int length)
    {
        EnsureLayout();
        var result = new List<Rect>();
        var end = location + length;
        foreach (var box in lines)
        {
            var from = Math.Max(location, box.Start);
            var to = Math.Min(end, box.End);
            if (to <= from) continue;
            foreach (var bounds in box.Line.GetTextBounds(from, to - from))
            {
                var r = bounds.Rectangle;
                result.Add(new Rect(box.X + r.X, box.Top + r.Y, r.Width, box.Line.Height));
            }
        }
        return result;
    }

    // ---- layout ----

    private void EnsureLayout()
    {
        if (!double.IsNaN(laidOutWidth)) return;
        var width = Bounds.Width > 0 ? Bounds.Width : double.PositiveInfinity;
        Layout(width);
    }

    private void ClearLines()
    {
        foreach (var box in lines) box.Line.Dispose();
        lines.Clear();
        laidOutWidth = double.NaN;
        laidOutHeight = 0;
    }

    private double Build(double width)
    {
        var text = Text.String;
        if (text.Length == 0) return 0;
        var y = 0.0;
        var start = 0;
        while (true)
        {
            var end = start;
            while (end < text.Length && !IsParagraphSeparator(text[end])) end++;
            var separator = end >= text.Length ? 0 : text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 : 1;
            var styleIndex = start < end ? start : end < text.Length ? end : start - 1;
            var run = styleIndex >= 0 && styleIndex < text.Length ? Text.RunAt(styleIndex) : null;
            var style = run?.Style ?? ParagraphStyle.Default;
            y += style.ParagraphSpacingBefore;
            y = FormatParagraph(start, end, style, run, width, y);
            y += style.ParagraphSpacing;
            if (separator == 0) break;
            start = end + separator;
            if (start >= text.Length)
            {
                // A final separator leaves an empty last paragraph (NSLayoutManager's extra line fragment), in the
                // separator's own attributes.
                y = FormatParagraph(start, start, style, run, width, y);
                break;
            }
        }
        return y;
    }

    private double FormatParagraph(int start, int end, ParagraphStyle style, ParagraphTextRun? styleRun, double width, double y)
    {
        var defaults = RunProperties(styleRun);
        var source = new Source(this, end, style);
        TextLineBreak? previous = null;
        var index = start;
        var first = true;
        do
        {
            var x = first ? style.FirstLineHeadIndent : style.HeadIndent;
            var available = double.IsInfinity(width) ? double.PositiveInfinity : Math.Max(0, width - x);
            source.PrepareLine(index, x, defaults);
            // ED-290 (1): a text style's line box, passed explicitly (this control draws no TextBlock); an NSFont has none.
            var properties = new GenericTextParagraphProperties(FlowDirection.LeftToRight, TextAlignment.Left, first, false,
                defaults, TextWrapping.Wrap, styleRun?.Font?.LineBox ?? 0, 0, 0);
            var line = TextFormatter.Current.FormatLine(source, index, available, properties, previous);
            if (line is null) break;
            lines.Add(new LineBox(line, x, y, index, Math.Min(end, index + line.Length), start, end));
            y += line.Height;
            if (line.Length <= 0) break;
            index += line.Length;
            previous = line.TextLineBreak;
            first = false;
        }
        while (index < end);
        return y;
    }

    // U+0085 NEXT LINE, U+2029 PARAGRAPH SEPARATOR
    private static bool IsParagraphSeparator(char c) => c is '\n' or '\r' or (char)0x0085 or (char)0x2029;

    // The tab after the pen position `pen` (container coordinates): NSTextTab stops, then defaultTabInterval.
    private static double TabStop(ParagraphStyle style, double pen)
    {
        foreach (var stop in style.TabStops)
            if (stop > pen) return stop;
        if (style.DefaultTabInterval > 0) return (Math.Floor(pen / style.DefaultTabInterval) + 1) * style.DefaultTabInterval;
        return pen;
    }

    private TextRunProperties RunProperties(ParagraphTextRun? run)
    {
        var font = run?.Font ?? SwiftFont.SystemFont(13);
        var resolved = font.Resolve();
        var color = run?.Link is not null && LinkColor is { } linkColor ? linkColor : run?.ForegroundColor ?? SwiftColor.Black;
        return new GenericTextRunProperties(
            new Typeface(resolved.FontFamily, resolved.FontStyle, resolved.FontWeight),
            resolved.FontSize,
            null,
            Brush(color),
            null,
            BaselineAlignment.Baseline,
            null,
            resolved.FontFeatures);
    }

    private IBrush Brush(SwiftColor color)
    {
        if (brushes.TryGetValue(color, out var brush)) return brush;
        brush = color.Brush(this);
        brushes.AddOrUpdate(color, brush);
        return brush;
    }

    /// <summary>The characters of one paragraph as Avalonia text runs, split at attribute runs, tabs and attachments.</summary>
    private sealed class Source : ITextSource
    {
        private readonly ParagraphTextPresenter owner;
        private readonly int end;
        private readonly ParagraphStyle style;
        private readonly Dictionary<int, double> tabWidths = [];
        private int limit;

        public Source(ParagraphTextPresenter owner, int end, ParagraphStyle style)
        {
            this.owner = owner;
            this.end = end;
            this.style = style;
            limit = end;
        }

        // Before a line is formatted: its start and x are known, so each tab from there on gets the advance it would
        // have if it sat on this line (a tab that wraps to a later line is measured again for that line).
        public void PrepareLine(int lineStart, double x, TextRunProperties lineDefaults)
        {
            tabWidths.Clear();
            var text = owner.Text.String;
            for (var index = lineStart; index < end; index++)
            {
                if (text[index] != '\t') continue;
                limit = index;
                var measuring = new GenericTextParagraphProperties(FlowDirection.LeftToRight, TextAlignment.Left, true, false,
                    lineDefaults, TextWrapping.NoWrap, 0, 0, 0);
                using var line = TextFormatter.Current.FormatLine(this, lineStart, double.PositiveInfinity, measuring, null);
                var pen = x + (index > lineStart && line is not null ? line.WidthIncludingTrailingWhitespace : 0);
                tabWidths[index] = TabStop(style, pen) - pen;
            }
            limit = end;
        }

        public TextRun GetTextRun(int textSourceIndex)
        {
            if (textSourceIndex >= limit)
                return limit == end ? new TextEndOfParagraph() : new TextEndOfLine(1);
            var (run, runStart) = owner.RunContaining(textSourceIndex);
            var runEnd = Math.Min(runStart + run.Text.Length, limit);
            var properties = owner.RunProperties(run);
            if (run.Attachment is { } attachment) return new AttachmentRun(attachment, properties);
            var text = owner.Text.String;
            if (text[textSourceIndex] == '\t')
                return new TabRun(tabWidths.TryGetValue(textSourceIndex, out var width) ? width : 0, properties);
            var stop = textSourceIndex;
            while (stop < runEnd && text[stop] != '\t') stop++;
            return new TextCharacters(text.AsMemory(textSourceIndex, stop - textSourceIndex), properties);
        }
    }

    private (ParagraphTextRun Run, int Location) RunContaining(int index)
    {
        foreach (var (run, location) in Text.RunsWithLocations())
            if (index < location + run.Text.Length) return (run, location);
        throw new ArgumentOutOfRangeException(nameof(index));
    }

    /// <summary>An NSTextAttachment: a box of its bounds, its bottom <c>bounds.y</c> above the baseline.</summary>
    private sealed class AttachmentRun(ParagraphTextAttachment attachment, TextRunProperties properties) : DrawableTextRun
    {
        public override TextRunProperties? Properties => properties;

        public override Size Size => new(attachment.Width, attachment.Height);

        // Avalonia's drawable-run convention (EmbeddedControlRun): minus the distance from the box's top to the baseline.
        public override double Baseline => -(attachment.Height + attachment.Y);

        public override void Draw(DrawingContext drawingContext, Point origin)
        {
            // Drawn by the presenter from the laid-out box (Render), which knows the line's baseline.
        }
    }

    /// <summary>A tab: an empty advance to its stop.</summary>
    private sealed class TabRun(double width, TextRunProperties properties) : DrawableTextRun
    {
        public override TextRunProperties? Properties => properties;

        public override Size Size => new(width, 0);

        public override double Baseline => 0;

        public override void Draw(DrawingContext drawingContext, Point origin)
        {
        }
    }

    // ---- measure and draw ----

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = availableSize.Width;
        var height = Math.Ceiling(Layout(width));
        if (double.IsInfinity(width))
        {
            var natural = 0.0;
            foreach (var box in lines) natural = Math.Max(natural, box.X + box.Line.WidthIncludingTrailingWhitespace);
            return new Size(natural, height);
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Layout(finalSize.Width);
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        EnsureLayout();
        // LinkParagraphView.swift:299-332,444-451: NSTextView takes presses throughout its text area,
        // including the space above a glyph; drawing text alone leaves those points out of Avalonia's hit scene.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (SelectionEnd > SelectionStart && SelectionBrush is { } highlight)
            foreach (var rect in RangeRects(SelectionStart, SelectionEnd - SelectionStart))
                context.FillRectangle(highlight, rect);

        foreach (var box in lines) box.Line.Draw(context, new Point(box.X, box.Top));

        foreach (var (run, location) in Text.RunsWithLocations())
        {
            if (run.Attachment is not { Image: { } image } attachment) continue;
            foreach (var box in lines)
            {
                if (location < box.Start || location >= box.End) continue;
                var rects = RangeRects(location, 1);
                if (rects.Count == 0) break;
                var baseline = box.Top + box.Line.Baseline;
                context.DrawImage(image, new Rect(rects[0].X + attachment.X, baseline - attachment.Height - attachment.Y,
                    attachment.Width, attachment.Height));
                break;
            }
        }
    }

    // ---- selection (NSTextView: press places the caret, drag extends, shift extends, two presses a word, three a
    // paragraph; Ctrl+C copies, Ctrl+A selects all) ----

    private int CaretIndexAt(Point point)
    {
        EnsureLayout();
        if (lines.Count == 0) return 0;
        var box = lines[^1];
        foreach (var candidate in lines)
        {
            if (point.Y < candidate.Top + candidate.Line.Height)
            {
                box = candidate;
                break;
            }
        }
        var hit = box.Line.GetCharacterHitFromDistance(point.X - box.X);
        return Math.Clamp(hit.FirstCharacterIndex + hit.TrailingLength, box.Start, box.End);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.Handled) return;
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;
        Focus();
        var index = CaretIndexAt(point.Position);
        switch (e.ClickCount)
        {
            case 2:
                SelectWord(index);
                break;
            case >= 3:
                SelectParagraph(index);
                break;
            default:
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) active = index;
                else anchor = active = index;
                selecting = true;
                e.Pointer.Capture(this);
                break;
        }
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!selecting) return;
        active = CaretIndexAt(e.GetPosition(this));
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!selecting) return;
        active = CaretIndexAt(e.GetPosition(this));
        selecting = false;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        selecting = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        if (e.Key == Key.C)
        {
            Copy();
            e.Handled = true;
        }
        else if (e.Key == Key.A)
        {
            SelectAll();
            e.Handled = true;
        }
    }

    private void SelectWord(int index)
    {
        var text = Text.String;
        if (text.Length == 0) return;
        var at = Math.Min(index, text.Length - 1);
        if (!IsWordCharacter(text, at) && at > 0 && IsWordCharacter(text, at - 1)) at--;
        if (!IsWordCharacter(text, at))
        {
            Select(at, at + 1);
            return;
        }
        var start = at;
        while (start > 0 && IsWordCharacter(text, start - 1)) start--;
        var end = at + 1;
        while (end < text.Length && IsWordCharacter(text, end)) end++;
        Select(start, end);
    }

    private void SelectParagraph(int index)
    {
        var text = Text.String;
        var start = Math.Min(index, text.Length);
        while (start > 0 && !IsParagraphSeparator(text[start - 1])) start--;
        var end = Math.Min(index, text.Length);
        while (end < text.Length && !IsParagraphSeparator(text[end])) end++;
        if (end < text.Length) end += text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 : 1;
        Select(start, end);
    }

    private static bool IsWordCharacter(string text, int index)
    {
        var character = char.IsSurrogatePair(text, index) ? text.Substring(index, 2) : text[index].ToString();
        return SwiftCharacter.IsLetter(character) || SwiftCharacter.IsNumber(character) || character == "_";
    }
}
