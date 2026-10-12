// PORT-ONLY: web surfaces W16c (DESIGN.md 7.2 "Diagrams (Mermaid): not a native Mermaid engine. The Mermaid source is rendered to
// SVG once per completed statement by the vendored Mermaid library inside a hidden, sandboxed artifacts-profile view (no
// network, no bridge), then the SVG is sanitised ... and drawn natively"; 14 W16c; ED-NEW-web-w16c-1). No Swift counterpart.
// The seam between the live view (Marrow.UI, which cannot reach Services, ED-064) and the hidden Mermaid renderer
// (Marrow.Services, which owns every web view): the view asks for a diagram's SVG, the renderer answers with markup that the
// view then sanitises itself, so nothing the page produced is trusted on this side of the seam.
using Marrow.Core.Port;

namespace Marrow.Core.Web;

/// <summary>The colours and type a diagram is drawn in: Mermaid's base theme with these variables, so the diagram sits in
/// nth's glass language and follows the thread's light or dark theme.</summary>
/// <param name="Dark">Whether the thread is dark (Mermaid's <c>darkMode</c>, which flips the colours it derives).</param>
/// <param name="FontFamily">The font family list the labels are measured and drawn in.</param>
/// <param name="FontSize">The label size in pixels.</param>
/// <param name="Variables">Mermaid theme variables (<c>primaryColor</c>, <c>lineColor</c> ...), opaque hex colours.</param>
public sealed record DiagramTheme(bool Dark, string FontFamily, double FontSize, IReadOnlyDictionary<string, string> Variables)
{
    /// <summary>A key that is equal for equal themes, so a drawn diagram can be reused.</summary>
    public string Key
    {
        get
        {
            var builder = new System.Text.StringBuilder();
            builder.Append(Dark ? 'd' : 'l').Append('|').Append(FontFamily).Append('|').Append(FontSize.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            foreach (var (name, value) in Variables.OrderBy(v => v.Key, StringComparer.Ordinal)) builder.Append('|').Append(name).Append('=').Append(value);
            return builder.ToString();
        }
    }
}

/// <summary>One diagram to draw.</summary>
/// <param name="Source">The Mermaid source (at most <see cref="DiagramLimits.MaxSource"/> characters).</param>
/// <param name="Theme">How it is drawn.</param>
public sealed record DiagramRenderRequest(string Source, DiagramTheme Theme);

/// <summary>The renderer's answer: the SVG markup exactly as the page produced it, with every style already written into
/// the elements. It is untrusted; the caller sanitises it.</summary>
public sealed record DiagramRenderResult(string Svg);

/// <summary>Why a diagram was not drawn.</summary>
public enum DiagramFailure
{
    /// <summary>No renderer in this build, no web runtime, or no window to host it; the caller shows the source.</summary>
    Unavailable,

    /// <summary>Mermaid could not read the source (an unfinished statement while streaming, or a mistake).</summary>
    Syntax,

    /// <summary>The source or the drawing was larger than nth draws.</summary>
    TooLarge,

    /// <summary>The renderer did not answer in time.</summary>
    Timeout,

    /// <summary>The renderer stopped (its page crashed, or it was shut down) before it answered.</summary>
    Stopped,
}

/// <summary>A diagram could not be drawn.</summary>
public sealed class DiagramRenderException(DiagramFailure failure, string message, int? line = null) : Exception(message)
{
    public DiagramFailure Failure { get; } = failure;

    /// <summary>The line of the source Mermaid complained about, when it said.</summary>
    public int? Line { get; } = line;
}

/// <summary>Limits the renderer holds to.</summary>
public static class DiagramLimits
{
    /// <summary>The longest Mermaid source drawn (the body limit of a <c>diagram</c> in docs/nth-ui.md).</summary>
    public const int MaxSource = 262_144;

    /// <summary>The longest SVG accepted from the renderer.</summary>
    public const int MaxSvg = 4_000_000;
}

/// <summary>Draws Mermaid diagrams to SVG, once per completed statement, off the live view's thread of work.</summary>
public interface IDiagramEngine
{
    /// <summary>False when no diagram can be drawn at all (the view then shows the source at once).</summary>
    bool IsAvailable { get; }

    /// <summary>Raised when <see cref="IsAvailable"/> changes.</summary>
    event Action? AvailabilityChanged;

    /// <summary>Starts the renderer ahead of the first drawing (a diagram line has just appeared), so its start-up overlaps with the
    /// stream. Never throws; does nothing when nothing can be drawn.</summary>
    void Prepare();

    /// <summary>Draws one diagram. Throws <see cref="DiagramRenderException"/> when it cannot, and
    /// <see cref="OperationCanceledException"/> when <paramref name="cancellationToken"/> ends it first.</summary>
    Task<DiagramRenderResult> RenderAsync(DiagramRenderRequest request, CancellationToken cancellationToken);
}

/// <summary>An engine whose hidden web view needs a window to live in (the UI puts the surface in an off-screen host, as it does
/// for any web view, and takes it out again when the engine closes the view).</summary>
public interface IDiagramEngineHost
{
    /// <summary>The view to host now, or null while none is open.</summary>
    IWebViewSurface? Hosted { get; }

    /// <summary>Raised on the UI thread when <see cref="Hosted"/> changes.</summary>
    event Action? HostedChanged;
}

/// <summary>The process's diagram renderer, bound at start-up; until then nothing can be drawn and every diagram shows its
/// source.</summary>
public static class DiagramEngines
{
    public static Ambient<IDiagramEngine> Shared { get; } = new(Unavailable.Instance);

    /// <summary>The engine of a build with no renderer.</summary>
    public sealed class Unavailable : IDiagramEngine
    {
        public static Unavailable Instance { get; } = new();

        public bool IsAvailable => false;

        public event Action? AvailabilityChanged
        {
            add { }
            remove { }
        }

        public void Prepare()
        {
        }

        public Task<DiagramRenderResult> RenderAsync(DiagramRenderRequest request, CancellationToken cancellationToken) =>
            Task.FromException<DiagramRenderResult>(new DiagramRenderException(DiagramFailure.Unavailable, "No diagram renderer is bound."));
    }
}
