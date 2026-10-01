using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

/// <summary>
/// The charge around an active Hydra mark (App/Views/Chat/HydraButton.swift:40-43, :340-386): a ring of the roster's first six
/// colours and a glow, turning and breathing only while a head works, and standing still otherwise or under Reduce Motion.
/// </summary>
[Collection("Headless")]
public sealed class HydraButtonChargeTests(HeadlessSession headless)
{
    // UI-HYDRA-003: the first six roster colours, Hank to Remy (Hydra.swift:232-237).
    private static readonly Color[] Roster =
    [
        Color.FromRgb(0xFF, 0x8A, 0x3D), Color.FromRgb(0x3D, 0x8B, 0xFF), Color.FromRgb(0x34, 0xC4, 0x6A),
        Color.FromRgb(0xA3, 0x5B, 0xE0), Color.FromRgb(0xF2, 0x5C, 0x9A), Color.FromRgb(0x2B, 0xB5, 0xB0),
    ];

    private void Run(Action<HydraButtonRig> body, bool hydraOn = true, bool hydraEnabledInSettings = true, bool reduceMotion = false) =>
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var rig = new HydraButtonRig(hydraOn, hydraEnabledInSettings: hydraEnabledInSettings, reduceMotion: reduceMotion);
            body(rig);
        });

    private static HydraCharge? Charge(HydraButtonRig rig) => LogicalQuery.SelfAndDescendants<HydraCharge>(rig.Button).SingleOrDefault();

    private static (Ellipse Glow, Ellipse Ring) Still(HydraCharge charge)
    {
        var grid = Assert.IsType<Grid>(charge.Child);
        Assert.Equal(2, grid.Children.Count);
        return (Assert.IsType<Ellipse>(grid.Children[0]), Assert.IsType<Ellipse>(grid.Children[1]));
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-001")] // HydraButton.swift:40-43 the charge is there only with Hydra on for the chat
    [Trait("Req", "PR-HYDRA-166")] // HydraButton.swift:41-43
    public void The_charge_is_present_only_with_hydra_on_for_the_chat()
    {
        Run(rig => Assert.NotNull(Charge(rig)));
        Run(rig => Assert.Null(Charge(rig)), hydraOn: false);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-166")] // HydraButton.swift:357 .allowsHitTesting(false)
    public void The_charge_never_takes_hits()
    {
        Run(rig =>
        {
            var charge = Assert.IsType<HydraCharge>(Charge(rig));
            Assert.False(charge.IsHitTestVisible);
            rig.AddHead(HydraHeadInfo.Status.Running); // the moving form too
            charge = Assert.IsType<HydraCharge>(Charge(rig));
            Assert.False(charge.IsHitTestVisible);

            var hit = rig.Window.InputHitTest(rig.MarkCentre) as Visual;
            Assert.NotNull(hit);
            Assert.DoesNotContain(hit.GetSelfAndVisualAncestors(), visual => ReferenceEquals(visual, charge));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-166")] // HydraButton.swift:350-354 no head out: the still glow and the still ring
    [Trait("Req", "UI-HYDRA-003")] // HydraButton.swift:360-386 idle: ring still at 0.85, glow still at 0.6
    public void With_no_head_out_the_charge_stands_still()
    {
        Run(rig =>
        {
            var charge = Assert.IsType<HydraCharge>(Charge(rig));
            var (glow, ring) = Still(charge);

            Assert.Null(charge.LayerView);
            Assert.Equal(0.6, glow.Opacity); // :371
            Assert.Equal(0.85, ring.Opacity); // :385
            Assert.Equal(new Thickness(2), glow.Margin); // :370
            Assert.Equal(new Thickness(1), ring.Margin); // :383
            Assert.Equal(2, ring.StrokeThickness); // :381
            Assert.Null(ring.RenderTransform); // :384 not turned
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-003")] // HydraButton.swift:376-381 the ring's angular gradient over the six colours plus the first again
    [Trait("Req", "PR-HYDRA-166")] // HydraButton.swift:344, :378
    public void The_ring_is_an_angular_gradient_over_the_first_six_roster_colours_and_the_first_again()
    {
        Run(rig =>
        {
            var (_, ring) = Still(Assert.IsType<HydraCharge>(Charge(rig)));
            var brush = Assert.IsType<ConicGradientBrush>(ring.Stroke);

            var stops = brush.GradientStops;
            Assert.Equal(7, stops.Count);
            for (var index = 0; index < 7; index++)
            {
                Assert.Equal(Roster[index % 6], stops[index].Color); // the first again closes it
                Assert.Equal(index / 6.0, stops[index].Offset, 6); // spread evenly
            }
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-003")] // HydraButton.swift:362-372 the glow: accent at 0.46 fading to none, radius 2 to 15, inset 2
    [Trait("Req", "PR-HYDRA-166")] // HydraButton.swift:364-368
    public void The_glow_is_the_accent_at_point_four_six_fading_out_between_radius_two_and_fifteen()
    {
        Run(rig =>
        {
            var (glow, _) = Still(Assert.IsType<HydraCharge>(Charge(rig)));
            var brush = Assert.IsType<RadialGradientBrush>(glow.Fill);
            var accent = Chrome.Accent.Resolve(glow.ActualThemeVariant);

            Assert.Equal(new Size(28, 28), glow.Bounds.Size); // the 32-point frame, padded 2
            Assert.Equal(new RelativePoint(0.5, 0.5, RelativeUnit.Relative), brush.Center);
            Assert.Equal(15.0 / 28, brush.RadiusX.Scalar, 6); // an end radius of 15 points
            Assert.Equal(15.0 / 28, brush.RadiusY.Scalar, 6);
            Assert.Equal(3, brush.GradientStops.Count);
            Assert.Equal(Color.FromArgb(117, accent.R, accent.G, accent.B), brush.GradientStops[0].Color); // 0.46 of 255
            Assert.Equal(0, brush.GradientStops[0].Offset);
            Assert.Equal(brush.GradientStops[0].Color, brush.GradientStops[1].Color); // flat out to the start radius of 2
            Assert.Equal(2.0 / 15, brush.GradientStops[1].Offset, 6);
            Assert.Equal(Color.FromArgb(0, accent.R, accent.G, accent.B), brush.GradientStops[2].Color);
            Assert.Equal(1, brush.GradientStops[2].Offset);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-166")] // HydraButton.swift:350 while a head works the ring turns and the glow breathes: the moving form
    public void While_a_head_works_the_charge_is_in_motion_and_it_stills_again_when_the_last_one_is_back()
    {
        Run(rig =>
        {
            var head = rig.AddHead(HydraHeadInfo.Status.Running);
            var charge = Assert.IsType<HydraCharge>(Charge(rig));
            Assert.NotNull(charge.LayerView);
            Assert.IsType<HydraChargeMotion>(charge.Child);

            rig.SetHeadStatus(head, HydraHeadInfo.Status.Completed);
            Assert.Same(charge, Charge(rig)); // the same charge: its body took the other branch
            Assert.Null(charge.LayerView);
            Still(charge);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-166")] // HydraButton.swift:350 Reduce Motion keeps the still ring, even while a head works
    [Trait("Req", "UI-HYDRA-003")] // HydraButton.swift:350 Reduce Motion: the still form, even while working
    public void Under_reduce_motion_a_working_charge_stands_still()
    {
        Run(rig =>
        {
            rig.AddHead(HydraHeadInfo.Status.Running);
            var charge = Assert.IsType<HydraCharge>(Charge(rig));

            Assert.Null(charge.LayerView);
            var (glow, ring) = Still(charge);
            Assert.Equal(0.6, glow.Opacity);
            Assert.Equal(0.85, ring.Opacity);
        }, reduceMotion: true);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-166")] // HydraButton.swift:350 the setting is read live, each time the body runs
    public void The_reduce_motion_setting_is_read_each_time_the_charge_is_evaluated()
    {
        Run(rig =>
        {
            rig.Motion.AccessibilityDisplayShouldReduceMotion = true;
            var head = rig.AddHead(HydraHeadInfo.Status.Running);
            Assert.Null(Assert.IsType<HydraCharge>(Charge(rig)).LayerView);

            rig.Motion.AccessibilityDisplayShouldReduceMotion = false;
            rig.SetHeadStatus(head, HydraHeadInfo.Status.Completed);
            rig.AddHead(HydraHeadInfo.Status.Running); // working again: evaluated afresh
            Assert.NotNull(Assert.IsType<HydraCharge>(Charge(rig)).LayerView);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-166")] // HydraButton.swift:42, :63 switched on, the charge scales from 0.6 and fades, on the panel slide
    [Trait("Req", "UI-HYDRA-001")] // HydraButton.swift:42, :63 switching on or off: the charge scales from 0.6 and fades with Chrome.panelSlide
    public void Switching_hydra_on_scales_the_charge_in_from_point_six_and_fades_it_on_the_panel_slide()
    {
        Run(rig =>
        {
            Assert.Null(Charge(rig));
            rig.Main.OnMain(() => rig.Model.Settings.HydraEnabled = true);
            rig.Drain();

            var charge = Assert.IsType<HydraCharge>(Charge(rig));
            var slide = Animation.Spring(response: 0.32, dampingFraction: 0.9).Progress(0.05); // Chrome.swift:102 panelSlide
            Assert.InRange(slide, 0.05, 0.95);
            Assert.Equal(0, HydraTransitionProbe.Opacity(charge, rig.Mark), 0.01); // arrives transparent
            Assert.Equal(0.6, HydraTransitionProbe.Scale(charge, rig.Mark), 0.01); // and at 0.6

            rig.Tick(0.05);
            Assert.Equal(slide, HydraTransitionProbe.Opacity(charge, rig.Mark), 0.01);
            Assert.Equal(0.6 + 0.4 * slide, HydraTransitionProbe.Scale(charge, rig.Mark), 0.01);

            rig.Tick(2);
            Assert.Equal(1, HydraTransitionProbe.Opacity(charge, rig.Mark), 0.01);
            Assert.Equal(1, HydraTransitionProbe.Scale(charge, rig.Mark), 0.01);
        }, hydraEnabledInSettings: false);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-166")] // HydraButton.swift:63 switched off, it scales and fades out rather than popping
    [Trait("Req", "UI-HYDRA-001")] // HydraButton.swift:63
    public void Switching_hydra_off_fades_the_charge_out_rather_than_popping_it()
    {
        Run(rig =>
        {
            var charge = Assert.IsType<HydraCharge>(Charge(rig));
            rig.Main.OnMain(() => rig.Model.Settings.HydraEnabled = false);
            rig.Drain();

            Assert.Same(charge, Charge(rig)); // still there, on its way out
            Assert.Equal(1, HydraTransitionProbe.Opacity(charge, rig.Mark), 0.01);
            rig.Tick(0.05);
            Assert.InRange(HydraTransitionProbe.Opacity(charge, rig.Mark), 0.05, 0.99);
            rig.Tick(2);
            Assert.Null(Charge(rig));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-166")] // HydraButton.swift:371, :384-385 the glow breathes to full, the ring turns and brightens when asked
    public void The_glow_and_the_ring_take_their_other_forms_from_their_arguments()
    {
        Run(_ =>
        {
            Assert.Equal(1, HydraCharge.Glow(true).Opacity); // :371 breathing ? 1 : 0.6
            Assert.Equal(0.6, HydraCharge.Glow(false).Opacity);
            Assert.Equal(1, HydraCharge.Ring(false, bright: true).Opacity); // :385 bright ? 1 : 0.85
            Assert.Equal(0.85, HydraCharge.Ring(false).Opacity);
            var turned = Assert.IsType<RotateTransform>(HydraCharge.Ring(true).RenderTransform); // :384 turned ? 360 : 0
            Assert.Equal(360, turned.Angle);
            Assert.Null(HydraCharge.Ring(false).RenderTransform);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-239")] // HydraButton.swift:403-405, :500 every re-render of the button (a hover) configures the layer view again; an animation lost is put back by it
    public void A_re_render_of_the_button_reinstalls_only_a_lost_turn()
    {
        Run(rig =>
        {
            rig.AddHead(HydraHeadInfo.Status.Running);
            var view = Assert.IsType<HydraChargeLayerView>(Assert.IsType<HydraCharge>(Charge(rig)).LayerView);
            var installs = view.InstallCount;

            rig.Main.OnMain(() => rig.Window.MouseMove(rig.MarkCentre, Avalonia.Input.RawInputModifiers.None)); // hover: the button renders again
            rig.Drain();
            Assert.Equal(installs, view.InstallCount);

            view.RemoveAnimation(HydraChargeLayerView.ChargeLayer.Ring, HydraChargeLayerView.TurnKey);
            rig.Main.OnMain(() => rig.Window.MouseMove(new Point(600, 600), Avalonia.Input.RawInputModifiers.None));
            rig.Drain();
            Assert.Equal(installs + 1, view.InstallCount);
            Assert.True(view.HasAnimation(HydraChargeLayerView.ChargeLayer.Ring, HydraChargeLayerView.TurnKey));
        });
    }
}
