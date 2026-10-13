// PORT-ONLY: web surfaces W16c (DESIGN.md 7.2 "inside a hidden, sandboxed artifacts-profile view"; 2.1 and 2.4: a web view is a child
// window that a host control must hold; ED-NEW-web-w16c-1). No Swift counterpart. The seat the hidden diagram renderer sits in: an
// invisible control in a window's overlay layer that hosts the renderer's web view (through the same WebViewHost every web view
// uses) 10,000 DIPs outside the window, so it takes no pixel, no pointer and no focus, yet is a real child window the page can run
// in. Only one seat hosts at a time (the first window's); when that window closes the view loses its host, the engine replaces the
// view on the next drawing and the next window's seat takes it. A build or machine with no renderer, or an engine that needs no
// host, leaves every seat empty.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Marrow.Core.Web;
using Marrow.UI.Web;

namespace Marrow.UI.NthUi.Diagram;

public sealed class NthUiDiagramSeat : Decorator
{
    /// <summary>Where the host sits: far enough outside any window that no display shows it.</summary>
    public const double Offscreen = -10_000;

    /// <summary>The size the hidden page lays out in.</summary>
    public static readonly Size PageSize = new(800, 600);

    private static readonly List<NthUiDiagramSeat> Seats = [];
    private static bool installed;

    private readonly Func<IWebViewSurface, Control>? liveFactory;
    private IDiagramEngineHost? subscribed;
    private IWebViewSurface? hosted;

    /// <param name="liveFactory">Makes the live content of the host; the platform web view by default. Tests pass a plain control.</param>
    public NthUiDiagramSeat(Func<IWebViewSurface, Control>? liveFactory = null)
    {
        this.liveFactory = liveFactory;
        IsHitTestVisible = false;
        Focusable = false;
        IsTabStop = false;
    }

    /// <summary>The seat that hosts the renderer now, or null.</summary>
    public static NthUiDiagramSeat? Active => Seats.FirstOrDefault();

    /// <summary>The host in the seat, or null while the renderer has no view (or another window's seat holds it).</summary>
    public WebViewHost? Host => Child as WebViewHost;

    /// <summary>
    /// Puts a seat in the overlay layer of every window that opens from now on. Called once at start-up; harmless when no renderer
    /// is bound.
    /// </summary>
    public static void Install()
    {
        if (installed) return;
        installed = true;
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => Place(window), RoutingStrategies.Direct);
    }

    /// <summary>Puts a seat in <paramref name="window"/>'s overlay layer (once its template has one).</summary>
    public static NthUiDiagramSeat? Place(Window window, Func<IWebViewSurface, Control>? liveFactory = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (OverlayLayer.GetOverlayLayer(window) is not { } layer)
        {
            EventHandler<RoutedEventArgs>? later = null;
            later = (_, _) =>
            {
                window.Loaded -= later;
                Place(window, liveFactory);
            };
            window.Loaded += later;
            return null;
        }
        var seat = new NthUiDiagramSeat(liveFactory);
        layer.Children.Add(seat);
        return seat;
    }

    /// <summary>Re-reads every seat's engine (a test binds another renderer; the process binds one late).</summary>
    public static void Refresh()
    {
        foreach (var seat in Seats.ToList()) seat.Sync();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Seats.Add(this);
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Seats.Remove(this);
        Unhost();
        Unsubscribe();
        Refresh();
    }

    private void Sync()
    {
        var engine = NthUiDiagrams.Engine as IDiagramEngineHost;
        if (!ReferenceEquals(engine, subscribed))
        {
            Unsubscribe();
            subscribed = engine;
            if (engine is not null) engine.HostedChanged += OnHostedChanged;
        }

        // Only the first attached seat hosts; the others wait for it to go.
        var wanted = ReferenceEquals(Seats.FirstOrDefault(), this) ? engine?.Hosted : null;
        if (ReferenceEquals(wanted, hosted)) return;
        Unhost();
        if (wanted is null) return;
        hosted = wanted;
        Child = new WebViewHost(wanted, liveFactory);
    }

    private void OnHostedChanged() => Refresh();

    private void Unhost()
    {
        hosted = null;
        Child = null;
    }

    private void Unsubscribe()
    {
        if (subscribed is not null) subscribed.HostedChanged -= OnHostedChanged;
        subscribed = null;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Child?.Measure(PageSize);
        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(new Point(Offscreen, Offscreen), PageSize));
        return default;
    }
}
