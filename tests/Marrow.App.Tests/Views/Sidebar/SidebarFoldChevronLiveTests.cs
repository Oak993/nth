using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Sidebar;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using static Marrow.App.Tests.Views.Sidebar.SidebarViewTests;

namespace Marrow.App.Tests.Views.Sidebar;

// Source: DroppyCode/App/Views/Sidebar/SidebarView.swift:1803-1814 (the chevron), :882 (onToggleFold: hasHelpers ? { fold(thread.id) } : nil)
// and :487-490 (fold: withAnimation(Chrome.panelSlide) { model.toggleHelpersFold }). UI-SIDEBAR-022 and PR-SIDEBAR-110 had
// the chevron's callback proven on the static overlay only; this clicks the chevron on a live SidebarView and reads the
// transaction the model's change happens under.
[Collection("Headless")]
public sealed class SidebarFoldChevronLiveTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "UI-SIDEBAR-022")] // :1803-1814 the chevron; :487-490 fold(_:): `withAnimation(Chrome.panelSlide) { model.toggleHelpersFold(id) }`
    [Trait("Req", "PR-SIDEBAR-110")]
    public void Clicking_the_hovered_rows_Hide_helpers_chevron_in_the_live_sidebar_folds_the_helpers_inside_the_panelSlide_transaction() => headless.OnUiThread(() =>
    {
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var parent = NewThread(project, "Parent", 0) with { ExpandsHelpers = true };
        var helper = NewThread(project, "Helper", 1) with { ParentThreadID = parent.Id };
        var model = Model(harness, [project], [parent, helper]);
        using var shared = RowGlideAnimator.Shared.Override(new RowGlideAnimator(new FakeGeniePlatform()));
        SidebarView view = null!;
        Animation? during = null;
        var changes = 0;
        using var probe = new OnChange<bool>(() => model.Thread(parent.Id)!.ExpandsHelpers, (_, _) => { changes++; during = Transaction.Current; });
        try
        {
            harness.Main.OnMain(() => view = Attached(model));
            Control row = null!;
            harness.Main.OnMain(() =>
            {
                row = LogicalQuery.SelfAndDescendants<Control>(view)
                    .Single(c => c.GetType() == Private("SidebarThreadRow") && Texts(c).Contains("Parent"));
                var window = WindowOf(view);
                window.UpdateLayout();
                window.MouseMove(row.TranslatePoint(new Point(10, 10), window)!.Value);
            });
            harness.Main.AdvanceMilliseconds(300);
            harness.Main.OnMain(() => { manual.Advance(TimeSpan.FromMilliseconds(300)); WindowOf(view).UpdateLayout(); });
            harness.Main.OnMain(() =>
            {
                Assert.Equal(0, changes);
                PointerClick(ButtonNamed(row, "Hide helpers")); // the chevron of the hovered row
            });
            harness.Main.OnMain(() =>
            {
                Assert.False(model.Thread(parent.Id)!.ExpandsHelpers); // :488 toggleHelpersFold
                Assert.Equal(1, changes);
                Assert.Equal(Chrome.PanelSlide, during); // :487 withAnimation(Chrome.panelSlide)
            });
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });
}
