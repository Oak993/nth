using System;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Markdown;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/AboutUpdateViews.swift (B3 52bc792), :190-845.
[Collection("Headless")]
public sealed class AboutUpdateViewsSweepTests(HeadlessSession headless)
{
    private static Avalonia.Controls.Window Show(Control content)
    {
        var window = new Avalonia.Controls.Window { Width = 600, Height = 500, Content = content };
        window.Show(); Dispatcher.UIThread.RunJobs(); return window;
    }

    private static AvailableUpdate Release(string notes = "", string version = "9.0.0") => new()
    {
        Version = version, Tag = "v" + version, Notes = notes,
        DownloadURL = SwiftURL.Create("http://127.0.0.1:1/artifact")!,
        PageURL = SwiftURL.Create("https://example.com/release")!,
    };

    // Seed the dependency's private-set observable properties, rather than making a live network request.
    // These are fixture inputs named by the Swift's checker state, never expectations copied from the view.
    private static void Set(UpdateChecker checker, string property, object? value) =>
        typeof(UpdateChecker).GetProperty(property, BindingFlags.Public | BindingFlags.Instance)!.SetValue(checker, value);

    [Theory]
    [Trait("Req", "PR-SETTINGS-242")] // App/Views/Settings/AboutUpdateViews.swift:580-707
    [Trait("Rule", "R-TEST-1")] // App/Views/Settings/AboutUpdateViews.swift:620-624
    [Trait("Req", "PR-UPDATE-075")] // App/Views/Settings/AboutUpdateViews.swift:678-707
    [Trait("Req", "UI-SETTINGS-056")] // App/Views/Settings/AboutUpdateViews.swift:628-707
    [Trait("Req", "UI-UPDATE-002")] // App/Views/Settings/AboutUpdateViews.swift:596-624,649-660
    [Trait("Req", "UI-UPDATE-003")] // App/Views/Settings/AboutUpdateViews.swift:596-624,649-660
    [Trait("Req", "UI-UPDATE-004")] // App/Views/Settings/AboutUpdateViews.swift:596-624,649-660
    [InlineData(false, null, "Not checked yet", "clock", "nth has not asked GitHub for a release yet")]
    [InlineData(true, null, "Up to date", "checkmark.seal.fill", "nth 1.7.0 is the latest version")]
    [InlineData(false, "Fixture error", "Check failed", "exclamationmark.triangle.fill", "Fixture error")]
    [InlineData(true, "Fixture error", "Check failed", "exclamationmark.triangle.fill", "Fixture error")]
    public void Standing_priority_help_and_accessibility_follow_the_checker_state(bool checkedBefore, string? error, string label, string symbol, string help)
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
            runtime.OnMain(() =>
            {
                var checker = new UpdateChecker(AppInfo.Current, [], "");
                if (checkedBefore) Set(checker, nameof(UpdateChecker.LastCheckedAt), SwiftDate.Now);
                Set(checker, nameof(UpdateChecker.LastError), error);
                using var shared = UpdateChecker.OverrideShared(checker);
                using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
                var pill = new AboutVersionPill();
                var window = Show(pill);
                var button = Assert.Single(pill.GetVisualDescendants().OfType<Button>());
                Assert.False(button.IsHitTestVisible);
                Assert.False(button.Focusable);
                Assert.Equal(help, ToolTip.GetTip(Assert.Single(pill.GetVisualDescendants().OfType<Control>(), c => ToolTip.GetTip(c) is not null))); // :674-680 .help outside .allowsHitTesting
                Assert.Equal("nth 1.7.0, " + label.ToLowerInvariant(), AutomationProperties.GetName(button));
                var title = Assert.Single(pill.GetVisualDescendants().OfType<TextBlock>());
                Assert.Equal(label, title.Text); Assert.Equal(12, title.FontSize); Assert.Equal(FontWeight.SemiBold, title.FontWeight);
                Assert.Contains(pill.GetVisualDescendants().OfType<SwiftImage>(), image => image.SymbolName == symbol);
                Assert.Contains(pill.GetVisualDescendants().OfType<StackPanel>(), row => row.Orientation == Orientation.Horizontal && row.Spacing == 6);
                window.Close();
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-073")] // App/Views/Settings/AboutUpdateViews.swift:580-583,644-675
    [Trait("Rule", "R-TEST-1")] // App/Views/Settings/AboutUpdateViews.swift:644-688
    public void Newer_stored_release_makes_the_pill_an_enabled_blue_install_button_with_exact_help()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
            runtime.OnMain(() =>
            {
                var checker = new UpdateChecker(AppInfo.Current, [], "");
                Set(checker, nameof(UpdateChecker.Update), Release());
                using var shared = UpdateChecker.OverrideShared(checker);
                using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
                var pill = new AboutVersionPill(); var window = Show(pill);
                var button = Assert.Single(pill.GetVisualDescendants().OfType<Button>());
                Assert.True(button.IsHitTestVisible); Assert.True(button.Focusable);
                Assert.Equal("Update & restart", AutomationProperties.GetName(button));
                Assert.Equal("Installs nth 9.0.0 and relaunches", ToolTip.GetTip(Assert.Single(pill.GetVisualDescendants().OfType<Control>(), c => ToolTip.GetTip(c) is not null))); // :674-680
                Assert.Contains(pill.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Update & restart");
                Assert.Contains(pill.GetVisualDescendants().OfType<SwiftImage>(), image => image.SymbolName == "arrow.down.circle.fill");
                Assert.Contains(pill.GetVisualDescendants().OfType<Border>(), border => border.Background is ISolidColorBrush brush && brush.Color.A is >= 50 and <= 52);
                window.Close();
            });
        });
    }

    [Theory]
    [Trait("Rule", "R-TEST-1")] // App/Views/Settings/AboutUpdateViews.swift:628-641,693-696
    [Trait("Req", "UI-UPDATE-006")] // App/Views/Settings/AboutUpdateViews.swift:628-641
    [InlineData(false)]
    [InlineData(true)]
    public void Install_or_celebration_shows_the_168_by_26_install_capsule_instead_of_the_button(bool celebration)
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
            runtime.OnMain(() =>
            {
                var checker = new UpdateChecker(AppInfo.Current, [], "");
                Set(checker, nameof(UpdateChecker.Update), Release());
                using var shared = UpdateChecker.OverrideShared(checker);
                var progress = UpdateInstallProgressFixtures.NewProgress();
                using var running = UpdateInstallProgress.Running.Override(progress);
                if (celebration) { progress.ArmCelebration(); progress.ConsumePendingCelebration(); } else progress.Begin();
                var pill = new AboutVersionPill(); var window = Show(pill);
                var slider = Assert.Single(pill.GetVisualDescendants().OfType<SmoothedUpdateProgressSlider>());
                Assert.Equal(168, slider.Width); Assert.Equal(26, slider.Height); // nth motion pack step 10: the install capsule
                Assert.Empty(pill.GetVisualDescendants().OfType<Button>());
                Assert.Contains(pill.GetVisualDescendants().OfType<UpdateProgressSlider>(), child => AutomationProperties.GetName(child) == "Updating nth");
                window.Close();
                progress.CompleteCelebration();
            });
        });
    }

    [Theory]
    [Trait("Rule", "R-TEST-1")] // App/Views/Settings/AboutUpdateViews.swift:824-844
    [Trait("Rule", "R-TEST-1")] // App/Views/Settings/AboutUpdateViews.swift:836-844
    [Trait("Rule", "R-TEST-1")] // App/Views/Settings/AboutUpdateViews.swift:826-833
    [Trait("Req", "UI-UPDATE-021")] // App/Views/Settings/AboutUpdateViews.swift:824-844
    [Trait("Rule", "R-TEST-1")] // App/Views/Settings/AboutUpdateViews.swift:826-833
    [InlineData(false, null, false, "nth checks GitHub for new versions a few times a day. Not checked yet.")]
    [InlineData(false, "Fixture error", false, "Fixture error Not checked yet.")]
    [InlineData(false, null, true, "nth 9.0.0 is ready to install. Not checked yet.")]
    [InlineData(true, "Fixture error", true, "Asking GitHub for the latest release…")]
    public void Check_row_prioritizes_checking_then_error_then_release_and_disables_the_checking_button(bool checking, string? error, bool ready, string detail)
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime(); using var frames = FrameClock.Source.Override(new ManualFrameSource());
            runtime.OnMain(() =>
            {
                var checker = new UpdateChecker(ReleaseFeed.Running("1.7.0"), [], "");
                Set(checker, nameof(UpdateChecker.IsChecking), checking); Set(checker, nameof(UpdateChecker.LastError), error);
                if (ready) Set(checker, nameof(UpdateChecker.Update), Release());
                using var shared = UpdateChecker.OverrideShared(checker);
                var row = new AboutUpdateCheckRow(); var window = Show(row);
                var texts = row.GetVisualDescendants().OfType<TextBlock>().ToArray();
                Assert.Contains(texts, text => text.Text == "Software update");
                Assert.Contains(texts, text => text.Text == detail);
                Assert.Contains(texts, text => text.Text == (checking ? "Checking…" : "Check now"));
                Assert.Equal(!checking, Assert.Single(row.GetVisualDescendants().OfType<Button>()).IsEnabled);
                window.Close();
            });
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")] // App/Views/Settings/AboutUpdateViews.swift:826-829,837
    [Trait("Rule", "R-TEST-1")] // App/Views/Settings/AboutUpdateViews.swift:837
    public void Check_now_runs_a_foreground_check_and_then_includes_the_relative_checked_stamp()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime(); var source = new ManualFrameSource(); using var frames = FrameClock.Source.Override(source);
            using var locale = SwiftFormatters.CurrentLocale.Override("en_US");
            var checker = runtime.OnMain(() => new UpdateChecker(ReleaseFeed.Running("1.7.0"), [], ""));
            using var shared = UpdateChecker.OverrideShared(checker);
            var row = new AboutUpdateCheckRow();
            Avalonia.Controls.Window? window = null;
            runtime.OnMain(() =>
            {
                window = Show(row);
                Assert.Single(row.GetVisualDescendants().OfType<Button>()).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            });
            runtime.RunUntilIdle();
            runtime.OnMain(() =>
            {
                Dispatcher.UIThread.RunJobs();
                // OD-0076: the new sentence replaces the old one over 0.18 s (the swap is finished before the words are read).
                runtime.Time.Advance(TimeSpan.FromMilliseconds(300)); source.Advance(TimeSpan.FromMilliseconds(300)); Dispatcher.UIThread.RunJobs();
                Assert.NotNull(checker.LastCheckedAt);
                Assert.Equal("No update feed is set.", checker.LastError); // OD-0012 I1
                var stamp = "Checked " + SwiftFormatters.RelativeNamed(checker.LastCheckedAt!.Value) + "."; // ED-096 formatter oracle
                Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "No update feed is set. " + stamp);
                window!.Close();
            });
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-1")] // App/Views/Settings/AboutUpdateViews.swift:259-299
    [Trait("Rule", "R-TEST-1")] // App/Views/Settings/AboutUpdateViews.swift:259-299
    public void Release_popover_lists_bullets_in_order_and_the_smaller_rollup_last()
    {
        headless.OnUiThread(() =>
        {
            var section = new UpdateReleaseNotesDigest.Section
            {
                Category = ReleaseNoteCategory.NewFeatures, Bullets = new ValueList<string>(["First bullet", "Second bullet"]),
                Rollup = "And 3 extra additions", RollupCount = 3,
            };
            var view = new UpdateReleaseSectionPopover(section, "9.0.0"); var window = Show(view);
            var outer = Assert.IsType<Border>(view.Child);
            Assert.Equal(320, outer.Width); Assert.Equal(new Thickness(14), outer.Padding);
            Assert.Equal(12, Assert.IsType<StackPanel>(outer.Child).Spacing);
            var scroll = Assert.Single(view.GetVisualDescendants().OfType<ScrollViewer>());
            Assert.Equal(320, scroll.MaxHeight); Assert.Equal(ScrollBarVisibility.Hidden, scroll.VerticalScrollBarVisibility);
            var bullets = Assert.IsType<StackPanel>(scroll.Content);
            Assert.Equal(9, bullets.Spacing);
            Assert.Equal(new[] { "First bullet", "Second bullet", "And 3 extra additions" }, bullets.Children.Cast<TextBlock>().Select(text => text.Text));
            Assert.Equal(new[] { 13.0, 13.0, 12.0 }, bullets.Children.Cast<TextBlock>().Select(text => text.FontSize));
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "New features" && text.FontWeight == FontWeight.SemiBold);
            window.Close();
        });
    }

    [Theory]
    [Trait("Req", "UI-UPDATE-012")] // App/Views/Settings/AboutUpdateViews.swift:206-240
    [Trait("Req", "UI-UPDATE-013")] // App/Views/Settings/AboutUpdateViews.swift:206-240
    [Trait("Req", "UI-UPDATE-014")] // App/Views/Settings/AboutUpdateViews.swift:206-240
    [InlineData(ReleaseNoteCategory.NewFeatures, "New", "1 addition", "Every change in new features")]
    [InlineData(ReleaseNoteCategory.BugFixes, "Bugs", "1 fixed", "Every change in bug fixes")]
    [InlineData(ReleaseNoteCategory.Refinements, "Refinements", "1 improved", "Every change in refinements")]
    public void Release_cards_have_exact_titles_counts_tooltips_and_fixed_icon_size(ReleaseNoteCategory category, string title, string count, string help)
    {
        headless.OnUiThread(() =>
        {
            var digest = new UpdateReleaseNotesDigest { Sections = new ValueList<UpdateReleaseNotesDigest.Section>([
                new() { Category = category, Bullets = new ValueList<string>(["Fixture bullet"]), RollupCount = 0 }]) };
            var view = new UpdateReleaseSectionCards(digest, "9.0.0"); var window = Show(view);
            var button = Assert.Single(view.GetVisualDescendants().OfType<Button>(), child => child.IsEnabled);
            Assert.Equal(help, ToolTip.GetTip(button));
            Assert.Contains(button.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == title);
            Assert.Contains(button.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == count);
            var icon = Assert.Single(button.GetVisualDescendants().OfType<SwiftImage>());
            Assert.Equal(26, icon.Width); Assert.Equal(26, icon.Height);
            Assert.Equal(new Thickness(8, 12), Assert.IsType<Border>(button.Content).Padding);
            window.Close();
        });
    }

    [Theory]
    [Trait("Req", "UI-UPDATE-016")] // App/Views/Settings/AboutUpdateViews.swift:750-762
    [Trait("Req", "UI-UPDATE-017")] // App/Views/Settings/AboutUpdateViews.swift:764-791 (OD-0012 H)
    [Trait("Req", "UI-UPDATE-018")] // App/Views/Settings/AboutUpdateViews.swift:765-776
    [InlineData("Raw note without recognized sections", true)]
    [InlineData("## New features\n- First addition", false)]
    public void Release_body_falls_back_to_raw_markdown_and_keeps_the_truthful_artifact_row_and_link(string notes, bool raw)
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime(); using var frames = FrameClock.Source.Override(new ManualFrameSource());
            runtime.OnMain(() =>
            {
                var checker = new UpdateChecker(ReleaseFeed.Running("1.7.0"), [], "");
                Set(checker, nameof(UpdateChecker.Update), Release(notes));
                using var shared = UpdateChecker.OverrideShared(checker);
                using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
                var section = new AboutSoftwareUpdateSection(); var window = Show(section);
                Assert.Equal(raw, section.GetVisualDescendants().OfType<MarkdownView>().Any());
                if (raw) Assert.Contains(section.GetVisualDescendants().OfType<ScrollViewer>(), scroll => scroll.MaxHeight == 240);
                else Assert.Single(section.GetVisualDescendants().OfType<UpdateReleaseSectionCards>());
                Assert.Contains(section.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Unsigned Windows installer");
                Assert.Contains(section.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Update & restart installs it in place and relaunches.");
                var link = Assert.Single(section.GetVisualDescendants().OfType<Button>(), button => Equals(ToolTip.GetTip(button), "Open the release on GitHub"));
                Assert.Contains(link.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Release notes");
                Assert.Equal("Release notes", AutomationProperties.GetName(link));
                window.Close();
            });
        });
    }

    [Theory]
    [Trait("Req", "PR-SETTINGS-243")] // App/Views/Settings/AboutUpdateViews.swift:731-744,794-818
    [Trait("Req", "PR-UPDATE-060")] // App/Views/Settings/AboutUpdateViews.swift:731-736,798-803
    [Trait("Rule", "R-TEST-1")] // App/Views/Settings/AboutUpdateViews.swift:794-818 (windows_path specifies the shared no-op haptic shim)
    [Trait("Req", "PR-UPDATE-063")] // App/Views/Settings/AboutUpdateViews.swift:738-744
    [InlineData(false, 2600)]
    [InlineData(true, 1450)]
    public void Opening_consumes_the_celebration_checks_and_runs_the_source_timing_then_leaving_cancels(bool reduceMotion, int totalMilliseconds)
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime(); using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var progress = UpdateInstallProgressFixtures.NewProgress();
            using var running = UpdateInstallProgress.Running.Override(progress);
            var checker = runtime.OnMain(() => new UpdateChecker(ReleaseFeed.Running("1.7.0"), [], ""));
            using var shared = UpdateChecker.OverrideShared(checker);
            Avalonia.Controls.Window? window = null;
            runtime.OnMain(() =>
            {
                progress.ArmCelebration();
                var section = new AboutSoftwareUpdateSection();
                EnvironmentValues.SetAccessibilityReduceMotion(section, reduceMotion);
                window = Show(section);
            });
            runtime.RunUntilIdle();
            runtime.OnMain(() =>
            {
                Assert.False(progress.HasPendingCelebration);
                Assert.Equal(UpdateInstallProgress.Phase.Celebrating, progress.UpdateInstallProgressPhase);
                Assert.Equal(0.86, progress.Fraction);
                Assert.NotNull(checker.LastCheckedAt);
            });
            runtime.AdvanceMilliseconds(549);
            runtime.OnMain(() => Assert.Equal(0.86, progress.Fraction));
            runtime.AdvanceMilliseconds(1);
            runtime.OnMain(() => Assert.Equal(1, progress.Fraction));
            runtime.AdvanceMilliseconds(totalMilliseconds - 551);
            runtime.OnMain(() => Assert.Equal(UpdateInstallProgress.Phase.Celebrating, progress.UpdateInstallProgressPhase));
            runtime.AdvanceMilliseconds(1);
            runtime.OnMain(() =>
            {
                Assert.Equal(UpdateInstallProgress.Phase.Idle, progress.UpdateInstallProgressPhase);
                window!.Close();
                progress.ArmCelebration();
                window = Show(new AboutSoftwareUpdateSection());
            });
            runtime.RunUntilIdle();
            runtime.OnMain(() => { window!.Close(); Assert.Equal(UpdateInstallProgress.Phase.Idle, progress.UpdateInstallProgressPhase); });
            runtime.AdvanceMilliseconds(3000);
            runtime.OnMain(() => Assert.Equal(UpdateInstallProgress.Phase.Idle, progress.UpdateInstallProgressPhase));
        });
    }
}

// PR-UPDATE-061 windows_path specifies the shared no-op haptic shim (port_impacts X6).
// Alignment and LevelChange calls remain at the Swift sites; a native haptic boundary is not required on Windows.
// The 550/900/450/700 ms sequence, 1450 ms reduced path, and cancellation are exercised above.
