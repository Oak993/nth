using Marrow.UI.Port.Nth.Beads;
using SkiaSharp;
using Xunit;
using static Marrow.UI.Port.Nth.Beads.NthBeadsGeometry;

namespace Marrow.UI.Tests.Nth;

// Calculi WU3 (ED-NEW-calculi-wu3-1; DESIGN.md section 11): once warm, a frame allocates nothing managed. 10,000 Update + Frame +
// Paint calls over a live turn (steps and state changes included), each also painting an overlapping merged pair with a neck and a
// Scriptio stroke, so the bead layer, the neck path and the cut path are all on the measured path.
public sealed class NthBeadsAllocationTests
{
    [Fact]
    [Trait("Decision", "ED-NEW-calcmerge-2")] // ED-NEW-calculi-wu3-1
    public void Ten_thousand_update_frame_and_paint_calls_allocate_nothing_after_warm_up()
    {
        using var painter = new NthBeadsPainter();
        using var surface = SKSurface.Create(new SKImageInfo(80, 90));
        var canvas = surface.Canvas;
        var engine = new NthBeadsEngine(NthBeadsEngine.SeedFrom("alloc-turn"));
        engine.SetState(NthBeadsState.Thinking, 0);
        engine.Enter(NthBeadsEntrance.Scriptio, 0);

        var merged = new NthBeadsSample { Present = true, Glint = -1, B0 = new(-.2, .3, 1, 1), B1 = new(.2, .3, .9, 1), B2 = new(.6, .3, 1, 1) };
        var stroke = new NthBeadsSample { Present = true, Hidden = true, Glint = -1, StrokeCount = 3,
            Stroke0From = .2, Stroke0To = SLeft, Stroke1From = SLeft + .3, Stroke1To = SCrown + .4, Stroke2From = SRight, Stroke2To = SRight };
        var states = new[] { NthBeadsState.Tool, NthBeadsState.Streaming, NthBeadsState.Thinking, NthBeadsState.Tactus };
        var t = 0.0;

        void Run(int from, int to)
        {
            for (var i = from; i < to; i++)
            {
                t = i / 60.0;
                if (i % 150 == 75) engine.Step(t);
                if (i % 600 == 300) engine.SetState(states[i / 600 % states.Length], t);
                engine.Update(t);
                var sample = engine.Frame(t);
                var dark = (i & 1) == 0;
                canvas.Clear(SKColors.Transparent);
                painter.Paint(canvas, sample, 18, dark);
                painter.Paint(canvas, merged, 32, dark);
                painter.Paint(canvas, stroke, 18, dark);
                if (i % 7 == 0) painter.Paint(canvas, merged, 12, dark, SKColors.Yellow);
            }
        }

        Run(0, 2400); // warm: the entrance, several motifs and hand-offs, both themes, every paint path once
        int layers = painter.Layers, necks = painter.Necks;
        var before = GC.GetAllocatedBytesForCurrentThread();
        Run(2400, 12400);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(painter.Layers - layers >= 10000, "the merged pair took the bead layer every frame");
        Assert.True(painter.Necks - necks >= 10000, "and drew its necks");
        // ED-NEW-calculi-wu2-1 item 4: the counter can jump by part of an 8 KB allocation quantum once per run with nothing allocated.
        // A real allocation repeats, so a run that shows bytes is replayed (the next 10,000 frames, twice) and only bytes on every
        // replay fail.
        for (var replay = 0; allocated != 0 && replay < 2; replay++)
        {
            var from = 12400 + replay * 10000;
            before = GC.GetAllocatedBytesForCurrentThread();
            Run(from, from + 10000);
            allocated = Math.Min(allocated, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        Assert.Equal(0, allocated);
        Assert.Equal(NthBeadsMode.Running, engine.Mode);
    }
}
