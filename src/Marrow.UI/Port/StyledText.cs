// PORT-ONLY: immutable SwiftUI Text values with run-local styles (ED-092 part 3).
using System.Collections.ObjectModel;

// Source: DroppyCode/UI/Common/HydraGlyph.swift (B2 1fbf81e), :226-242.
namespace Marrow.UI.Port;

// Partial foundation: values and modifiers only. Render(TextBlock) is not implemented.
// ED-092 requires numeric baseline offsets and the packet requires one Avalonia Run per run.
// Avalonia 12.1.2 exposes only BaselineAlignment on Run; its BuildTextRun hook is internal,
// and Inline.CreateTextRunProperties is nonvirtual. A rendering approach needs a recorded ruling.
public sealed class StyledText
{
    // ED-092(3): all styles are optional, preserving the difference between an absent trait and a value.
    public sealed record Run(
        string Text,
        SwiftFont? Font = null,
        SwiftColor? Color = null,
        SwiftFont.Weight? Weight = null,
        bool? IsItalic = null,
        bool? IsUnderlined = null,
        bool? IsStrikethrough = null,
        bool? HasMonospacedDigits = null,
        double? BaselineOffset = null);

    private readonly ReadOnlyCollection<Run> runs;

    // :229-230 Text(verbatim: persona.name); the string is retained without interpretation.
    public StyledText(string verbatim) : this([new Run(verbatim)])
    {
    }

    private StyledText(Run[] runs) => this.runs = Array.AsReadOnly(runs);

    // ED-092(3): callers can inspect but cannot mutate the sequence or any of its run values.
    public IReadOnlyList<Run> Runs => runs;

    // App/Views/Chat/TimelineRows.swift:454: Text + Text retains the independent styles.
    public static StyledText operator +(StyledText left, StyledText right) =>
        new([.. left.runs, .. right.runs]);

    // :242; App/Views/Chat/TimelineRows.swift:455 applies the font to the concatenated value.
    public StyledText Font(SwiftFont font) => Map(run => run with { Font = font });

    // :230
    public StyledText ForegroundColor(SwiftColor color) => Map(run => run with { Color = color });

    // UI/Markdown/MarkdownView.swift:502; UI/Markdown/LinkPopover.swift:80.
    public StyledText FontWeight(SwiftFont.Weight weight) => Map(run => run with { Weight = weight });

    // ED-092(3) explicitly includes italic, underline and strikethrough in the Text value.
    public StyledText Italic() => Map(run => run with { IsItalic = true });

    // ED-092(3).
    public StyledText Underline() => Map(run => run with { IsUnderlined = true });

    // ED-092(3).
    public StyledText Strikethrough() => Map(run => run with { IsStrikethrough = true });

    // UI/Markdown/MarkdownView.swift:512-514.
    public StyledText MonospacedDigit() => Map(run => run with { HasMonospacedDigits = true });

    // UI/Markdown/MarkdownView.swift:386,493; ED-092(3) includes numeric baseline offsets on runs.
    public StyledText BaselineOffset(double offset) => Map(run => run with { BaselineOffset = offset });

    // ED-092 packet: modifiers produce a new value and apply to every run of the receiver.
    private StyledText Map(Func<Run, Run> modifier) => new(runs.Select(modifier).ToArray());
}
