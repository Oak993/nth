using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

/// <summary>
/// What a click on the Hydra mark does (App/Views/Chat/HydraButton.swift:36-37, :77-93, :106-108, :200-294): the intro, the
/// idle popover or the panel on a left-click; the per-chat switch on a right-click or a Control-click, which goes no
/// further.
/// </summary>
[Collection("Headless")]
public sealed class HydraButtonClickTests(HeadlessSession headless)
{
    private void Run(Action<HydraButtonRig> body, bool hydraOn = true, bool introRead = true, bool hydraEnabledInSettings = true) =>
        ChatHeadlessFixtures.Run(headless, () =>
        {
            using var rig = new HydraButtonRig(hydraOn, introRead, hydraEnabledInSettings);
            body(rig);
        });

    private static void Press(HydraButtonRig rig, Point at, MouseButton button, RawInputModifiers modifiers = RawInputModifiers.None) =>
        rig.Press(at, button, modifiers);

    [Fact]
    [Trait("Req", "PR-HYDRA-158")] // HydraButton.swift:37 the intro comes first, even with heads out
    public void An_unread_intro_opens_on_a_click_before_anything_else()
    {
        Run(rig =>
        {
            rig.AddHead(HydraHeadInfo.Status.Running);
            Assert.Empty(rig.Presented()); // Hydra is off for this chat: nothing opened by itself (:93)
            rig.Click();

            Assert.IsType<HydraIntroPopover>(Assert.Single(rig.Presented()));
            Assert.False(rig.Runtime.IsHydraPanelHidden);
        }, hydraOn: false, introRead: false);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-158")] // HydraButton.swift:37 no heads at all: the idle popover
    public void With_the_intro_read_and_no_heads_a_click_opens_the_idle_popover()
    {
        Run(rig =>
        {
            rig.Click();

            Assert.IsType<HydraIdlePopover>(Assert.Single(rig.Presented()));
            Assert.False(rig.Runtime.IsHydraPanelHidden); // the panel is left alone
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-158")] // HydraButton.swift:37, :106-108 heads out: the panel is shown or hidden
    public void With_heads_out_a_click_shows_or_hides_the_panel_and_opens_no_popover()
    {
        Run(rig =>
        {
            rig.AddHead(HydraHeadInfo.Status.Completed); // any head counts, not only a running one
            Assert.False(rig.Runtime.IsHydraPanelHidden);

            rig.Click();
            Assert.True(rig.Runtime.IsHydraPanelHidden);
            rig.Click();
            Assert.False(rig.Runtime.IsHydraPanelHidden);
            Assert.Empty(rig.Presented());
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-158")] // HydraButton.swift:107 withAnimation(Chrome.panelSlide) { runtime.isHydraPanelHidden.toggle() }
    public void The_panel_is_toggled_with_the_panel_slide_animation()
    {
        Run(rig =>
        {
            rig.AddHead(HydraHeadInfo.Status.Running);
            rig.Tick(1);
            Animation? seen = null;
            rig.Runtime.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(rig.Runtime.IsHydraPanelHidden)) seen = Transaction.Current;
            };

            rig.Click();

            Assert.Equal(Chrome.PanelSlide, seen);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-158")] // HydraButton.swift:93 .onAppear { if !hasSeenHydraIntro, isOn { isIntroShown = true } }
    public void The_intro_opens_by_itself_when_the_mark_appears_with_hydra_on_and_the_intro_unread()
    {
        Run(rig => Assert.IsType<HydraIntroPopover>(Assert.Single(rig.Presented())), introRead: false);
        Run(rig => Assert.Empty(rig.Presented()), introRead: true);
        Run(rig => Assert.Empty(rig.Presented()), hydraOn: false, introRead: false);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-158")] // HydraButton.swift:90 the intro's dismissal writes AppSettings.hasSeenHydraIntro
    public void Dismissing_the_intro_writes_the_setting_and_later_clicks_fall_through_to_the_idle_popover()
    {
        Run(rig =>
        {
            var intro = rig.Open<HydraIntroPopover>();
            Assert.False(rig.Model.Settings.HasSeenHydraIntro);
            var gotIt = LogicalQuery.SelfAndDescendants<Button>(intro)
                .Single(button => LogicalQuery.SelfAndDescendants<TextBlock>(button).Any(text => text.Text == "Got it"));
            rig.Main.OnMain(() => gotIt.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            rig.Drain();

            Assert.True(rig.Model.Settings.HasSeenHydraIntro);
            Assert.Empty(rig.Presented()); // :90 isIntroShown = false

            rig.Click();
            Assert.IsType<HydraIdlePopover>(Assert.Single(rig.Presented()));
        }, introRead: false);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-162")] // HydraButton.swift:77-79, :273-277, :288-293 a right-click opens the switch and is consumed
    public void A_right_click_on_the_mark_opens_the_switch_popover_and_goes_no_further()
    {
        Run(rig =>
        {
            Press(rig, rig.MarkCentre, MouseButton.Right);

            Assert.IsType<HydraSwitchPopover>(Assert.Single(rig.Presented()));
            Assert.False(rig.Runtime.IsHydraPanelHidden);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-162")] // HydraButton.swift:78 isHydraSwitchShown.toggle()
    public void A_second_right_click_closes_the_switch_popover_again()
    {
        Run(rig =>
        {
            Press(rig, rig.MarkCentre, MouseButton.Right);
            Assert.NotNull(rig.PopupShowing<HydraSwitchPopover>());
            Press(rig, rig.MarkCentre, MouseButton.Right);
            Assert.Null(rig.PopupShowing<HydraSwitchPopover>());
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-162")] // HydraButton.swift:290 Control-click counts as a secondary click, and the button never sees it
    public void A_control_click_counts_as_a_secondary_click_and_the_left_click_does_not_run()
    {
        Run(rig =>
        {
            Press(rig, rig.MarkCentre, MouseButton.Left, RawInputModifiers.Control);

            Assert.IsType<HydraSwitchPopover>(Assert.Single(rig.Presented())); // no idle popover from the click
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-162")] // HydraButton.swift:273 only the right and the left button are watched
    public void A_middle_click_with_control_held_is_not_a_secondary_click()
    {
        Run(rig =>
        {
            Press(rig, rig.MarkCentre, MouseButton.Middle, RawInputModifiers.Control);

            Assert.Empty(rig.Presented());
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-162")] // HydraButton.swift:292 a click outside the mark's bounds passes through untouched
    public void A_right_click_outside_the_mark_opens_nothing_and_still_reaches_what_is_there()
    {
        Run(rig =>
        {
            var neighbour = new Border { Width = 40, Height = 32, Background = Avalonia.Media.Brushes.Transparent };
            var reached = 0;
            neighbour.PointerPressed += (_, _) => reached++;
            rig.Main.OnMain(() => rig.Content.Children.Add(neighbour));
            rig.Drain();
            var inside = neighbour.TranslatePoint(new Point(20, 16), rig.Window)!.Value;

            Press(rig, inside, MouseButton.Right);
            Press(rig, inside, MouseButton.Left, RawInputModifiers.Control);

            Assert.Empty(rig.Presented());
            Assert.Equal(2, reached);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-162")] // HydraButton.swift:276 a caught event is swallowed: what is beneath the mark never sees it
    public void A_caught_click_is_swallowed()
    {
        Run(rig =>
        {
            var neighbour = new Border { Width = 40, Height = 32, Background = Avalonia.Media.Brushes.Transparent };
            var reached = 0;
            rig.Content.PointerPressed += (_, _) => reached++; // the row the mark sits in
            rig.Main.OnMain(() => rig.Content.Children.Add(neighbour));
            rig.Drain();

            Press(rig, neighbour.TranslatePoint(new Point(20, 16), rig.Window)!.Value, MouseButton.Right);
            Assert.Equal(1, reached); // a right-click beside the mark reaches the row
            Press(rig, rig.MarkCentre, MouseButton.Right);
            Assert.Equal(1, reached); // over the mark it goes no further
            Assert.NotNull(rig.PopupShowing<HydraSwitchPopover>());
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-162")] // HydraButton.swift:289 a hidden mark's frame takes no secondary click
    public void A_hidden_mark_takes_no_secondary_click()
    {
        Run(rig =>
        {
            var centre = rig.MarkCentre;
            rig.Main.OnMain(() => rig.Button.IsVisible = false);
            rig.Drain();

            Press(rig, centre, MouseButton.Right);

            Assert.Empty(rig.Presented());
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-244")] // HydraButton.swift:266-269, :271-284 the monitor goes when the host leaves its window, and comes back with it
    public void Leaving_the_window_removes_the_monitor_and_joining_one_installs_it_once()
    {
        Run(rig =>
        {
            var installed = HydraRightTapHost.InstalledMonitors;
            var centre = rig.MarkCentre;
            Assert.True(installed >= 1);

            rig.Main.OnMain(() => rig.Content.Children.Remove(rig.Button));
            rig.Drain();
            Assert.Equal(installed - 1, HydraRightTapHost.InstalledMonitors);
            // once removed, a secondary click at that spot is no longer intercepted for that mark
            Press(rig, centre, MouseButton.Right);
            Assert.Empty(rig.Presented());

            rig.Main.OnMain(() => rig.Content.Children.Add(rig.Button));
            rig.Drain();
            Assert.Equal(installed, HydraRightTapHost.InstalledMonitors); // at most once
            Press(rig, rig.MarkCentre, MouseButton.Right);
            Assert.IsType<HydraSwitchPopover>(Assert.Single(rig.Presented()));
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-244")] // HydraButton.swift:264 deinit: closing the chat's window takes every monitor with it
    public void Closing_the_window_removes_the_monitor()
    {
        ChatHeadlessFixtures.Run(headless, () =>
        {
            var before = HydraRightTapHost.InstalledMonitors;
            var rig = new HydraButtonRig();
            Assert.Equal(before + 1, HydraRightTapHost.InstalledMonitors);
            rig.Dispose();
            Assert.Equal(before, HydraRightTapHost.InstalledMonitors);
        });
    }
}
