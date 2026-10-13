using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Assistant.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Window.Native;
using Marrow.Core.Port.Assistant;
using static Marrow.App.Tests.Fixtures.TeamSamples;

namespace Marrow.App.Tests.Assistant.Org;

// Owed to the lead: real-window transition sampling. Written only; the CU-DX-FIX worker never runs Native tests.
[Collection("WindowManagerNative")]
[Trait("Decision", "ED-NEW-dextra-fix-10")]
[Trait("Decision", "ED-NEW-dextra-fix-12")]
public sealed class DextraFixMotionNativeTests(WindowManagerNativeSession native)
{
    private OrgChart Host()
    {
        var chart = new OrgChart();
        chart.Show(TeamSnapshot.Build(Team(ChannelA, ChannelB), [], []));
        var window = new Avalonia.Controls.Window { Width = 900, Height = 700, ShowInTaskbar = false };
        native.Register(window);
        window.Template = new FuncControlTemplate<Avalonia.Controls.Window>((owner, _) => new ContentPresenter { Content = owner.Content });
        window.Content = chart;
        window.Show();
        window.Activate();
        window.UpdateLayout();
        return chart;
    }

    [Fact]
    public void Retargeting_an_in_flight_glide_keeps_the_current_drawn_position()
    {
        native.Run(_ =>
        {
            var chart = native.OnUi(Host);
            Thread.Sleep(300); // Native-only: await a real window's first animation frames.
            native.OnUi(() => { chart.Show(TeamSnapshot.Build(Team(ChannelA, ChannelB, Store), [], [])); chart.UpdateLayout(); });
            Thread.Sleep(100); // Native-only: sample during the 420 ms glide, not after it.
            native.OnUi(() =>
            {
                var node = chart.NodeOf(ChannelA.Id)!;
                var shift = Assert.IsType<TranslateTransform>(node.RenderTransform);
                Assert.True(Math.Abs(shift.X) + Math.Abs(shift.Y) > .5);
                var before = node.TranslatePoint(default, chart.Panel)!.Value;
                chart.Show(TeamSnapshot.Build(Team(Store, ChannelA, ChannelB), [], []));
                chart.UpdateLayout();
                var after = node.TranslatePoint(default, chart.Panel)!.Value;
                Assert.InRange(Math.Abs(after.X - before.X), 0, .5);
                Assert.InRange(Math.Abs(after.Y - before.Y), 0, .5);
            });
        }, nameof(Retargeting_an_in_flight_glide_keeps_the_current_drawn_position), typeof(DextraFixMotionNativeTests));
    }

    [Fact]
    public void Connectors_fade_both_ways_and_retarget_without_an_opacity_cut()
    {
        native.Run(_ =>
        {
            var chart = native.OnUi(Host);
            Thread.Sleep(300); // Native-only: initial window frames.
            native.OnUi(() =>
            {
                var lines = chart.Panel.Connectors;
                var before = lines.Opacity;
                var geometry = lines.Plan;
                chart.Show(TeamSnapshot.Build(Team(ChannelA, ChannelB, Store), [], []));
                chart.UpdateLayout();
                Assert.InRange(Math.Abs(lines.Opacity - before), 0, .01);
                Assert.Same(geometry, lines.Plan);
            });
            Thread.Sleep(90); // Native-only: fade-out midpoint.
            native.OnUi(() =>
            {
                var lines = chart.Panel.Connectors;
                Assert.InRange(lines.Opacity, .001, .999);
                var before = lines.Opacity;
                var geometry = lines.Plan;
                chart.Show(TeamSnapshot.Build(Team(Store, ChannelA, ChannelB), [], []));
                chart.UpdateLayout();
                Assert.InRange(Math.Abs(lines.Opacity - before), 0, .01);
                Assert.Same(geometry, lines.Plan);
            });
            Thread.Sleep(510); // Native-only: retargeted move has ended; the 260 ms return fade is in flight.
            Assert.InRange(native.OnUi(() => chart.Panel.Connectors.Opacity), .001, .999);
            Thread.Sleep(300); // Native-only: return fade complete.
            Assert.InRange(native.OnUi(() => chart.Panel.Connectors.Opacity), .99, 1);
        }, nameof(Connectors_fade_both_ways_and_retarget_without_an_opacity_cut), typeof(DextraFixMotionNativeTests));
    }
}
