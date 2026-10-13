using Marrow.UI.Port;

namespace Marrow.UI.Tests.Port;

// F.SwiftAnimation, part 1: the SwiftUI animation curves of src/Marrow.UI/Port/SwiftAnimation.cs (PORT-ONLY,
// ED-092 part 2, ED-011, R-VIEW-4). Every expected number is derived here from the closed forms ED-092 and ED-011
// state, never from the port:
//   spring: omega0 = 2*pi/response; zeta = dampingFraction, or 1 - bounce (bounce >= 0), or 1/(1 + bounce) (bounce < 0);
//           a unit step from rest: zeta < 1: 1 - e^(-zeta*w0*t) * (cos(wd*t) + zeta*w0/wd * sin(wd*t)), wd = w0*sqrt(1 - zeta^2);
//           zeta = 1: 1 - e^(-w0*t) * (1 + w0*t); zeta > 1: 1 - (r2*e^(r1*t) - r1*e^(r2*t)) / (r2 - r1), r1,2 = -w0*(zeta -+ sqrt(zeta^2 - 1));
//           settlingDuration = ln(1000) / (zeta*w0) (ED-011).
//   timing curves: cubic Beziers (0.42,0,1,1) easeIn, (0,0,0.58,1) easeOut, (0.42,0,0.58,1) easeInOut. The points used
//           are exact: at curve parameter s, x = 3(1-s)^2 s x1 + 3(1-s) s^2 x2 + s^3 and y = 3(1-s) s^2 + s^3.
//   defaults: 0.35 s for the timing curves and linear, 0.5 s for smooth, snappy and spring(duration:); smooth is
//           bounce 0, snappy bounce 0.15. These are the documented SwiftUI values, platform knowledge, not B2 facts:
//           UNVERIFIED under OD-0001 as ED-092 records.
// Progress(t) is the fraction of the change reached t seconds after the animation starts (delay included).
// Duration is the seconds from start to done, delay included; for a spring it is its settling duration (ED-011),
// and ToEasing() is Progress over [0, Duration] as an Avalonia easing.
public sealed class SwiftAnimationTests
{
    private const double Exact = 1e-9;
    private const double Solved = 1e-6; // a Bezier is solved for x numerically

    [Fact]
    [Trait("Decision", "ED-011")]
    [Trait("Decision", "ED-092")]
    public void The_glide_spring_settles_in_ln_1000_over_zeta_omega0_which_is_0_767_s() // UI/Chrome/Chrome.swift:108, App/Views/Sidebar/RowGlide.swift:231
    {
        var glide = new Spring(response: 0.6, dampingRatio: 0.86);
        Assert.Equal(0.6, glide.Response);
        Assert.Equal(0.86, glide.DampingRatio);
        Assert.Equal(Math.Log(1000) / (0.86 * 2 * Math.PI / 0.6), glide.SettlingDuration, Exact);
        Assert.Equal(0.76702562673428487, glide.SettlingDuration, Exact);
        Assert.Equal(0.767, Math.Round(glide.SettlingDuration, 3));
    }

    [Fact]
    [Trait("Decision", "ED-011")]
    [Trait("Decision", "ED-092")]
    public void A_spring_animation_from_a_Spring_follows_the_underdamped_closed_form() // Chrome.swift:108, :114 `.spring(glideSpring)`
    {
        var animation = Animation.Spring(new Spring(response: 0.6, dampingRatio: 0.86));
        Assert.Equal(0, animation.Progress(0), Exact);
        Assert.Equal(0.30155107726937647, animation.Progress(0.1), Exact);
        Assert.Equal(0.88916691609734633, animation.Progress(0.3), Exact);
        // It overshoots, as a spring of zeta 0.86 does (near its peak at 0.5625 s: the free curve up to its last crossing of 1 at 0.488 s, then its distance from 1 drawn in), and it lands
        // on 1 at its 0.767 s settling duration: ED-NEW-perf-motion-10 (ED-333 UNRESOLVED run-out, the lead's ruling 2026-10-13), stricter than the free curve's 1.0019536899873904 there,
        // which the last frame jumped back to 1.
        Assert.Equal(1.004219309803881, animation.Progress(0.5625), Exact);
        Assert.Equal(1, animation.Progress(0.76702562673428487), Exact);
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    public void Spring_response_and_damping_fraction_is_omega0_2pi_over_response_and_zeta_the_fraction() // Chrome.swift:102 panelSlide, HydraButton.swift:59
    {
        var panelSlide = Animation.Spring(response: 0.32, dampingFraction: 0.9);
        Assert.Equal(0.26982644982000648, panelSlide.Progress(0.05), Exact);
        Assert.Equal(0.86847411750814452, panelSlide.Progress(0.16), Exact);
        Assert.Equal(1, panelSlide.Progress(0.4), Exact); // past its 0.391 s settling time it is on 1 (ED-NEW-perf-motion-10 (ED-333 UNRESOLVED run-out, the lead's ruling 2026-10-13); the free curve's 1.0013068423096614 was jumped)
        Assert.Equal(0.75706093433458532, Animation.Spring(response: 0.3, dampingFraction: 0.7).Progress(0.1), Exact);
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    public void Spring_duration_and_bounce_is_zeta_1_minus_bounce_with_the_duration_as_response() // App/Views/Settings/WallpaperCard.swift:91
    {
        var wallpaper = Animation.Spring(duration: 0.45, bounce: 0.2);
        Assert.Equal(0.45675720240307405, wallpaper.Progress(0.1), Exact);
        Assert.Equal(1.0008868349704654, wallpaper.Progress(0.3), Exact);
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    public void A_negative_bounce_is_zeta_1_over_1_plus_bounce_an_overdamped_spring()
    {
        // zeta = 1/(1 - 0.25) = 1.333..., omega0 = 2*pi/0.5: the overdamped closed form at t = 0.2.
        Assert.Equal(0.59710560259954715, Animation.Spring(duration: 0.5, bounce: -0.25).Progress(0.2), Exact);
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    public void Spring_duration_without_bounce_is_critically_damped() // App/Views/Settings/MCPSettingsPage.swift:58
    {
        Assert.Equal(0.53573215609108205, Animation.Spring(duration: 0.35).Progress(0.1), Exact);
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    public void Smooth_is_a_spring_with_bounce_0() // UI/Chrome/Chrome.swift:960 softAppear
    {
        var softAppear = Animation.Smooth(duration: 0.32);
        Assert.Equal(0.25752684954300020, softAppear.Progress(0.05), Exact);
        Assert.Equal(0.82102555358593099, softAppear.Progress(0.16), Exact);
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    public void Snappy_is_a_spring_with_bounce_0_15() // App/Views/Composer/EffortSlider.swift:631
    {
        var snappy = Animation.Snappy(duration: 0.2);
        Assert.Equal(0.50937995473294806, snappy.Progress(0.05), Exact);
        Assert.Equal(0.89450945319185016, snappy.Progress(0.1), Exact);
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    public void EaseIn_is_the_cubic_Bezier_0_42_0_1_1() // App/Views/Sidebar/SidebarView.swift:1073
    {
        var easeIn = Animation.EaseIn(duration: 1);
        Assert.Equal(0.15625, easeIn.Progress(0.3334375), Solved);
        Assert.Equal(0.5, easeIn.Progress(0.6575), Solved);
        Assert.Equal(0.84375, easeIn.Progress(0.9028125), Solved);
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    public void EaseOut_is_the_cubic_Bezier_0_0_0_58_1() // UI/Chrome/Chrome.swift:98 hover
    {
        var easeOut = Animation.EaseOut(duration: 1);
        Assert.Equal(0.15625, easeOut.Progress(0.0971875), Solved);
        Assert.Equal(0.5, easeOut.Progress(0.3425), Solved);
        Assert.Equal(0.84375, easeOut.Progress(0.6665625), Solved);
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    public void EaseInOut_is_the_cubic_Bezier_0_42_0_0_58_1() // App/Views/Composer/ComposerView.swift:672
    {
        var easeInOut = Animation.EaseInOut(duration: 1);
        Assert.Equal(0.15625, easeInOut.Progress(0.274375), Solved);
        Assert.Equal(0.5, easeInOut.Progress(0.5), Solved);
        Assert.Equal(0.84375, easeInOut.Progress(0.725625), Solved);
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    public void A_timing_curve_scales_its_curve_to_its_duration_and_holds_its_ends()
    {
        var easeInOut = Animation.EaseInOut(duration: 0.4);
        Assert.Equal(0.15625, easeInOut.Progress(0.274375 * 0.4), Solved);
        Assert.Equal(0, easeInOut.Progress(0), Exact);
        Assert.Equal(1, easeInOut.Progress(0.4), Exact);
        Assert.Equal(1, easeInOut.Progress(3), Exact);
        var hover = Animation.EaseOut(duration: 0.1);
        Assert.Equal(0.5, hover.Progress(0.03425), Solved);
        Assert.Equal(1, hover.Progress(0.1), Exact);
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    public void Linear_moves_in_proportion_to_time()
    {
        var linear = Animation.Linear(duration: 0.5);
        Assert.Equal(0, linear.Progress(0), Exact);
        Assert.Equal(0.4, linear.Progress(0.2), Exact);
        Assert.Equal(1, linear.Progress(0.5), Exact);
        Assert.Equal(1, linear.Progress(2), Exact);
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    public void The_timing_curves_default_to_0_35_s()
    {
        Assert.Equal(Animation.EaseIn(duration: 0.35), Animation.EaseIn());
        Assert.Equal(Animation.EaseOut(duration: 0.35), Animation.EaseOut());
        Assert.Equal(Animation.EaseInOut(duration: 0.35), Animation.EaseInOut());
        Assert.Equal(Animation.Linear(duration: 0.35), Animation.Linear());
        Assert.Equal(0.5, Animation.Linear().Progress(0.175), Exact);
        Assert.Equal(0.5, Animation.EaseInOut().Progress(0.175), Solved);
        Assert.Equal(0.35, Animation.EaseOut().Duration, Exact);
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    public void Smooth_snappy_and_spring_duration_default_to_0_5_s_and_blend_duration_to_0()
    {
        Assert.Equal(Animation.Smooth(duration: 0.5), Animation.Smooth());
        Assert.Equal(Animation.Snappy(duration: 0.5), Animation.Snappy());
        Assert.Equal(Animation.Spring(duration: 0.5, bounce: 0), Animation.Spring(bounce: 0));
        Assert.Equal(Animation.Spring(duration: 0.35, bounce: 0), Animation.Spring(duration: 0.35));
        Assert.Equal(Animation.Spring(response: 0.3, dampingFraction: 0.8, blendDuration: 0), Animation.Spring(response: 0.3, dampingFraction: 0.8));
        // smooth(duration: 0.5): critically damped, omega0 = 4*pi, at t = 0.1.
        Assert.Equal(1 - Math.Exp(-4 * Math.PI * 0.1) * (1 + 4 * Math.PI * 0.1), Animation.Smooth().Progress(0.1), Exact);
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    public void A_delay_holds_the_start_then_runs_the_curve_after_it() // App/Views/Sidebar/SidebarView.swift:1073
    {
        var sidebarRow = Animation.EaseIn(duration: 0.18).Delay(0.12);
        Assert.Equal(0, sidebarRow.Progress(0), Exact);
        Assert.Equal(0, sidebarRow.Progress(0.06), Exact);
        Assert.Equal(0, sidebarRow.Progress(0.12), Exact);
        Assert.Equal(0.5, sidebarRow.Progress(0.12 + 0.18 * 0.6575), Solved);
        Assert.Equal(1, sidebarRow.Progress(0.30), Exact);
        Assert.Equal(1, sidebarRow.Progress(1), Exact);
        Assert.Equal(0.30, sidebarRow.Duration, Exact);
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    public void A_delayed_spring_starts_its_closed_form_at_the_delay() // UI/Common/Genie.swift:197
    {
        var morph = Animation.Smooth(duration: 0.32).Delay(0.2);
        Assert.Equal(0, morph.Progress(0.2), Exact);
        Assert.Equal(0.82102555358593099, morph.Progress(0.2 + 0.16), Exact);
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    [Trait("Rule", "R-VIEW-4")]
    public void Animations_are_values_equal_by_curve_and_parameters()
    {
        Assert.Equal(Animation.EaseOut(duration: 0.15), Animation.EaseOut(duration: 0.15));
        Assert.NotEqual(Animation.EaseOut(duration: 0.15), Animation.EaseOut(duration: 0.12));
        Assert.NotEqual(Animation.EaseOut(duration: 0.15), Animation.EaseIn(duration: 0.15));
        Assert.Equal(Animation.EaseIn(duration: 0.18).Delay(0.12), Animation.EaseIn(duration: 0.18).Delay(0.12));
        Assert.NotEqual(Animation.EaseIn(duration: 0.18), Animation.EaseIn(duration: 0.18).Delay(0.12));
        Assert.Equal(Animation.Spring(new Spring(response: 0.6, dampingRatio: 0.86)), Animation.Spring(new Spring(response: 0.6, dampingRatio: 0.86)));
        Assert.NotEqual(Animation.Spring(response: 0.32, dampingFraction: 0.9), Animation.Spring(response: 0.32, dampingFraction: 0.8));
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    public void A_timing_curve_as_an_Avalonia_easing_is_its_progress_over_its_duration()
    {
        var easeInOut = Animation.EaseInOut(duration: 0.4);
        Assert.Equal(0.4, easeInOut.Duration, Exact);
        Avalonia.Animation.Easings.Easing easing = easeInOut.ToEasing();
        Assert.Equal(0, easing.Ease(0), Exact);
        Assert.Equal(0.15625, easing.Ease(0.274375), Solved);
        Assert.Equal(0.5, easing.Ease(0.5), Solved);
        Assert.Equal(1, easing.Ease(1), Exact);
    }

    [Fact]
    [Trait("Decision", "ED-092")]
    public void A_delayed_curve_as_an_Avalonia_easing_holds_0_through_the_delay()
    {
        var sidebarRow = Animation.EaseIn(duration: 0.18).Delay(0.12);
        var easing = sidebarRow.ToEasing();
        Assert.Equal(0, easing.Ease(0.2), Exact);
        Assert.Equal(0, easing.Ease(0.4), Exact);
        Assert.Equal(0.5, easing.Ease((0.12 + 0.18 * 0.6575) / 0.30), Solved);
        Assert.Equal(1, easing.Ease(1), Exact);
    }

    [Fact]
    [Trait("Decision", "ED-011")]
    [Trait("Decision", "ED-092")]
    public void A_spring_runs_for_its_settling_duration_and_its_easing_samples_the_closed_form()
    {
        var glide = Animation.Spring(new Spring(response: 0.6, dampingRatio: 0.86));
        Assert.Equal(0.76702562673428487, glide.Duration, Exact);
        var easing = glide.ToEasing();
        Assert.Equal(0, easing.Ease(0), Exact);
        Assert.Equal(0.96725833315122334, easing.Ease(0.5), Exact);
        // smooth(duration: 0.32): zeta 1, so the settling duration is ln(1000) / (2*pi/0.32).
        Assert.Equal(Math.Log(1000) / (2 * Math.PI / 0.32), Animation.Smooth(duration: 0.32).Duration, Exact);
    }
}
