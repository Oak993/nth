using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/AboutUpdateViews.swift (B3 52bc792), :190-704.
// These tests assert source-defined properties on attached production controls. ED-059/180/181 describe
// the approved point-size/material/shape representations; no macOS-delegated pixels are inferred.
[Collection("Headless")]
public sealed class AboutUpdateViewsProofGeometryTests(HeadlessSession headless)
{
    private static Avalonia.Controls.Window Show(Control content, double width = 500, double height = 400)
    {
        var window = new Avalonia.Controls.Window { Width = width, Height = height, Content = content, UseLayoutRounding = false };
        window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void Set(object target, string property, object? value) =>
        target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)!.SetValue(target, value);

    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static void Glass(Control view)
    {
        // Chrome.swift:165,180: regular, continuous capsule, glassTint.opacity(0.3), interactive.
        var effect = Assert.Single(view.GetVisualDescendants().OfType<Control>().Select(SwiftGlass.GetEffect).OfType<GlassEffect>());
        Assert.Equal(GlassShapeKind.Capsule, effect.Shape.Kind);
        Assert.Equal(RoundedCornerStyle.Continuous, effect.Shape.Style);
        Assert.Equal(0.3, effect.TintOpacity); Assert.Equal(Chrome.GlassTint, effect.TintColor);
        Assert.True(effect.IsInteractive); // ED-180 metadata, not a claim that the deferred press renderer exists.
    }

    private static Point Center(Control control, Avalonia.Controls.Window window) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    private static void Click(Control control, Avalonia.Controls.Window window)
    {
        var point = Center(control, window);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
        window.MouseMove(point, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-055")] // App/Views/Settings/AboutUpdateViews.swift:551-571
    [Trait("Req", "UI-UPDATE-001")] // App/Views/Settings/AboutUpdateViews.swift:551-571; SettingsView.swift:333-335 (10pt accessory gap)
    public void Identity_has_source_icon_font_padding_height_glass_and_accessibility_in_the_real_accessory() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.4.2"));
        runtime.OnMain(() =>
        {
            using var checker = UpdateChecker.OverrideShared(new UpdateChecker(AppInfo.Current, [], ""));
            using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
            var accessory = new AboutUpdateChromeAccessory(); var window = Show(accessory);
            var row = Assert.IsType<StackPanel>(accessory.Child); Assert.Equal(10, row.Spacing);
            var identity = Assert.IsType<AboutIdentityPill>(row.Children[0]); Assert.IsType<AboutVersionPill>(row.Children[1]);
            var icon = Assert.Single(identity.GetVisualDescendants().OfType<Image>());
            Assert.Equal(20, icon.Width); Assert.Equal(20, icon.Height); Assert.IsType<Bitmap>(icon.Source);
            Assert.Equal(BitmapInterpolationMode.HighQuality, RenderOptions.GetBitmapInterpolationMode(icon));
            Assert.Equal(Stretch.Uniform, icon.Stretch); // OD-0015 / OD-0033: the same 20 pt aspect-fit app image.
            Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(icon));
            var label = Assert.Single(identity.GetVisualDescendants().OfType<TextBlock>());
            Assert.Equal("v1.4.2", label.Text); Assert.Equal(12, label.FontSize); Assert.Equal(FontWeight.SemiBold, label.FontWeight);
            Assert.Equal(Chrome.PrimaryText.Resolve(label.ActualThemeVariant), ColorOf(label.Foreground));
            var padded = Assert.Single(identity.GetVisualDescendants().OfType<Border>(), b => b.Child is StackPanel);
            Assert.Equal(new Thickness(8, 2, 12, 2), padded.Padding); Assert.Equal(32, padded.Height);
            Assert.Equal(6, Assert.IsType<StackPanel>(padded.Child).Spacing);
            Assert.Equal("Nth 1.4.2", AutomationProperties.GetName(identity));
            Assert.Empty(identity.GetVisualDescendants().OfType<Button>()); Assert.False(identity.Focusable);
            Assert.Equal(32, identity.Bounds.Height); Glass(identity); window.Close();
        });
    });

    [Theory]
    [Trait("Req", "PR-SETTINGS-242")] // App/Views/Settings/AboutUpdateViews.swift:573-704 (with lifecycle and defensive-input tests)
    [Trait("Req", "PR-UPDATE-073")] // App/Views/Settings/AboutUpdateViews.swift:581-583,638-675 (with lifecycle and modifier tests)
    [Trait("Req", "PR-UPDATE-075")] // App/Views/Settings/AboutUpdateViews.swift:678-703 (with lifecycle tests; nil-error fallback unreachable by :617)
    [Trait("Req", "UI-SETTINGS-056")] // App/Views/Settings/AboutUpdateViews.swift:573-704 (with lifecycle and modifier tests)
    [Trait("Req", "UI-UPDATE-002")] // App/Views/Settings/AboutUpdateViews.swift:587-621,650-682
    [Trait("Req", "UI-UPDATE-003")] // App/Views/Settings/AboutUpdateViews.swift:587-621,693-703
    [Trait("Req", "UI-UPDATE-004")] // App/Views/Settings/AboutUpdateViews.swift:587-621,693-703
    [Trait("Req", "PR-UPDATE-072")] // App/Views/Settings/AboutUpdateViews.swift:587-621: standing order error, then never checked, then up to date; labels, symbols, tints
    [Trait("Req", "UI-UPDATE-005")] // App/Views/Settings/AboutUpdateViews.swift:638-682: the ready face (symbol, text, blue, 0.2 wash, tooltip, accessibility)
    [InlineData(false, null, false, "Not checked yet", "clock", "Nth has not asked GitHub for a release yet")]
    [InlineData(true, null, false, "Up to date", "checkmark.seal.fill", "Nth 1.7.0 is the latest version")]
    [InlineData(false, "GitHub answered with HTTP 503.", false, "Check failed", "exclamationmark.triangle.fill", "GitHub answered with HTTP 503.")]
    [InlineData(true, "GitHub answered with HTTP 503.", false, "Check failed", "exclamationmark.triangle.fill", "GitHub answered with HTTP 503.")]
    [InlineData(true, null, true, "Update & restart", "arrow.down.circle.fill", "Installs Nth 9.0.0 and relaunches")]
    public void Pill_faces_have_exact_metrics_tints_glass_help_and_only_ready_has_the_button_role(bool checkedBefore, string? error, bool ready, string text, string symbol, string help) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
        runtime.OnMain(() =>
        {
            var checker = new UpdateChecker(AppInfo.Current, [], "");
            if (checkedBefore) Set(checker, nameof(UpdateChecker.LastCheckedAt), SwiftDate.Now);
            Set(checker, nameof(UpdateChecker.LastError), error);
            if (ready) Set(checker, nameof(UpdateChecker.Update), AboutUpdateViewsProofLifecycleTests.Release());
            using var shared = UpdateChecker.OverrideShared(checker);
            using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
            var pill = new AboutVersionPill(); var window = Show(pill);
            var button = Assert.Single(pill.GetVisualDescendants().OfType<Button>());
            var face = Assert.IsType<Border>(button.Content); var row = Assert.IsType<StackPanel>(face.Child);
            Assert.Equal(new Thickness(12, 2), face.Padding); Assert.Equal(32, face.Height); Assert.Equal(6, row.Spacing);
            Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(face)); Assert.Equal(16, face.CornerRadius.TopLeft);
            var icon = Assert.IsType<SwiftImage>(row.Children[0]); var title = Assert.IsType<TextBlock>(row.Children[1]);
            Assert.Equal(symbol, icon.SymbolName); Assert.Equal(11, SwiftImage.GetFont(icon)!.PointSize);
            Assert.Equal(SwiftFont.System(SwiftFont.TextStyle.Subheadline, weight: SwiftFont.Weight.Semibold), SwiftImage.GetFont(icon));
            Assert.Equal(text, title.Text); Assert.Equal(12, title.FontSize); Assert.Equal(FontWeight.SemiBold, title.FontWeight);
            // :659-660 lineLimit(1) + fixedSize(horizontal: true): the title is laid out on one line at its full width.
            Assert.Equal(1, title.MaxLines); Assert.Single(title.TextLayout.TextLines); Assert.Equal(title.DesiredSize.Width, title.Bounds.Width);
            Assert.True(title.TextLayout.WidthIncludingTrailingWhitespace <= title.Bounds.Width);
            // :673-674 the standing is a plain button with hit testing off, not a disabled one (windows_path: non-interactive, not disabled-looking).
            Assert.True(button.IsEnabled);
            // Chrome.swift:79 primary.opacity(0.7), :82-84 theme success/warning, :86 fixed blue.
            var tint = ready ? new SwiftColor(0.040, 0.478, 1.000) : error is not null ? Chrome.Warning : checkedBefore ? Chrome.Success : Chrome.PrimaryText.Opacity(0.7);
            Assert.Equal(tint.Resolve(icon.ActualThemeVariant), ColorOf(icon.Foreground));
            Assert.Equal((ready ? new SwiftColor(0.040, 0.478, 1.000) : Chrome.PrimaryText).Resolve(title.ActualThemeVariant), ColorOf(title.Foreground));
            Assert.Equal(new SwiftColor(0.040, 0.478, 1.000).Opacity(ready ? 0.2 : 0).Resolve(face.ActualThemeVariant), ColorOf(face.Background));
            Assert.Equal(ready, button.IsHitTestVisible); Assert.Equal(ready, button.Focusable);
            Assert.Equal(help, ToolTip.GetTip(Assert.Single(pill.GetVisualDescendants().OfType<Control>(), c => ToolTip.GetTip(c) is not null))); // :674-680 .help outside .allowsHitTesting: the capsule owns the tip
            Assert.Equal(ready ? "Update & restart" : "Nth 1.7.0, " + text.ToLowerInvariant(), AutomationProperties.GetName(button));
            var peer = ControlAutomationPeer.CreatePeerForElement(button)!;
            Assert.Equal(ready ? AutomationControlType.Button : AutomationControlType.Text, peer.GetAutomationControlType());
            Assert.Equal(ready, peer is IInvokeProvider);
            if (!ready) { Click(button, window); Assert.Empty(pill.GetVisualDescendants().OfType<SmoothedUpdateProgressSlider>()); }
            Assert.Equal(pill.DesiredSize.Width, pill.Bounds.Width); Assert.Equal(32, pill.Bounds.Height);
            Glass(pill); window.Close();
        });
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-242")] // App/Views/Settings/AboutUpdateViews.swift:581-583,638-642,693-703
    [Trait("Req", "PR-UPDATE-075")] // App/Views/Settings/AboutUpdateViews.swift:693-703
    [Trait("Req", "UI-UPDATE-003")] // App/Views/Settings/AboutUpdateViews.swift:693-699
    [Trait("Req", "PR-UPDATE-073")] // App/Views/Settings/AboutUpdateViews.swift:581-583 (checker.update != nil)
    [Trait("Req", "UI-SETTINGS-056")] // App/Views/Settings/AboutUpdateViews.swift:581-583
    public void Production_defensive_branches_keep_nil_release_inert_and_nil_error_help_verbatim() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
        using var checker = UpdateChecker.OverrideShared(new UpdateChecker(AppInfo.Current, [], ""));
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        var pill = new AboutVersionPill();
        // The normal checker derives updateAvailable from a non-nil update, and :617 only selects failed
        // for a non-nil error. The spec explicitly requests these impossible inputs. Inject them at the
        // production snapshot/helper boundary; do not invent a mock checker or reproduce either branch.
        var snapshotType = typeof(AboutVersionPill).GetNestedType("Snapshot", BindingFlags.NonPublic)!;
        var snapshot = Activator.CreateInstance(snapshotType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            new object?[] { null, true, false, null, null, UpdateInstallProgress.Phase.Idle, true }, null)!;
        var built = (Control)typeof(AboutVersionPill).GetMethod("Build", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(pill, [snapshot])!;
        // Attach the returned production subtree directly: attaching pill would re-read the ordinary checker
        // through Track and overwrite the deliberately impossible snapshot before its assertions.
        var window = Show(built); var button = Assert.Single(built.GetVisualDescendants().OfType<Button>());
        Assert.False(button.IsHitTestVisible); Assert.False(button.Focusable); Assert.Contains(built.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Not checked yet");
        var standingType = typeof(AboutVersionPill).GetNestedType("Standing", BindingFlags.NonPublic)!;
        var help = typeof(AboutVersionPill).GetMethod("StandingHelp", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [snapshot, Enum.Parse(standingType, "Failed")]);
        Assert.Equal("The last check did not reach GitHub", help); window.Close();
    });

    [Fact]
    [Trait("Decision", "ED-180")] // PR-UPDATE-074: the Swift is the proof where no frame exists (OD-0098); every phase is walked below.
    [Trait("Req", "PR-UPDATE-074")] // App/Views/Settings/AboutUpdateViews.swift:625-636: 104 pt slider in the capsule (12/28/2), no label or button, failed phase not in the set
    [Trait("Req", "UI-UPDATE-006")] // App/Views/Settings/AboutUpdateViews.swift:625-636; App/AppUpdater.swift:72-78
    [Trait("Req", "UI-SETTINGS-056")] // App/Views/Settings/AboutUpdateViews.swift:625-636
    [Trait("Req", "PR-SETTINGS-242")] // App/Views/Settings/AboutUpdateViews.swift:625-636 (104-point slider in the glass capsule)
    public void Every_progress_phase_replaces_the_face_at_104_plus_padding_and_failure_restores_it() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); var source = new ManualFrameSource(); using var frames = FrameClock.Source.Override(source);
        using var info = AppInfo.Running.Override(ReleaseFeed.Running("1.7.0"));
        runtime.OnMain(() =>
        {
            var checker = new UpdateChecker(AppInfo.Current, [], ""); Set(checker, nameof(UpdateChecker.Update), AboutUpdateViewsProofLifecycleTests.Release());
            using var shared = UpdateChecker.OverrideShared(checker);
            var progress = UpdateInstallProgressFixtures.NewProgress(); using var running = UpdateInstallProgress.Running.Override(progress);
            var pill = new AboutVersionPill(); var window = Show(pill);
            var startY = pill.Bounds.Y;
            void Slider()
            {
                Dispatcher.UIThread.RunJobs(); AboutSwap.Settle(runtime, source); // a face and the slider swap over 0.18 s (OD-0076): the phase is read once that is over
                var slider = Assert.Single(pill.GetVisualDescendants().OfType<SmoothedUpdateProgressSlider>());
                Assert.Equal(104, slider.Width); Assert.Equal(28, slider.Height);
                var padded = Assert.Single(pill.GetVisualDescendants().OfType<Border>(), b => b.Padding != default);
                Assert.Equal(new Thickness(12, 2), padded.Padding);
                Assert.Equal(128, pill.Bounds.Width); Assert.Equal(32, pill.Bounds.Height); Assert.Equal(startY, pill.Bounds.Y);
                Assert.Empty(pill.GetVisualDescendants().OfType<Button>()); Assert.Empty(pill.GetVisualDescendants().OfType<TextBlock>());
                Glass(pill);
                var value = Assert.Single(pill.GetVisualDescendants().OfType<UpdateProgressSlider>()).Fraction;
                Click(slider, window); Assert.Equal(value, Assert.Single(pill.GetVisualDescendants().OfType<UpdateProgressSlider>()).Fraction);
            }
            Assert.Empty(pill.GetVisualDescendants().OfType<SmoothedUpdateProgressSlider>());
            progress.Begin(); Slider(); progress.NoteDownloadProgress(0.5); Slider();
            progress.BeginVerification(); Slider(); progress.BeginStaging(); Slider(); progress.NoteHandoff(); Slider();
            progress.Fail(); Dispatcher.UIThread.RunJobs(); AboutSwap.Settle(runtime, source);
            Assert.Empty(pill.GetVisualDescendants().OfType<SmoothedUpdateProgressSlider>()); Assert.Single(pill.GetVisualDescendants().OfType<Button>());
            progress.ArmCelebration(); progress.ConsumePendingCelebration(); Slider();
            progress.CompleteCelebration(); Dispatcher.UIThread.RunJobs(); AboutSwap.Settle(runtime, source);
            Assert.Empty(pill.GetVisualDescendants().OfType<SmoothedUpdateProgressSlider>()); Assert.Single(pill.GetVisualDescendants().OfType<Button>());
            window.Close();
        });
    });

    [Theory]
    [Trait("Req", "UI-SETTINGS-057")] // App/Views/Settings/AboutUpdateViews.swift:19-59,190-252 (with existing artwork tests)
    [Trait("Req", "UI-UPDATE-012")] // App/Views/Settings/AboutUpdateViews.swift:19-59,190-252
    [Trait("Req", "UI-UPDATE-013")] // App/Views/Settings/AboutUpdateViews.swift:19-59,190-252
    [Trait("Req", "UI-UPDATE-014")] // App/Views/Settings/AboutUpdateViews.swift:19-59,190-252
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Cards_have_equal_widths_source_layout_fonts_tints_states_and_pointer_toggle(int count) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance); using var presentation = Presentation.Presenter.Override(new RecordingPresenter());
        runtime.OnMain(() =>
        {
            var sections = ReleaseNoteCategoryExtensions.AllCases.Select(c => new UpdateReleaseNotesDigest.Section
                { Category = c, Bullets = new ValueList<string>(Enumerable.Repeat("Change", count)), RollupCount = 0 }).ToArray();
            var cards = new UpdateReleaseSectionCards(new UpdateReleaseNotesDigest { Sections = new ValueList<UpdateReleaseNotesDigest.Section>(count == 0 ? [] : sections) }, "9.0.0");
            var window = Show(cards); var buttons = cards.GetVisualDescendants().OfType<Button>().ToArray(); Assert.Equal(3, buttons.Length);
            var row = Assert.IsType<Grid>(cards.Child); Assert.Equal(10, row.ColumnDefinitions[1].Width.Value); Assert.Equal(10, row.ColumnDefinitions[3].Width.Value);
            var titles = new[] { "New", "Bugs", "Refinements" }; var longTitles = new[] { "New features", "Bug fixes", "Refinements" };
            var summaries = count == 1 ? new[] { "1 addition", "1 fixed", "1 improved" } : new[] { "2 additions", "2 fixed", "2 improved" };
            var tints = new[] { new SwiftColor(0.09, 0.44, 0.85), new SwiftColor(0.90, 0.30, 0.24), new SwiftColor(0.43, 0.23, 0.72) };
            for (var i = 0; i < 3; i++)
            {
                var button = buttons[i]; var surface = Assert.IsType<Border>(button.Content);
                Assert.Equal(buttons[0].Bounds.Width, button.Bounds.Width); Assert.Equal(buttons[0].Bounds.Y, button.Bounds.Y);
                Assert.Equal(new Thickness(8, 12), surface.Padding); Assert.Equal(new CornerRadius(14), surface.CornerRadius);
                Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(surface)); Assert.Equal(default, surface.BorderThickness);
                ContinuousCornerChecks.Drawn(surface, expectApple: true); // AboutUpdateViews.swift:206-216 `.rect(cornerRadius: 14, style: .continuous)` (ED-181 closed): the card is far taller than the 43 pt two run-ins need
                Assert.Equal(tints[i].Opacity(0.09).Resolve(surface.ActualThemeVariant), ColorOf(surface.Background));
                var labels = Assert.Single(surface.GetVisualDescendants().OfType<StackPanel>()); Assert.Equal(6, labels.Spacing);
                var cardIcon = Assert.IsType<SwiftImage>(labels.Children[0]); Assert.Equal(26, cardIcon.Bounds.Width); Assert.Equal(26, cardIcon.Bounds.Height);
                Assert.Equal(BitmapInterpolationMode.HighQuality, RenderOptions.GetBitmapInterpolationMode(cardIcon)); // :216 .interpolation(.high)
                var texts = button.GetVisualDescendants().OfType<TextBlock>().ToArray(); Assert.Equal(2, texts.Length);
                Assert.Equal(titles[i], texts[0].Text); Assert.Equal(count == 0 ? "Nothing here" : summaries[i], texts[1].Text);
                Assert.Equal(12.5, texts[0].FontSize); Assert.Equal(FontWeight.Medium, texts[0].FontWeight);
                Assert.Equal(11, texts[1].FontSize); Assert.Equal(FontWeight.Normal, texts[1].FontWeight);
                Assert.Equal(Chrome.PrimaryText.Resolve(texts[0].ActualThemeVariant), ColorOf(texts[0].Foreground));
                Assert.Equal(Chrome.PrimaryText.Opacity(0.7).Resolve(texts[1].ActualThemeVariant), ColorOf(texts[1].Foreground));
                Assert.All(texts, t => { Assert.Equal(1, t.MaxLines); Assert.Equal(0.8, TextScaling.GetMinimumScaleFactor(t)); Assert.Equal(TextAlignment.Center, t.TextAlignment); });
                Assert.Equal(count != 0, button.IsEnabled); Assert.Equal(count == 0 ? 0.45 : 1, button.Opacity);
                Assert.Equal(count == 0 ? "Nothing here" : "Every change in " + longTitles[i].ToLowerInvariant(), ToolTip.GetTip(button));
                Assert.Equal(longTitles[i], AutomationProperties.GetName(button));
                var recorder = Assert.IsType<RecordingPresenter>(Presentation.Presenter.Current); var before = recorder.Calls.Count;
                Click(button, window);
                if (count == 0) Assert.Equal(before, recorder.Calls.Count);
                else { Assert.True(recorder.Calls.Last().Presented); Click(button, window); Assert.False(recorder.Calls.Last().Presented); }
            }
            Assert.Equal(cards.DesiredSize.Height, cards.Bounds.Height); window.Close();
        });
    });

    [Theory]
    [Trait("Decision", "ED-059")] // App/Views/Settings/AboutUpdateViews.swift:255-301; the Swift is the proof where no frame exists (OD-0098).
    [Trait("Req", "PR-SETTINGS-246")] // App/Views/Settings/AboutUpdateViews.swift:255-301: header, bullets 9 apart, rollup last and smaller, one colour
    [Trait("Req", "UI-UPDATE-015")] // App/Views/Settings/AboutUpdateViews.swift:255-301: popover geometry, typography, colours, scroll cap
    [InlineData(false)] [InlineData(true)]
    public void Popover_with_and_without_rollup_has_source_header_colours_wrapping_duplicate_bullets_and_scroll(bool rollup) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var main = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        runtime.OnMain(() =>
        {
            var section = new UpdateReleaseNotesDigest.Section { Category = ReleaseNoteCategory.NewFeatures,
                Bullets = new ValueList<string>(Enumerable.Repeat("Duplicate bullet long enough to wrap in the available width.", 30)), Rollup = rollup ? "And 3 extra additions" : null, RollupCount = rollup ? 3 : 0 };
            var view = new UpdateReleaseSectionPopover(section, "9.0.0"); var window = Show(view);
            var outer = Assert.IsType<Border>(view.Child); Assert.Equal(320, outer.Width); Assert.Equal(new Thickness(14), outer.Padding);
            var stack = Assert.IsType<StackPanel>(outer.Child); Assert.Equal(12, stack.Spacing);
            var header = Assert.IsType<StackPanel>(stack.Children[0]); Assert.Equal(9, header.Spacing);
            var icon = Assert.IsType<SwiftImage>(header.Children[0]); Assert.Equal(24, icon.Width); Assert.Equal(24, icon.Height);
            var names = Assert.IsType<StackPanel>(header.Children[1]); Assert.Equal(1, names.Spacing);
            var title = Assert.IsType<TextBlock>(names.Children[0]); var version = Assert.IsType<TextBlock>(names.Children[1]);
            Assert.Equal("New features", title.Text); Assert.Equal(13, title.FontSize); Assert.Equal(FontWeight.SemiBold, title.FontWeight);
            Assert.Equal("9.0.0", version.Text); Assert.Equal(11, version.FontSize);
            Assert.Equal(Chrome.PrimaryText.Resolve(title.ActualThemeVariant), ColorOf(title.Foreground));
            Assert.Equal(Chrome.PrimaryText.Opacity(0.7).Resolve(version.ActualThemeVariant), ColorOf(version.Foreground));
            var scroll = Assert.IsType<ScrollViewer>(stack.Children[1]); Assert.Equal(320, scroll.MaxHeight);
            Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, scroll.VerticalScrollBarVisibility);
            var bullets = Assert.IsType<StackPanel>(scroll.Content); Assert.Equal(9, bullets.Spacing); Assert.Equal(30 + (rollup ? 1 : 0), bullets.Children.Count);
            Assert.All(bullets.Children.Cast<TextBlock>().Take(30), t => { Assert.Equal(section.Bullets[0], t.Text); Assert.Equal(13, t.FontSize); });
            Assert.All(bullets.Children.Cast<TextBlock>(), t => { Assert.Equal(TextWrapping.Wrap, t.TextWrapping); Assert.Equal(Chrome.PrimaryText.Opacity(0.7).Resolve(t.ActualThemeVariant), ColorOf(t.Foreground)); });
            if (rollup) { var last = Assert.IsType<TextBlock>(bullets.Children.Last()); Assert.Equal("And 3 extra additions", last.Text); Assert.Equal(12, last.FontSize); }
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height); scroll.Offset = new Vector(0, 40); Dispatcher.UIThread.RunJobs(); Assert.Equal(40, scroll.Offset.Y);
            window.Close();
        });
    });
}
