using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Sidebar;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Sidebar;

// SidebarView.swift:2122-2132 SettleCheck: a settled thread's check is filled until hovered, then the outline
// (:2124), and the two replace each other with .contentTransition(.symbolEffect(.replace)) (:2129, SymbolReplace).
[Collection("Headless")]
public sealed class SettleCheckReplaceTests(HeadlessSession headless)
{
    private static string[] Symbols(Control root) =>
        root.GetLogicalDescendants().OfType<SwiftImage>().Select(image => image.SymbolName ?? "").ToArray();

    [Fact]
    [Trait("Req", "PR-SIDEBAR-078")]
    public void Hovering_a_settled_check_replaces_the_filled_check_with_the_outline_in_two_steps() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource(); using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        var check = new SettleCheck(isSettled: true, isPopping: false, () => { });
        var window = new Avalonia.Controls.Window { Width = 100, Height = 100, Content = check };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.Equal(["checkmark.circle.fill"], Symbols(check));
            var centre = check.TranslatePoint(new Point(check.Bounds.Width / 2, check.Bounds.Height / 2), window)!.Value;
            window.MouseMove(centre, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["checkmark.circle", "checkmark.circle.fill"], Symbols(check).Order().ToArray()); // both, while the filled one goes
            clock.Advance(TimeSpan.FromSeconds(0.4)); frames.Advance(TimeSpan.FromSeconds(0.4));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(["checkmark.circle"], Symbols(check));
        }
        finally { window.Close(); }
    });
}
