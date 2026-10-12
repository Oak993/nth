using Marrow.Core.Web;

namespace Marrow.UI.Tests.NthUi.Diagram;

/// <summary>ED-NEW-web-w16c-1: a diagram renderer a test controls. By default it answers at once with a small SVG whose height
/// grows with the source's lines; a test can hold an answer, fail one, or make the engine unavailable and available again.</summary>
internal sealed class FakeDiagramEngine : IDiagramEngine
{
    public bool Available { get; set; } = true;

    public List<DiagramRenderRequest> Requests { get; } = [];

    /// <summary>Decides each answer (by request number, from 1); null answers at once.</summary>
    public Func<int, DiagramRenderRequest, Task<DiagramRenderResult>>? Answer { get; set; }

    public double Width { get; set; } = 200;

    public bool IsAvailable => Available;

    public event Action? AvailabilityChanged;

    public int Prepared { get; private set; }

    public void Prepare() => Prepared++;

    public void RaiseAvailability() => AvailabilityChanged?.Invoke();

    public Task<DiagramRenderResult> RenderAsync(DiagramRenderRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Answer is { } answer ? answer(Requests.Count, request) : Task.FromResult(Drawing(request));
    }

    public DiagramRenderResult Drawing(DiagramRenderRequest request)
    {
        var lines = Math.Max(1, request.Source.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        var fill = request.Theme.Dark ? "#2a2a2f" : "#ffffff";
        var ink = request.Theme.Dark ? "#ececee" : "#1c1c1e";
        var height = 20 + lines * 30;
        return new DiagramRenderResult(
            $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {Width.ToString(System.Globalization.CultureInfo.InvariantCulture)} {height}"><rect x="10" y="10" width="{(Width - 20).ToString(System.Globalization.CultureInfo.InvariantCulture)}" height="{height - 20}" rx="8" fill="{fill}" stroke="#888888"/><text x="20" y="34" font-size="14" fill="{ink}">{lines} lines</text></svg>""");
    }
}
