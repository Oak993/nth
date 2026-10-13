// PORT-ONLY: the terminal grid's paint, kept per row (ED-NEW-perf-motion-1). A frame's rows are immutable arrays shared between snapshots
// (ED-NEW-lean-10), so a row's paint is built once, when its array first shows, and drawn again until it leaves the screen: an output frame
// rebuilds only the rows it changed and a repaint shapes nothing. The paint is the per-cell drawing it replaces, merged: a background per run of
// cells with one brush, one glyph run per run of cells with one face and ink (each glyph placed on its own column, as SwiftTerm draws a line of
// runs), one underline per run; a cell whose glyph is not a single glyph inside its cell (wide, combining, fallback, a box-drawing stroke to the
// edge) keeps its own clipped text, as before.
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;

namespace Marrow.App.Views.Terminal.Port;

internal sealed class TerminalPainter
{
    private const int GlyphCap = 4096;
    private const int BrushCap = 512;
    // Glyph ink must stay this far inside its cell to be drawn without the cell's clip (an antialiased or ClearType edge spreads under a pixel).
    private const double InkMargin = 0.5;

    private Size cell;
    private FontFamily? family;
    private FontStyle style;
    private FontWeight weight;
    private double size;
    private IBrush? foreground;
    private IBrush? background;
    private Typeface regular;
    private Typeface bold;
    private Dictionary<TerminalCell[], Row> rows = new(ReferenceEqualityComparer.Instance);
    private Dictionary<TerminalCell[], Row> drawn = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(string Text, bool Bold), Glyph?> glyphs = [];
    private readonly Dictionary<Color, IBrush> brushes = [];
    private readonly Dictionary<IBrush, IPen> pens = new(ReferenceEqualityComparer.Instance);

    /// <summary>Rows whose paint was built (for tests).</summary>
    internal int RowsBuilt { get; private set; }

    /// <summary>Distinct cell texts shaped (for tests).</summary>
    internal int TextsShaped { get; private set; }

    /// <summary>Merged glyph runs and clipped single cells built (for tests).</summary>
    internal int GlyphRunsBuilt { get; private set; }

    internal int ClippedCellsBuilt { get; private set; }

    /// <summary>Row paints held (for tests): at most the rows drawn by the last frame.</summary>
    internal int RowsHeld => rows.Count;

    /// <summary>The cell size, font and default colours the paint is for; a change drops every row's paint.</summary>
    public void Configure(Size cellSize, FontFamily fontFamily, FontStyle fontStyle, FontWeight fontWeight, double fontSize,
        IBrush defaultForeground, IBrush? defaultBackground)
    {
        if (cellSize == cell && Equals(fontFamily, family) && fontStyle == style && fontWeight == weight && fontSize.Equals(size)
            && ReferenceEquals(defaultForeground, foreground) && ReferenceEquals(defaultBackground, background)) return;
        Drop(rows);
        Drop(drawn);
        glyphs.Clear();
        cell = cellSize;
        family = fontFamily;
        style = fontStyle;
        weight = fontWeight;
        size = fontSize;
        foreground = defaultForeground;
        background = defaultBackground;
        regular = new Typeface(fontFamily, fontStyle, fontWeight);
        bold = new Typeface(fontFamily, fontStyle, FontWeight.Bold);
    }

    /// <summary>Draws one row at the context's origin. The selection is the row's text range [from, to) in the row's own text indices.</summary>
    public void DrawRow(DrawingContext context, TerminalCell[] cells, IBrush? selectionBrush, int from, int to)
    {
        if (!drawn.TryGetValue(cells, out var row))
        {
            if (rows.Remove(cells, out var kept)) row = kept;
            else row = Build(cells);
            drawn[cells] = row;
        }
        foreach (var (brush, rect) in row.Fills) context.FillRectangle(brush, rect);
        if (selectionBrush is not null && row.Selected(from, to) is { } selected)
            context.FillRectangle(selectionBrush, selected);
        foreach (var (brush, run) in row.Runs) context.DrawGlyphRun(brush, run);
        foreach (var (layout, clip) in row.Clipped)
        {
            using var _ = context.PushClip(clip);
            layout.Draw(context, clip.Position);
        }
        foreach (var (pen, start, end) in row.Lines) context.DrawLine(pen, start, end);
    }

    /// <summary>Ends a frame: the paint of every row the frame did not draw is released.</summary>
    public void EndFrame()
    {
        Drop(rows);
        (rows, drawn) = (drawn, rows);
    }

    private static void Drop(Dictionary<TerminalCell[], Row> held)
    {
        foreach (var row in held.Values) row.Dispose();
        held.Clear();
    }

    private IBrush BrushOf(Color color)
    {
        if (brushes.TryGetValue(color, out var known)) return known;
        if (brushes.Count >= BrushCap) brushes.Clear();
        return brushes[color] = new ImmutableSolidColorBrush(color);
    }

    private IPen PenOf(IBrush brush)
    {
        if (pens.TryGetValue(brush, out var known)) return known;
        if (pens.Count >= BrushCap) pens.Clear();
        return pens[brush] = new Pen(brush, 1);
    }

    private Row Build(TerminalCell[] cells)
    {
        RowsBuilt++;
        var row = new Row(cells.Length, cell.Height);
        IBrush? fillBrush = null;
        double fillFrom = 0, fillTo = 0;
        IBrush? lineBrush = null;
        double lineFrom = 0, lineTo = 0;
        var run = new RunBuilder();
        var textIndex = 0;
        for (var column = 0; column < cells.Length; column++)
        {
            var c = cells[column];
            if (c.IsContinuation) continue;
            var left = column * cell.Width;
            var right = left + Math.Max(1, c.Width) * cell.Width;
            var text = c.Text ?? " ";
            row.AddCell(textIndex, text.Length, left, right);
            textIndex += text.Length;
            IBrush fg = c.Foreground is { } fgColor ? BrushOf(fgColor) : foreground!;
            IBrush? bg = c.Background is { } bgColor ? BrushOf(bgColor) : background;
            if (c.Inverse) (fg, bg) = (bg ?? Brushes.Black, fg);

            if (bg is not null && ReferenceEquals(bg, fillBrush) && fillTo == left) fillTo = right;
            else
            {
                if (fillBrush is not null) row.Fills.Add((fillBrush, new Rect(fillFrom, 0, fillTo - fillFrom, cell.Height)));
                (fillBrush, fillFrom, fillTo) = (bg, left, right);
            }

            if (c.Text is not { Length: > 0 })
            {
                FlushRun(row, ref run);
                FlushLine(row, ref lineBrush, lineFrom, lineTo);
                continue;
            }
            if (c.Width <= 1 && GlyphOf(c.Text, c.Bold) is { } glyph)
            {
                if (!run.Continues(glyph, fg, left)) FlushRun(row, ref run);
                run.Add(glyph, c.Text, fg, left, cell.Width);
            }
            else
            {
                FlushRun(row, ref run);
                var layout = new TextLayout(c.Text, c.Bold ? bold : regular, size, fg);
                row.Clipped.Add((layout, new Rect(left, 0, right - left, cell.Height)));
                ClippedCellsBuilt++;
            }
            if (c.Underline)
            {
                if (ReferenceEquals(lineBrush, fg) && lineTo == left) lineTo = right;
                else
                {
                    FlushLine(row, ref lineBrush, lineFrom, lineTo);
                    (lineBrush, lineFrom, lineTo) = (fg, left, right);
                }
            }
            else FlushLine(row, ref lineBrush, lineFrom, lineTo);
        }
        if (fillBrush is not null) row.Fills.Add((fillBrush, new Rect(fillFrom, 0, fillTo - fillFrom, cell.Height)));
        FlushRun(row, ref run);
        FlushLine(row, ref lineBrush, lineFrom, lineTo);
        return row;
    }

    private void FlushRun(Row row, ref RunBuilder run)
    {
        if (run.Take() is not { } built) return;
        row.Runs.Add(built);
        GlyphRunsBuilt++;
    }

    private void FlushLine(Row row, ref IBrush? brush, double from, double to)
    {
        if (brush is null) return;
        // As each cell drew it: a one-point line one point above the cell's bottom.
        row.Lines.Add((PenOf(brush), new Point(from, cell.Height - 1), new Point(to, cell.Height - 1)));
        brush = null;
    }

    // The cell's text as the single glyph the per-cell FormattedText drew, where it drew it; null when it is anything else (more than one glyph,
    // a placement the merged run could not give, ink reaching the cell's edge), which keeps the cell's own clipped text.
    private Glyph? GlyphOf(string text, bool isBold)
    {
        if (glyphs.TryGetValue((text, isBold), out var known)) return known;
        if (glyphs.Count >= GlyphCap) glyphs.Clear();
        TextsShaped++;
        var typeface = isBold ? bold : regular;
        Glyph? glyph = null;
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, null);
        using (var layout = new TextLayout(text, typeface, size, null))
        {
            if (layout.TextLines is [var line] && line.Start == 0 && line.Baseline == formatted.Baseline && OnlyShaped(line) is { } shaped
                && shaped.ShapedBuffer.Length == 1 && shaped.GlyphRun.BaselineOrigin.X == 0)
            {
                var info = shaped.ShapedBuffer[0];
                var face = shaped.GlyphRun.GlyphTypeface;
                var em = shaped.GlyphRun.FontRenderingEmSize;
                using var probe = new GlyphRun(face, em, text.AsMemory(), [new GlyphInfo(info.GlyphIndex, 0, cell.Width, info.GlyphOffset)],
                    new Point(0, line.Baseline));
                var ink = probe.InkBounds;
                if (ink.Width <= 0 || ink.Height <= 0
                    || (ink.Left >= InkMargin && ink.Top >= InkMargin && ink.Right <= cell.Width - InkMargin && ink.Bottom <= cell.Height - InkMargin))
                    glyph = new Glyph(face, em, info.GlyphIndex, info.GlyphOffset, line.Baseline);
            }
        }
        glyphs[(text, isBold)] = glyph;
        return glyph;
    }

    private static ShapedTextRun? OnlyShaped(TextLine line)
    {
        ShapedTextRun? only = null;
        foreach (var run in line.TextRuns)
        {
            if (run is not DrawableTextRun) continue;
            if (only is not null || run is not ShapedTextRun shaped) return null;
            only = shaped;
        }
        return only;
    }

    private readonly record struct Glyph(GlyphTypeface Face, double EmSize, ushort Index, Vector Offset, double Baseline);

    // A run of glyphs of one face and one ink on consecutive cells: one glyph run whose advances are the cell width.
    private struct RunBuilder
    {
        private List<GlyphInfo>? infos;
        private System.Text.StringBuilder? characters;
        private Glyph first;
        private IBrush? brush;
        private double start;
        private double next;

        public readonly bool Continues(Glyph glyph, IBrush ink, double left) =>
            infos is { Count: > 0 } && ReferenceEquals(ink, brush) && next == left && ReferenceEquals(glyph.Face, first.Face)
            && glyph.EmSize.Equals(first.EmSize) && glyph.Baseline.Equals(first.Baseline);

        public void Add(Glyph glyph, string text, IBrush ink, double left, double advance)
        {
            if (infos is not { Count: > 0 })
            {
                infos = [];
                characters = new System.Text.StringBuilder();
                first = glyph;
                brush = ink;
                start = left;
            }
            infos.Add(new GlyphInfo(glyph.Index, characters!.Length, advance, glyph.Offset));
            characters.Append(text);
            next = left + advance;
        }

        public (IBrush, GlyphRun)? Take()
        {
            if (infos is not { Count: > 0 } || brush is null) return null;
            var run = new GlyphRun(first.Face, first.EmSize, characters!.ToString().AsMemory(), infos.ToArray(), new Point(start, first.Baseline));
            infos = null;
            characters = null;
            var ink = brush;
            brush = null;
            return (ink, run);
        }
    }

    private sealed class Row(int columns, double height) : IDisposable
    {
        private readonly int[] textStart = new int[columns];
        private readonly int[] textLength = new int[columns];
        private readonly double[] left = new double[columns];
        private readonly double[] right = new double[columns];
        private int count;

        public List<(IBrush Brush, Rect Rect)> Fills { get; } = [];
        public List<(IBrush Brush, GlyphRun Run)> Runs { get; } = [];
        public List<(TextLayout Layout, Rect Clip)> Clipped { get; } = [];
        public List<(IPen Pen, Point Start, Point End)> Lines { get; } = [];

        public void AddCell(int start, int length, double from, double to)
        {
            textStart[count] = start;
            textLength[count] = length;
            left[count] = from;
            right[count] = to;
            count++;
        }

        // The cells whose text overlaps [from, to), as the per-cell test chose them (they are consecutive), as one rectangle.
        public Rect? Selected(int from, int to)
        {
            int firstCell = -1, lastCell = -1;
            for (var i = 0; i < count; i++)
            {
                if (textStart[i] < to && textStart[i] + textLength[i] > from)
                {
                    if (firstCell < 0) firstCell = i;
                    lastCell = i;
                }
                else if (firstCell >= 0) break;
            }
            if (firstCell < 0) return null;
            return new Rect(left[firstCell], 0, right[lastCell] - left[firstCell], height);
        }

        public void Dispose()
        {
            foreach (var (_, run) in Runs) run.Dispose();
            foreach (var (layout, _) in Clipped) layout.Dispose();
        }
    }
}
