using Marrow.Services.Web;
using Marrow.Testing;

namespace Marrow.Services.Tests.Web;

/// <summary>
/// W7 test rig (DESIGN 13.2): one <see cref="WebSurface"/> over <see cref="FakeWebEngine"/> with a manual clock and a
/// post seam the test runs by hand, so every async step of the engine layer is deterministic.
/// </summary>
internal sealed class WebRig : IDisposable
{
    public const string UserData = @"C:\nth-test\WebView2";

    private readonly SynchronizationContext? context = SynchronizationContext.Current;

    public WebRig(string? installed = "141.0.3537.71")
    {
        // xUnit's concurrency context would post each continuation elsewhere; with none, an awaited fake task that the
        // test completes continues right there, so the order of every step is the test's.
        SynchronizationContext.SetSynchronizationContext(null);
        Engine = new FakeWebEngine(Time) { InstalledVersion = installed };
        Surface = new WebSurface(Engine, new WebProfiles(UserData, "en-US"), Time, Posted.Add);
    }

    public ManualTimeProvider Time { get; } = new();

    public FakeWebEngine Engine { get; }

    public WebSurface Surface { get; }

    /// <summary>Work the surface posted to run after the current input event.</summary>
    public List<Action> Posted { get; } = [];

    /// <summary>Opens a view and attaches a platform view to it, as the host does.</summary>
    public WebSurfaceView OpenLive(WebSurfaceKind kind = WebSurfaceKind.VisusTab, string window = "w1", string? url = "https://a.test/", string? folder = null, bool shown = false)
    {
        var view = Surface.Open(kind, window, url, folder);
        view.AttachNative(1, 2);
        if (shown) view.SetShown(true);
        return view;
    }

    public static FakeWebView Fake(WebSurfaceView view) => Assert.IsType<FakeWebView>(view.Engine);

    /// <summary>The result of a task the test expects to have finished already (the fakes answer in line).</summary>
    public static T Done<T>(Task<T> task)
    {
        Assert.True(task.IsCompletedSuccessfully, task.Status.ToString());
        return task.Result;
    }

    /// <summary>The decision a request ended with, which must be made by now.</summary>
    public static T Decided<T>(WebDeferredRequest<T> request) => Done(request.Decision);

    public void RunPosted()
    {
        var work = Posted.ToList();
        Posted.Clear();
        foreach (var item in work) item();
    }

    public void Dispose()
    {
        Surface.Dispose();
        SynchronizationContext.SetSynchronizationContext(context);
    }
}
