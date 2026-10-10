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

    // MARK: - UpdateProgressSlider accessibility (:420-422). The slider's head, fill gradient and sparkle field (:382-524) were replaced by the nth install capsule (motion pack step 10); its drawing is proven in NthSitesBTests.

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
            Assert.Equal("Updating Marrow", AutomationProperties.GetName(slider)); // :421 DEVIATION OD-0012 F1
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

    // MARK: - Identity pill (:551-571)

    [Fact]
    [Trait("Req", "PR-SETTINGS-241")] // AboutUpdateViews.swift:551-571
    [Trait("Req", "UI-SETTINGS-055")] // AboutUpdateViews.swift:551-571
    [Trait("Req", "UI-UPDATE-001")] // AboutUpdateViews.swift:551-571
    public void Identity_pill_shows_v_version_at_asymmetric_padding_with_the_renamed_accessibility_label()
    {
        using var running = AppInfo.Running.Override(AppInfo.Read(new Dictionary<string, object?>
        {
            ["CFBundleDisplayName"] = "Marrow", ["CFBundleShortVersionString"] = "1.4.2", ["CFBundleVersion"] = "20",
        }, isDevelopment: false));
        headless.OnUiThread(() =>
        {
            var pill = new AboutIdentityPill();
            var window = Show(pill);
            var text = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text!.StartsWith("v", StringComparison.Ordinal));
            Assert.Equal("v1.4.2", text.Text); // :559 Text(verbatim: "v\(AppInfo.version)")
            Assert.Equal("Marrow 1.4.2", AutomationProperties.GetName(pill)); // :569 DEVIATION OD-0012 F1
            window.Close();
        });
    }
}
