using Marrow.Setup.Screens;
using Marrow.Setup.Shell;
using Marrow.Setup.Tests.Fakes;
using Marrow.Setup.Tests.Fixtures;
using Marrow.Setup.Text;

namespace Marrow.Setup.Tests.Shell;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 10, "Close and shutdown", unit U10 (ED-NEW-inst-u10-1). The four ways of
// ending the window, each acted out through the real window, its real message hook and the real install flow on a fake
// Setup.exe: the X (a press and a release on the caption button, which posts SC_CLOSE), Alt+F4 (WM_SYSKEYDOWN, which
// Windows turns into SC_CLOSE), WM_SYSCOMMAND SC_CLOSE itself (the taskbar and the system menu, with a mouse position)
// and WM_QUERYENDSESSION (sign-out and shutdown). Before Setup.exe starts each one works; from the moment it starts to the
// moment the setup's own step is done each is refused, the Close-paused line brightens and Windows holds shutdown with the
// reason; afterwards each works again and the reason is gone. Windows' own step after a message the hook leaves alone
// (DefWindowProc turning SC_CLOSE into WM_CLOSE) is the window's Close, as the hook's tests do it.
[Collection("SetupHeadless")]
public sealed class CloseWaysTests(SetupHeadlessSession headless)
{
    private const uint NcButtonDown = 0x00A1, NcButtonUp = 0x00A2, SysKeyDown = 0x0104, SysCommand = 0x0112, QueryEndSession = 0x0011;
    private const int ScClose = 0xF060, HtClose = 20, VkF4 = 0x73;
    private const nint Handle = 0x5150, AltPressed = 1 << 29, CloseApp = 1;
    private static readonly nint LogOff = unchecked((nint)0x80000000);

    public static TheoryData<string> Ways => new() { "x-button", "alt-f4", "sc-close", "end-session-shutdown", "end-session-logoff", "end-session-restart-manager" };

    [Theory]
    [MemberData(nameof(Ways))]
    [Trait("Decision", "OD-0072")]
    public void Close_works_before_Setup_exe_starts_for_every_way(string way)
    {
        headless.OnUiThread(() =>
        {
            using var rig = new InstallFlowRig(VelopackScenarios.Build("fast"));
            var window = rig.Window.Window;
            var calls = new FakeCaptionCalls();
            var hooks = new SetupWindowHooks(window, calls);
            rig.Session.ChooseInstall();
            Assert.Equal(0, rig.Scenario.Runner.Starts);
            Assert.False(rig.Paused);

            var refused = Try(way, hooks, calls, window);

            Assert.False(refused);
            Assert.Equal(0, window.Guard.Refusals);
            Assert.Null(rig.Window.Blocker.Reason);
            Assert.Equal(!IsEndSession(way), !window.IsVisible); // a close ends the window; an agreed shutdown leaves it for Windows to end
        });
    }

    [Theory]
    [MemberData(nameof(Ways))]
    [Trait("Decision", "OD-0072")]
    public void From_Setup_exe_s_start_to_the_setup_s_own_step_every_way_is_refused_and_afterwards_works_again(string way)
    {
        headless.OnUiThread(() =>
        {
            using var rig = new InstallFlowRig(VelopackScenarios.Build("fast"));
            var window = rig.Window.Window;
            var calls = new FakeCaptionCalls();
            var hooks = new SetupWindowHooks(window, calls);
            rig.Session.ChooseInstall();
            rig.Staged();
            rig.Step();
            Assert.Equal(1, rig.Runner.Starts);
            Assert.True(rig.Paused);
            Assert.Equal(SetupStrings.ShutdownInstalling, rig.Window.Blocker.Reason);

            // refused, every time, with the line brightening
            var hold = Assert.IsAssignableFrom<CanvasScreen>(rig.Session.Current).Hold!;
            rig.Window.Run(2);
            Assert.Equal(0, hold.Brightness);
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                Assert.True(Try(way, hooks, calls, window), $"{way} was let through (attempt {attempt})");
                Assert.True(window.IsVisible);
                Assert.Equal(attempt, window.Guard.Refusals);
                Assert.Equal(SetupStrings.ShutdownInstalling, rig.Window.Blocker.Reason);
                rig.Window.Run(0.3);
                Assert.Equal(1, hold.Brightness);
                rig.Window.Run(2);
            }

            // done: the setup's own step has run, Close works again and Windows holds nothing
            rig.Until(() => !rig.Poll.Running);
            Assert.Equal(1, rig.Scenario.After.Calls);
            Assert.False(rig.Paused);
            Assert.Null(rig.Window.Blocker.Reason);
            Assert.Equal("block:" + SetupStrings.ShutdownInstalling, rig.Window.Blocker.Calls[0]); // set when Setup.exe started, cleared when the step was done
            Assert.Equal("release", rig.Window.Blocker.Calls[^1]);
            var refusals = window.Guard.Refusals;

            Assert.False(Try(way, hooks, calls, window));
            Assert.Equal(refusals, window.Guard.Refusals);
            Assert.Equal(!IsEndSession(way), !window.IsVisible);
        });
    }

    private static bool IsEndSession(string way) => way.StartsWith("end-session", StringComparison.Ordinal);

    /// <summary>
    /// Sends the way's messages to the hook and, when the hook leaves the close alone, does what Windows does next (closes the
    /// window). True when the hook refused it.
    /// </summary>
    private static bool Try(string way, SetupWindowHooks hooks, FakeCaptionCalls calls, SetupWindow window)
    {
        (nint Value, bool Handled) answer;
        switch (way)
        {
            case "x-button":
            {
                Assert.Equal((0, true), Send(hooks, NcButtonDown, HtClose, 0));
                Assert.Equal((0, true), Send(hooks, NcButtonUp, HtClose, 0));
                var posted = calls.Commands[^1]; // the click posted SC_CLOSE to the window, which comes back as a message
                Assert.Equal(ScClose, posted);
                answer = Send(hooks, SysCommand, posted, 0);
                break;
            }
            case "alt-f4":
            {
                Assert.False(Send(hooks, SysKeyDown, VkF4, AltPressed).Handled); // the hook leaves the key to Windows, which makes SC_CLOSE of it
                answer = Send(hooks, SysCommand, ScClose, 0);
                break;
            }
            case "sc-close":
                answer = Send(hooks, SysCommand, ScClose, SetupWindowHooks.Pack(640, 880)); // the taskbar or the system menu: a mouse position
                break;
            case "end-session-shutdown":
                answer = Send(hooks, QueryEndSession, 0, 0);
                break;
            case "end-session-logoff":
                answer = Send(hooks, QueryEndSession, 0, LogOff);
                break;
            case "end-session-restart-manager":
                answer = Send(hooks, QueryEndSession, 0, CloseApp);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(way), way, "No such way.");
        }
        if (answer.Handled)
        {
            Assert.Equal(0, answer.Value); // FALSE for a session end, 0 for a refused close
            return true;
        }
        if (!IsEndSession(way)) window.Close();
        return false;
    }

    private static (nint Value, bool Handled) Send(SetupWindowHooks hooks, uint message, nint wParam, nint lParam)
    {
        var handled = false;
        var value = hooks.WndProc(Handle, message, wParam, lParam, ref handled);
        return (value, handled);
    }
}
