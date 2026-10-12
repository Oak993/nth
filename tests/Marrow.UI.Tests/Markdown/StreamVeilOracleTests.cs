using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;
using static Marrow.UI.Tests.Fixtures.StreamVeilFlowHost;

namespace Marrow.UI.Tests.Markdown;

// ED-NEW-lean-12: the veiled text paints exactly the runs it painted before it kept its Characters between ticks. A frozen copy of the
// painting as it stood on the base (StreamVeil.cs at f8be2e20d, VeiledText.Text, Plain, Styled, SliceStyled: every Character cut and
// asked for its alpha on every tick) is the oracle. Random streams (fixed seed) of plain and segmented texts, with combining accents,
// surrogate pairs and flag halves on run and cut boundaries, bold, italic, code and link runs and fixed segments, are fed to a real
// VeiledText through its place's veil, and after every flush and every frame its text block must hold the runs the oracle builds from a
// veil fed the same texts at the same times: the same texts, colours, opacities and fonts, in the same order.
[Collection("Headless")]
public sealed class StreamVeilOracleTests(HeadlessSession headless)
{
    private const string Notes = "file:///C:/notes.md";
    private static readonly string[] Pieces = ["a", "b", " ", "e", "\u0301", "\U0001F600", "\U0001F1FA", "\U0001F1F8", "\n", "xyz"]; // U+0301; U+1F600; U+1F1FA, U+1F1F8 (flag halves)

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Decision", "ED-NEW-lean-12")]
    public void A_veiled_text_paints_the_runs_the_per_character_painting_painted(bool segmented) => headless.OnUiThread(() =>
    {
        var random = new Random(segmented ? 1207 : 1206);
        var fading = 0;
        for (var stream = 0; stream < 60; stream++) fading += Run(random, segmented, stream);
        Assert.True(fading >= 300, $"frames compared while a chunk dissolved: {fading}"); // the comparison is not of settled text alone
    });

    private static int Run(Random random, bool segmented, int stream)
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var scrollScope = ScrollActivity.Shared.Override(new ScrollActivity());
        StreamVeilLeanTests.FreshVeilClock();
        var place = new MarkdownVeils().Root.Child("b0");
        var panel = new StackPanel();
        var expectedBlock = new SelectableTextBlock(); // in the same window, so colours resolve against the same theme
        StreamVeil.SetMarkdownVeiled(panel, true);
        var oracle = new StreamVeil();

        // The whole text: a plain string, or segments (fixed runs between prose made of styled runs).
        var verbatim = segmented ? "" : Text(random, 40);
        var layout = segmented ? Layout(random) : [];
        var total = segmented ? layout.Sum(segment => segment.Prose?.Sum(run => run.Text.Length) ?? 0) : verbatim.Length;
        var cuts = Cuts(random, total);

        VeiledText? current = null;
        var fading = 0;
        Window? window = null;
        try
        {
            for (var step = 0; step < cuts.Length; step++)
            {
                var length = cuts[step];
                if (!segmented && length > 0 && length < verbatim.Length && char.IsHighSurrogate(verbatim[length - 1])) length--; // never half a pair
                var segments = segmented ? Cut(layout, length) : null;
                var text = segmented ? "" : verbatim[..length];
                runtime.OnMain(() =>
                {
                    current = segmented ? new VeiledText(segments!, place) : new VeiledText(text, place);
                    panel.Children.Clear();
                    panel.Children.Add(current);
                    window ??= Show(new StackPanel { Children = { panel, expectedBlock } });
                    Pump();
                });
                var joined = segmented ? Joined(segments!) : text;
                oracle.Advance(joined, SwiftDate.Now);
                fading += Compare(current!, expectedBlock, oracle, segments, text, $"stream {stream} step {step} flush");
                for (var frame = 0; frame < 2 + random.Next(3); frame++)
                {
                    var milliseconds = 40 + random.Next(80);
                    runtime.AdvanceMilliseconds(milliseconds);
                    runtime.OnMain(() => { frames.Advance(TimeSpan.FromMilliseconds(milliseconds)); Pump(); });
                    oracle.Advance(joined, SwiftDate.Now);
                    fading += Compare(current!, expectedBlock, oracle, segments, text, $"stream {stream} step {step} frame {frame}");
                }
            }
        }
        finally
        {
            for (var i = 0; i < 20; i++)
            {
                runtime.AdvanceMilliseconds(50);
                runtime.OnMain(() => { frames.Advance(TimeSpan.FromMilliseconds(50)); Pump(); });
            }

            window?.Close();
        }

        return fading;
    }

    // The VeiledText's text block against what the base painted for the same veil at the same time (StreamVeil.swift:273-279: fading, the
    // veiled text; settled, the plain one).
    private static int Compare(VeiledText view, SelectableTextBlock host, StreamVeil oracle, IReadOnlyList<VeiledText.Segment>? segments, string text, string where)
    {
        var at = SwiftDate.Now;
        var fading = oracle.IsFading(at);
        var expected = fading ? StreamVeilFrozenPainting.Text(oracle.Spans(at), segments, text) : StreamVeilFrozenPainting.Settled(segments, text);
        expected.Render(host);
        var block = (TextBlock)view.Child!;
        Assert.True(Describe(host) == Describe(block), $"{where}:\nexpected\n{Describe(host)}\nactual\n{Describe(block)}");
        return fading ? 1 : 0;
    }

    private static string Describe(TextBlock block)
    {
        var output = new StringBuilder();
        foreach (var inline in block.Inlines ?? [])
        {
            if (inline is not Run run)
            {
                output.Append(inline.GetType().Name).Append('\n');
                continue;
            }

            // Local values only: the oracle's block stands outside the window, so what a run inherits differs; what the painting sets does not.
            var brush = !run.IsSet(TextElement.ForegroundProperty) ? "-" : run.Foreground switch
            {
                null => "none",
                ISolidColorBrush solid => solid.Color + "@" + solid.Opacity.ToString("R", CultureInfo.InvariantCulture),
                var other => other.GetType().Name,
            };
            output.Append('[').Append(run.Text).Append("] ").Append(brush)
                .Append(' ').Append(Local(run, TextElement.FontWeightProperty)).Append(' ').Append(Local(run, TextElement.FontStyleProperty))
                .Append(' ').Append(Local(run, TextElement.FontSizeProperty)).Append(' ').Append(Local(run, TextElement.FontFamilyProperty))
                .Append(' ').Append(Local(run, TextElement.FontFeaturesProperty)).Append(' ').Append(run.TextDecorations?.Count ?? 0).Append('\n');
        }

        return output.ToString();
    }

    private static string Local(Run run, Avalonia.AvaloniaProperty property) =>
        run.IsSet(property) ? Convert.ToString(run.GetValue(property), CultureInfo.InvariantCulture) ?? "null" : "-";

    private static string Text(Random random, int pieces)
    {
        var builder = new StringBuilder();
        var count = 1 + random.Next(pieces);
        for (var i = 0; i < count; i++) builder.Append(Pieces[random.Next(Pieces.Length)]);
        return builder.ToString();
    }

    private static List<(StyledText? Fixed, AttributedRun[]? Prose)> Layout(Random random)
    {
        var parts = new List<(StyledText? Fixed, AttributedRun[]? Prose)>();
        var count = 1 + random.Next(4);
        for (var i = 0; i < count; i++)
        {
            if (random.Next(3) == 0) parts.Add((new StyledText("[" + i + "]").ForegroundColor(SwiftColor.Secondary), null));
            var runs = new List<AttributedRun>();
            var runCount = 1 + random.Next(5);
            for (var r = 0; r < runCount; r++)
            {
                runs.Add(new AttributedRun(
                    Text(random, 8),
                    IsCode: random.Next(5) == 0,
                    Link: random.Next(6) == 0 ? SwiftURL.Create(Notes) : null,
                    IsBold: random.Next(4) == 0,
                    IsItalic: random.Next(4) == 0));
            }

            parts.Add((null, [.. runs]));
        }

        return parts;
    }

    // Increasing cut lengths, never between the halves of a surrogate pair (checked when cutting), ending with the whole text.
    private static int[] Cuts(Random random, int total)
    {
        var cuts = new List<int>();
        var at = random.Next(Math.Max(1, total / 3) + 1);
        while (at < total)
        {
            cuts.Add(at);
            at += 1 + random.Next(6);
        }

        cuts.Add(total);
        return [.. cuts];
    }

    // The segments with their prose cut to the first `length` UTF-16 units of all the prose (a fixed part comes with the prose after it).
    private static List<VeiledText.Segment> Cut(List<(StyledText? Fixed, AttributedRun[]? Prose)> layout, int length)
    {
        var left = length;
        var segments = new List<VeiledText.Segment>();
        foreach (var part in layout)
        {
            if (part.Fixed is { } fixedText)
            {
                segments.Add(new VeiledText.Segment.Fixed(fixedText));
                continue;
            }

            var kept = new List<AttributedRun>();
            foreach (var run in part.Prose!)
            {
                var take = Math.Clamp(left, 0, run.Text.Length);
                if (take > 0 && take < run.Text.Length && char.IsHighSurrogate(run.Text[take - 1])) take--;
                if (take > 0) kept.Add(run with { Text = run.Text[..take] });
                left -= run.Text.Length;
            }

            segments.Add(new VeiledText.Segment.Prose(new AttributedText(kept)));
        }

        return segments;
    }

    private static string Joined(IReadOnlyList<VeiledText.Segment> segments) =>
        string.Concat(segments.OfType<VeiledText.Segment.Prose>().Select(prose => prose.Value.PlainText));
}
