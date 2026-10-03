using System.Reflection;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Settings;

// REVIEW (defects-priority, adversarial round 1). Source: DroppyCode/App/Views/Settings/MCPKeyPanel.swift:47-57 (`.padding(18).frame(width: 380)
// .background { WindowDragArea() }.background { WindowBackdrop(opacity: 0.5) }.clipShape(RoundedRectangle(cornerRadius: Chrome.windowCornerRadius,
// style: .continuous))`), FloatingGuidePanel.swift:33-56 (a clear panel, isOpaque false, backgroundColor .clear); OD-0075 (owner, 2026-10-02: "the
// wedge is filled with the window's scrim", settling OD-0072's "Window corner wedge (unscrimmed blur in the 8 px rounded corner)").
//
// The guide panels are the same kind of window as the main and Settings windows: system Acrylic over the whole rectangle
// (FloatingGuidePanel.MakePanel: TransparencyLevelHint AcrylicBlur; WindowManager.cs's ED-236 note: "the tour window and the guide panels carry it
// too") with the 26 pt shape and its scrim drawn over it. Between the rectangle and the shape the blur shows without the scrim: the very wedge
// OD-0075 rules on. The unit fills it in the main window and in Settings only, and its ED lists only the tour card (20 pt, no ruling) as not
// covered. Headless, the panel's background is a known grey standing in for the blur, so a corner's colour is the scrim over the grey.
[Collection("Headless")]
public sealed class FloatingGuidePanelCornerTests(HeadlessSession headless)
{
    private static readonly Color Grey = Color.FromRgb(128, 128, 128);

    // A local keys page: nothing is opened headless (MCPKeyPanel.OpenURL needs the desktop lifetime's main window).
    private static MCPCatalogEntry KeyEntry() =>
        new("notion", "Notion", "Notion", "Notes", MCPCategory.Developer, "notion",
            0, new MCPTransport.Stdio("notion", ValueList<string>.Empty), "https://notion.example/docs")
        {
            Fields = new ValueList<MCPField>([new MCPField("api_key", "API key", "Paste the key", MCPField.Kind.Secret, null, IsRequired: false)]),
        };

    private static AvaloniaWindow PanelOf(FloatingGuidePanel owner) =>
        (AvaloniaWindow)typeof(FloatingGuidePanel)
            .GetField("panel", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(owner)!;

    // The scrim over the grey: black in the dark theme, white in the light one (Chrome.swift:256-257), as a fraction of 255.
    private static double Over(double alpha, bool dark) => (Grey.R * (1 - alpha) + (dark ? 0 : 255) * alpha) / 255.0;

    private static void Settle(AvaloniaWindow window)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Decision", "ED-236")] // App/Views/Settings/MCPKeyPanel.swift:47-57 (WindowBackdrop(opacity: 0.5) clipped to Chrome.windowCornerRadius); OD-0075
    public void The_key_guide_panels_four_corners_outside_the_26_point_curve_wear_its_scrim(bool dark)
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() => { MCPKeyPanel.Shared.Present(KeyEntry(), model); Dispatcher.UIThread.RunJobs(); });
            var window = PanelOf(MCPKeyPanel.Shared);
            try
            {
                harness.Main.OnMain(() =>
                {
                    window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                    Settle(window);
                    // The panel is clear over the system's Acrylic: the known grey stands in for that blur.
                    window.Background = new SolidColorBrush(Grey);
                    Settle(window);
                    var width = (int)window.ClientSize.Width;
                    var height = (int)window.ClientSize.Height;
                    var expected = Over(WindowBackdrop.Scrim(0.5, dark), dark);
                    // The premise: inside the 26 pt shape, left of the content's 18 pt padding, the panel's own backdrop is drawn over the grey.
                    var inside = FramePixels.Levels(window, (8, height / 2))[0];
                    Assert.True(Math.Abs(inside - Grey.R / 255.0) > 0.02, $"the backdrop inside the shape is drawn ({inside:0.###})");
                    (int X, int Y)[] corners =
                    [
                        (0, 0), (1, 1), (5, 5), (width - 1, 0), (width - 2, 1), (width - 6, 5),
                        (0, height - 1), (1, height - 2), (5, height - 6), (width - 1, height - 1), (width - 2, height - 2), (width - 6, height - 6),
                    ];
                    var levels = FramePixels.Levels(window, corners);
                    for (var index = 0; index < levels.Length; index++)
                        Assert.True(Math.Abs(levels[index] - expected) <= 0.012,
                            $"corner pixel {corners[index]} of the key guide panel is {levels[index]:0.###}; the scrim over the grey is {expected:0.###} (bare grey {Grey.R / 255.0:0.###})");
                });
            }
            finally { MCPKeyPanel.Shared.Close(); }
        });
    }
}
