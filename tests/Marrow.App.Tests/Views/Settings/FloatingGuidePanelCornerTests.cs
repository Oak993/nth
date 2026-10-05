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

// REVIEW (defects-priority, adversarial round 1). Source: DroppyCode/App/Views/Settings/MCPKeyPanel.swift:47-57 and MCPSetupPanel.swift:117-131 (`.padding(18)
// .frame(width: 380 | 400).background { WindowDragArea() }.background { WindowBackdrop(opacity: 0.5) }.clipShape(RoundedRectangle(cornerRadius:
// Chrome.windowCornerRadius, style: .continuous))`), FloatingGuidePanel.swift:33-56 (a clear panel, isOpaque false, backgroundColor .clear); OD-0075 (owner,
// 2026-10-02: "the wedge is filled with the window's scrim", settling OD-0072's "Window corner wedge (unscrimmed blur in the 8 px rounded corner)").
//
// The guide panels are the same kind of window as the main and Settings windows: system Acrylic over the whole rectangle
// (FloatingGuidePanel.MakePanel: TransparencyLevelHint AcrylicBlur; WindowManager.cs's ED-236 note: "the tour window and the guide panels carry it
// too") with the 26 pt shape and its scrim drawn over it. Between the rectangle and the shape the blur shows without the scrim: the very wedge
// OD-0075 rules on. Headless, the panel's background is a known grey standing in for the blur, so a corner's colour is the scrim over the grey.
// The backdrop of the setup guide's panel fills the whole 400 pt frame, padding included, as the Swift modifier order says (the 18 pt band is
// measured too: it is inside the shape, so it wears the backdrop's own scrim).
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

    private static FloatingGuidePanel SharedOf(string panel) => panel == "key" ? MCPKeyPanel.Shared : MCPSetupPanel.Shared;

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
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [Theory]
    [InlineData("key", true, false)]
    [InlineData("key", false, false)]
    [InlineData("setup", true, false)]
    [InlineData("setup", false, false)]
    [InlineData("key", true, true)]
    [InlineData("key", false, true)]
    [InlineData("setup", true, true)]
    [InlineData("setup", false, true)]
    [Trait("Decision", "ED-236")] // App/Views/Settings/MCPKeyPanel.swift:47-57, MCPSetupPanel.swift:117-131 (WindowBackdrop(opacity: 0.5) clipped to Chrome.windowCornerRadius); OD-0075
    public void The_guide_panels_four_corners_outside_the_26_point_curve_are_the_body_just_inside_it_and_the_backdrop_fills_the_frame(string panel, bool dark, bool busy)
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var shared = SharedOf(panel);
            // A panel window of its own: the shared panel's window, hidden by an earlier test and shown again, keeps handing back the frame it
            // last rendered headless, so this test makes the panel build its window afresh (FloatingGuidePanel.swift:17 `self.panel ?? makePanel()`).
            harness.Main.OnMain(() =>
            {
                shared.Close();
                typeof(FloatingGuidePanel).GetField("panel", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shared, null);
                if (panel == "key")
                    MCPKeyPanel.Shared.Present(KeyEntry(), model);
                else
                {
                    // No probe, no I/O: the state of the server is written straight into the store, as the setup panel's own tests do.
                    var entry = MCPSetupPanelRig.Entry();
                    MCPSetupPanelRig.SetState(model, entry.Id, new MCPConnectionState.NeedsSetup(MCPSetupPanelRig.Guide()));
                    MCPSetupPanel.Shared.Present(entry, MCPSetupPanelRig.Guide(), model);
                }

                Dispatcher.UIThread.RunJobs();
            });
            var window = PanelOf(shared);
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
                    // The premise: inside the 26 pt shape, left of the content's 18 pt padding, the panel's own backdrop is drawn over the grey
                    // (the setup guide's backdrop fills the padded frame, so it reads here too).
                    var inside = FramePixels.Levels(window, (8, height / 2), (width - 9, height / 2), (width / 2, 8), (width / 2, height - 9));
                    Assert.All(inside, level => Assert.True(Math.Abs(level - Grey.R / 255.0) > 0.02, $"the backdrop inside the shape, within 18 pt of an edge, is drawn ({level:0.###})"));
                    // OD-0075, ED-NEW-corner-seam: the wedge outside the 26 pt curve is the body just inside it, over a flat or a striped backdrop.
                    if (busy) { window.Background = Marrow.App.Tests.Views.RootViewCornerWedgeTests.Stripes(); Settle(window); }
                    var pairs = CornerSeam.Pairs(width, height, Chrome.WindowCornerRadius, [2, 3, 4, 5, 6, 8, 10], edgeInset: 1)
                        .Concat(CornerSeam.Pairs(width, height, Chrome.WindowCornerRadius, [3, 4, 5, 6, 7], edgeInset: 3)).ToArray();
                    CornerSeam.AssertSeamless(window, pairs, $"the {panel} guide panel, {(dark ? "dark" : "light")}, {(busy ? "striped" : "grey")} backdrop", busy ? null : (Grey.R, Grey.G, Grey.B));
                });
            }
            finally
            {
                shared.Close();
                if (panel == "setup") harness.Main.OnMain(() => MCPSetupPanelRig.CancelProbes(model));
            }
        });
    }
}
