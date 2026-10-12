// PORT-ONLY: Tactus native smoke (unit 15, P19; ED-NEW-pcctl-43; DESIGN.md 5.4 items 4 and 7, 2.2 parallel use, 4.4 the
// stop chord). NATIVE: item 4 borrows the real mouse in Paint (rung R4, real input on); item 7 runs long batches in one
// Notepad while the owner types and moves the mouse in another app, then waits for the owner's Ctrl+Alt+Esc. Runs only
// from scripts/pc-control-smoke.ps1, which tells the owner what to do and when; every regular run excludes it by name.
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;

namespace Marrow.App.Tests.Port.PcControl.Native;

[Trait("Decision", "ED-NEW-pcctl-43")]
public sealed class PcSmokeParallelNativeTests
{
    private const int Lines = 20;

    private static object Batch(string window, int round) => new
    {
        window,
        actions = Enumerable.Range(1, Lines).Select(line => new
        {
            tool = "pc_type",
            target = new { name = "Text editor" },
            text = "round " + round + " line " + line + "\n",
        }).ToArray(),
        observe = "none",
    };

    [Fact]
    public async Task S4_Paint_draws_a_line_with_a_borrowed_drag_and_the_next_screenshot_shows_it()
    {
        // R4 refuses every borrow until NP-4's proofs are imported; this item tests the drag itself, so it assumes them for
        // Paint and says so in its log. Hands off the mouse while it runs.
        using var rig = new PcSmokeRig("s4-paint", PcSettings.Default with { Enabled = true, RealInput = true });
        rig.Prove("mspaint.exe", PcBorrowProofs.Pointer, PcBorrowProofs.Restore);
        var handle = await rig.Launch("mspaint.exe", null, (_, title) => title.EndsWith("Paint", StringComparison.Ordinal));
        var window = PcWindowRef.Format(handle);
        await Task.Delay(2_000);

        var before = await rig.Call("pc_observe", new { window, screenshot = "yes" });
        Assert.True(before.Ok, before.Render());
        Assert.NotNull(before.Image);
        var (width, height) = (before.Image.Width, before.Image.Height);
        var path = new[] { new[] { width * 0.35, height * 0.55 }, new[] { width * 0.5, height * 0.6 }, new[] { width * 0.65, height * 0.55 } };
        var drag = await rig.Call("pc_drag", new { window, obs = before.Image.ObsId, path });
        var after = await rig.Call("pc_observe", new { window, screenshot = "yes" });

        var folder = Path.Combine(Path.GetTempPath(), "tactus-smoke");
        File.WriteAllBytes(Path.Combine(folder, "s4-before.jpg"), before.Image.Jpeg);
        if (after.Image is { } shot) File.WriteAllBytes(Path.Combine(folder, "s4-after.jpg"), shot.Jpeg);
        rig.Result("drag " + (drag.Error?.ToString() ?? drag.Status.ToString()) + "; before " + before.Image.Jpeg.Length + " bytes, after "
            + (after.Image?.Jpeg.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none") + " bytes; eye check s4-after.jpg for the line");
        Assert.True(drag.Ok, drag.Render());
        Assert.NotNull(after.Image);
        Assert.False(after.Image.Jpeg.AsSpan().SequenceEqual(before.Image.Jpeg));
        PcSmokeRig.Close(handle);
    }

    [Fact]
    public async Task S7_Long_batches_finish_untouched_while_the_owner_types_and_presses_Esc_elsewhere()
    {
        // The script asks the owner to type, move the mouse and press Esc in the desk window for the whole run.
        using var rig = new PcSmokeRig("s7-parallel");
        var handle = await rig.Launch("notepad.exe", null, (cls, _) => cls == "Notepad");
        var desk = await rig.Form("Tactus smoke desk", "$t = New-Object System.Windows.Forms.TextBox; $t.Multiline = $true; $t.Dock = 'Fill'; $f.Controls.Add($t);");
        var window = PcWindowRef.Format(handle);
        rig.Result("click into the \"Tactus smoke desk\" window, then type, move the mouse and press Esc there for 45 s");
        var start = PcSmokeRig.Snapshot();

        var rounds = 0;
        var deadline = Environment.TickCount64 + 45_000;
        while (Environment.TickCount64 < deadline)
        {
            rounds++;
            var batch = await rig.Call("pc_batch", Batch(window, rounds), timeoutSeconds: 120);
            Assert.True(batch.Ok, "round " + rounds + ": " + batch.Render());
        }
        var read = await rig.Call("pc_read", new { window, target = new { name = "Text editor" }, length = 20000 });
        var end = PcSmokeRig.Snapshot();

        var missing = Enumerable.Range(1, rounds).SelectMany(round => Enumerable.Range(1, Lines).Select(line => "round " + round + " line " + line))
            .Count(text => !(read.Body ?? "").Contains(text, StringComparison.Ordinal));
        rig.Result(rounds + " batches of " + Lines + " lines, " + missing + " lines missing; foreground at start " + PcSmokeRig.Describe(start) + ", at end " + PcSmokeRig.Describe(end) + "; owner: check your typing in the second Notepad is whole");
        Assert.True(read.Ok, read.Render());
        Assert.Equal(0, missing);
        PcSmokeRig.Close(desk);
    }

    [Fact]
    public async Task S7_The_stop_chord_stops_a_running_batch()
    {
        using var rig = new PcSmokeRig("s7-stop");
        var handle = await rig.Launch("notepad.exe", null, (cls, _) => cls == "Notepad");
        var window = PcWindowRef.Format(handle);
        rig.Result("press Ctrl+Alt+Esc now (within 90 s)");

        PcResult? stopped = null;
        var rounds = 0;
        var deadline = Environment.TickCount64 + 90_000;
        while (stopped is null && Environment.TickCount64 < deadline)
        {
            rounds++;
            var batch = await rig.Call("pc_batch", Batch(window, rounds), timeoutSeconds: 120);
            if (batch.Error == PcErrorCode.StoppedByUser || (batch.Body ?? "").Contains("stopped_by_user", StringComparison.Ordinal)) stopped = batch;
            else Assert.True(batch.Ok, "round " + rounds + ": " + batch.Render());
        }
        var after = stopped is null ? null : await rig.Call("pc_observe", new { window, screenshot = "no" });

        rig.Result(stopped is null ? "no stop seen in 90 s after " + rounds + " batches" : "stopped in batch " + rounds + "; the next call: " + (after!.Error?.ToString() ?? after.Status.ToString()));
        Assert.NotNull(stopped);
        PcSmokeRig.Close(handle);
    }
}
