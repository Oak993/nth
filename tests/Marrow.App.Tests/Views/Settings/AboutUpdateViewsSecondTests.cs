using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// Source-derived tests for DroppyCode/App/Views/Settings/AboutUpdateViews.swift (B2 1fbf81e), unit
// App.Views.Settings.AboutUpdateViews, part 2 of 3. Every expected value is transcribed from the cited Swift
// lines or a record's "deviation" field (OD-0012 F1 "Droppy Code" -> "Marrow"), never from the C# under test.
//
// This part covers the release-note cards (UpdateReleaseSectionCards, :190-253, neither needs UpdateChecker
// nor AppUpdater, so it is exercised directly through Avalonia.Headless), the progress slider's pure geometry
// and colour math (UpdateProgressSlider, :382-524, likewise independent of UpdateChecker/AppUpdater since it
// takes a plain fraction), and the identity pill (AboutIdentityPill, :551-571).
[Collection("Headless")]
public sealed class AboutUpdateViewsSecondTests(HeadlessSession headless)
{
    private static Avalonia.Controls.Window Show(Control content)
    {
        var window = new Avalonia.Controls.Window { Width = 500, Height = 500, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static UpdateReleaseNotesDigest.Section Section(ReleaseNoteCategory category, params string[] bullets) =>
        new()
        {
            Category = category,
            Bullets = new ValueList<string>(bullets),
            Rollup = null,
            RollupCount = 0,
        };

    private static UpdateReleaseNotesDigest Digest(params UpdateReleaseNotesDigest.Section[] sections) =>
        new() { Sections = new ValueList<UpdateReleaseNotesDigest.Section>(sections) };

    // MARK: - UpdateReleaseSectionCards (:190-253)

    [Fact]
    [Trait("Req", "PR-UPDATE-078")] // AboutUpdateViews.swift:196-203, :206-240
    [Trait("Req", "UI-SETTINGS-057")] // AboutUpdateViews.swift:190-252
    public void Three_cards_render_always_in_new_bugs_refinements_order_with_the_count_summary_when_present()
    {
        var digest = Digest(
            Section(ReleaseNoteCategory.NewFeatures, "Added dark mode"),
            Section(ReleaseNoteCategory.BugFixes, "Fixed A", "Fixed B"));
        headless.OnUiThread(() =>
        {
            var window = Show(new UpdateReleaseSectionCards(digest, "1.4.2"));
            var buttons = window.GetVisualDescendants().OfType<Button>().ToList();
            Assert.Equal(3, buttons.Count); // :198 ForEach over all three cases, always three cards

            var texts = buttons.Select(b => b.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList()).ToList();
            Assert.Contains("New", texts[0]); // :219 cardTitle for newFeatures first
            Assert.Contains("1 addition", texts[0]); // :46 singular
            Assert.Contains("Bugs", texts[1]);
            Assert.Contains("2 fixed", texts[1]);
            Assert.Contains("Refinements", texts[2]);
            Assert.Contains("Nothing here", texts[2]); // :222 absent section
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-078")] // AboutUpdateViews.swift:237-239
    public void An_absent_section_card_is_disabled_at_45_percent_opacity_with_the_nothing_here_tooltip()
    {
        var digest = Digest();
        headless.OnUiThread(() =>
        {
            var window = Show(new UpdateReleaseSectionCards(digest, "1.4.2"));
            var buttons = window.GetVisualDescendants().OfType<Button>().ToList();
            Assert.All(buttons, b => Assert.False(b.IsEnabled)); // :237 .disabled(section == nil)
            Assert.All(buttons, b => Assert.Equal(0.45, b.Opacity)); // :238
            Assert.All(buttons, b => Assert.Equal("Nothing here", ToolTip.GetTip(b))); // :239
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-078")] // AboutUpdateViews.swift:239-240
    public void A_present_section_card_shows_the_every_change_in_tooltip_and_the_long_section_title_as_accessibility_label()
    {
        var digest = Digest(Section(ReleaseNoteCategory.BugFixes, "Fixed A"));
        headless.OnUiThread(() =>
        {
            var window = Show(new UpdateReleaseSectionCards(digest, "1.4.2"));
            var buttons = window.GetVisualDescendants().OfType<Button>().ToList();
            var bugsButton = buttons[1];
            Assert.True(bugsButton.IsEnabled);
            Assert.Equal("Every change in bug fixes", ToolTip.GetTip(bugsButton)); // :239 lower-cased section title
            Assert.Equal("Bug fixes", AutomationProperties.GetName(bugsButton)); // :240 the long title, not "Bugs"
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-079")] // AboutUpdateViews.swift:209-212
    public void Tapping_a_card_with_a_section_toggles_the_presented_category_and_tapping_a_disabled_card_does_nothing()
    {
        var digest = Digest(Section(ReleaseNoteCategory.NewFeatures, "Added dark mode"));
        headless.OnUiThread(() =>
        {
            var presenter = new RecordingPresenter();
            using var presentation = Presentation.Presenter.Override(presenter);
            var cards = new UpdateReleaseSectionCards(digest, "1.4.2");
            var window = Show(cards);
            var buttons = window.GetVisualDescendants().OfType<Button>().ToList();
            Assert.Empty(presenter.Calls); // :194 @State presented starts nil
            buttons[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { true }, presenter.Calls.Select(call => call.Presented)); // :211 toggled on
            buttons[1].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { true }, presenter.Calls.Select(call => call.Presented)); // disabled card does nothing
            buttons[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { true, false }, presenter.Calls.Select(call => call.Presented)); // :211 toggled off
            window.Close();
        });
    }

    // MARK: - UpdateProgressSlider pure geometry and colour (:382-524)

    [Theory]
    [InlineData(0.0, 7.0)] // :427 trackHeight/2 at value 0, default track 14
    [InlineData(1.0, 97.0)] // trackHeight/2 + (104-14)*1 = 7 + 90
    [InlineData(0.5, 52.0)] // 7 + 90*0.5
    [Trait("Req", "PR-UPDATE-109")] // AboutUpdateViews.swift:426-428
    [Trait("Req", "UI-UPDATE-008")] // AboutUpdateViews.swift:426-428
    public void HeadX_travels_between_the_two_end_cap_centres_over_a_104_point_slider(double value, double expected)
    {
        Assert.Equal(expected, UpdateProgressSlider.HeadX(value, width: 104, trackHeight: 14), 6);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-109")] // AboutUpdateViews.swift:427 CGFloat(min(max(value, 0), 1))
    public void HeadX_clamps_the_fraction_to_0_and_1()
    {
        Assert.Equal(UpdateProgressSlider.HeadX(0, 104, 14), UpdateProgressSlider.HeadX(-5, 104, 14));
        Assert.Equal(UpdateProgressSlider.HeadX(1, 104, 14), UpdateProgressSlider.HeadX(5, 104, 14));
    }

    [Theory]
    [InlineData(0.0, 0.0)] // below start -> 0
    [InlineData(0.011, 0.0)]
    [InlineData(0.05, 1.0)] // at or above end -> 1
    [InlineData(0.2, 1.0)]
    [Trait("Req", "PR-UPDATE-108")] // AboutUpdateViews.swift:431-437
    public void FillOpacity_is_zero_below_start_and_one_at_or_above_end(double fraction, double expected)
    {
        Assert.Equal(expected, UpdateProgressSlider.FillOpacity(fraction));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-108")] // AboutUpdateViews.swift:436-437
    public void FillOpacity_follows_the_smoothstep_polynomial_between_start_and_end()
    {
        var midpoint = (0.012 + 0.05) / 2.0;
        var t = (midpoint - 0.012) / (0.05 - 0.012);
        var expected = t * t * (3 - 2 * t); // :437, smoothstep is exactly 0.5 at the midpoint by symmetry
        Assert.Equal(0.5, expected, 10);
        Assert.Equal(expected, UpdateProgressSlider.FillOpacity(midpoint), 10);

        // Monotonic within the ramp.
        Assert.True(UpdateProgressSlider.FillOpacity(0.02) < UpdateProgressSlider.FillOpacity(0.03));
        Assert.True(UpdateProgressSlider.FillOpacity(0.03) < UpdateProgressSlider.FillOpacity(0.04));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-111")] // AboutUpdateViews.swift:390-394
    [Trait("Req", "UI-UPDATE-007")] // AboutUpdateViews.swift:390-394
    public void The_fill_gradient_is_the_fixed_three_stop_literal_in_both_themes()
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var slider = new UpdateProgressSlider(0.5);
            var window = Show(slider);
            var brush = slider.GetVisualDescendants().OfType<Border>()
                .Select(border => border.Background).OfType<LinearGradientBrush>().First();
            var stops = brush.GradientStops;
            Assert.Equal(3, stops.Count);
            Assert.Equal(Color.FromRgb(0x67, 0xB9, 0xEE), stops[0].Color); // :391
            Assert.Equal(Color.FromRgb(0x1A, 0x77, 0xB4), stops[1].Color); // :392
            Assert.Equal(Color.FromRgb(0x12, 0x6C, 0xAF), stops[2].Color); // :393

            window.RequestedThemeVariant = ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();
            var darkBrush = slider.GetVisualDescendants().OfType<Border>()
                .Select(border => border.Background).OfType<LinearGradientBrush>().First();
            Assert.Equal(stops.Select(stop => stop.Color), darkBrush.GradientStops.Select(stop => stop.Color));
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-107")] // AboutUpdateViews.swift:420-422
    [Trait("Req", "UI-SETTINGS-058")] // AboutUpdateViews.swift:420-422
    [Trait("Decision", "ED-096")] // OD-0012 F1 product rename
    public void The_slider_accessibility_label_reads_updating_marrow_and_the_value_is_the_whole_percent()
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var slider = new UpdateProgressSlider(0.554);
            var window = Show(slider);
            Assert.Equal("Updating nth", AutomationProperties.GetName(slider)); // :421 DEVIATION OD-0012 F1
            Assert.Equal("55%", Assert.IsAssignableFrom<IValueProvider>(ControlAutomationPeer.CreatePeerForElement(slider)).Value); // :422 .percent.precision(.fractionLength(0))
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-107")] // AboutUpdateViews.swift:422
    public void The_accessibility_value_text_rounds_to_the_nearest_whole_percent()
    {
        headless.OnUiThread(() =>
        {
            var originalCulture = CultureInfo.CurrentCulture;
            var originalUiCulture = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
                var low = new UpdateProgressSlider(0.004);
                var high = new UpdateProgressSlider(0.999);
                Assert.Equal("0%", Assert.IsAssignableFrom<IValueProvider>(ControlAutomationPeer.CreatePeerForElement(low)).Value);
                Assert.Equal("100%", Assert.IsAssignableFrom<IValueProvider>(ControlAutomationPeer.CreatePeerForElement(high)).Value);
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
                CultureInfo.CurrentUICulture = originalUiCulture;
            }
        });
    }

    // MARK: - Sparkle field determinism (:494-524)

    [Fact]
    [Trait("Req", "PR-UPDATE-110")] // AboutUpdateViews.swift:502-523
    [Trait("Req", "UI-UPDATE-009")] // AboutUpdateViews.swift:502-523
    public void Field_produces_fourteen_particles_deterministically_from_the_seed()
    {
        var field = SparkleParticle.Field(14, 0x5EED_D80B);
        Assert.Equal(14, field.Count); // :395 count: 14
        var again = SparkleParticle.Field(14, 0x5EED_D80B);
        Assert.Equal(field, again); // same seed, same sequence

        // First particle, computed by hand from the same SplitMix64 recurrence at :506-511 seeded 0x5EEDD80B,
        // walked in the source's own draw order x, y, size, speed, phase, drift (:513-521); not read from the
        // C# under test (R-TEST-4).
        var first = field[0];
        Assert.Equal(0.14227714622904464, first.X, 12);
        Assert.Equal(0.57059065349669102, first.Y, 12); // :516 0.12 + next()*0.76
        Assert.Equal(1.415433605072985, first.Size, 10); // :517 1.2 + next()*1.6
        Assert.Equal(1.7804096046289621, first.Speed, 10); // :518 0.9 + next()*2.4
        Assert.Equal(3.962854788881442, first.Phase, 9); // :519 next() * pi * 2
        Assert.Equal(1.2641436300426923, first.Drift, 10); // :521 0.6 + next()*1.4
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-110")] // AboutUpdateViews.swift:513-516
    public void Every_particle_stays_within_its_documented_normalised_bounds()
    {
        var field = SparkleParticle.Field(14, 0x5EED_D80B);
        foreach (var p in field)
        {
            Assert.InRange(p.X, 0.0, 1.0); // :515 next() in [0, 1)
            Assert.InRange(p.Y, 0.12, 0.88); // :516 0.12 + next()*0.76
            Assert.InRange(p.Size, 1.2, 2.8); // :517
            Assert.InRange(p.Speed, 0.9, 3.3); // :518
            Assert.InRange(p.Phase, 0.0, Math.PI * 2); // :519
            Assert.InRange(p.Drift, 0.6, 2.0); // :521
        }
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-110")] // AboutUpdateViews.swift:406-409
    public void Reduce_motion_shows_no_sparkles_at_all()
    {
        headless.OnUiThread(() =>
        {
            var slider = new UpdateProgressSlider(0.5);
            EnvironmentValues.SetAccessibilityReduceMotion(slider, true); // :387, R-VIEW-6
            var window = Show(slider);
            Assert.Equal(0, slider.GetVisualDescendants().OfType<Ellipse>().Count(ellipse => ellipse.Classes.Contains("update-sparkle"))); // :408 reduceMotion ? [] : Self.sparkles
            window.Close();
        });
    }

    // MARK: - Identity pill (:551-571)

    [Fact]
    [Trait("Req", "PR-SETTINGS-241")] // AboutUpdateViews.swift:551-571
    [Trait("Req", "UI-SETTINGS-055")] // AboutUpdateViews.swift:551-571
    [Trait("Req", "UI-UPDATE-001")] // AboutUpdateViews.swift:551-571
    public void Identity_pill_shows_v_version_at_asymmetric_padding_with_the_renamed_accessibility_label()
    {
        using var running = AppInfo.Running.Override(AppInfo.Read(new Dictionary<string, object?>
        {
            ["CFBundleDisplayName"] = "nth", ["CFBundleShortVersionString"] = "1.4.2", ["CFBundleVersion"] = "20",
        }, isDevelopment: false));
        headless.OnUiThread(() =>
        {
            var pill = new AboutIdentityPill();
            var window = Show(pill);
            var text = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text!.StartsWith("v", StringComparison.Ordinal));
            Assert.Equal("v1.4.2", text.Text); // :559 Text(verbatim: "v\(AppInfo.version)")
            Assert.Equal("nth 1.4.2", AutomationProperties.GetName(pill)); // :569 DEVIATION OD-0012 F1
            window.Close();
        });
    }
}
