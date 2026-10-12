using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.UI.Markdown;

namespace Marrow.App.Tests.Views.Chat;

// ED-NEW-lean-11 (lean pass L11), the pixel-equality guard: a scripted 20k character stream (flushes from a few characters to over a thousand, gaps from
// one frame to over a second, then the end of the stream and its rest) drawn by the real row twice on the same manual time and frames: once building the
// whole body for every flush, as the row did before (AssistantMessageRow.StreamsInPlace off), once keeping the settled blocks (on). Every rendered frame,
// at every flush and after every gap, is the same to the pixel, and every block place's arrival is recorded at the same instant.
[Collection("Headless")]
public sealed class TimelineRowsSettledBlocksPixelTests(HeadlessSession headless)
{
    private static (List<string> Frames, List<string> Arrivals, int Kept) Run(bool inPlace)
    {
        using var seam = AssistantMessageRow.StreamsInPlace.Override(() => inPlace);
        var frames = new List<string>();
        var arrivals = new List<string>();
        var kept = 0;
        var reply = SettledBlocksRig.Reply(20_000);
        using var rig = new SettledBlocksRig("Paragraph", height: 9000);
        MarkdownBlockView? first = null;
        foreach (var (text, gap) in SettledBlocksRig.Script(reply))
        {
            rig.Flush(text);
            frames.Add($"flush {text.Length} {rig.Frame()}");
            arrivals.Add($"flush {text.Length} {rig.Arrivals()}");
            var now = rig.Row.GetVisualDescendants().OfType<MarkdownBlockView>().First();
            if (ReferenceEquals(now, first)) kept++;
            first = now;
            rig.Tick(gap);
            frames.Add($"after {text.Length} {rig.Frame()}");
        }
        Assert.True(rig.Row.GetVisualDescendants().OfType<MarkdownView>().Single().Bounds.Height < 9000, "the whole reply is in the frame");
        rig.Flush(reply, streaming: false);
        frames.Add($"end {rig.Frame()}");
        foreach (var rest in new[] { 100, 200, 400 })
        {
            rig.Tick(rest);
            frames.Add($"rest {rest} {rig.Frame()}");
        }
        return (frames, arrivals, kept);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-11")]
    public void Keeping_the_settled_blocks_draws_every_frame_of_a_20k_stream_as_rebuilding_them_did() => headless.OnUiThread(() =>
    {
        var rebuilt = Run(inPlace: false);
        var kept = Run(inPlace: true);
        Assert.Equal(0, rebuilt.Kept); // the old way builds the first block again for every flush
        Assert.True(kept.Kept >= 20, $"the settled first block was kept through {kept.Kept} flushes"); // the new way keeps it once it has rested
        Assert.Equal(rebuilt.Frames.Count, kept.Frames.Count);
        for (var index = 0; index < rebuilt.Frames.Count; index++) Assert.Equal(rebuilt.Frames[index], kept.Frames[index]);
        Assert.Equal(rebuilt.Arrivals, kept.Arrivals);
    });
}
