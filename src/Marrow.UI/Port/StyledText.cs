// PORT-ONLY: immutable SwiftUI Text values with run-local styles (ED-092 part 3, ED-158).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Marrow.Core.Port;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using AvaloniaRun = Avalonia.Controls.Documents.Run;

// Source: DroppyCode/UI/Common/HydraGlyph.swift (B2 1fbf81e), :226-242.
namespace Marrow.UI.Port;

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
        double? BaselineOffset = null,
        IImage? Image = null,
        bool IsTemplateImage = false,
        // UI/Markdown/MarkdownView.swift:497 `name.link = target.url` (AttributedString.link). ED-158(7):
        // "Links stay with the MarkdownView unit" -- added here by that unit. Carries the URL only; there is
        // no B2 caller that reads it back from a rendered Text run, so AppendTo below applies only the colour
        // survival ED-158(7) names (the explicit foreground colour set after the link, so it is not
        // recoloured the way a native text view would recolour an unstyled link) and leaves opening the link
        // to the environment the way the rest of the app already opens a URL (ThreadTimeline precedent).
        SwiftURL? Link = null);

    private static readonly ConditionalWeakTable<TextBlock, Dictionary<SwiftColor, IBrush>> BrushCache = new();
    private readonly ReadOnlyCollection<Run> runs;

    // :229-230 Text(verbatim: persona.name); the string is retained without interpretation.
    public StyledText(string verbatim) : this([new Run(verbatim)])
    {
    }

    // UI/Markdown/MarkdownView.swift:391-403, :491 Text(Image).
    public StyledText(IImage image, bool isTemplate) : this([new Run("", Image: image, IsTemplateImage: isTemplate)])
    {
    }

    private StyledText(Run[] runs) => this.runs = Array.AsReadOnly(runs);

    // A value made of the given runs, as Text + Text would be.
    public static StyledText FromRuns(IEnumerable<Run> runs) => new(runs.ToArray());

    // ED-092(3): callers can inspect but cannot mutate the sequence or any of its run values.
    public IReadOnlyList<Run> Runs => runs;

    // App/Views/Chat/TimelineRows.swift:454: Text + Text retains the independent styles.
    public static StyledText operator +(StyledText left, StyledText right) =>
        new([.. left.runs, .. right.runs]);

    // :242; App/Views/Chat/TimelineRows.swift:455 applies the font to the concatenated value.
    public StyledText Font(SwiftFont font) => Map(run => run.Font is null ? run with { Font = font } : run);

    // :230
    public StyledText ForegroundColor(SwiftColor color) => Map(run => run.Color is null ? run with { Color = color } : run);

    // UI/Markdown/MarkdownView.swift:502; UI/Markdown/LinkPopover.swift:80.
    public StyledText FontWeight(SwiftFont.Weight weight) => Map(run => run.Weight is null ? run with { Weight = weight } : run);

    // ED-092(3) explicitly includes italic, underline and strikethrough in the Text value.
    public StyledText Italic() => Map(run => run.IsItalic is null ? run with { IsItalic = true } : run);

    // ED-092(3).
    public StyledText Underline() => Map(run => run.IsUnderlined is null ? run with { IsUnderlined = true } : run);

    // ED-092(3).
    public StyledText Strikethrough() => Map(run => run.IsStrikethrough is null ? run with { IsStrikethrough = true } : run);

    // UI/Markdown/MarkdownView.swift:512-514.
    public StyledText MonospacedDigit() => Map(run => run.HasMonospacedDigits is null ? run with { HasMonospacedDigits = true } : run);

    // UI/Markdown/MarkdownView.swift:386,493; ED-092(3) includes numeric baseline offsets on runs.
    public StyledText BaselineOffset(double offset) => Map(run => run.BaselineOffset is null ? run with { BaselineOffset = offset } : run);

    // UI/Markdown/MarkdownView.swift:497 `name.link = target.url`. ED-158(7); see the Run.Link field above.
    public StyledText Link(SwiftURL url) => Map(run => run.Link is null ? run with { Link = url } : run);

    // ED-158: replace the host's content while preserving one inline for every stored run.
    public void Render(TextBlock host)
    {
        host.Inlines ??= new InlineCollection();
        host.Inlines.Clear();
        AppendTo(host.Inlines, host);
    }

    // UI/Markdown/StreamVeil.swift:295-300, :326-331 interleaves styled values with its own prose.
    public void AppendTo(InlineCollection inlines, TextBlock host)
    {
        foreach (var run in runs)
        {
            if (run.Image is { } image)
            {
                var child = ImageControl(image, run);
                var baseline = image.Size.Height + (run.BaselineOffset ?? 0);
                TextBlock.SetBaselineOffset(child, baseline == 0 ? 1e-6 : baseline);
                inlines.Add(new InlineUIContainer { Child = child });
                continue;
            }

            if (run.BaselineOffset is { } offset && offset != 0)
                throw new NotSupportedException("StyledText: a numeric baseline offset on a text run has no B2 caller (ED-158)");

            var inline = new AvaloniaRun(run.Text);
            ApplyTextStyle(inline, run, host);
            inlines.Add(inline);
        }
    }

    private static Control ImageControl(IImage image, Run run)
    {
        var size = image.Size;
        if (!run.IsTemplateImage)
            return new Image { Source = image, Width = size.Width, Height = size.Height };

        IBrush mask = image switch
        {
            IImageBrushSource source => new ImageBrush(source) { Stretch = Stretch.Fill },
            DrawingImage { Drawing: { } drawing } => new DrawingBrush(drawing),
            _ => throw new NotSupportedException("StyledText: template image source is unsupported (ED-158)"),
        };
        var border = new Border { Width = size.Width, Height = size.Height, OpacityMask = mask };
        if (run.Color is { } color) TextElement.SetForeground(border, color.Brush(border));
        border.Bind(Border.BackgroundProperty, border.GetObservable(TextElement.ForegroundProperty));
        return border;
    }

    private static void ApplyTextStyle(AvaloniaRun inline, Run run, TextBlock host)
    {
        if (run.Font is { } font)
        {
            if (run.Weight is { } weight) font = font.FontWeight(weight);
            if (run.IsItalic == true) font = font.Italic();
            if (run.HasMonospacedDigits == true) font = font.MonospacedDigit();
            var resolved = font.Resolve();
            inline.FontFamily = resolved.FontFamily;
            inline.FontSize = resolved.FontSize;
            inline.FontWeight = resolved.FontWeight;
            inline.FontStyle = resolved.FontStyle;
            inline.FontFeatures = resolved.FontFeatures;
        }
        else
        {
            if (run.Weight is { } weight) inline.FontWeight = SwiftFont.System(12, weight: weight).Resolve().FontWeight;
            if (run.IsItalic == true) inline.FontStyle = FontStyle.Italic;
            if (run.HasMonospacedDigits == true)
                inline.FontFeatures = new FontFeatureCollection { new FontFeature { Tag = "tnum", Value = 1 } };
        }

        if (run.Color is { } color)
        {
            var cache = BrushCache.GetValue(host, _ => new Dictionary<SwiftColor, IBrush>());
            if (!cache.TryGetValue(color, out var brush)) cache[color] = brush = color.Brush(host);
            inline.Foreground = brush;
        }

        if (run.IsUnderlined == true || run.IsStrikethrough == true)
        {
            var decorations = new TextDecorationCollection();
            if (run.IsUnderlined == true) decorations.Add(new TextDecoration { Location = TextDecorationLocation.Underline });
            if (run.IsStrikethrough == true) decorations.Add(new TextDecoration { Location = TextDecorationLocation.Strikethrough });
            inline.TextDecorations = decorations;
        }
    }

    // ED-092 packet: modifiers produce a new value and apply to every run of the receiver.
    private StyledText Map(Func<Run, Run> modifier) => new(runs.Select(modifier).ToArray());
}
