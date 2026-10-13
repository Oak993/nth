using Avalonia;
using Avalonia.Controls;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Window.Native;
using Marrow.App.Tray;

namespace Marrow.App.Tests.Assistant;

// Owed native check (never run by the unit agent; the lead runs it in a quiet window): DESIGN.md rev 5, 16.1 "Native gate",
// AppTray. On a real Avalonia platform, with no window of nth on screen: the icon appears while the window is hidden, the items
// contributed by the Assistant show, Stop runs without any window, and the icon goes when the window is back. The owner also
// looks at the notification area once to see the mark in each state (Ready, Working, Paused, Needs you) at 100 % and 200 %.
[Collection("WindowManagerNative")]
[Trait("Decision", "ED-NEW-dextra-as6-10")]
[Trait("Feature", "AS-6")]
public sealed class AppTrayNativeTests(WindowManagerNativeSession native)
{
    private static TrayIcon? Icon() => Application.Current is { } app && TrayIcon.GetIcons(app) is { Count: 1 } icons ? icons[0] : null;

    private T Eventually<T>(Func<T?> read, string what) where T : class
    {
        T? found = null;
        Assert.True(SpinWait.SpinUntil(() => (found = native.OnUi(read)) is not null, TimeSpan.FromSeconds(10)), "Timed out waiting for " + what);
        return found!;
    }

    [Fact]
    public void The_icon_appears_while_the_window_is_hidden_shows_the_contributed_items_and_Stop_works_with_no_window()
    {
        native.Run(_ =>
        {
            var hidden = false;
            var stops = 0;
            var host = native.OnUi(() => new AvaloniaTrayHost());
            using var tray = new AppTray(host, () => hidden, open: () => { });
            using var contribution = tray.Contribute("Assistant", () => new TrayContribution([
                new TrayItem("assistant.stop", "Stop Assistant", () => stops++),
                new TrayItem("assistant.pause", "Pause all", () => { }),
            ], Live: false, State: TrayState.Working));
            Assert.Null(native.OnUi(Icon));

            hidden = true; // nth's window is closed to the tray
            tray.Refresh();
            var icon = Eventually(Icon, "the tray icon");
            Assert.True(native.OnUi(() => icon.IsVisible));
            var labels = native.OnUi(() => icon.Menu!.Items.OfType<NativeMenuItem>().Select(i => i.Header).ToList());
            Assert.Equal(["Open nth", "Stop Assistant", "Pause all"], labels);

            // Stop belongs to a service, not to the window: it runs with none open.
            Assert.True(tray.Invoke("assistant.stop"));
            Assert.Equal(1, stops);

            hidden = false; // the window is back
            tray.Refresh();
            Assert.True(SpinWait.SpinUntil(() => native.OnUi(Icon) is null, TimeSpan.FromSeconds(10)));
        });
    }
}
