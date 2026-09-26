// PORT-ONLY: the NSMutableAttributedString / NSMutableParagraphStyle / NSTextAttachment subset that
// UI/Markdown/LinkParagraphView.swift's attributed(source:...) and UI/Markdown/ProseRunView.swift's
// attributed(blocks:...) build (ED-XXX, the ProseRunView representation decision). Nothing else in B3 sets a paragraph
// style (grep NSMutableParagraphStyle: ProseRunView.swift:131, :155, :180 only).
using System.Text;
using Avalonia.Media;
using Marrow.Core.Port;

namespace Marrow.UI.Port;

/// <summary>
/// <c>NSParagraphStyle</c>, only the properties ProseRunView.swift sets (:132-134, :156-159, :181-185). A fresh
/// <c>NSMutableParagraphStyle()</c> is <see cref="Default"/>: every indent and spacing 0, AppKit's twelve left tab
/// stops 28 points apart and a default tab interval of 0 (NSParagraphStyle.default, platform knowledge, UNVERIFIED
/// under OD-0001 like ED-051's label literals).
/// </summary>
public sealed record ParagraphStyle
{
    /// <summary><c>firstLineHeadIndent</c>: the first line's distance from the container's leading edge.</summary>
    public double FirstLineHeadIndent { get; init; }

    /// <summary><c>headIndent</c>: every later line's distance from the container's leading edge.</summary>
    public double HeadIndent { get; init; }

    /// <summary><c>paragraphSpacing</c>: space after the paragraph's last line.</summary>
    public double ParagraphSpacing { get; init; }

    /// <summary><c>paragraphSpacingBefore</c>: space above the paragraph's first line.</summary>
    public double ParagraphSpacingBefore { get; init; }

    /// <summary><c>tabStops</c>: left-aligned <c>NSTextTab</c> locations, container coordinates, ascending.</summary>
    public ValueList<double> TabStops { get; init; } = DefaultTabStops;

    /// <summary><c>defaultTabInterval</c>: past the last tab stop, tabs land on multiples of this (0: none).</summary>
    public double DefaultTabInterval { get; init; }

    private static readonly ValueList<double> DefaultTabStops =
        new(Enumerable.Range(1, 12).Select(index => index * 28.0));

    /// <summary><c>NSParagraphStyle.default</c>, which is also what <c>NSMutableParagraphStyle()</c> starts as.</summary>
    public static ParagraphStyle Default { get; } = new();
}

/// <summary><c>NSTextAttachment</c> with its <c>image</c> and <c>bounds</c>: the bounds' y is measured from the
/// baseline, positive upwards (LinkParagraphView.swift:139, :199).</summary>
public sealed record ParagraphTextAttachment(IImage? Image, double X, double Y, double Width, double Height);

/// <summary>One attribute run: its UTF-16 text and the attributes it carries. An attachment run is the single
/// character U+FFFC, as <c>NSAttributedString(attachment:)</c> stores it.</summary>
public sealed record ParagraphTextRun(
    string Text,
    SwiftFont? Font = null,
    SwiftColor? ForegroundColor = null,
    SwiftURL? Link = null,
    SwiftURL? HydraHead = null,
    ParagraphTextAttachment? Attachment = null,
    ParagraphStyle? Style = null);

/// <summary>
/// <c>NSMutableAttributedString</c> with the attributes <c>.font</c>, <c>.foregroundColor</c>, <c>.link</c>,
/// <c>.hydraHead</c> (LinkParagraphView.swift:248), <c>.attachment</c> and <c>.paragraphStyle</c>. Indices and
/// lengths are UTF-16 code units, as NSString's are (R-STR-1).
/// </summary>
public sealed class ParagraphText
{
    /// <summary>U+FFFC OBJECT REPLACEMENT CHARACTER, the character an attachment occupies.</summary>
    public const string AttachmentCharacter = "\U0000FFFC"; // U+FFFC OBJECT REPLACEMENT CHARACTER

    private readonly List<ParagraphTextRun> runs = [];
    private readonly StringBuilder text = new();

    public IReadOnlyList<ParagraphTextRun> Runs => runs;

    /// <summary><c>length</c>.</summary>
    public int Length => text.Length;

    /// <summary><c>string</c>.</summary>
    public string String => text.ToString();

    /// <summary><c>append(NSAttributedString(string:attributes:))</c>. An empty string appends nothing.</summary>
    public void Append(string value, SwiftFont? font = null, SwiftColor? foregroundColor = null, SwiftURL? link = null,
        SwiftURL? hydraHead = null)
    {
        if (value.Length == 0) return;
        Add(new ParagraphTextRun(value, font, foregroundColor, link, hydraHead));
    }

    /// <summary><c>append(NSAttributedString(attachment:))</c>, with the attributes added over it.</summary>
    public void Append(ParagraphTextAttachment attachment, SwiftURL? hydraHead = null) =>
        Add(new ParagraphTextRun(AttachmentCharacter, HydraHead: hydraHead, Attachment: attachment));

    /// <summary><c>append(_ attrString: NSAttributedString)</c>.</summary>
    public void Append(ParagraphText other)
    {
        foreach (var run in other.runs) Add(run);
    }

    /// <summary><c>addAttribute(.paragraphStyle, value:, range:)</c>.</summary>
    public void AddParagraphStyle(ParagraphStyle style, int location, int length) =>
        Change(location, length, run => run with { Style = style });

    /// <summary><c>addAttribute(.font, value:, range:)</c>.</summary>
    public void AddFont(SwiftFont font, int location, int length) =>
        Change(location, length, run => run with { Font = font });

    /// <summary><c>enumerateAttribute(.font, in: whole range, options: [])</c>: each run's font (null where there is
    /// none) and range, in order. The list is taken before the caller changes anything.</summary>
    public IReadOnlyList<(SwiftFont? Font, int Location, int Length)> FontRuns()
    {
        var result = new List<(SwiftFont?, int, int)>();
        var location = 0;
        foreach (var run in runs)
        {
            if (result.Count > 0 && Equals(result[^1].Item1, run.Font))
            {
                var last = result[^1];
                result[^1] = (last.Item1, last.Item2, last.Item3 + run.Text.Length);
            }
            else
            {
                result.Add((run.Font, location, run.Text.Length));
            }
            location += run.Text.Length;
        }
        return result;
    }

    /// <summary>The run holding the character at <paramref name="index"/> (<c>attributes(at:effectiveRange:)</c>).
    /// Out of range traps, as NSAttributedString raises.</summary>
    public ParagraphTextRun RunAt(int index)
    {
        if (index < 0 || index >= text.Length) throw new ArgumentOutOfRangeException(nameof(index));
        var location = 0;
        foreach (var run in runs)
        {
            if (index < location + run.Text.Length) return run;
            location += run.Text.Length;
        }
        throw new ArgumentOutOfRangeException(nameof(index));
    }

    /// <summary>The runs with their start offsets.</summary>
    public IEnumerable<(ParagraphTextRun Run, int Location)> RunsWithLocations()
    {
        var location = 0;
        foreach (var run in runs)
        {
            yield return (run, location);
            location += run.Text.Length;
        }
    }

    /// <summary><c>isEqual(to:)</c>: the same characters with the same attributes at every index.</summary>
    public bool IsEqual(ParagraphText? other)
    {
        if (other is null || other.Length != Length || !string.Equals(other.String, String, StringComparison.Ordinal))
            return false;
        // Walk both run lists together: every stretch of characters must carry equal attributes on both sides,
        // however each side happens to split its runs.
        int mine = 0, theirs = 0, mineOffset = 0, theirsOffset = 0;
        while (mine < runs.Count && theirs < other.runs.Count)
        {
            if (runs[mine] with { Text = "" } != other.runs[theirs] with { Text = "" }) return false;
            var mineLeft = runs[mine].Text.Length - mineOffset;
            var theirsLeft = other.runs[theirs].Text.Length - theirsOffset;
            var step = Math.Min(mineLeft, theirsLeft);
            mineOffset += step;
            theirsOffset += step;
            if (mineOffset == runs[mine].Text.Length) { mine++; mineOffset = 0; }
            if (theirsOffset == other.runs[theirs].Text.Length) { theirs++; theirsOffset = 0; }
        }
        return true;
    }

    private void Add(ParagraphTextRun run)
    {
        runs.Add(run);
        text.Append(run.Text);
    }

    // Applies a change to every character in [location, location + length), splitting runs at the edges.
    private void Change(int location, int length, Func<ParagraphTextRun, ParagraphTextRun> change)
    {
        if (location < 0 || length < 0 || location + length > text.Length) throw new ArgumentOutOfRangeException(nameof(length));
        if (length == 0) return;
        var end = location + length;
        var result = new List<ParagraphTextRun>(runs.Count + 2);
        var start = 0;
        foreach (var run in runs)
        {
            var runEnd = start + run.Text.Length;
            if (runEnd <= location || start >= end)
            {
                result.Add(run);
            }
            else
            {
                var from = Math.Max(location, start) - start;
                var to = Math.Min(end, runEnd) - start;
                if (from > 0) result.Add(run with { Text = run.Text[..from] });
                result.Add(change(run with { Text = run.Text[from..to] }));
                if (to < run.Text.Length) result.Add(run with { Text = run.Text[to..] });
            }
            start = runEnd;
        }
        runs.Clear();
        runs.AddRange(result);
    }
}
