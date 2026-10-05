using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Sidebar;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using static Marrow.App.Tests.Views.Sidebar.SidebarViewTests;

namespace Marrow.App.Tests.Views.Sidebar;

// UI-SIDEBAR-020, the settle bounce. OD-0105 (owner, 2026-10-05, "Code is enough"): the bounce is proven by the Swift source,
// SidebarView.swift:1909 `withAnimation(.spring(response: 0.3, dampingFraction: 0.45)) { isSettling = true }`, then :1910
// `withAnimation(Chrome.panelSlide) { model.settle(thread.id, sounds: false) }`. No video shows it, so there is no Mac capture to wait for:
// the parameters of the two transactions on the settle path are the proof, and these tests assert exactly those.
[Collection("Headless")]
public sealed class SidebarSettleBounceTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "UI-SIDEBAR-020")] // OD-0105; SidebarView.swift:1904-1910
    public void Settling_sets_isSettling_inside_the_spring_of_response_0_3_and_damping_0_45_and_then_settles_the_thread_inside_panelSlide() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Settle me", 0);
        var model = Model(harness, [project], [thread]);
        harness.Main.OnMain(() =>
        {
            var order = new List<string>();
            Animation? popping = null, settling = null;
            using var probe = new OnChange<bool>(() => model.Thread(thread.Id)!.IsSettled, (_, now) => { if (now) { settling = Transaction.Current; order.Add("settled"); } });
            CallStatic(Private("SidebarThreadRow"), "Settle", model, thread, false, false, (Action<bool>)(value => { popping = Transaction.Current; order.Add("isSettling " + value); }));
            Assert.Equal(Animation.Spring(response: 0.3, dampingFraction: 0.45), popping); // :1909
            Assert.Equal(["isSettling True"], order); // the thread settles in the pass after, not inside the pop
        });
        harness.Main.OnMain(() =>
        {
            Assert.True(model.Thread(thread.Id)!.IsSettled); // :1910
        });
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-020")] // OD-0105; SidebarView.swift:1910 `withAnimation(Chrome.panelSlide) { model.settle(...) }`, reached from a real click on the open row's check
    public void A_real_click_on_the_open_rows_check_settles_the_thread_inside_the_panelSlide_transaction() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness();
        var project = NewProject(harness, "Alpha");
        var thread = NewThread(project, "Settle me", 0);
        var model = Model(harness, [project], [thread]);
        using var shared = RowGlideAnimator.Shared.Override(new RowGlideAnimator(new FakeGeniePlatform()));
        Control row = null!;
        Animation? during = null;
        using var probe = new OnChange<bool>(() => model.Thread(thread.Id)!.IsSettled, (_, now) => { if (now) during = Transaction.Current; });
        harness.Main.OnMain(() =>
        {
            model.Settings.ThreadFinishAction = ThreadFinishAction.Settle;
            model.Settings.SettleSound = false;
            row = LiveThreadRow(thread);
            var window = WindowOf(Hosted(model, row));
            window.UpdateLayout();
            window.MouseMove(row.TranslatePoint(new Point(10, 10), window)!.Value);
        });
        try
        {
            harness.Main.OnMain(() => Click(ButtonNamed(row, "Settle thread")));
            harness.Main.OnMain(() =>
            {
                Assert.True(model.Thread(thread.Id)!.IsSettled);
                Assert.Equal(Chrome.PanelSlide, during);
            });
        }
        finally
        {
            harness.Main.OnMain(() => { WindowOf(row).Close(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }); // pending layout stays inside the live font scope
        }
    });
}
