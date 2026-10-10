using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Port.Ignis.Runtime;
using Marrow.App.Port.Ignis.Views;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Tests.Port.Ignis;
using Marrow.App.Views.Sidebar;
using Marrow.Core.Port.Ignis;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using static Marrow.App.Tests.Views.Sidebar.SidebarViewTests;

namespace Marrow.App.Tests.Views.Sidebar;

// ED-NEW-ignis-guards: the Ignis keep-warm mark in a live sidebar (owner 2026-10-10, variant B): a row Ignis keeps warm carries
// the mark, its zone sits just before the row's trailing content, and the row's context menu offers "Turn off" / "Turn on"
// (the pill by keyboard). Unbound, the rows are exactly as before (every other sidebar test).
[Collection("Headless")]
[Trait("Decision", "OD-0134")]
public sealed class SidebarIgnisMarkTests(HeadlessSession headless)
{
    [Fact]
    public void A_warm_chats_row_carries_the_mark_and_its_menu_offers_Turn_off_then_Turn_on() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var warm = NewThread(project, "Warm chat", 0);
        var plain = NewThread(project, "Plain chat", 1);
        var model = Model(harness, [project], [warm, plain]);
        var settings = new IgnisSettings(new InMemorySettingsStore());
        var ledger = new FakeSavingsLedger();
        var controller = harness.Main.OnMain(() => new IgnisController(settings, new FakeIgnisPoints(), ledger, new FakeIgnisEngine(), new FakeIgnisEnvironment()));
        var port = new FakeIgnisPort(warm.Id);
        harness.Main.OnMain(() =>
        {
            controller.Attach(port);
            controller.Observe(warm.Id, IgnisSamples.Usage(read: 1_000, write1h: 200));
            controller.TurnFinished(warm.Id);
        });
        using var bound = IgnisViews.Override(controller);
        using var shared = RowGlideAnimator.Shared.Override(new RowGlideAnimator(new FakeGeniePlatform()));
        SidebarView view = null!;
        try
        {
            harness.Main.OnMain(() => view = Attached(model));
            harness.Main.OnMain(() =>
            {
                WindowOf(view).UpdateLayout();
                var rows = LogicalQuery.SelfAndDescendants<Control>(view).OfType<SidebarThreadRow>().ToList();
                var warmRow = rows.Single(r => Texts(r).Contains("Warm chat"));
                var plainRow = rows.Single(r => Texts(r).Contains("Plain chat"));
                Assert.NotNull(warmRow.Ember);
                Assert.True(warmRow.Ember!.Shows);
                Assert.False(plainRow.Ember!.Shows); // never kept warm: nothing
                Assert.False(plainRow.Ember.IsVisible);
                Assert.Same(warmRow.Ember, warmRow.GetVisualDescendants().OfType<IgnisSidebarMark>().Single());

                // the zone ends where the title's room ends: the row's padding plus its trailing clearance
                var zone = warmRow.Ember.Zone;
                var zoneRight = zone.TranslatePoint(new Point(zone.Bounds.Width, 0), warmRow)!.Value.X;
                var face = warmRow.GetVisualDescendants().OfType<ThreadRowFace>().Single();
                var titleRoomRight = face.Bounds.Width - ((Border)face.Child!).Padding.Right;
                Assert.Equal(titleRoomRight, zoneRight, 3);
                Assert.Equal(warmRow.Bounds.Height, zone.Bounds.Height, 3);

                var lit = warmRow.WithIgnis([]);
                Assert.Equal(IgnisWords.TurnOff, Assert.Single(lit).Title);
                var withOthers = warmRow.WithIgnis([new RowAction { Title = "Rename", Action = () => { } }]);
                Assert.Equal(IgnisWords.TurnOff, withOthers[^1].Title);
                Assert.True(withOthers[^1].StartsGroup);
                Assert.Empty(plainRow.WithIgnis([]));
                withOthers[^1].Action();
            });
            harness.Main.OnMain(() =>
            {
                var warmRow = LogicalQuery.SelfAndDescendants<Control>(view).OfType<SidebarThreadRow>().Single(r => Texts(r).Contains("Warm chat"));
                Assert.Equal(IgnisEmber.Cooled, controller.Ember(warm.Id).Ember);
                Assert.True(warmRow.Ember!.IsVisible); // dimmed, still there
                var cooled = Assert.Single(warmRow.WithIgnis([]));
                Assert.Equal(IgnisWords.TurnOn, cooled.Title);
                cooled.Action();
                Assert.Equal(IgnisEmber.Lit, controller.Ember(warm.Id).Ember);
            });
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });
}
