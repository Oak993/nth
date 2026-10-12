using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Marrow.Core.Tests.Fixtures;
using Marrow.Core.Web;
using Marrow.UI.NthUi;
using Marrow.UI.NthUi.Diagram;
using Marrow.UI.NthUi.Map;
using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Tests.NthUi.Diagram;

namespace Marrow.UI.Tests.NthUi.Map;

// Web surfaces W16c (ED-NEW-web-w16c-1): the live answers this unit draws (a map, line art, a diagram), captured in light and dark
// and saved as PNGs next to the test binaries (nthui-snapshots) for review, as W16a's theme tests do. The checks are that each
// capture is not blank and differs between the themes; the review is by eye.
[Collection("Headless")]
public sealed class NthUiW16cSnapshotTests(HeadlessSession headless)
{
    private const string Page = """
        text "A map, line art and a diagram in one answer."
        map "Lisbon to Paris" zoom=4
          stop "Lisbon" lat=38.72 lon=-9.14
          stop "Madrid" lat=40.42 lon=-3.70 detail="Overnight"
          stop "Paris" lat=48.86 lon=2.35
          route "By train" via="Lisbon,Madrid,Paris"
        svg alt="A rising line" width=240
          <svg viewBox="0 0 240 80" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
            <path d="M8 66 L60 48 L112 54 L164 26 L232 10"/>
            <circle cx="232" cy="10" r="5" fill="currentColor"/>
            <text x="8" y="78" font-size="11" fill="currentColor" stroke="none">2024</text>
            <text x="200" y="78" font-size="11" fill="currentColor" stroke="none">2026</text>
          </svg>
        diagram "Request flow" alt="Client to server to database"
          graph LR
            A[Client] --> B[Server]
            B --> C[(Database)]
        text "That is all."
        """;

    private static void Save(RenderTargetBitmap bitmap, string path)
    {
        using var stream = File.Create(path);
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
    }

    private static string Folder()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "nthui-snapshots");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void Wait(NthUiRig rig, Func<bool> done)
    {
        var end = DateTime.UtcNow.AddSeconds(5);
        while (!done() && DateTime.UtcNow < end)
        {
            rig.Tick();
            Thread.Sleep(1);
        }
    }

    private static string Flow(bool dark)
    {
        var fill = dark ? "#2a2a2d" : "#ffffff";
        var line = dark ? "#b5b5b9" : "#5f5f61";
        var ink = dark ? "#ececee" : "#1c1c1e";
        string Box(int x, string label) =>
            $"<g transform=\"translate({x}, 40)\"><rect x=\"-52\" y=\"-22\" width=\"104\" height=\"44\" rx=\"10\" fill=\"{fill}\" stroke=\"{line}\" stroke-width=\"1\"/><text x=\"0\" y=\"0\" text-anchor=\"middle\" dominant-baseline=\"central\" fill=\"{ink}\" font-family=\"Segoe UI\" font-size=\"14\">{label}</text></g>";
        return $"""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 440 80"><defs><marker id="a" viewBox="0 0 10 10" refX="5" refY="5" markerUnits="userSpaceOnUse" markerWidth="8" markerHeight="8" orient="auto"><path d="M0 0 L10 5 L0 10 z" fill="{line}" stroke="{line}"/></marker></defs>
            <path d="M104,40 L170,40" fill="none" stroke="{line}" stroke-width="1.6" marker-end="url(#a)"/><path d="M274,40 L340,40" fill="none" stroke="{line}" stroke-width="1.6" marker-end="url(#a)"/>
            {Box(52, "Client")}{Box(222, "Server")}{Box(388, "Database")}</svg>
            """;
    }

    [Theory]
    [Trait("Decision", "ED-NEW-web-w16c-1")]
    [InlineData(false)]
    [InlineData(true)]
    public void A_map_line_art_and_a_diagram_are_captured_for_review(bool dark) => headless.OnUiThread(() =>
    {
        NthUiDiagrams.ClearCache();
        var engine = new FakeDiagramEngine { Answer = (_, request) => Task.FromResult(new DiagramRenderResult(Flow(request.Theme.Dark))) };
        using var _ = DiagramEngines.Shared.Override(engine);
        using var rig = new NthUiRig(NthUiView.Show(Page, culture: NthUiRig.English), width: 600, theme: dark ? ThemeVariant.Dark : null);
        rig.Window.Height = 1100;
        rig.Settle();
        var diagram = rig.ViewOf<NthUiDiagramView>();
        Wait(rig, () => diagram.Document is not null);
        rig.Settle(2);
        rig.Run(1.2);
        var pixels = rig.Capture();
        Assert.NotNull(diagram.Document);
        var name = dark ? "dark" : "light";
        var file = Path.Combine(Folder(), $"w16c-answer-{name}.png");
        using (var bitmap = new RenderTargetBitmap(new PixelSize(600, 1100)))
        {
            bitmap.Render(rig.Window);
            Save(bitmap, file);
        }
        Assert.True(new FileInfo(file).Length > 5_000);
        Assert.NotNull(pixels.InkRows(40));
        // The map alone, at a world view and close in.
        var map = rig.ViewOf<NthUiMapView>().Canvas;
        map.ZoomBy(-3, null);
        rig.Run(2);
        using (var bitmap = new RenderTargetBitmap(new PixelSize(600, 1100)))
        {
            bitmap.Render(rig.Window);
            Save(bitmap, Path.Combine(Folder(), $"w16c-map-world-{name}.png"));
        }
        map.ZoomBy(5, new Point(300, 150));
        rig.Run(2);
        using (var bitmap = new RenderTargetBitmap(new PixelSize(600, 1100)))
        {
            bitmap.Render(rig.Window);
            Save(bitmap, Path.Combine(Folder(), $"w16c-map-close-{name}.png"));
        }
    });
}
