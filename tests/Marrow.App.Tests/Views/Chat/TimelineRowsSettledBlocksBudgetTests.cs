using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Markdown;

namespace Marrow.App.Tests.Views.Chat;

// ED-NEW-lean-11 (lean pass L11, RELEASE_PLAN.md "L11. Settled markdown blocks"): what one flush of a streaming reply costs the row, measured at a 10k and a
// 100k character reply. Every flush used to build every block of the reply again, so the cost of a flush grew with the reply and the stream was quadratic.
// The parse of the flush's text is taken out of the measure (MarkdownView.StreamingParse is warmed with the text first): the parser's own cost is lean unit
// L13's budget (MarkdownStreamingParseBudgetTests measures StreamingParse here). Headless and deterministic: manual frame source and time, no wall clock;
// 10 warm-up flushes, then 5 measured; Debug build.
[Collection("Headless")]
public sealed class TimelineRowsSettledBlocksBudgetTests(HeadlessSession headless)
{
    // Bytes allocated and controls created by one flush, averaged over 5 flushes, for a reply of `length` characters.
    private static (long Bytes, int Controls) PerFlush(int length)
    {
        var reply = SettledBlocksRig.Reply(length);
        using var rig = new SettledBlocksRig("Paragraph");
        // The reply arrives in 20 flushes, 50 ms apart, then rests: every veil has settled and every settled block has been quiet for a while.
        for (var step = 1; step <= 20; step++)
        {
            rig.Flush(reply[..(reply.Length * step / 20)]);
            rig.Tick(50);
        }
        rig.Tick(1500);
        var text = reply;
        long bytes = 0;
        var controls = 0;
        for (var flush = 0; flush < 15; flush++)
        {
            text += " more words keep arriving";
            MarkdownView.StreamingParse(text);
            var shown = rig.Shown();
            var before = GC.GetAllocatedBytesForCurrentThread();
            rig.Flush(text);
            var after = GC.GetAllocatedBytesForCurrentThread();
            if (flush >= 10) // after 10 warm-up flushes
            {
                bytes += after - before;
                controls += rig.Row.GetVisualDescendants().Count(visual => !shown.Contains(visual));
            }
            rig.Tick(50);
        }
        return (bytes / 5, controls / 5);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-11")]
    public void A_flush_costs_the_same_at_10k_and_at_100k_characters_of_reply() => headless.OnUiThread(() =>
    {
        var small = PerFlush(10_000);
        var large = PerFlush(100_000);
        var report = $"10k: {small.Bytes} B, {small.Controls} controls; 100k: {large.Bytes} B, {large.Controls} controls per flush";
        // Measured on the unchanged code (2026-10-12, Debug): 10k 11,695,742 B and 457 controls, 100k 112,767,465 B and 4,269 controls per flush.
        Assert.True(small.Bytes <= 17_500_000, report);
        Assert.True(large.Bytes <= 169_000_000, report);
        Assert.True(small.Controls <= 690, report);
        Assert.True(large.Controls <= 6_400, report);
    });
}
