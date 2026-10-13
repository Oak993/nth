using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Browser;
using Marrow.Core.Models;

namespace Marrow.App.Tests.Views.Panes;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-review-22")]
public sealed class WebReviewCanvasTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Visus_chrome_is_rendered_inside_the_workspace_for_canvas_review(bool dark) => headless.OnUiThread(() =>
    {
        using var visus = new VisusRig();
        using var rig = new PaneWorkspaceRig(contents: kind => kind == PaneKind.Browser ? VisusPane.Create(visus.Session, null) : null);
        rig.Window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        rig.Window.Background = new SolidColorBrush(dark ? Color.FromRgb(30, 30, 33) : Color.FromRgb(245, 245, 247));
        rig.Workspace.Open(PaneKind.Browser);
        rig.Settle();
        visus.Session.Go("https://example.org/");
        var tab = visus.Session.Selected!;
        VisusRig.Live(tab).RaiseNavigationCompleted(tab.Url);
        rig.Layout();
        var card = rig.Card(PaneKind.Browser);
        Assert.Equal(36, card.Header.Bounds.Height); // DESIGN 3.12, native header authority.
        Assert.True(card.Header.CloseButton.IsEffectivelyVisible);
        Assert.True(card.Bounds.Right <= rig.Window.ClientSize.Width);
        Capture(rig, "visus", dark);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Tabula_document_chrome_is_rendered_inside_the_workspace_for_canvas_review(bool dark) => headless.OnUiThread(() =>
    {
        using var tabula = new TabulaRig();
        tabula.Show(ArtifactKind.Markdown, "# Orbit\n\nA document kept in this thread.\n\n- Calm cards\n- Versioned artifacts\n");
        tabula.Window.Content = null;
        using var rig = new PaneWorkspaceRig(contents: kind => kind == PaneKind.Artifacts ? tabula.View.Content : null);
        rig.Window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        rig.Window.Background = new SolidColorBrush(dark ? Color.FromRgb(30, 30, 33) : Color.FromRgb(245, 245, 247));
        rig.Workspace.Open(PaneKind.Artifacts);
        rig.Settle();
        var card = rig.Card(PaneKind.Artifacts);
        Assert.Equal(36, card.Header.Bounds.Height);
        Assert.True(card.Header.CloseButton.IsEffectivelyVisible);
        Assert.Contains("Export", tabula.View.Content.Actions.Select(a => a.Name));
        Assert.True(card.Bounds.Right <= rig.Window.ClientSize.Width);
        Capture(rig, "tabula", dark);
    });

    private static void Capture(PaneWorkspaceRig rig, string name, bool dark)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "web-review-evidence");
        Directory.CreateDirectory(folder);
        using var frame = rig.Window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(folder, name + (dark ? "-dark.png" : "-light.png")), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }
}
