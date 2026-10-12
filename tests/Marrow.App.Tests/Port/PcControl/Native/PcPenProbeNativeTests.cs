// PORT-ONLY: Tactus native probe NP-3, the synthetic pen (unit 15, P19; ED-NEW-pcctl-43; DESIGN.md 2.6 R3, 5.4 item 11,
// critique C8). NATIVE: draws one short pen stroke on Paint's canvas with InjectSyntheticPointerInput and records whether
// the stroke moved the cursor (GetCursorPos) or the foreground, whether a topmost window sat over the point, and whether
// the canvas changed. It only decides whether the optional R3 is ever switched on, so the smoke script runs it last.
// Hands off the mouse while it runs. Never run by a regular test run (the class name carries "Native").
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl;

namespace Marrow.App.Tests.Port.PcControl.Native;

[Trait("Decision", "ED-NEW-pcctl-43")]
public sealed class PcPenProbeNativeTests
{
    [Fact]
    public async Task NP3_A_synthetic_pen_stroke_on_Paint_moves_neither_the_cursor_nor_the_foreground()
    {
        using var rig = new PcSmokeRig("np3-pen");
        var handle = await rig.Launch("mspaint.exe", null, (_, title) => title.EndsWith("Paint", StringComparison.Ordinal));
        var window = PcWindowRef.Format(handle);
        await Task.Delay(2_000);
        var desk = await rig.Form("Tactus smoke desk");
        await Task.Delay(500);
        var before = await rig.Call("pc_observe", new { window, screenshot = "yes" });

        var bounds = PcSmokeRig.Bounds(handle);
        var points = Enumerable.Range(0, 12).Select(i => new PcPoint(bounds.X + (int)(bounds.Width * (0.35 + (0.025 * i))), bounds.Y + (int)(bounds.Height * 0.6))).ToList();
        var start = PcSmokeRig.Snapshot();
        var topmost = PcSmokePen.WindowAt(points[0]);
        var device = PcSmokePen.Create();
        var injected = 0;
        var failures = new List<int>();
        var cursorMoved = false;
        var foregroundMoved = false;
        try
        {
            Assert.NotEqual(0, device);
            for (var i = 0; i <= points.Count; i++)
            {
                var flags = i == 0 ? PcSmokePen.Down : i == points.Count ? PcSmokePen.Up : PcSmokePen.Move;
                var error = PcSmokePen.Inject(device, points[Math.Min(i, points.Count - 1)], flags, i == points.Count ? 0u : 512u);
                if (error == 0) injected++;
                else failures.Add(error);
                await Task.Delay(16);
                var now = PcSmokeRig.Snapshot();
                cursorMoved |= now.X != start.X || now.Y != start.Y;
                foregroundMoved |= now.Foreground != start.Foreground;
            }
        }
        finally
        {
            if (device != 0) PcSmokePen.Destroy(device);
        }
        await Task.Delay(500);
        var after = await rig.Call("pc_observe", new { window, screenshot = "yes" });
        var end = PcSmokeRig.Snapshot();

        var changed = before.Image is { } a && after.Image is { } b && !a.Jpeg.AsSpan().SequenceEqual(b.Jpeg);
        if (after.Image is { } shot) File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "tactus-smoke", "np3-after.jpg"), shot.Jpeg);
        rig.Result("injected " + injected + " of " + (points.Count + 1) + (failures.Count > 0 ? " (errors " + string.Join(",", failures.Distinct()) + ")" : "")
            + "; cursor " + (cursorMoved ? "MOVED" : "kept") + ", foreground " + (foregroundMoved ? "CHANGED" : "kept")
            + "; window at the point " + PcWindowRef.Format(topmost) + (PcSmokeRig.ExeOf(topmost) == "mspaint.exe" ? " (Paint)" : " (NOT Paint: covered or topmost)")
            + "; canvas " + (changed ? "changed" : "unchanged") + "; start " + PcSmokeRig.Describe(start) + ", end " + PcSmokeRig.Describe(end)
            + "; R3 may be switched on only if all of: every frame injected, cursor kept, foreground kept, canvas changed");
        PcSmokeRig.Close(desk);
        PcSmokeRig.Close(handle);
    }
}
