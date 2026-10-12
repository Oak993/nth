using Avalonia;
using Avalonia.Controls;
using Marrow.Core.Models;
using Marrow.Core.Web;
using Marrow.Services.Web;
using Marrow.UI.Web;
using AvaloniaWindow = Avalonia.Controls.Window;

// PORT-ONLY: web surfaces W14 (E:/Droppy/work/design/web-surfaces/DESIGN.md 6.3 Export "pdf (print to PDF)", 6.4 "Markdown
// renders with nth's own MarkdownView natively (no web view)"; ED-NEW-web-w14-1). No Swift counterpart. A version printed or
// captured by a page that is not on screen: a Markdown document (drawn natively in the pane) printed to PDF, or a page whose
// pane shows another view. The page loads on the artifact's own site in a borderless window far off every screen, so
// nothing flashes, and is closed when the export is done. Its behaviour with the real engine is checked in the native pass.

namespace Marrow.App.Views.Artifacts;

internal sealed class TabulaOffscreen : IDisposable
{
    /// <summary>How long a page may take to load before the export goes ahead with what it has.</summary>
    public static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(10);

    private readonly WebSurface surface;
    private readonly WebSurfaceView view;
    private readonly ArtifactMount mount;
    private readonly AvaloniaWindow window;
    private bool disposed;

    private TabulaOffscreen(WebSurface surface, WebSurfaceView view, ArtifactMount mount, AvaloniaWindow window)
    {
        this.surface = surface;
        this.view = view;
        this.mount = mount;
        this.window = window;
    }

    public IWebEngineView? Engine => view.Engine;

    public static async Task<TabulaOffscreen?> OpenAsync(TabulaContext context, ArtifactSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (context.Runtime is not { } runtime || context.Surface() is not { } surface) return null;
        var view = surface.Open(WebSurfaceKind.Artifact, TabulaPreview.WindowId);
        var mount = runtime.Mount(view, snapshot.Artifact.Thread, snapshot.Artifact.Id, snapshot.Version.Number);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        mount.Ready += _ => ready.TrySetResult();
        var window = new AvaloniaWindow
        {
            WindowDecorations = WindowDecorations.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            CanResize = false,
            Width = 900,
            Height = 1200,
            Position = new PixelPoint(-32000, -32000),
            Content = new WebViewHost(view, context.LiveFactory),
        };
        window.Show();
        view.SetShown(true);
        await Task.WhenAny(ready.Task, Task.Delay(LoadTimeout, cancellationToken)).ConfigureAwait(true);
        return new TabulaOffscreen(surface, view, mount, window);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        mount.Dispose();
        surface.Close(view);
        window.Close();
    }
}
