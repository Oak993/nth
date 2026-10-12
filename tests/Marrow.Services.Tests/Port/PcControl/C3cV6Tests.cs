using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// ED-NEW-pcctl-cx-19: the trait names the owner decision, as a two-word ED id is not a well-formed trait id (ED-NEW-pcctl-35).
[Trait("Decision", "OD-0144")]
public sealed class C3cV6Tests
{
    [Fact]
    public async Task An_existing_R4_plan_refuses_when_Enabled_turns_off_but_RealInput_stays_on()
    {
        var rig = new PcBorrowRig().ProveAll();
        var settings = PcSettings.Default with { Enabled = true, RealInput = true };
        var window = new PcWindowInfo(PcBorrowRig.Target, "Paint", "MSPaintApp", 42, @"c:\windows\mspaint.exe");
        var plan = Assert.Single(new PcBorrowRung(() => settings, rig.Lease).Offer(
            new PcInputRequest(PcInputKind.Click, window) { Point = new PcPoint(300, 200) }, PcFramework.Win32).Plans);
        rig.CardUp = true;
        var action = plan.Run(CancellationToken.None);
        await PcTestWait.Until(() => rig.Lease.Waits.Count == 1);
        settings = settings with { Enabled = false };
        rig.CardUp = false;
        rig.Clock.Advance(RealInputLease.PollMs);
        Assert.Equal(PcErrorCode.NeedsRealInput, (await action.WaitAsync(TimeSpan.FromSeconds(5))).Error);
        Assert.Empty(rig.Host.Sent);
        Assert.Empty(rig.Hold.Log);
    }

    [Theory]
    [InlineData("attended", PcErrorCode.Unattended)]
    [InlineData("secure", PcErrorCode.SecureDesktop)]
    [InlineData("denied", PcErrorCode.DeniedApp)]
    [InlineData("always", PcErrorCode.NotApproved)]
    public async Task Launch_rechecks_live_gates_after_approval(string changed, PcErrorCode expected)
    {
        var h = new PcEngineHarness();
        var attended = true;
        using var engine = new PcControlEngine(h.Parts, new PcCaller("chat-1", () => attended, true));
        h.Desktop.OnLaunch = _ => h.Notepad(0x6f, 606, @"C:\Apps\calc.exe");
        h.Approver.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var launching = PcEngineHarness.Call(engine, "pc_launch_app", "{\"path\":\"C:\\\\Apps\\\\calc.exe\"}");
        await PcTestWait.Until(() => h.Approver.Asked.Count == 1);
        h.Approver.Standings[("calc.exe", PcCapability.Act)] = PcApproval.Always;
        h.Desktop.BeforeWindows = () =>
        {
            h.Desktop.BeforeWindows = null;
            switch (changed)
            {
                case "attended": attended = false; break;
                case "secure": h.Desktop.Secure = true; break;
                case "denied": h.Settings = h.Settings with { UserDeniedApps = ["calc.exe"] }; break;
                case "always": h.Approver.Standings.Remove(("calc.exe", PcCapability.Act)); break;
            }
        };
        h.Approver.Hold.SetResult(PcApproval.Always);
        Assert.Equal(expected, (await launching.WaitAsync(TimeSpan.FromSeconds(5))).Error);
        Assert.DoesNotContain(h.Desktop.Log, line => line.StartsWith("launch", StringComparison.Ordinal));
    }
}
