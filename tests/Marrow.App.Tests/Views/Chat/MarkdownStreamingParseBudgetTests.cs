using Marrow.App.Tests.Fixtures;
using Marrow.UI.Markdown;

namespace Marrow.App.Tests.Views.Chat;

// ED-NEW-lean-11 (lean pass L11): the bytes MarkdownView.StreamingParse allocates for one flush that extends a 100k character reply. It used to encode
// the whole earlier text and the whole new text as UTF-8 to probe 128 of their bytes (Extends, MarkdownView.swift:154-167), and measured each kept
// text's UTF-8 length again for every flush; now it encodes the probes only. What is left is the incremental parse itself, lean unit L13's budget.
// Runs on the UI thread (the parse cache is main-actor state). 10 warm-up flushes, then 5 measured; Debug build.
[Collection("Headless")]
public sealed class MarkdownStreamingParseBudgetTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-NEW-lean-11")]
    public void A_flush_of_a_100k_reply_parses_without_encoding_the_whole_reply_again() => headless.OnUiThread(() =>
    {
        var text = SettledBlocksRig.Reply(100_000) + Guid.NewGuid().ToString("N");
        MarkdownView.StreamingParse(text);
        long bytes = 0;
        for (var flush = 0; flush < 15; flush++)
        {
            text += " more words keep arriving";
            var before = GC.GetAllocatedBytesForCurrentThread();
            MarkdownView.StreamingParse(text);
            var after = GC.GetAllocatedBytesForCurrentThread();
            if (flush >= 10) bytes += after - before;
        }
        bytes /= 5;
        // Measured on the unchanged code (2026-10-12, Debug): 545,793 B per flush.
        Assert.True(bytes <= 820_000, $"StreamingParse at 100k: {bytes} B per flush");
    });
}
