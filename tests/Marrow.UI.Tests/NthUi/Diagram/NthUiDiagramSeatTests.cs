using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.Core.Web;
using Marrow.UI.NthUi.Diagram;
using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Tests.Web;
using Marrow.UI.Web;

namespace Marrow.UI.Tests.NthUi.Diagram;

/// <summary>A renderer whose view needs a window to live in.</summary>
internal sealed class HostingDiagramEngine : IDiagramEngine, IDiagramEngineHost
{
    public IWebViewSurface? Hosted { get; private set; }

    public event Action? HostedChanged;

    public bool IsAvailable => true;

    public event Action? AvailabilityChanged
    {
        add { }
        remove { }
    }

    public void Prepare()
    {
    }

    public Task<DiagramRenderResult> RenderAsync(DiagramRenderRequest request, CancellationToken cancellationToken) =>
        Task.FromException<DiagramRenderResult>(new DiagramRenderException(DiagramFailure.Unavailable, "not used"));

    public void Host(IWebViewSurface? surface)
    {
        Hosted = surface;
        HostedChanged?.Invoke();
    }
}

// DESIGN.md 7.2 and 2.1 (web surfaces W16c; ED-NEW-web-w16c-1): the hidden renderer's web view is a child window, so a control must
// hold it. The seat does that invisibly: far outside the window, with no pointer or focus, one window at a time, following the
// engine's view as it opens and closes.
[Collection("Headless")]
public sealed class NthUiDiagramSeatTests(HeadlessSession headless)
{
    private static Control Plain(IWebViewSurface _) => new Border { Background = Avalonia.Media.Brushes.Red };

    private static (Window Window, NthUiDiagramSeat Seat) Open()
    {
        var window = HeadlessPopupHost.OverlayWindow(new Border());
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var seat = NthUiDiagramSeat.Place(window, Plain) ?? throw new Xunit.Sdk.XunitException("the window has no overlay layer");
        window.UpdateLayout();
        return (window, seat);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void The_engines_view_is_hosted_far_outside_the_window_and_taken_out_when_the_engine_closes_it() => headless.OnUiThread(() =>
    {
        var engine = new HostingDiagramEngine();
        using var _ = DiagramEngines.Shared.Override(engine);
        var (window, seat) = Open();
        try
        {
            Assert.Null(seat.Host);
            var surface = new FakeWebViewSurface();
            engine.Host(surface);
            window.UpdateLayout();
            Assert.NotNull(seat.Host);
            Assert.Same(surface, seat.Host!.Surface);
            Assert.True(seat.Host.Bounds.X <= NthUiDiagramSeat.Offscreen + 1 && seat.Host.Bounds.Y <= NthUiDiagramSeat.Offscreen + 1, $"hosted at {seat.Host.Bounds}");
            Assert.Equal(NthUiDiagramSeat.PageSize, seat.Host.Bounds.Size);
            Assert.False(seat.IsHitTestVisible);
            Assert.False(seat.Focusable);
            Assert.Equal(default, seat.Bounds.Size);
            engine.Host(null);
            Assert.Null(seat.Host);
            var next = new FakeWebViewSurface();
            engine.Host(next);
            Assert.Same(next, seat.Host!.Surface);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void Only_the_first_window_hosts_and_the_next_takes_over_when_it_closes() => headless.OnUiThread(() =>
    {
        var engine = new HostingDiagramEngine();
        using var _ = DiagramEngines.Shared.Override(engine);
        var (first, a) = Open();
        var (second, b) = Open();
        try
        {
            var surface = new FakeWebViewSurface();
            engine.Host(surface);
            Assert.NotNull(a.Host);
            Assert.Null(b.Host);
            first.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(a.Host);
            Assert.Same(surface, b.Host!.Surface); // the engine would replace the view whose host went away; the next seat holds whatever it opens
        }
        finally
        {
            second.Close();
        }
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    public void An_engine_that_needs_no_host_or_none_at_all_leaves_the_seat_empty() => headless.OnUiThread(() =>
    {
        var (window, seat) = Open();
        try
        {
            Assert.Null(seat.Host); // the process has no renderer
            using var _ = DiagramEngines.Shared.Override(new FakeDiagramEngine());
            NthUiDiagramSeat.Refresh();
            Assert.Null(seat.Host);
        }
        finally
        {
            window.Close();
        }
    });
}
