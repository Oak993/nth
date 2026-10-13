using System.Reflection;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Diff;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Xunit;
using static Marrow.App.Tests.Fixtures.DiffInspectorTree;

namespace Marrow.App.Tests.Views.Diff;

// ED-NEW-perf-memory-1: the runtime lives as long as the thread, so every changes inspector the popover shows must stop
// listening to it when it closes. Before, each open left a live listener that rebuilt its chrome row on every runtime change
// and parsed the diff again on every revision. Source of the behaviour kept: DroppyCode/App/Views/Diff/DiffInspector.swift :110-120.
[Collection("Headless")]
public sealed class DiffInspectorListenerTests(HeadlessSession headless)
{
    private static int Listeners(ThreadRuntime runtime) =>
        ((Delegate?)typeof(ObservableModel).GetField("PropertyChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(runtime))?.GetInvocationList().Length ?? 0;

    [Fact]
    [Trait("Req", "PR-GIT-132")]
    public void Each_inspector_that_closes_leaves_no_listener_on_the_runtime() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patches("a.txt"));
        var baseline = Listeners(rig.Runtime);

        for (var open = 1; open <= 6; open++)
        {
            var inspector = rig.NewInspector();
            var window = rig.Present(inspector);
            rig.FinishLoad(inspector);
            Assert.Equal(baseline + 1, Listeners(rig.Runtime)); // one open inspector, one listener
            rig.Store.Main.OnMain(() => window.Content = null); // the popover drops its content on close
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(baseline, Listeners(rig.Runtime));
        }
    });

    [Fact]
    [Trait("Req", "PR-GIT-132")]
    public void A_closed_inspector_neither_rebuilds_its_chrome_nor_loads_again_when_the_runtime_changes() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patches("a.txt"));
        var closed = rig.NewInspector();
        var closedWindow = rig.Present(closed);
        rig.FinishLoad(closed);
        var open = rig.NewInspector();
        rig.Present(open);
        rig.FinishLoad(open);
        var closedMenu = Assert.Single(Descendants<ChromeTextMenu>(closed));
        var openMenu = Assert.Single(Descendants<ChromeTextMenu>(open));
        var closedTask = typeof(DiffInspector).GetField("loadTask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(closed);

        rig.Store.Main.OnMain(() => closedWindow.Content = null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        rig.Store.Main.OnMain(rig.Runtime.NoteDiffChanged); // a revision: the open one loads again, the closed one must not
        rig.FinishLoad(open);

        Assert.NotSame(openMenu, Assert.Single(Descendants<ChromeTextMenu>(open))); // control: a live inspector follows the runtime
        Assert.Same(closedMenu, Assert.Single(Descendants<ChromeTextMenu>(closed)));
        Assert.Same(closedTask, typeof(DiffInspector).GetField("loadTask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(closed));
    });

    [Fact]
    [Trait("Req", "PR-GIT-132")]
    public void An_inspector_shown_again_follows_the_runtime_once_more_and_catches_up_on_what_it_missed() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patches("a.txt"));
        var baseline = Listeners(rig.Runtime);
        var inspector = rig.NewInspector();
        var first = rig.Present(inspector);
        rig.FinishLoad(inspector);
        rig.Store.Main.OnMain(() => first.Content = null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(baseline, Listeners(rig.Runtime));
        rig.Store.Main.OnMain(rig.Runtime.NoteDiffChanged); // missed while closed

        var second = rig.Present(inspector);
        rig.FinishLoad(inspector);

        Assert.Equal(baseline + 1, Listeners(rig.Runtime)); // once, not twice
        Assert.Single(Descendants<DiffFileCard>(inspector));
        Assert.NotNull(second);
    });
}
