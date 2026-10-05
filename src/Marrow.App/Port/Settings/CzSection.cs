// PORT-ONLY CZ-4 (customization-SPEC CZ-3.3, CZ-12.8; DESIGN gen.py SECS()): one section of the Customization page: an optional heading (ChromeSection's) over a card, 20 points of room below it
// (the design's `.czsec` padding, kept inside the fold so a folded section leaves no gap), folding open and shut as the style changes.
using Avalonia;
using Avalonia.Controls;
using Marrow.UI.Chrome;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

internal sealed class CzSection : Decorator
{
    private readonly CzFoldRow fold;

    /// <summary>The heading text, or null for a section with no heading (the Advanced card, Reset, Off).</summary>
    internal string? Title { get; }

    /// <summary>The card's rows.</summary>
    internal CzRowStack Rows { get; }

    /// <param name="title">The heading, or null for none.</param>
    /// <param name="rows">The card's rows.</param>
    /// <param name="open">Whether the section is shown now.</param>
    internal CzSection(string? title, CzRowStack rows, bool open)
    {
        Title = title;
        Rows = rows;
        Control body = title is null
            ? new ChromeCard(() => rows)
            : new ChromeSection(title, () => new ChromeCard(() => rows));
        fold = new CzFoldRow(new Border { Padding = new Thickness(0, 0, 0, Chrome.SectionSpacing), Child = body }, open);
        Child = fold;
    }

    /// <summary>Whether the section is shown (open) or folded shut.</summary>
    internal bool Open { get => fold.Open; set => fold.Open = value; }

    /// <summary>How far the fold is open now (0 shut, 1 open) and how opaque the section is (CZ-12.8).</summary>
    internal double Fraction => fold.Fraction;
    internal double Fade => fold.Opacity;
}
