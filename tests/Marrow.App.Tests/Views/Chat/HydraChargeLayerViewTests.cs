using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Theme;

namespace Marrow.App.Tests.Views.Chat;

/// <summary>
/// The moving charge (App/Views/Chat/HydraButton.swift:389-613): the constants, what installAnimations puts on the two layers,
/// and the lifecycle that puts them back. What the render server would draw is a pure function of the shared clock.
/// </summary>
[Collection("Headless")]
public sealed class HydraChargeLayerViewTests(HeadlessSession headless)
{
    private static readonly SwiftColor Accent = new(0.2, 0.4, 0.9);

    private void Run(Action<TestRuntime, ManualFrameSource> body) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        body(runtime, frames);
    });

    private static HydraChargeLayerView Configured(SwiftColor? accent = null)
    {
        var view = new HydraChargeLayerView();
        view.Configure(HydraCharge.LayerColors, accent ?? Accent);
        return view;
    }

    private static Avalonia.Controls.Window Host(Control content)
    {
        var window = new Avalonia.Controls.Window { Content = content, Width = 100, Height = 100 };
        window.Show();
        return window;
    }

    private static void Advance(TestRuntime runtime, ManualFrameSource frames, double seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        runtime.Time.Advance(span);
        frames.Advance(span);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-167")] // HydraButton.swift:428-438 the nine constants
    public void The_constants_are_the_ones_the_source_declares()
    {
        Run((_, _) =>
        {
            Assert.Equal("turn", HydraChargeLayerView.TurnKey); // :428
            Assert.Equal("breathe", HydraChargeLayerView.BreathKey); // :429
            Assert.Equal(0.85f, HydraChargeLayerView.RingRestOpacity); // :431
            Assert.Equal(0.6f, HydraChargeLayerView.GlowRestOpacity); // :432
            Assert.Equal(4, HydraChargeLayerView.TurnDuration); // :433
            Assert.Equal(1.4, HydraChargeLayerView.BreathDuration); // :434
            Assert.Equal(2, HydraChargeLayerView.LineWidth); // :435
            Assert.Equal(2, HydraChargeLayerView.GlowStartRadius); // :437
            Assert.Equal(15, HydraChargeLayerView.GlowEndRadius); // :438
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-167")] // HydraButton.swift:583-612 the turn and the two breaths, each as the source installs them
    public void Installing_puts_one_turn_on_the_ring_and_one_breath_on_each_layer()
    {
        Run((_, _) =>
        {
            var view = Configured();
            var animations = view.Animations;
    
            Assert.Equal(3, animations.Count);
            var turn = Assert.Single(animations, item => item.Key == "turn");
            Assert.Equal(HydraChargeLayerView.ChargeLayer.Ring, turn.Layer); // :598
            Assert.Equal("transform.rotation.z", turn.KeyPath); // :588
            Assert.Equal(0, turn.From); // :589
            Assert.Equal(-2 * Math.PI, turn.To); // :592 negative: the layer is y-up, so it turns clockwise on screen
            Assert.Equal(4, turn.Duration); // :593
            Assert.Equal("linear", turn.Timing); // :595
            Assert.True(turn.RepeatsForever); // :594
            Assert.False(turn.AutoReverses);
            Assert.Equal(1, turn.BeginTime); // :586 ring.convertTime(1, from: nil): one second on the shared clock
            Assert.False(turn.IsRemovedOnCompletion); // :597
    
            var breaths = animations.Where(item => item.Key == "breathe").ToArray();
            Assert.Equal(2, breaths.Length);
            var ring = Assert.Single(breaths, item => item.Layer == HydraChargeLayerView.ChargeLayer.Ring);
            var glow = Assert.Single(breaths, item => item.Layer == HydraChargeLayerView.ChargeLayer.Glow);
            Assert.Equal(0.85, ring.From, 6); // :600 from the rest opacity
            Assert.Equal(0.6, glow.From, 6);
            foreach (var breath in breaths)
            {
                Assert.Equal("opacity", breath.KeyPath); // :601
                Assert.Equal(1, breath.To); // :603 to full
                Assert.Equal(1.4, breath.Duration); // :604
                Assert.True(breath.AutoReverses); // :605
                Assert.True(breath.RepeatsForever); // :606
                Assert.Equal("easeInEaseOut", breath.Timing); // :607
                Assert.Equal(1, breath.BeginTime); // :608 the one origin
                Assert.False(breath.IsRemovedOnCompletion); // :609
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-167")] // HydraButton.swift:584-585 installAnimations removes everything first, so reinstalling is idempotent
    public void Reinstalling_replaces_the_animations_and_does_not_stack_them()
    {
        Run((_, _) =>
        {
            var view = Configured();
            var window = Host(view);
            Assert.Equal(3, view.Animations.Count);
            HydraChargeLayerView.PostApplicationDidBecomeActive();
            HydraChargeLayerView.PostApplicationDidBecomeActive();
            Assert.Equal(3, view.Animations.Count);
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-167")] // HydraButton.swift:588-595 linear, once every 4 s, clockwise on screen, without end
    public void The_ring_turns_once_every_four_seconds_clockwise_and_without_end()
    {
        Run((runtime, frames) =>
        {
            var view = Configured();
            var window = Host(view);
            Advance(runtime, frames, 2); // past the origin, one second on the shared clock, before which nothing has begun
            var start = view.TurnDegrees;

            Advance(runtime, frames, 1);
            Assert.Equal(90, Normalised(view.TurnDegrees - start), 0.001); // a quarter turn in a second, clockwise (positive)
            Advance(runtime, frames, 1);
            Assert.Equal(180, Normalised(view.TurnDegrees - start), 0.001); // linear
            Advance(runtime, frames, 2);
            Assert.Equal(0, Normalised(view.TurnDegrees - start), 0.001); // once every four seconds
            Advance(runtime, frames, 40);
            Assert.Equal(0, Normalised(view.TurnDegrees - start), 0.001); // forever
            window.Close();
        });
    }

    private static double Normalised(double degrees) => ((degrees % 360) + 360) % 360;

    [Fact]
    [Trait("Req", "PR-HYDRA-167")] // HydraButton.swift:600-611 both layers breathe from the rest opacity to full over 1.4 s and back, ease-in ease-out
    public void The_ring_and_the_glow_breathe_between_their_rest_opacity_and_full_every_two_point_eight_seconds()
    {
        Run((runtime, frames) =>
        {
            var view = Configured();
            var window = Host(view);
            var ring = new List<double>();
            var glow = new List<double>();
            for (var step = 0; step < 56; step++)
            {
                ring.Add(view.RingOpacity);
                glow.Add(view.GlowOpacity);
                Advance(runtime, frames, 0.05);
            }

            Assert.InRange(ring.Min(), 0.85, 0.8501); // never below its rest opacity, and it gets there
            Assert.InRange(ring.Max(), 0.999, 1.0); // and to full
            Assert.InRange(glow.Min(), 0.6, 0.6001);
            Assert.InRange(glow.Max(), 0.999, 1.0);
            // the same opacity every 2.8 s (1.4 up, 1.4 back), the next sample of the two cycles being 56 steps apart
            var before = (view.RingOpacity, view.GlowOpacity);
            Advance(runtime, frames, 2.8);
            Assert.Equal(before.RingOpacity, view.RingOpacity, 6);
            Assert.Equal(before.GlowOpacity, view.GlowOpacity, 6);
            // glow and ring move in step: the same fraction of their way to full
            Assert.Equal((view.RingOpacity - 0.85) / 0.15, (view.GlowOpacity - 0.6) / 0.4, 6);
            // ease-in ease-out, not linear: a quarter of the way along the first leg it has not yet covered a quarter
            Assert.True(HydraChargeLayerView.Phase(0.35).BreathProgress < 0.25);
            Assert.True(HydraChargeLayerView.Phase(1.05).BreathProgress > 0.75);
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-167")] // HydraButton.swift:588-611 the three animations as a pure function of the seconds since the origin
    public void Phase_runs_the_turn_in_four_seconds_and_the_breath_up_in_one_point_four_and_back_in_one_point_four()
    {
        Run((_, _) =>
        {
            Assert.Equal((0, 0), HydraChargeLayerView.Phase(0));
            Assert.Equal(0.25, HydraChargeLayerView.Phase(1).TurnFraction, 9);
            Assert.Equal(0.5, HydraChargeLayerView.Phase(2).TurnFraction, 9);
            Assert.Equal(0, HydraChargeLayerView.Phase(4).TurnFraction, 9);
            Assert.Equal(0.5, HydraChargeLayerView.Phase(0.7).BreathProgress, 6); // ease-in ease-out is symmetric about its middle
            Assert.Equal(1, HydraChargeLayerView.Phase(1.4).BreathProgress, 6);
            Assert.Equal(0.5, HydraChargeLayerView.Phase(2.1).BreathProgress, 6); // autoreverses
            Assert.Equal(0, HydraChargeLayerView.Phase(2.8).BreathProgress, 6);
            Assert.Equal(0, HydraChargeLayerView.Phase(-3).TurnFraction); // before its beginTime nothing has moved
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-167")] // HydraButton.swift:581-586 every animation is begun at one fixed origin: putting it back changes nothing on screen
    public void Putting_the_animations_back_leaves_the_ring_and_the_glow_where_the_clock_says()
    {
        Run((runtime, frames) =>
        {
            var view = Configured();
            var window = Host(view);
            Advance(runtime, frames, 1.3);
            var before = (view.TurnDegrees, view.RingOpacity, view.GlowOpacity);
            Assert.NotEqual(0, before.TurnDegrees);

            HydraChargeLayerView.PostApplicationDidBecomeActive();

            Assert.Equal(before.TurnDegrees, view.TurnDegrees, 9);
            Assert.Equal(before.RingOpacity, view.RingOpacity, 9);
            Assert.Equal(before.GlowOpacity, view.GlowOpacity, 9);
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-167")] // HydraButton.swift:584-585, :597 with an animation gone, its layer shows its model value until the lifecycle puts it back
    public void A_removed_animation_stops_that_layer_where_its_model_value_is()
    {
        Run((runtime, frames) =>
        {
            var view = Configured();
            var window = Host(view);
            Advance(runtime, frames, 1.3);
            Assert.NotEqual(0, view.TurnDegrees);

            view.RemoveAnimation(HydraChargeLayerView.ChargeLayer.Ring, HydraChargeLayerView.TurnKey);
            view.RemoveAnimation(HydraChargeLayerView.ChargeLayer.Glow, HydraChargeLayerView.BreathKey);
            Assert.Equal(0, view.TurnDegrees);
            Assert.Equal(0.6, view.GlowOpacity, 6);
            Assert.NotEqual(0.85, view.RingOpacity); // the ring's own breath is still there
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-239")] // HydraButton.swift:493-501 colours and accent are stored; applyColors only on a real change
    public void Configure_recolors_only_when_the_colours_or_the_accent_differ_by_value()
    {
        Run((_, _) =>
        {
            var view = new HydraChargeLayerView();
            Assert.Equal(0, view.RecolorCount);
    
            view.Configure(HydraCharge.LayerColors, Accent);
            Assert.Equal(1, view.RecolorCount);
            Assert.Equal(6, view.StoredColors.Count);
            Assert.Same(Accent, view.StoredAccent); // stored unconditionally
    
            view.Configure(HydraCharge.LayerColors, new SwiftColor(0.2, 0.4, 0.9)); // equal by value, not by identity
            Assert.Equal(1, view.RecolorCount);
    
            view.Configure(HydraCharge.LayerColors, new SwiftColor(0.9, 0.4, 0.2));
            Assert.Equal(2, view.RecolorCount); // a different accent
    
            var other = HydraCharge.LayerColors.Reverse().ToArray();
            view.Configure(other, view.StoredAccent);
            Assert.Equal(3, view.RecolorCount); // a different colour array
            Assert.Equal(other.Length, view.StoredColors.Count);
            Assert.Equal(other[0].Resolve(ThemeVariant.Light), view.StoredColors[0].Resolve(ThemeVariant.Light));

            // an accent that differs only in the dark appearance is a different accent
            var light = new SwiftColor(0.2, 0.4, 0.9);
            view.Configure(HydraCharge.LayerColors, SwiftColor.Dynamic(light, new SwiftColor(0.1, 0.1, 0.1)));
            var recolors = view.RecolorCount;
            view.Configure(HydraCharge.LayerColors, SwiftColor.Dynamic(light, new SwiftColor(0.1, 0.1, 0.1))); // the same again
            Assert.Equal(recolors, view.RecolorCount);
            view.Configure(HydraCharge.LayerColors, SwiftColor.Dynamic(light, new SwiftColor(0.7, 0.7, 0.7)));
            Assert.Equal(recolors + 1, view.RecolorCount);
            view.Configure(HydraCharge.LayerColors, SwiftColor.Dynamic(new SwiftColor(0.9, 0.9, 0.1), new SwiftColor(0.7, 0.7, 0.7))); // and one only in the light
            Assert.Equal(recolors + 2, view.RecolorCount);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-239")] // HydraButton.swift:500 the animations are reinstalled ONLY when the turn animation is gone
    public void Configure_reinstalls_only_when_the_turn_animation_is_gone()
    {
        Run((_, _) =>
        {
            var view = Configured();
            Assert.Equal(1, view.InstallCount);
    
            view.Configure(HydraCharge.LayerColors, Accent); // a re-render of the button, every hover
            view.Configure(HydraCharge.LayerColors, new SwiftColor(0.1, 0.1, 0.1));
            Assert.Equal(1, view.InstallCount);
    
            view.RemoveAnimation(HydraChargeLayerView.ChargeLayer.Ring, HydraChargeLayerView.BreathKey);
            view.Configure(HydraCharge.LayerColors, Accent); // the breath is gone but the turn is not
            Assert.Equal(1, view.InstallCount);
    
            var recolors = view.RecolorCount;
            view.RemoveAnimation(HydraChargeLayerView.ChargeLayer.Ring, HydraChargeLayerView.TurnKey);
            view.Configure(HydraCharge.LayerColors, Accent);
            Assert.Equal(2, view.InstallCount); // one install
            Assert.Equal(3, view.Animations.Count);
            Assert.Equal(recolors, view.RecolorCount); // and no recolor, the values being the stored ones
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-239")] // HydraButton.swift:569 applyColors has nothing to apply without a colour
    public void With_no_colours_there_is_nothing_to_recolor()
    {
        Run((_, _) =>
        {
            var view = new HydraChargeLayerView();
            view.Configure([], new SwiftColor(0.9, 0.4, 0.2));
    
            Assert.Equal(0, view.RecolorCount);
            Assert.Equal(1, view.InstallCount); // the animations go on all the same
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-240")] // HydraButton.swift:503-510 joining a window recolors and installs, unconditionally
    public void Joining_a_window_recolors_and_reinstalls_and_leaving_it_does_nothing()
    {
        Run((_, _) =>
        {
            var view = Configured();
            // in the appearance it will join, so that nothing but joining recolors it
            var host = new ThemeVariantScope { Child = view, RequestedThemeVariant = ThemeVariant.Light };
            var (recolors, installs) = (view.RecolorCount, view.InstallCount);
            var window = Host(host);

            Assert.Equal(recolors + 1, view.RecolorCount);
            Assert.Equal(installs + 1, view.InstallCount); // though the turn animation is there: no test on this path
            Assert.Equal(3, view.Animations.Count);

            (recolors, installs) = (view.RecolorCount, view.InstallCount);
            window.Content = null; // out of its window, in the appearance it had
            Assert.Equal(recolors, view.RecolorCount);
            Assert.Equal(installs, view.InstallCount);
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-241")] // HydraButton.swift:518-521 the app becoming active reinstalls, for a view in a window, and does not recolor
    public void The_app_becoming_active_reinstalls_for_views_in_a_window_only()
    {
        Run((_, _) =>
        {
            var inside = Configured();
            var second = Configured(); // the notification has no object filter: every live view hears it
            var outside = Configured();
            var host = new StackPanel { Children = { inside, second } };
            var window = Host(host);
            var counts = new[] { inside, second, outside }.Select(view => (view.InstallCount, view.RecolorCount)).ToArray();

            HydraChargeLayerView.PostApplicationDidBecomeActive();

            Assert.Equal(counts[0].InstallCount + 1, inside.InstallCount);
            Assert.Equal(counts[1].InstallCount + 1, second.InstallCount);
            Assert.Equal(counts[2].InstallCount, outside.InstallCount); // guard window != nil
            Assert.Equal(counts[0].RecolorCount, inside.RecolorCount); // no recolor on this path
            Assert.Equal(counts[1].RecolorCount, second.RecolorCount);
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-242")] // HydraButton.swift:523-528 three gates: its own window, by identity, now visible
    public void An_occlusion_change_reinstalls_only_for_the_views_own_window_when_it_became_visible()
    {
        Run((_, _) =>
        {
            var view = Configured();
            var window = Host(view);
            var otherWindow = Host(new Border());
            var loose = Configured();
            var (installs, recolors) = (view.InstallCount, view.RecolorCount);

            HydraChargeLayerView.PostWindowDidChangeOcclusionState(otherWindow, true); // a popover or tooltip ordered in
            Assert.Equal(installs, view.InstallCount);
            HydraChargeLayerView.PostWindowDidChangeOcclusionState(window, false); // going out of view
            Assert.Equal(installs, view.InstallCount);
            HydraChargeLayerView.PostWindowDidChangeOcclusionState(window, true);
            Assert.Equal(installs + 1, view.InstallCount);
            Assert.Equal(recolors, view.RecolorCount); // no recolor on this path

            var looseInstalls = loose.InstallCount; // a view with no window at all
            HydraChargeLayerView.PostWindowDidChangeOcclusionState(window, true);
            Assert.Equal(looseInstalls, loose.InstallCount);
            window.Close();
            otherWindow.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-243")] // HydraButton.swift:530-533 a theme change re-reads the accent and recolors, touching no animation
    public void A_theme_change_replaces_the_stored_accent_and_recolors_without_touching_the_animations()
    {
        Run((_, _) =>
        {
            var previous = ThemeManager.Current;
            try
            {
                ThemeManager.Current = AppTheme.System;
                var view = Configured(Accent);
                var window = Host(view);
                var (recolors, installs, animations) = (view.RecolorCount, view.InstallCount, view.Animations.ToArray());

                ThemeManager.Current = AppTheme.Dracula;

                Assert.NotSame(Accent, view.StoredAccent);
                Assert.Same(Marrow.UI.Chrome.Chrome.AccentNSColor, view.StoredAccent); // Chrome.accentNSColor now
                Assert.Equal(recolors + 1, view.RecolorCount);
                Assert.Equal(installs, view.InstallCount);
                Assert.Equal(animations, view.Animations);
                window.Close();
            }
            finally { ThemeManager.Current = previous; }
        });
    }

    // ThemeManager is set from any actor and posts DidChange on the setter's thread (AppTheme.cs); every other observer turns on
    // its own control's thread. The charge's observer recoloured the view on the setter's thread, so a theme set off the UI thread
    // while a charge was alive threw out of the setter ("a different thread owns it") and the observers after it never heard it.
    [Fact]
    [Trait("Req", "PR-HYDRA-243")] // HydraButton.swift:530-533, on the view's own thread
    public void A_theme_change_from_another_thread_recolors_the_charge_on_its_own_thread()
    {
        Run((_, _) =>
        {
            var previous = ThemeManager.Current;
            try
            {
                ThemeManager.Current = AppTheme.System;
                var view = Configured(Accent);
                var window = Host(view);
                var recolors = view.RecolorCount;

                var change = Task.Run(() => ThemeManager.Current = AppTheme.Dracula);
                Assert.True(change.Wait(TimeSpan.FromSeconds(5)));
                change.GetAwaiter().GetResult(); // the setter completes: no observer threw on its thread
                Assert.Equal(recolors, view.RecolorCount); // nothing recoloured off the view's thread

                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Same(Marrow.UI.Chrome.Chrome.AccentNSColor, view.StoredAccent);
                Assert.Equal(recolors + 1, view.RecolorCount);
                window.Close();
            }
            finally { ThemeManager.Current = previous; }
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-243")] // HydraButton.swift:530-533 not gated on having a window
    public void A_theme_change_reaches_a_view_that_is_not_in_a_window()
    {
        Run((_, _) =>
        {
            var previous = ThemeManager.Current;
            try
            {
                ThemeManager.Current = AppTheme.System;
                var view = Configured(Accent);
                var recolors = view.RecolorCount;

                ThemeManager.Current = AppTheme.Dracula;

                Assert.Equal(recolors + 1, view.RecolorCount);
                Assert.Same(Marrow.UI.Chrome.Chrome.AccentNSColor, view.StoredAccent);
            }
            finally { ThemeManager.Current = previous; }
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-243")] // HydraButton.swift:512-516 an appearance change recolors and does not re-read the accent
    public void An_appearance_change_recolors_without_re_reading_the_accent()
    {
        Run((_, _) =>
        {
            var view = Configured(Accent);
            var host = new ThemeVariantScope { Child = view, RequestedThemeVariant = ThemeVariant.Light };
            var window = Host(host);
            var (recolors, installs) = (view.RecolorCount, view.InstallCount);

            host.RequestedThemeVariant = ThemeVariant.Dark;

            Assert.Equal(recolors + 1, view.RecolorCount);
            Assert.Same(Accent, view.StoredAccent); // not re-read
            Assert.Equal(installs, view.InstallCount); // the animations untouched
            window.Close();
        });
    }

    // The first six roster colours as the ring's colours run from 3 o'clock round clockwise (UI-HYDRA-003): Hank at 3, Otto
    // (the fourth, half way round) at 9.
    private static readonly (double Red, double Green, double Blue) Hank = (1, 0x8A / 255.0 / (0xFF / 255.0), 0x3D / 255.0 / (0xFF / 255.0));
    private static readonly (double Red, double Green, double Blue) Otto = (0xA3 / (double)0xE0, 0x5B / (double)0xE0, 1);

    private static void AssertHue((double Red, double Green, double Blue) actual, (double Red, double Green, double Blue) expected)
    {
        Assert.Equal(expected.Red, actual.Red, 0.1);
        Assert.Equal(expected.Green, actual.Green, 0.1);
        Assert.Equal(expected.Blue, actual.Blue, 0.1);
    }

    private static Avalonia.Controls.Window Black(Control content, double side = 32)
    {
        content.Width = side;
        content.Height = side;
        var window = new Avalonia.Controls.Window
        {
            Width = 100,
            Height = 100,
            Background = Brushes.Black,
            Content = new Border { Child = content, Width = side, Height = side, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top },
        };
        window.Show();
        return window;
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-003")] // HydraButton.swift:376-381 the still ring's angular gradient: Hank at 3 o'clock, round clockwise
    public void The_still_ring_starts_with_the_first_colour_at_three_oclock_and_runs_clockwise()
    {
        Run((_, _) =>
        {
            var window = Black(HydraCharge.Ring(false));
            var hues = FramePixels.Hues(window, (30, 16), (1, 16));

            AssertHue(hues[0], Hank); // 3 o'clock
            AssertHue(hues[1], Otto); // 9 o'clock: half way round, the fourth of six
            Assert.Equal(0.85, FramePixels.Levels(window, (30, 16))[0], 0.06); // at its still opacity, 0.85
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-167")] // HydraButton.swift:568-579, :590-592 the moving ring draws the same colours the same way round, and turns clockwise on screen
    public void The_moving_ring_draws_the_same_gradient_and_turns_clockwise()
    {
        Run((runtime, frames) =>
        {
            var view = Configured();
            var window = Black(view);
            var hues = FramePixels.Hues(window, (30, 16), (1, 16));
            AssertHue(hues[0], Hank); // before the origin, nothing has turned: as still
            AssertHue(hues[1], Otto);
            Assert.Equal(0.85, FramePixels.Levels(window, (30, 16))[0], 0.06); // the ring at its rest opacity

            Advance(runtime, frames, 2); // one second past the origin: a quarter turn
            // what was at 9 o'clock has come round to 12 o'clock, and what was at 12 o'clock has gone on to 3
            AssertHue(FramePixels.Hues(window, (16, 1))[0], Otto);
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-167")] // HydraButton.swift:543-550 the glow: a circle two points in, its gradient fading from two to fifteen points out of the centre
    public void The_glow_is_the_accent_at_point_four_six_of_its_rest_opacity_fading_with_the_distance()
    {
        Run((_, _) =>
        {
            var view = Configured(new SwiftColor(0, 0, 1)); // a pure blue accent: its strength is the blue channel
            var window = Black(view);

            var levels = FramePixels.Levels(window, (16, 16), (16 + 8, 16), (16 + 11, 16), (16 + 12, 16));
            Assert.Equal(0.46 * 0.6, levels[0], 0.04); // alpha 0.46 at the glow's rest opacity 0.6
            Assert.InRange(levels[1], 0.01, levels[0] - 0.05); // fainter further out
            Assert.InRange(levels[2], 0.0, levels[1]); // and fainter still
            Assert.True(levels[3] > 0.02, "the glow reaches to 14 points from the centre: the frame less its 2-point padding");
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-167")] // HydraButton.swift:538 nothing is laid out in a frame of 4 points or less
    public void A_frame_of_four_points_or_less_draws_nothing()
    {
        Run((_, _) =>
        {
            var tiny = Configured(new SwiftColor(0, 0, 1));
            var window = Black(tiny, 4);
            Assert.Equal(0, FramePixels.Levels(window, (2, 2))[0], 0.001);
            window.Close();

            var small = Configured(new SwiftColor(0, 0, 1));
            window = Black(small, 8);
            Assert.True(FramePixels.Levels(window, (4, 4))[0] > 0.05);
            window.Close();
        });
    }
}
