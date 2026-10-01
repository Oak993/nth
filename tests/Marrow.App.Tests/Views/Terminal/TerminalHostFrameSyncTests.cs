using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Terminal;
using Marrow.App.Views.Terminal.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Terminal;

// Source: DroppyCode/App/Views/Terminal/TerminalPanel.swift (B3 52bc792) TerminalHostContainer.syncFrame :312-317: nothing
// happens unless the hosted view is still this container's own subview, both bounds are non-zero and the frame
// differs; otherwise the frame is set to the bounds, which is what makes the terminal re-wrap. The guard matrix is
// driven on a container holding a real terminal surface (no shell is started: ResizeCount is the observable re-wrap).
[Collection("Headless")]
public sealed class TerminalHostFrameSyncTests(HeadlessSession headless)
{
    private static void Settle(AvaloniaWindow window)
    {
        for (var pass = 0; pass < 5; pass++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    // syncFrame is private and its only callers are timers and window-resize notifications, so the guard matrix is
    // reached directly (ED-119: nothing public observes the individual guards).
    private static void SyncFrame(TerminalHostContainer container) =>
        typeof(TerminalHostContainer).GetMethod("SyncFrame", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(container, [false]);

    private static (TerminalHostContainer Container, TerminalHostView View, AvaloniaWindow Window) Hosted(double width = 400, double height = 300)
    {
        var view = new TerminalHostView { Font = TerminalFont.Preferred() };
        var container = new TerminalHostContainer { HostedView = view };
        var window = new AvaloniaWindow { Width = width, Height = height, Content = container };
        window.Show();
        Settle(window);
        return (container, view, window);
    }

    [Fact]
    [Trait("Req", "PR-TERM-035")] // App/Views/Terminal/TerminalPanel.swift:312-317 guard hostedView.frame != bounds
    public void A_frame_that_already_equals_the_bounds_is_left_alone_with_no_reflow() => headless.OnUiThread(() =>
    {
        var (container, view, window) = Hosted();
        try
        {
            Assert.Equal(container.Bounds.Size, view.Bounds.Size);
            var syncs = container.SyncFrameCount;
            var reflows = view.ResizeCount;
            SyncFrame(container);
            SyncFrame(container);
            Assert.Equal(syncs, container.SyncFrameCount);
            Assert.Equal(reflows, view.ResizeCount);
            Assert.Equal(container.Bounds.Size, view.Bounds.Size);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    [Trait("Req", "PR-TERM-035")] // App/Views/Terminal/TerminalPanel.swift:312-317 hostedView.frame = bounds
    public void A_different_frame_is_set_to_the_container_bounds_which_re_wraps_the_terminal_once() => headless.OnUiThread(() =>
    {
        var (container, view, window) = Hosted();
        try
        {
            view.ResizeTo(new Size(120, 90)); // the terminal wrapped to a smaller frame ...
            view.Arrange(new Rect(0, 0, 120, 90)); // ... which then drifted from the container's bounds
            Assert.NotEqual(container.Bounds.Size, view.Bounds.Size);
            var syncs = container.SyncFrameCount;
            var reflows = view.ResizeCount;

            SyncFrame(container);

            Assert.Equal(container.Bounds.Size, view.Bounds.Size);
            Assert.Equal(syncs + 1, container.SyncFrameCount);
            Assert.Equal(reflows + 1, view.ResizeCount); // the single action that triggers the terminal's re-wrap
            SyncFrame(container); // now equal again: nothing more
            Assert.Equal(syncs + 1, container.SyncFrameCount);
            Assert.Equal(reflows + 1, view.ResizeCount);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Req", "PR-TERM-035")] // App/Views/Terminal/TerminalPanel.swift:312-317 guard bounds.width > 0, bounds.height > 0
    public void A_container_with_a_zero_width_or_zero_height_does_not_sync_and_the_terminal_keeps_its_last_geometry(bool zeroWidth) => headless.OnUiThread(() =>
    {
        var (container, view, window) = Hosted();
        try
        {
            if (zeroWidth) container.Width = 0; else container.Height = 0;
            Settle(window);
            Assert.True(zeroWidth ? container.Bounds.Width == 0 : container.Bounds.Height == 0);
            view.ResizeTo(new Size(120, 90));
            view.Arrange(new Rect(0, 0, 120, 90)); // the terminal's last non-zero geometry
            var syncs = container.SyncFrameCount;
            var reflows = view.ResizeCount;

            SyncFrame(container);

            Assert.Equal(new Size(120, 90), view.Bounds.Size);
            Assert.Equal(syncs, container.SyncFrameCount);
            Assert.Equal(reflows, view.ResizeCount);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    [Trait("Req", "PR-TERM-035")] // App/Views/Terminal/TerminalPanel.swift:312-313 guard hostedView.superview === self
    public void A_view_that_has_been_reparented_to_another_container_is_not_touched_by_the_old_one() => headless.OnUiThread(() =>
    {
        var view = new TerminalHostView { Font = TerminalFont.Preferred() };
        var container = new TerminalHostContainer { HostedView = view };
        var other = new TerminalHostContainer();
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("400,200"), RowDefinitions = new RowDefinitions("300") };
        Grid.SetColumn(other, 1);
        grid.Children.Add(container);
        grid.Children.Add(other);
        var window = new AvaloniaWindow { Width = 600, Height = 300, Content = grid };
        window.Show();
        try
        {
            Settle(window);
            other.HostedView = view; // the view moves to the other container (TerminalHost.attach)
            Settle(window);
            Assert.Null(container.HostedView);
            Assert.Equal(other.Bounds.Size, view.Bounds.Size);
            var syncs = container.SyncFrameCount;
            var reflows = view.ResizeCount;
            var frame = view.Bounds;

            SyncFrame(container); // the old container's bounds are 400x300, the view lives in a 200x300 one

            Assert.Equal(syncs, container.SyncFrameCount);
            Assert.Equal(reflows, view.ResizeCount);
            Assert.Equal(frame, view.Bounds);
            Assert.Same(view, other.HostedView);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });
}
