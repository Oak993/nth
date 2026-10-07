// PORT-ONLY: a minimal stand-in for Foundation's AttributedString, sufficient for what
// UI/Markdown/MarkdownView.swift's autolinker layer needs (ED-092 (3) leaves AttributedString with the units
// that use it; ED-158 (7): "Links stay with the MarkdownView unit"). This type is NOT a general attributed-text
// port: it carries only what PR-THEME-127/128/129 require -- plain, code and link runs in source order,
// preserving every character between runs, including whitespace and newlines. Converting inline styling to a
// renderable StyledText (bold/italic weights, colours, favicons, pretty link titles) is left to the members
// implemented in MarkdownView.cs (InlineText.Attributed, RichLink.PrettyAttributed/Build).
using System.Text;
using Marrow.Core.Port;

namespace Marrow.UI.Port;

/// <summary>One run of an <see cref="AttributedText"/>: its text and whether it is already an inline code span
/// or a link, which is what MarkdownView.swift:772-826's autolinker (<c>autolinkedBareDomains</c>) checks before
/// scanning a run for a bare domain. <see cref="Link"/> is <see cref="SwiftURL"/> (F.URL), the same type
/// RichLink's other members (IsBareDisplay, PrettyTitle) take, so a link run built here needs no re-parse.</summary>
public sealed record AttributedRun(string Text, bool IsCode = false, SwiftURL? Link = null, bool IsBold = false, bool IsItalic = false);

/// <summary>An ordered sequence of <see cref="AttributedRun"/>s. Concatenating every run's <see cref="AttributedRun.Text"/>
/// reproduces the source exactly (MarkdownView.swift:828-834's whitespace-preserving parse).</summary>
public sealed class AttributedText
{
    private readonly IReadOnlyList<AttributedRun> runs;

    public AttributedText(IEnumerable<AttributedRun> runs) => this.runs = [.. runs];

    public static AttributedText Empty { get; } = new([]);

    public static AttributedText Plain(string text) => text.Length == 0 ? Empty : new([new AttributedRun(text)]);

    public IReadOnlyList<AttributedRun> Runs => runs;

    public bool IsEmpty => runs.Count == 0;

    public string PlainText
    {
        get
        {
            var builder = new StringBuilder();
            foreach (var run in runs) builder.Append(run.Text);
            return builder.ToString();
        }
    }
}
