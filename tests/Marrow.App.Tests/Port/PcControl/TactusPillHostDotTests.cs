using Avalonia.Threading;
using Marrow.App.Port.PcControl.Overlay;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Testing;
using Marrow.UI.Port.PcControl;

namespace Marrow.App.Tests.Port.PcControl;

// PORT-ONLY: Tactus pill v2, the full-screen dot in the host (OD-0145, ED-NEW-pcctl-36; DESIGN.md 10.3 "Auto-dodge" row 4,
// canvas `.sdot`). On fakes: a borderless full-screen window on every monitor makes the pill a 32 DIP dot where it is; the
// pointer on it grows it back, and the dot returns once the pointer has been off it 300 ms (never sooner); an ask opens its
// card over the dot; a monitor that clears takes the pill, whole.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-pcctl-36")]
public sealed class TactusPillHostDotTests(HeadlessSession headless)
{
    private static readonly PillApp Excel = new("excel", "Excel", "Filling row 12 of 40");
    private static readonly PillAsk First = new("a1", PillAskKind.Approval, "Outlook", "Quarterly report", "GPT-6.1 Sol");

    // Time passes in 50 ms steps, the UI thread running what each step posted (the host refreshes every 100 ms).
    private static void Pass(PcPillHostRig rig, double seconds)
    {
        for (var passed = 0.0; passed < seconds - 1e-9; passed += 0.05)
        {
            rig.Clock.Advance(TimeSpan.FromSeconds(0.05));
            Dispatcher.UIThread.RunJobs();
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static (PcPillHostRig Rig, Action<bool> SetRightClear) Make()
    {
        var rightClear = false;
        var rig = PcPillHostRig.Create(shade: m => m.Id == PcTestMonitors.Right.Id && rightClear ? MonitorShade.Clear : MonitorShade.BorderlessFullscreen);
        rig.Host.Update(new PillFacts { Apps = [Excel] });
        Pass(rig, 2);
        return (rig, clear => rightClear = clear);
    }

    private static void IsDot(ControlPill pill)
    {
        Assert.True(pill.View!.Dot);
        Assert.Equal(PillLayout.DotSize, pill.Motion.Final.Width, 9);
        Assert.Equal(PillLayout.DotSize, pill.Motion.Final.Height, 9);
        Assert.Equal(PillGroupKind.None, pill.ShownGroup);
        Assert.False(pill.StopSlot.IsVisible);
    }

    [Fact]
    public void Over_a_borderless_full_screen_on_every_monitor_the_pill_is_a_dot_where_it_is() => headless.OnUiThread(() =>
    {
        var (rig, _) = Make();
        Assert.Equal(PcTestMonitors.Main.Id, rig.Host.MonitorId);
        IsDot(rig.Host.Pill(PcTestMonitors.Main.Id)!);
    });

    [Fact]
    public void The_pointer_on_the_dot_grows_it_and_the_dot_returns_300_ms_after_the_pointer_leaves_never_sooner() => headless.OnUiThread(() =>
    {
        var (rig, _) = Make();
        var pill = rig.Host.Pill(PcTestMonitors.Main.Id)!;
        var drawn = pill.Motion.At(pill.Now).Drawn;
        PillVector? pointer = new PillVector(drawn.X + (drawn.Width / 2), drawn.Y + (drawn.Height / 2));
        rig.Host.PointerAt = _ => pointer;

        Pass(rig, 0.15);
        Assert.False(pill.View!.Dot);
        Assert.True(pill.Motion.Final.Width >= PillLayout.MinWidth - 1e-9);
        Assert.Equal(PillLayout.Height, pill.Motion.Final.Height, 9);
        // held while the pointer stays (it is still over the grown pill, which grows round the dot)
        Pass(rig, 1);
        Assert.False(pill.View!.Dot);
        Assert.Equal(PillGroupKind.Run, pill.ShownGroup);

        pointer = null;
        Pass(rig, 0.25);
        Assert.False(pill.View!.Dot);
        Pass(rig, 0.35);
        Assert.True(pill.View!.Dot);
        Pass(rig, 1);
        IsDot(pill);
    });

    [Fact]
    public void An_ask_opens_its_card_over_the_dot() => headless.OnUiThread(() =>
    {
        var (rig, _) = Make();
        var pill = rig.Host.Pill(PcTestMonitors.Main.Id)!;
        rig.Host.Bodies = id => id == First.Id ? new PillApprovalBody(First.Id, 0, "Outlook", @"c:\office\outlook.exe", Risky: false, UseAsked: true, AllowAlways: true) : null;
        rig.Host.Update(new PillFacts { Apps = [Excel], Asks = [First] });
        Pass(rig, 2);
        Assert.False(pill.View!.Dot);
        Assert.Equal(PillGroupKind.Card, pill.ShownGroup);
        Assert.True(pill.Motion.Final.Width > PillLayout.DotSize);
    });

    [Fact]
    public void A_monitor_that_clears_takes_the_pill_whole() => headless.OnUiThread(() =>
    {
        var (rig, setRightClear) = Make();
        setRightClear(true);
        Pass(rig, 2);
        Assert.Equal(PcTestMonitors.Right.Id, rig.Host.MonitorId);
        var pill = rig.Host.Pill(PcTestMonitors.Right.Id)!;
        Assert.False(pill.View!.Dot);
        Assert.True(pill.Motion.Final.Width >= PillLayout.MinWidth - 1e-9);
        Assert.Equal(PillGroupKind.Run, pill.ShownGroup);
    });
}
