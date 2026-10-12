using Marrow.App.Tests.Fixtures;
using Marrow.Services.Store;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppModel.swift (B2 1fbf81e), :1307-1327, the one saveBeforeQuit step that needs a real
// terminal session: TerminalSession.init (App/Views/Terminal/TerminalStore.swift:17-41) always starts a shell inside a
// TerminalHostView, so this runs on the headless UI thread. The step reads the sessions' isRunning, which Terminate sets
// at once and no process exit waits on, so the shell here is a stand-in and no process is started. Expectations come
// from the cited Swift and PR-LAUNCH-056, never from running the port.
[Collection("Headless")]
public sealed class AppModelTerminalTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-LAUNCH-056")] // App/AppModel.swift:1325-1326; App/Views/Terminal/TerminalStore.swift:171-175
    public void Save_before_quit_ends_by_terminating_every_terminal_session_of_every_thread() => headless.OnUiThread(() =>
    {
        using var h = new AppModelHarness(withRuntime: false);
        h.UseFakeTerminalShell();
        var model = new AppModel();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var one = model.Terminals.Open(first, h.NewFolder("one"));
        var two = model.Terminals.Open(first, h.NewFolder("two"));
        var three = model.Terminals.Open(second, h.NewFolder("three"));
        try
        {
            Assert.True(UiPump.Until(() => one.IsRunning && two.IsRunning && three.IsRunning, UiPump.Bound));

            model.SaveBeforeQuit();

            Assert.True(File.Exists(Storage.LibraryURL)); // persistence came first (:1317)
            Assert.True(UiPump.Until(() => !one.IsRunning && !two.IsRunning && !three.IsRunning, UiPump.Bound));
        }
        finally
        {
            model.Terminals.TerminateAll();
        }
    });
}
