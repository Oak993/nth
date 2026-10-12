// PORT-ONLY: Tactus native probe NP-12, the quiet peek (unit 15, P19; ED-NEW-pcctl-43; DESIGN.md 2.10, 5.4 item 11,
// critique C18). NATIVE: runs the peek of DESIGN.md 2.10 by hand on minimized Notepad (opened here) and on Outlook, Excel
// and Chrome when the owner opened them first: save the placement, restore without activation, at once to the bottom of
// the Z-order, one WGC grab, minimize again without activation, put the exact placement back. It records whether the
// foreground or the order of the other windows changed, the grab, and the time; the owner watches the screen and the
// taskbar (each peek is announced 3 s ahead) and the smoke script asks what they saw. Only exes that pass may later peek
// without asking. Runs only from the smoke script.
using System.Diagnostics;
using Marrow.App.Port.PcControl;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;

namespace Marrow.App.Tests.Port.PcControl.Native;

[Trait("Decision", "ED-NEW-pcctl-43")]
public sealed class PcPeekProbeNativeTests
{
    [Fact]
    public async Task NP12_A_quiet_peek_of_each_minimized_app_is_recorded_for_the_owner_to_judge()
    {
        using var rig = new PcSmokeRig("np12-peek");
        using var capture = new WgcWindowCapture();
        var targets = new List<(string Name, nint Handle)> { ("Notepad", await rig.Launch("notepad.exe", null, (cls, _) => cls == "Notepad")) };
        foreach (var (name, exe) in new[] { ("Outlook (classic)", "outlook.exe"), ("Outlook (new)", "olk.exe"), ("Excel", "excel.exe"), ("Chrome", "chrome.exe") })
        {
            if (PcSmokeRig.WindowsOf(exe) is [var first, ..]) targets.Add((name, first.Handle));
            else rig.Result(name + " not open: skipped");
        }

        foreach (var (name, handle) in targets)
        {
            PcSmokeRig.Show(handle, minimized: true);
            await Task.Delay(1_500);
            rig.Result("watch the screen and the taskbar: peeking " + name + " in 3 s");
            await Task.Delay(3_000);

            var placement = PcSmokePlacement.Save(handle);
            var before = PcSmokeRig.Snapshot();
            var order = PcSmokeRig.Windows().Select(w => w.Handle).Where(w => w != handle).ToList();
            var watch = Stopwatch.StartNew();

            placement.ShowAtBottom();
            var shownMs = watch.ElapsedMilliseconds;
            var during = PcSmokeRig.Snapshot();
            PcFrame? frame = null;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                frame = await capture.Capture(handle, PcSmokeRig.Bounds(handle), timeout.Token);
            }
            catch (Exception error) when (error is OperationCanceledException or InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                rig.Note(name + " grab threw " + error.GetType().Name);
            }
            var grabMs = watch.ElapsedMilliseconds - shownMs;
            placement.Restore();
            var totalMs = watch.ElapsedMilliseconds;
            await Task.Delay(300);
            var after = PcSmokeRig.Snapshot();
            var orderAfter = PcSmokeRig.Windows().Select(w => w.Handle).Where(w => w != handle).ToList();

            rig.Result(name + ": foreground " + (before.Foreground == during.Foreground && before.Foreground == after.Foreground ? "kept" : "CHANGED")
                + ", focus " + (before.Focus == after.Focus ? "kept" : "CHANGED")
                + ", other windows' order " + (order.SequenceEqual(orderAfter) ? "kept" : "CHANGED")
                + ", grab " + (frame is null ? "NONE" : frame.Width + "x" + frame.Height + (PcFrames.IsBlank(frame) ? " BLANK" : ""))
                + ", shown " + shownMs + " ms, grab " + grabMs + " ms, total " + totalMs + " ms; owner: did you see an animation, a flash or a taskbar change?");
            if (handle != targets[0].Handle) PcSmokeRig.Show(handle, minimized: false);
        }
        PcSmokeRig.Close(targets[0].Handle);
    }
}
