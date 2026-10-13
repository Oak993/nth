using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Window.Native;
using Marrow.App.Tray;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

// PORT-ONLY: the Native gate of DESIGN.md 16.1 for AS-10, "Tray switches" (the lead runs it alone, in a quiet window; every unit filter
// excludes Native). On a real Avalonia platform, with no window of nth on screen: the tray icon shows the duties' items ("Pause duties for 1
// hour" and the two switches, both Off), a click on the pause item holds the scheduler and turns the item into "Resume duties", and a click on
// each switch flips it and its label. The Run entry is a fake here (the real one is StartWithWindowsNativeTests). The owner looks once at the
// icon in the notification area while a duty runs (the mark's Working state) and while paused.
[Collection("WindowManagerNative")]
[Trait("Decision", "ED-NEW-dextra-as10-14")]
[Trait("Feature", "AS-10")]
[Trait("Platform", "WINDOWS-ONLY")]
public sealed class DutyTrayNativeTests(WindowManagerNativeSession native)
{
    private static TrayIcon? Icon() => Application.Current is { } app && TrayIcon.GetIcons(app) is { Count: 1 } icons ? icons[0] : null;

    private static List<string?> Labels(TrayIcon icon) => [.. icon.Menu!.Items.OfType<NativeMenuItem>().Select(i => i.Header)];

    [Fact]
    public void The_duties_items_show_in_the_real_tray_icon_and_a_click_on_each_does_its_job()
    {
        native.Run(_ =>
        {
            JsonObject? stored = null;
            var key = new FakeRunKey();
            using var rig = new SchedulerRig { AutoEnd = false };
            rig.Store.Put("dextra", SchedulerRig.Every("d1", 10));
            rig.Begin();
            var switches = new BackgroundSwitches(() => stored?.DeepClone() as JsonObject, document => stored = (JsonObject)document.DeepClone(), new StartWithWindows(key), () => @"C:\nth-native-test\nth.exe");
            var host = native.OnUi(() => new AvaloniaTrayHost());
            using var tray = new AppTray(host, () => true, open: () => { });
            using var duties = DutyTray.Bind(tray, rig.Scheduler!, switches);
            tray.Refresh();
            var icon = SpinFor(() => native.OnUi(Icon));
            Assert.Equal(["Open nth", "Pause duties for 1 hour", "Keep working when the window is closed: Off", "Start with Windows, hidden: Off"], native.OnUi(() => Labels(icon)));

            Assert.True(tray.Invoke(DutyTray.PauseId)); // the click on the item runs its action
            Assert.NotNull(rig.Scheduler!.PausedUntil);
            Assert.Contains("Resume duties", native.OnUi(() => Labels(icon)));
            Assert.True(tray.Invoke(DutyTray.PauseId));
            Assert.Null(rig.Scheduler.PausedUntil);

            Assert.True(tray.Invoke(DutyTray.KeepId));
            Assert.True(switches.KeepWorkingWhenClosed);
            Assert.Contains("Keep working when the window is closed: On", native.OnUi(() => Labels(icon)));
            Assert.True(tray.Invoke(DutyTray.StartId));
            Assert.Single(key.Values);
            Assert.Contains("Start with Windows, hidden: On", native.OnUi(() => Labels(icon)));
        });
    }

    private static T SpinFor<T>(Func<T?> read) where T : class
    {
        T? found = null;
        Assert.True(SpinWait.SpinUntil(() => (found = read()) is not null, TimeSpan.FromSeconds(10)), "Timed out waiting for the tray icon");
        return found!;
    }
}
