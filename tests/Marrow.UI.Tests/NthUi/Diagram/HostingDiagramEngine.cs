using Marrow.Core.Web;

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
