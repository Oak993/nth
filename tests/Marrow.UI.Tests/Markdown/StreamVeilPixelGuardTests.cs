using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;
using static Marrow.UI.Tests.Fixtures.StreamVeilFlowHost;

namespace Marrow.UI.Tests.Markdown;

// ED-NEW-lean-12: the fade-in looks exactly as it did before the veil kept its character arrays between ticks. Three scripted streams, each
// captured at 30 fixed timestamps (ten flushes, each followed by two frames 50 ms apart, so up to four chunks dissolve at once and the
// backlog boost of StreamVeil.swift:130-132 is in play): the veiled texts on their own (StreamVeil, VeiledText's plain and segmented
// paths), a paragraph with links (LinkTextVeil) and a whole markdown reply (MarkdownVeils through the row host). Each is held two ways,
// both recorded on the unchanged code: the SHA-256 of every rendered frame's pixels, and the SHA-256 of every text run's text, colour,
// opacity and font (the same frames without the rasteriser, so a font or driver difference on another machine still has a guard to read).
// The streams carry what the arrays had to get right: surrogate pairs, a combining accent that arrives after its letter (a rewrite of the
// last character) and sits on a run boundary, a flag sequence, bold, italic, code and link runs, and fixed segments between prose ones.
[Collection("Headless")]
public sealed class StreamVeilPixelGuardTests(HeadlessSession headless)
{
    private const string VeiledTextPixels = "D7CF6A855F8D165DD34947089CC5328ED77B6C26A22B160B57951908A6219480";
    private const string VeiledTextRuns = "2890C85995B00006DF54A603410E90B1D31D0272A28D8E85F0A89A9BA2B2D24D";
    private const string LinkPixels = "281170970EED8A30F7CB6B53C3AB72F647A7DDCE5BF267F4540D8A9077647B0E";
    private const string LinkRuns = "14269292E6447A314AF482B3F258687DF75DB96E81E51AE126568BBF0A187056";
    private const string MarkdownPixels = "6DFC81190058C9E340B2812C7CB609AED25A9CB12A4FD3469C4FB5494EED3821";
    private const string MarkdownRuns = "5F2FEF9381D29761F1F6958F9A8C66024F3EAE034EF9AEEA22F36D62D1CE7437";

    private const string Notes = "file:///C:/notes.md"; // links with no host: nothing to fetch a favicon for
    private const string Docs = "file:///C:/docs.md";

    // A rich paragraph as runs: what VeiledText's segmented path slices (StreamVeil.swift:253-265, :309-338).
    private static readonly AttributedRun[] Rich =
    [
        new("Hello "), new("bold", IsBold: true), new(" and "), new("cafe", IsItalic: true), new("\u0301 then "), // U+0301 COMBINING ACUTE ACCENT
        new("x+y", IsCode: true), new(" \U0001F600 flags \U0001F1FA\U0001F1F8\U0001F1EB\U0001F1F7 "), // U+1F600 GRINNING FACE; U+1F1FA/U+1F1F8, U+1F1EB/U+1F1F7 flags
        new("link", Link: SwiftURL.Create(Notes)), new(" end of the line."),
    ];

    private static readonly AttributedRun[] Second = [new("Second "), new("part", IsBold: true), new(" grows on and on.")];

    private const string Verbatim = "func f() {\n  return \"cafe\u0301\" + \U0001F600\n}\n// a trailing comment that keeps growing"; // U+0301; U+1F600

    private const string LinkReply = $"Read [notes]({Notes}) now, **bold** words and *slanted* ones, then [docs]({Docs}) \U0001F600 cafe\u0301 and more words to the end."; // U+1F600; U+0301

    private const string MarkdownReply =
        "# Title grows\n\nHello **world** and *more* text \U0001F600 here.\n\n- one\n- two cafe\u0301\n\n> quoted words go on\n\n```\ncode line\nsecond line\n```\n\nTail paragraph with `code` and words."; // U+1F600; U+0301

    [Fact]
    [Trait("Decision", "ED-NEW-lean-12")]
    public void The_veiled_texts_fade_in_exactly_as_before() => headless.OnUiThread(() =>
    {
        var (pixels, runs) = Capture(VeiledTextStream);
        Assert.True(runs == VeiledTextRuns, $"runs: {runs} (recorded {VeiledTextRuns}); pixels: {pixels}");
        Assert.True(pixels == VeiledTextPixels, $"pixels: {pixels} (recorded {VeiledTextPixels}); runs: {runs}");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-12")]
    public void A_link_paragraph_fades_in_exactly_as_before() => headless.OnUiThread(() =>
    {
        var (pixels, runs) = Capture((runtime, frames, capture) => HostStream(runtime, frames, capture, LinkReply, [40, 52, 60, 70, 81, 90, 104, 112, 113, LinkReply.Length]));
        Assert.True(runs == LinkRuns, $"runs: {runs} (recorded {LinkRuns}); pixels: {pixels}");
        Assert.True(pixels == LinkPixels, $"pixels: {pixels} (recorded {LinkPixels}); runs: {runs}");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-12")]
    public void A_markdown_reply_fades_in_exactly_as_before() => headless.OnUiThread(() =>
    {
        var (pixels, runs) = Capture((runtime, frames, capture) => HostStream(runtime, frames, capture, MarkdownReply, [20, 35, 52, 70, 85, 86, 97, 110, 130, MarkdownReply.Length]));
        Assert.True(runs == MarkdownRuns, $"runs: {runs} (recorded {MarkdownRuns}); pixels: {pixels}");
        Assert.True(pixels == MarkdownPixels, $"pixels: {pixels} (recorded {MarkdownPixels}); runs: {runs}");
    });

    private delegate void Stream(TestRuntime runtime, ManualFrameSource frames, Action<Window, Control> capture);

    // Runs the stream on the manual clock and frame source; every capture hashes the window's rendered frame and the runs under the control.
    private static (string Pixels, string Runs) Capture(Stream stream)
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var scrollScope = ScrollActivity.Shared.Override(new ScrollActivity());
        StreamVeilLeanTests.FreshVeilClock();
        using var pixels = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var runs = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var captures = 0;
        Window? shown = null;
        try
        {
            stream(runtime, frames, (window, root) =>
            {
                shown = window;
                using var frame = window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
                using var buffer = frame.Lock();
                var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
                System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
                pixels.AppendData(bytes);
                runs.AppendData(Encoding.UTF8.GetBytes(Describe(root)));
                captures++;
            });
        }
        finally
        {
            for (var i = 0; i < 40; i++) Tick(runtime, frames, 50);
            shown?.Close();
        }

        Assert.Equal(30, captures);
        return (Convert.ToHexString(pixels.GetHashAndReset()), Convert.ToHexString(runs.GetHashAndReset()));
    }

    private static void Tick(TestRuntime runtime, ManualFrameSource frames, double milliseconds)
    {
        runtime.AdvanceMilliseconds(milliseconds);
        runtime.OnMain(() => { frames.Advance(TimeSpan.FromMilliseconds(milliseconds)); Pump(); });
    }

    // Ten flushes, a capture at each and after each of the two frames that follow it.
    private static void Steps(TestRuntime runtime, ManualFrameSource frames, Action<Window, Control> capture, Window window, Control root, Action<int> flush)
    {
        for (var step = 0; step < 10; step++)
        {
            flush(step);
            capture(window, root);
            Tick(runtime, frames, 50);
            capture(window, root);
            Tick(runtime, frames, 50);
            capture(window, root);
        }
    }

    private static void VeiledTextStream(TestRuntime runtime, ManualFrameSource frames, Action<Window, Control> capture)
    {
        var veils = new MarkdownVeils();
        var plainPlace = veils.Root.Child("b0");
        var richPlace = veils.Root.Child("b1");
        var panel = new StackPanel();
        StreamVeil.SetMarkdownVeiled(panel, true);
        Window window = null!;
        int[] plainCuts = [10, 20, 25, 26, 33, 41, 48, 55, 66, Verbatim.Length]; // 25 ends on the e, 26 brings its accent
        int[] richCuts = [12, 19, 20, 26, 30, 38, 44, 52, 60, Length(Rich)]; // 19 ends on the e, 20 brings its accent from the next run
        int[] secondCuts = [0, 3, 7, 9, 12, 15, 18, 22, 26, Length(Second)];

        void Build(int step)
        {
            panel.Children.Clear();
            panel.Children.Add(new VeiledText(Verbatim[..plainCuts[step]], plainPlace));
            panel.Children.Add(new VeiledText(
            [
                new VeiledText.Segment.Fixed(new StyledText("[A] ").ForegroundColor(SwiftColor.Secondary)),
                new VeiledText.Segment.Prose(Cut(Rich, richCuts[step])),
                new VeiledText.Segment.Fixed(new StyledText(" | ")),
                new VeiledText.Segment.Prose(Cut(Second, secondCuts[step])),
            ], richPlace));
        }

        runtime.OnMain(() => { Build(0); window = Show(panel); });
        Steps(runtime, frames, capture, window, panel, step => runtime.OnMain(() => { if (step > 0) Build(step); Pump(); }));
    }

    private static void HostStream(TestRuntime runtime, ManualFrameSource frames, Action<Window, Control> capture, string reply, int[] cuts)
    {
        var host = new StreamVeilFlowHost(runtime, reply[..cuts[0]]);
        Steps(runtime, frames, capture, host.Window, host.Decorator, step => { if (step > 0) host.Flush(runtime, reply[..cuts[step]]); });
    }

    private static int Length(AttributedRun[] runs) => runs.Sum(run => run.Text.Length);

    // The first `length` UTF-16 units of the runs, never between the halves of a surrogate pair.
    private static AttributedText Cut(AttributedRun[] runs, int length)
    {
        var left = length;
        var kept = new List<AttributedRun>();
        foreach (var run in runs)
        {
            if (left <= 0) break;
            var take = Math.Min(left, run.Text.Length);
            if (take < run.Text.Length && take > 0 && char.IsHighSurrogate(run.Text[take - 1])) take--;
            if (take > 0) kept.Add(run with { Text = run.Text[..take] });
            left -= run.Text.Length;
        }

        return new AttributedText(kept);
    }

    // Every text block under the control in visual order, with every inline's text, colour, opacity and font.
    private static string Describe(Control root)
    {
        var output = new StringBuilder();
        foreach (var block in root.GetVisualDescendants().OfType<TextBlock>())
        {
            output.Append("block|").Append(block.FontSize.ToString(CultureInfo.InvariantCulture)).Append('|').Append(Brush(block.Foreground)).Append('\n');
            if (block.Inlines is not { Count: > 0 } inlines)
            {
                output.Append("text|").Append(block.Text).Append('\n');
                continue;
            }

            foreach (var inline in inlines)
            {
                switch (inline)
                {
                    case Run run:
                        output.Append("run|").Append(run.Text).Append('|').Append(Brush(run.Foreground))
                            .Append('|').Append(run.FontWeight).Append('|').Append(run.FontStyle)
                            .Append('|').Append(run.FontSize.ToString(CultureInfo.InvariantCulture))
                            .Append('|').Append(run.FontFamily?.Name).Append('|').Append(run.TextDecorations?.Count ?? 0).Append('\n');
                        break;
                    case LineBreak:
                        output.Append("break\n");
                        break;
                    default:
                        output.Append(inline.GetType().Name).Append('\n');
                        break;
                }
            }
        }

        return output.ToString();
    }

    private static string Brush(IBrush? brush) => brush switch
    {
        null => "none",
        ISolidColorBrush solid => solid.Color + "@" + solid.Opacity.ToString("R", CultureInfo.InvariantCulture),
        _ => brush.GetType().Name,
    };
}
