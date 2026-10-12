using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-cx-3")]
public sealed class C3A3Tests
{
    [Fact]
    public async Task A_stop_after_approval_but_before_the_launcher_is_checked_again()
    {
        var h = new PcEngineHarness();
        h.Desktop.BeforeWindows = () => h.Control.StopKey();
        var result = await PcEngineHarness.Call(h.Engine(), "pc_launch_app", "{\"path\":\"C:\\\\Apps\\\\calc.exe\"}");
        Assert.Equal(PcErrorCode.StoppedByUser, result.Error);
        Assert.DoesNotContain(h.Desktop.Log, line => line.StartsWith("launch", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_stop_for_another_chat_or_app_does_not_cancel_this_launch(bool otherApp)
    {
        var h = new PcEngineHarness();
        h.Desktop.OnLaunch = _ => h.Notepad(0x6f, 606, "calc.exe");
        h.Approver.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var launch = PcEngineHarness.Call(h.Engine(), "pc_launch_app", "{\"path\":\"C:\\\\Apps\\\\calc.exe\"}");
        await PcTestWait.Until(() => h.Approver.Asked.Count == 1);
        h.Control.Stop(otherApp ? PcScope.App(new PcAppIdentity("C:\\Apps\\other.exe").Key) : PcScope.Thread("chat-2"), PcStopSource.Pill);
        h.Approver.Hold.SetResult(PcApproval.Chat);
        Assert.True((await launch.WaitAsync(TimeSpan.FromSeconds(5))).Ok);
    }

    [Fact]
    public async Task A_stop_for_the_app_cancels_its_pending_launch()
    {
        var h = new PcEngineHarness();
        h.Approver.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var launch = PcEngineHarness.Call(h.Engine(), "pc_launch_app", "{\"path\":\"C:\\\\Apps\\\\calc.exe\"}");
        await PcTestWait.Until(() => h.Approver.Asked.Count == 1);
        h.Control.Stop(PcScope.App(new PcAppIdentity("C:\\Apps\\calc.exe").Key), PcStopSource.Pill);
        h.Approver.Hold.SetResult(PcApproval.Chat);
        Assert.Equal(PcErrorCode.StoppedByUser, (await launch.WaitAsync(TimeSpan.FromSeconds(5))).Error);
        Assert.DoesNotContain(h.Desktop.Log, line => line.StartsWith("launch", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(PcStopSource.StopKey, PcErrorCode.StoppedByUser)]
    [InlineData(PcStopSource.Chat, PcErrorCode.StoppedByUser)]
    [InlineData(PcStopSource.Settings, PcErrorCode.StoppedByUser)]
    [InlineData(PcStopSource.Sleep, PcErrorCode.Locked)]
    public async Task A_launch_waiting_for_approval_is_cancelled_by_stop(PcStopSource source, PcErrorCode expected)
    {
        var h = new PcEngineHarness();
        h.Approver.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = h.Engine();
        var launch = PcEngineHarness.Call(engine, "pc_launch_app", "{\"path\":\"C:\\\\Apps\\\\calc.exe\"}");
        await PcTestWait.Until(() => h.Approver.Asked.Count == 1);
        h.Control.Stop(source == PcStopSource.Chat ? PcScope.Thread("chat-1") : PcScope.All, source);
        h.Approver.Hold.SetResult(PcApproval.Chat);
        var result = await launch.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(expected, result.Error);
        Assert.DoesNotContain(h.Desktop.Log, line => line.StartsWith("launch", StringComparison.Ordinal));
        if (source == PcStopSource.Sleep)
        {
            h.Approver.Hold = null;
            h.Desktop.OnLaunch = _ => h.Notepad(0x6f, 606, "calc.exe");
            Assert.True((await PcEngineHarness.Call(engine, "pc_launch_app", "{\"path\":\"C:\\\\Apps\\\\calc.exe\"}")).Ok);
        }
    }

    [Fact]
    public async Task Turning_off_settings_while_approval_waits_prevents_launch_even_without_a_stop_event()
    {
        var h = new PcEngineHarness();
        h.Approver.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var launch = PcEngineHarness.Call(h.Engine(), "pc_launch_app", "{\"path\":\"C:\\\\Apps\\\\calc.exe\"}");
        await PcTestWait.Until(() => h.Approver.Asked.Count == 1);
        h.Settings = h.Settings with { Enabled = false };
        h.Approver.Hold.SetResult(PcApproval.Chat);
        var result = await launch.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PcErrorCode.PcOff, result.Error);
        Assert.DoesNotContain(h.Desktop.Log, line => line.StartsWith("launch", StringComparison.Ordinal));
    }
}
