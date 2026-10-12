// PORT-ONLY: Tactus, the user's pointer on the pill's screen (ED-NEW-pcctl-34; DESIGN.md 10.3 "Auto-dodge" row 3, 10.5
// auto-expand, 10.6 "Hit testing"). The host reads where the pointer is (in the monitor's DIP) and whether a button is
// held; a press that began in another app holds the band's hit region still, and the newest region lands at the release.
// Fake bands, fake Win32 calls, the shared input monitor fed by hand, a manual clock; no input.
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Services.Port.PcControl;

namespace Marrow.App.Tests.Port.PcControl;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-pcctl-34")]
public sealed class TactusScreenPointerTests(HeadlessSession headless)
{
    private const string Notepad = "exe:c:\\windows\\notepad.exe";

    private static void Pass(PcScreenRig rig, double seconds)
    {
        rig.Pill.Clock.Advance(TimeSpan.FromSeconds(seconds));
        Dispatcher.UIThread.RunJobs();
    }

    private static PcRawInput Mouse(PcScreenRig rig, PcRawKind kind, PcPoint point, int heldButtons, bool down = false) =>
        new(rig.Parts.Desktop.Clock.NowMs, kind, down, kind == PcRawKind.Button ? (ushort)1 : (ushort)0, false, 0)
        {
            Window = PcScreenRig.Word,
            Point = point,
            HeldButtons = heldButtons,
        };

    [Fact]
    public void The_host_reads_the_pointer_and_a_press_in_another_app_holds_the_band_s_region_until_its_release() => headless.OnUiThread(() =>
    {
        using var rig = new PcScreenRig();
        using var notepad = rig.Assembly.Pause.Join("chat-a", Notepad, new PcWaitState());
        rig.Screen.Step(new PcAuditEntry(DateTimeOffset.UnixEpoch, PcToolSchemas.Click, "notepad.exe", "Untitled", "Save", "button", "ok", 9));
        Pass(rig, 1);
        var host = rig.Pill.Host;
        var pill = host.Pill(PcTestMonitors.Main.Id)!;
        Assert.Contains(rig.Pill.Native.Calls, c => c.StartsWith("region", StringComparison.Ordinal));
        Assert.Null(host.PointerAt!(PcTestMonitors.Main));
        Assert.False(host.ButtonHeld!());

        rig.Parts.Input.Record(Mouse(rig, PcRawKind.Move, new PcPoint(300, 40), 0));
        Assert.Equal(new PillVector(300, 40), host.PointerAt(PcTestMonitors.Main));
        Assert.Null(host.PointerAt(PcTestMonitors.Right));

        // a press in Word, then an ask narrows the pill (its card waits for the release too): its region waits
        rig.Parts.Input.Record(Mouse(rig, PcRawKind.Button, new PcPoint(300, 40), 1, down: true));
        Assert.True(host.ButtonHeld());
        rig.Pill.Native.Calls.Clear();
        var before = pill.Motion.Final.Width;
        rig.Assembly.Asks.Add(new PcAskDraft(PcAskKind.Approval, "chat-a", new PcApprovalAsk(PcCapability.Act, false, true)) { App = Notepad, AppName = "Notepad" });
        Pass(rig, 1);
        Assert.False(host.CardOpen);
        Assert.True(Math.Abs(pill.Motion.Final.Width - before) > 1, $"the pill stayed {before} DIP wide");
        Assert.DoesNotContain(rig.Pill.Native.Calls, c => c.StartsWith("region", StringComparison.Ordinal));

        rig.Parts.Input.Record(Mouse(rig, PcRawKind.Button, new PcPoint(320, 60), 0));
        Assert.Single(rig.Pill.Native.Calls, c => c.StartsWith("region", StringComparison.Ordinal));
        Assert.False(host.ButtonHeld());
    });
}
