using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.VisualTree;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Markdown;

// ED-NEW-lean-12: the budget for one tick of the stream veil's fade-in. A streaming paragraph keeps dissolving its newest words for
// about 0.40 s after every flush (StreamVeil.swift:40-44), and every veil-clock tick in that time repaints the paragraph. What a tick
// costs must not grow with how long the paragraph already is: only the tail that is still dissolving changes between ticks.
// Headless and deterministic (R-TEST-5): the manual clock, the manual frame source, no wall clock, no sleeps. A tick is what the
// frame source and the main loop do for one frame: the veil's own work and the repaint of the text block's inlines. Avalonia's text
// layout of the paragraph (the next layout pass) is not part of it and runs outside the measured window.
// Allocation figures are Debug builds (the heavy slot's build), bytes per tick after two warm-up flushes.
[Collection("Headless")]
public sealed class StreamVeilLeanTests(HeadlessSession headless)
{
    // Budgets per kind of paragraph and length, about 1.3x the cost after the fix (both numbers are in ED-NEW-lean-12). Before it, a tick
    // cut and compared the whole paragraph's Characters, rebuilt every run and swapped every inline of a SelectableTextBlock (which rebuilds
    // its whole text twice per inline added or removed): about five times as much at 4,000 characters as at 500.
    private const double Growth = 2.0; // a 4,000-character paragraph's tick may cost at most twice a 500-character one's
    private const long ProseShort = 34_200; // base 221,894; after the fix 26,315
    private const long ProseLong = 34_200; // base 1,258,184; after the fix 26,315
    private const long LinkShort = 26_100; // base 71,074; after the fix 20,040
    private const long LinkLong = 26_100; // base 351,074; after the fix 20,040
    private const long CodeShort = 34_100; // base 97,191; after the fix 26,235
    private const long CodeLong = 34_100; // base 510,191; after the fix 26,235

    private const string Local = "file:///C:/notes.md"; // a link with no host: nothing to fetch a favicon for
    private const double Frame = 34; // a little over 1/30 s: every frame is a veil-clock tick (StreamVeil.swift:174-181, preferred 30)

    [Theory]
    [InlineData("prose")]
    [InlineData("link")]
    [InlineData("code")]
    [Trait("Decision", "ED-NEW-lean-12")]
    public void A_veil_tick_costs_the_same_for_a_long_paragraph_as_for_a_short_one(string kind) => headless.OnUiThread(() =>
    {
        var (shortLimit, longLimit) = kind switch
        {
            "prose" => (ProseShort, ProseLong),
            "link" => (LinkShort, LinkLong),
            _ => (CodeShort, CodeLong),
        };
        var small = BytesPerTick(kind, 500);
        var large = BytesPerTick(kind, 4000);
        Assert.True(small <= shortLimit, $"{kind}: bytes per veil tick at 500 characters: {small} (limit {shortLimit}); at 4,000: {large}");
        Assert.True(large <= longLimit, $"{kind}: bytes per veil tick at 4,000 characters: {large} (limit {longLimit}); at 500: {small}");
        Assert.True(large <= small * Growth, $"{kind}: a veil tick at 4,000 characters costs {large} bytes, more than {Growth}x the {small} at 500");
    });

    /// <summary>A streaming reply of one paragraph (prose, a paragraph with a link, or a code block) about <paramref name="length"/> characters
    /// long, flushed with a few more words every ten frames; bytes per tick over the eight frames after the first two of each flush.</summary>
    internal static long BytesPerTick(string kind, int length)
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        using var scrollScope = ScrollActivity.Shared.Override(new ScrollActivity());
        FreshVeilClock();
        var body = Words(length);
        string Reply(int flush)
        {
            var text = body + string.Concat(Enumerable.Repeat(" more", flush));
            return kind switch
            {
                "link" => $"Read [notes]({Local}) {text}",
                "code" => $"```\n{text}\n```\n",
                _ => text,
            };
        }

        var host = new StreamVeilFlowHost(runtime, Reply(0));
        Action advance = () => frames.Advance(TimeSpan.FromMilliseconds(Frame));
        try
        {
            long bytes = 0;
            var ticks = 0;
            for (var flush = 1; flush <= 6; flush++)
            {
                host.Flush(runtime, Reply(flush));
                for (var frame = 0; frame < 10; frame++)
                {
                    runtime.AdvanceMilliseconds(Frame);
                    var before = GC.GetAllocatedBytesForCurrentThread();
                    runtime.OnMain(advance); // the veil clock's tick runs on the main actor (StreamVeil.swift:183-193), then what it posted
                    var spent = GC.GetAllocatedBytesForCurrentThread() - before;
                    if (flush > 2 && frame >= 2) { bytes += spent; ticks++; }
                    StreamVeilFlowHost.Pump(); // the layout and render passes, outside the measured window
                }

                // The tail still dissolves through the measured frames: the paragraph carries a run painted below full opacity.
                Assert.Contains(Inlines(host.Decorator), opacity => opacity < 0.999);
            }

            return bytes / ticks;
        }
        finally
        {
            for (var i = 0; i < 40; i++)
            {
                runtime.AdvanceMilliseconds(Frame);
                runtime.OnMain(() => { frames.Advance(TimeSpan.FromMilliseconds(Frame)); StreamVeilFlowHost.Pump(); });
            }

            host.Window.Close();
        }
    }

    // The opacity every text run under the control is painted at (1 for a run with no brush of its own).
    private static double[] Inlines(Control root) =>
        root.GetVisualDescendants().OfType<TextBlock>()
            .SelectMany(block => block.Inlines?.OfType<Run>() ?? [])
            .Where(run => !string.IsNullOrEmpty(run.Text))
            .Select(run => run.Foreground is Avalonia.Media.ISolidColorBrush brush ? brush.Opacity * brush.Color.A / 255.0 : 1.0)
            .ToArray();

    // Plain words, no markdown, exactly `length` characters.
    private static string Words(int length)
    {
        string[] words = ["lorem", "ipsum", "dolor", "sit", "amet", "consectetur", "adipiscing", "elit", "sed", "do", "eiusmod", "tempor"];
        var builder = new System.Text.StringBuilder();
        for (var i = 0; builder.Length < length; i++) builder.Append(i == 0 ? "" : " ").Append(words[i % words.Length]);
        builder.Length = length;
        return builder.ToString().TrimEnd();
    }

    // VeilClock.shared belongs to the whole process (StreamVeil.swift:164): each measurement starts it afresh, as StreamVeilFlowTests does.
    internal static void FreshVeilClock()
    {
        foreach (var name in new[] { "link", "deadline" })
            typeof(VeilClock).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(VeilClock.Shared, null);
        typeof(VeilClock).GetField("now", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(VeilClock.Shared, DateTimeOffset.MinValue);
    }
}
