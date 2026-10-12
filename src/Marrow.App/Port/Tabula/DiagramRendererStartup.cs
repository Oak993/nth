// PORT-ONLY: web surfaces W16c (DESIGN.md 7.2; ED-NEW-web-w16c-1). The one line that turns the hidden Mermaid renderer on: the unit
// that creates the process's WebSurface (Visus, W8a, or the Tabula pane, W14) calls Bind with it, once. From then on every live
// answer's diagram line is drawn natively from the renderer's SVG, and the seat each window carries (NthUiDiagramSeat, installed
// at start-up) hosts the renderer's view off screen. Until it is called nothing is drawn and a diagram shows its source, as before.
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.App.Port.Tabula;

public static class DiagramRendererStartup
{
    /// <summary>Binds the process's diagram renderer over <paramref name="surface"/>; returns it (the runtime flow calls
    /// <see cref="MermaidRenderEngine.RefreshAvailability"/> on it after installing the web runtime).</summary>
    public static MermaidRenderEngine Bind(WebSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        var libraries = ArtifactRuntime.Shared?.Libraries ?? new ArtifactLibraryFiles(ArtifactLibraryFiles.DefaultFolder);
        var engine = new MermaidRenderEngine(surface, libraries, post: MainActor.Post);
        DiagramEngines.Shared.SetProcessDefault(engine);
        return engine;
    }
}
