// PORT-ONLY: Tactus native probe NP-14, the stop chord with a hostile foreground (unit 15, P19; ED-NEW-pcctl-43; DESIGN.md
// 4.4, 5.4 item 11, critiques C1 and C11). NATIVE: runs batches on a Notepad it opened while the owner brings an elevated
// window (an administrator Terminal or Task Manager) or an exclusive-fullscreen game to the front, types there for 10 s,
// then presses Ctrl+Alt+Esc. It records whether the chord stopped the batch, and whether the hooks saw the owner's typing
// (the monitor's last key) while GetLastInputInfo did (the cross-check that catches the hooks' blindness to an elevated
// foreground). The pill's Stop and the tray's Stop are checked in the app itself (smoke script item NP-14b). Runs only from
// the smoke script.
using Marrow.App.Port.PcControl;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl;

namespace Marrow.App.Tests.Port.PcControl.Native;

[Trait("Decision", "ED-NEW-pcctl-43")]
public sealed class PcStopReachNativeTests
{
    // One fact per foreground, so the smoke script runs each alone after telling the owner what to do.
    [Fact]
    public Task NP14_Behind_an_elevated_window() => Reach("elevated");

    [Fact]
    public Task NP14_Behind_an_exclusive_fullscreen_game() => Reach("fullscreen");

    private static async Task Reach(string front)
    {
        using var rig = new PcSmokeRig("np14-" + front);
        var input = (rig.Composition.Assembly ?? throw new InvalidOperationException("Tactus is off.")).Input;
        var handle = await rig.Launch("notepad.exe", null, (cls, _) => cls == "Notepad");
        var window = PcWindowRef.Format(handle);
        using var keep = input.Keep();
        rig.Result("bring the " + front + " window to the front now, type in it for 10 s, then press Ctrl+Alt+Esc (within 120 s)");

        var hookKeys = 0;
        var idleDrops = 0;
        var lastKey = input.Snapshot().LastKeyMs;
        var lastIdle = TactusDesktop.IdleMs();
        PcResult? stopped = null;
        var rounds = 0;
        var deadline = Environment.TickCount64 + 120_000;
        while (stopped is null && Environment.TickCount64 < deadline)
        {
            rounds++;
            var actions = Enumerable.Range(1, 10).Select(line => new { tool = "pc_type", target = new { name = "Text editor" }, text = "np14 " + rounds + "." + line + "\n" }).ToArray();
            var batch = await rig.Call("pc_batch", new { window, actions, observe = "none" }, timeoutSeconds: 120);
            if (batch.Error == PcErrorCode.StoppedByUser || (batch.Body ?? "").Contains("stopped_by_user", StringComparison.Ordinal)) stopped = batch;

            var key = input.Snapshot().LastKeyMs;
            if (key != lastKey) hookKeys++;
            lastKey = key;
            var idle = TactusDesktop.IdleMs();
            if (idle < lastIdle) idleDrops++;
            lastIdle = idle;
        }

        var foreground = PcSmokeRig.Snapshot().Foreground;
        rig.Result(front + ": " + (stopped is null ? "NOT stopped in 120 s" : "stopped in batch " + rounds) + "; foreground at the end " + PcSmokeRig.ExeOf(foreground)
            + "; the hooks saw keys in " + hookKeys + " of " + rounds + " rounds, GetLastInputInfo saw input in " + idleDrops
            + " (more in GetLastInputInfo means the hooks were blind and the cross-check must hold the gate)");
        Assert.NotNull(stopped);
        PcSmokeRig.Close(handle);
    }
}
