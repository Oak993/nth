using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

/// <summary>
/// The Hydra mark button itself (App/Views/Chat/HydraButton.swift:36-101, :322-331): a 32-point circle of chrome glass with the
/// mark inside at 18 points, quiet while Hydra is off and the pointer is elsewhere.
/// </summary>
[Collection("Headless")]
public sealed class HydraButtonMarkTests(HeadlessSession headless)
{
    private void Run(Action<HydraButtonRig> body, bool hydraOn = true) => ChatHeadlessFixtures.Run(headless, () =>
    {
        using var rig = new HydraButtonRig(hydraOn);
        body(rig);
    });

    private static HydraMarkImage Image(HydraButtonRig rig) => LogicalQuery.SelfAndDescendants<HydraMarkImage>(rig.Button).Single();

    // The mark's foreground over its labelColor: 1 at full opacity, 0.6 when quiet (the alpha byte of the drawn colour).
    private static double Strength(HydraButtonRig rig)
    {
        var brush = Assert.IsAssignableFrom<ISolidColorBrush>(TextElement.GetForeground(Image(rig)));
        var full = Chrome.PrimaryText.Resolve(Image(rig).ActualThemeVariant).A;
        return brush.Color.A / (double)full;
    }

    private static void Hover(HydraButtonRig rig, Point at)
    {
        rig.Main.OnMain(() => rig.Window.MouseMove(at, RawInputModifiers.None));
        rig.Drain();
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-001")] // HydraButton.swift:46, :329 a 32 by 32 frame, the mark inside 18 by 18
    public void The_mark_is_a_32_point_frame_with_an_18_point_image()
    {
        Run(rig =>
        {
            Assert.Equal(32, Chrome.CapsuleHeight); // capsuleContentHeight 28 + 2 * 2
            Assert.Equal(new Size(32, 32), rig.Mark.Bounds.Size);
            Assert.Equal(new Size(32, 32), rig.Button.Bounds.Size);
            Assert.Equal(new Size(18, 18), Image(rig).Bounds.Size);
            // centred in the frame
            var origin = Image(rig).TranslatePoint(default, rig.Mark)!.Value;
            Assert.Equal(7, origin.X, 0.01);
            Assert.Equal(7, origin.Y, 0.01);
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-001")] // the chrome row's HStack centres its children: a taller neighbour does not stretch the mark
    public void A_taller_neighbour_in_the_row_centres_the_mark_and_does_not_stretch_it()
    {
        Run(rig =>
        {
            rig.Main.OnMain(() => rig.Content.Children.Add(new Border { Width = 10, Height = 100 }));
            rig.Drain();

            Assert.Equal(32, rig.Button.Bounds.Height);
            Assert.Equal(34, rig.Button.Bounds.Y, 0.01);
            Assert.Equal(new Size(32, 32), rig.Mark.Bounds.Size);
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-001")] // HydraButton.swift:47, :76 .contentShape(Circle()), drawn on a chrome glass circle
    public void The_mark_takes_clicks_in_a_circle_and_is_drawn_on_chrome_glass()
    {
        Run(rig =>
        {
            // the frame's corner is outside its inscribed circle
            rig.Press(rig.MarkAt(1.5, 1.5), MouseButton.Left);
            Assert.Empty(rig.Presented());
            rig.Press(rig.MarkAt(16, 16), MouseButton.Left);
            Assert.IsType<HydraIdlePopover>(Assert.Single(rig.Presented()));

            var glass = LogicalQuery.SelfAndDescendants<Control>(rig.Button).Select(SwiftGlass.GetEffect).OfType<GlassEffect>().Single();
            Assert.Equal(GlassShapeKind.Circle, glass.Shape.Kind);
            Assert.True(glass.IsInteractive);
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-001")] // HydraButton.swift:328 the mark is labelColor at full opacity with Hydra on, at 0.6 off
    public void The_mark_is_at_full_opacity_with_hydra_on_and_at_point_six_with_it_off()
    {
        Run(rig => Assert.Equal(1, Strength(rig), 0.01));
        Run(rig => Assert.Equal(0.6, Strength(rig), 0.01), hydraOn: false);
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-001")] // HydraButton.swift:80-82, :328 under the pointer it is full, eased out over 0.1 s (Chrome.hover)
    public void Hovering_a_quiet_mark_brings_it_to_full_opacity_on_the_hover_ease_out()
    {
        Run(rig =>
        {
            Assert.Equal(0.6, Strength(rig), 0.01);
            Hover(rig, rig.MarkCentre);
            var progress = Animation.EaseOut(duration: 0.1).Progress(0.05);
            Assert.InRange(progress, 0.2, 0.95);

            rig.Tick(0.05);
            Assert.Equal(0.6 + 0.4 * progress, Strength(rig), 0.02);
            rig.Tick(0.5);
            Assert.Equal(1, Strength(rig), 0.01);

            Hover(rig, new Point(600, 600)); // away again
            rig.Tick(0.05);
            Assert.Equal(1 - 0.4 * progress, Strength(rig), 0.02);
            rig.Tick(0.5);
            Assert.Equal(0.6, Strength(rig), 0.01);
        }, hydraOn: false);
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-001")] // HydraButton.swift:328 with Hydra on the mark is full whether or not it is hovered
    public void With_hydra_on_hovering_changes_nothing_about_the_mark()
    {
        Run(rig =>
        {
            Hover(rig, rig.MarkCentre);
            rig.Tick(0.05);
            Assert.Equal(1, Strength(rig), 0.01);
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-001")] // HydraButton.swift:93 turning Hydra off for the chat makes the mark quiet, on the panel slide
    public void Switching_hydra_off_quiets_the_mark_along_the_panel_slide()
    {
        Run(rig =>
        {
            Assert.Equal(1, Strength(rig), 0.01);
            rig.Main.OnMain(() => rig.Model.Settings.HydraEnabled = false);
            rig.Drain();
            var slide = Animation.Spring(response: 0.32, dampingFraction: 0.9).Progress(0.05); // Chrome.swift:102

            rig.Tick(0.05);
            Assert.Equal(1 - 0.4 * slide, Strength(rig), 0.02);
            rig.Tick(2);
            Assert.Equal(0.6, Strength(rig), 0.01);
        });
    }
}
