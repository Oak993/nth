// PORT-ONLY: Tactus native probe NP-13, locked, disconnected and display-off sessions (unit 15, P19; ED-NEW-pcctl-43;
// DESIGN.md 2.10 "Locked, disconnected or sleeping sessions", 5.4 item 11, critique C24). NATIVE: the owner locks the PC
// (Win+L), turns the display off, or disconnects a Remote Desktop session into this PC, as the script says, within 10 s;
// for the next 30 s the probe tries every 5 s a WGC grab, a PrintWindow grab, the UI Automation tree and an R2 WM_CHAR
// on a Notepad it opened, plus the engine's own pc_observe, and records what each gave. Runs only from the smoke script.
using Marrow.App.Port.PcControl;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Native;

namespace Marrow.App.Tests.Port.PcControl.Native;

[Trait("Decision", "ED-NEW-pcctl-43")]
public sealed class PcLockedSessionNativeTests
{
    // One fact per way, so the smoke script runs each alone after telling the owner what to do.
    [Fact]
    public Task NP13_Lock_screen() => Away("lock");

    [Fact]
    public Task NP13_Display_off() => Away("display-off");

    [Fact]
    public Task NP13_Remote_Desktop_disconnect() => Away("disconnect");

    private static async Task Away(string how)
    {
        using var rig = new PcSmokeRig("np13-" + how);
        using var reader = new UiaReader();
        using var wgc = new WgcWindowCapture();
        var print = new GdiCapture(fromScreen: false);
        var messages = new MessageSender();
        var handle = await rig.Launch("notepad.exe", null, (cls, _) => cls == "Notepad");
        var window = PcWindowRef.Format(handle);
        rig.Result(how + " now: the probe waits 10 s, then samples for 30 s");
        await Task.Delay(10_000);

        for (var sample = 1; sample <= 6; sample++)
        {
            var bounds = PcSmokeRig.Bounds(handle);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var grab = await Try(() => wgc.Capture(handle, bounds, timeout.Token));
            var printed = await Try(() => print.Capture(handle, bounds, timeout.Token));
            var tree = await Try(() => reader.Tree(handle, timeout.Token));
            var typed = messages.Chars(messages.FocusControl(handle), " np13-" + sample);
            var look = await rig.Call("pc_observe", new { window, screenshot = "yes" });

            rig.Result(how + " sample " + sample + ": wgc " + Frame(grab) + ", printwindow " + Frame(printed) + ", tree " + (tree is null ? "none" : "ok")
                + ", wm_char " + (typed ? "sent" : "refused") + ", pc_observe " + (look.Error?.ToString() ?? look.Status.ToString()) + (look.Image is null ? " without image" : " with image")
                + ((look.Body ?? "").Contains("tree only", StringComparison.Ordinal) ? " (tree only)" : ""));
            await Task.Delay(5_000);
        }
        rig.Result(how + ": done; unlock or reconnect, then check the np13 text landed in Notepad");
    }

    private static async Task<T?> Try<T>(Func<Task<T?>> run) where T : class
    {
        try
        {
            return await run();
        }
        catch (Exception error) when (error is OperationCanceledException or InvalidOperationException or PcUiaTimeoutException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    private static string Frame(PcFrame? frame) => frame is null ? "none" : frame.Width + "x" + frame.Height + (PcFrames.IsBlank(frame) ? " BLANK" : "");
}
