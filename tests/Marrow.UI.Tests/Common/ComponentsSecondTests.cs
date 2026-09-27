using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.UI.Tests.Common;

[Collection("Headless")]
public sealed class ComponentsSecondTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-USAGE-020")] // DroppyCode/UI/Common/Components.swift:4-14
    public void Provider_marks_are_fitted_at_14_by_default_and_13_when_requested_and_change_with_provider()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var icon = new ProviderIcon { Provider = ProviderKind.Claude };
            var window = Show(icon);
            var image = Assert.Single(VisualQuery.Descendants<SwiftImage>(icon));
            Assert.Equal("provider-claude", image.AssetName);
            Assert.False(image.IsEmpty);
            Assert.True(image.IsResizable);
            Assert.Equal(ContentMode.Fit, image.AspectContentMode);
            Assert.Equal(new Size(14, 14), image.Bounds.Size);
            icon.Provider = ProviderKind.OpencodeGo;
            icon.Size = 13;
            Dispatcher.UIThread.RunJobs();
            image = Assert.Single(VisualQuery.Descendants<SwiftImage>(icon));
            Assert.Equal("provider-opencode", image.AssetName);
            Assert.Equal(new Size(13, 13), image.Bounds.Size); window.Close();
        });
    }

    [Fact]
    [Trait("Req", "UI-GIT-007")] // DroppyCode/UI/Common/Components.swift:45-56
    public void Diff_statistics_draw_both_counts_with_Unicode_minus_theme_colours_and_caption_tabular_digits()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var label = new DiffStatLabel { Additions = 12, Deletions = 5 };
            var window = Show(label);
            var row = Assert.Single(VisualQuery.Descendants<StackPanel>(label));
            Assert.Equal(4, row.Spacing);
            var texts = VisualQuery.Descendants<TextBlock>(label).ToArray();
            Assert.Equal(new[] { "+12", "−5" }, texts.Select(t => t.Text));
            Assert.Equal(ChromeTokens.Success.Resolve(texts[0].ActualThemeVariant), ((ISolidColorBrush)texts[0].Foreground!).Color);
            Assert.Equal(ChromeTokens.Danger.Resolve(texts[1].ActualThemeVariant), ((ISolidColorBrush)texts[1].Foreground!).Color);
            foreach (var text in texts) { Assert.Equal(10, text.FontSize); Assert.Contains(text.FontFeatures!, f => f.Tag == "tnum"); }
            label.Additions = 0; label.Deletions = 0;
            Assert.Equal(new[] { "+0", "−0" }, texts.Select(t => t.Text)); window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-074")] // DroppyCode/UI/Common/Components.swift:22-34
    public void Older_dates_use_ICU_month_day_in_the_process_locale_and_cache_by_date()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-GB");
                var date = new DateTimeOffset(2025, 9, 3, 12, 0, 0, TimeSpan.Zero);
                var expected = SwiftFormatters.Formatted(date, DateFormatStyle.DateTime.Month().Day());
                Assert.NotEqual(date.ToString("MMM d", CultureInfo.InvariantCulture), expected);
                Assert.Equal(expected, runtime.OnMain(() => RelativeTime.Short(date, date.AddDays(8))));
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                Assert.Equal(expected, runtime.OnMain(() => RelativeTime.Short(date, date.AddDays(9)))); // :30 date-only cache survives locale change.
            }
            finally { CultureInfo.CurrentCulture = previous; }
        });
    }

    [Theory]
    [Trait("Req", "PR-SIDEBAR-074")] // DroppyCode/UI/Common/Components.swift:23-28
    [InlineData(-1, "now")]
    [InlineData(59, "now")]
    [InlineData(60, "1m")]
    [InlineData(3599, "59m")]
    [InlineData(3600, "1h")]
    [InlineData(86399, "23h")]
    [InlineData(86400, "1d")]
    [InlineData(604799, "6d")]
    public void Relative_boundaries_truncate_toward_zero(double seconds, string expected)
    {
        using var runtime = new TestRuntime();
        runtime.OnMain(() => Assert.Equal(expected, RelativeTime.Short(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddSeconds(seconds))));
    }

    [Fact]
    [Trait("Rule", "R-FMT-2")] // UI/Common/Components.swift:37-41; duration is not part of PR-SIDEBAR-074.
    public void Duration_uses_B3_ICU_units_zero_elision_two_units_and_rounds_half_away_from_zero()
    {
        using var runtime = new TestRuntime();
        using var locale = SwiftFormatters.CurrentLocale.Override("en_US");
        // :38 max(0, Int(interval.rounded())); :39 narrow units, maximumUnitCount 2, fraction hidden down.
        Assert.Equal("3s", RelativeTime.Duration(TimeSpan.FromSeconds(2.5)));
        Assert.Equal("1m", RelativeTime.Duration(TimeSpan.FromSeconds(60)));
        Assert.Equal("1h", RelativeTime.Duration(TimeSpan.FromSeconds(3600)));
        Assert.Equal("0s", RelativeTime.Duration(TimeSpan.FromSeconds(-2.5)));
    }

    [Fact]
    [Trait("Rule", "R-VIEW-1")] // UI/Common/Components.swift:56-89; shared style only, caller STOP below.
    public void Chip_style_applies_to_an_existing_Button_and_uses_all_hover_pressed_and_active_states()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var label = new TextBlock { Text = "Daily" };
            var button = new Button { Content = label };
            var apply = typeof(ChipButtonStyleHelper).GetMethod("Chip", [typeof(Button), typeof(bool)])
                ?? throw new InvalidOperationException("Reusable Chip(Button, bool) is missing");
            Assert.Same(button, apply.Invoke(null, [button, false]));
            var window = Show(button);
            var border = Assert.Single(VisualQuery.Descendants<Border>(button));
            Assert.Equal(new Thickness(9, 5), border.Padding);
            Assert.Equal(12, label.FontSize);
            Assert.Equal(1, label.MaxLines);
            Assert.Equal(new CornerRadius(border.Bounds.Height / 2), border.CornerRadius);
            Assert.Equal(0, ((ISolidColorBrush)border.Background!).Color.A);
            window.MouseMove(new Point(20, 14), RawInputModifiers.None);
            runtime.AdvanceMilliseconds(60); frames.Advance(TimeSpan.FromMilliseconds(60)); Dispatcher.UIThread.RunJobs();
            var half = ((ISolidColorBrush)border.Background!).Color.A;
            Assert.InRange(half, (byte)1, (byte)14);
            runtime.AdvanceMilliseconds(60); frames.Advance(TimeSpan.FromMilliseconds(60)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(ChromeTokens.PrimaryText.Opacity(0.07).Resolve(border.ActualThemeVariant), ((ISolidColorBrush)border.Background!).Color);
            window.MouseDown(new Point(20, 14), MouseButton.Left, RawInputModifiers.None);
            Assert.Equal(ChromeTokens.PrimaryText.Opacity(0.12).Resolve(border.ActualThemeVariant), ((ISolidColorBrush)border.Background!).Color);
            window.MouseUp(new Point(20, 14), MouseButton.Left, RawInputModifiers.None);
            apply.Invoke(null, [button, true]); Dispatcher.UIThread.RunJobs();
            border = Assert.Single(VisualQuery.Descendants<Border>(button));
            Assert.Equal(ChromeTokens.Accent.Opacity(0.16).Resolve(border.ActualThemeVariant), ((ISolidColorBrush)border.Background!).Color);
            window.MouseDown(new Point(20, 14), MouseButton.Left, RawInputModifiers.None);
            Assert.Equal(ChromeTokens.Accent.Opacity(0.26).Resolve(border.ActualThemeVariant), ((ISolidColorBrush)border.Background!).Color);
            Assert.Equal(ChromeTokens.Accent.Resolve(label.ActualThemeVariant), ((ISolidColorBrush)label.Foreground!).Color);
            window.MouseUp(new Point(20, 14), MouseButton.Left, RawInputModifiers.None); window.Close();
        });
    }

    private static Window Show(Control content)
    {
        content.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        content.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        var window = new Window { Width = 200, Height = 100, Content = content, Background = Brushes.White };
        window.Show(); Dispatcher.UIThread.RunJobs(); return window;
    }

    [Fact]
    [Trait("Rule", "R-VIEW-1")] // UI/Common/Components.swift:71-79; shared style only, caller STOP below.
    public void Chip_respects_explicit_label_font_inherited_tint_and_capsule_hit_target()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var label = new TextBlock { Text = "Weekly", FontSize = 13 };
            var button = new Button { Content = label };
            var helper = typeof(ChipButtonStyleHelper);
            helper.GetMethod("SetTint")!.Invoke(null, [button, SwiftColor.Rgb(0xFF0000)]);
            helper.GetMethod("Chip", [typeof(Button), typeof(bool)])!.Invoke(null, [button, true]);
            var clicks = 0; button.Click += (_, _) => clicks++;
            var window = Show(button);
            try
            {
                Assert.Equal(13, label.FontSize); // :73 supplies an environment default; label's explicit font wins.
                Assert.Equal(Colors.Red, ((ISolidColorBrush)label.Foreground!).Color);
                Assert.NotNull(button.Clip); // :82 contentShape(.capsule): corners outside the capsule cannot activate.
                Assert.False(button.Clip!.FillContains(new Point(0, 0)));
                window.MouseDown(new Point(0, 0), MouseButton.Left, RawInputModifiers.None);
                window.MouseUp(new Point(0, 0), MouseButton.Left, RawInputModifiers.None);
                Assert.Equal(0, clicks);
                window.MouseDown(new Point(20, 14), MouseButton.Left, RawInputModifiers.None);
                window.MouseUp(new Point(20, 14), MouseButton.Left, RawInputModifiers.None);
                Assert.Equal(1, clicks);
            }
            finally { window.Close(); }
        });
    }
    [Fact]
    [Trait("Rule", "R-VIEW-2")] // UI/Common/Components.swift:64-87; @State survives isActive changes.
    public void Restyling_active_then_inactive_under_a_stationary_pointer_preserves_the_hover_fill()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var button = new Button { Content = new TextBlock { Text = "Daily" } }.Chip();
            var enters = 0;
            button.PointerEntered += (_, _) => enters++;
            var window = Show(button);
            try
            {
                window.MouseMove(new Point(20, 14), RawInputModifiers.None);
                runtime.AdvanceMilliseconds(120);
                frames.Advance(TimeSpan.FromMilliseconds(120));
                Dispatcher.UIThread.RunJobs();
                Assert.True(button.IsPointerOver);
                Assert.Equal(1, enters);
                var surface = Assert.Single(VisualQuery.Descendants<Border>(button));
                Assert.Equal(ChromeTokens.PrimaryText.Opacity(0.07).Resolve(surface.ActualThemeVariant), ((ISolidColorBrush)surface.Background!).Color);

                for (var i = 0; i < 3; i++)
                {
                    button.Chip(active: true);
                    Dispatcher.UIThread.RunJobs();
                    surface = Assert.Single(VisualQuery.Descendants<Border>(button));
                    Assert.Equal(ChromeTokens.Accent.Opacity(0.16).Resolve(surface.ActualThemeVariant), ((ISolidColorBrush)surface.Background!).Color);
                    button.Chip(active: false);
                    Dispatcher.UIThread.RunJobs();
                    surface = Assert.Single(VisualQuery.Descendants<Border>(button));
                    Assert.True(button.IsPointerOver);
                    Assert.Equal(1, enters); // No second PointerEntered may repair the state.
                    Assert.Equal(ChromeTokens.PrimaryText.Opacity(0.07).Resolve(surface.ActualThemeVariant), ((ISolidColorBrush)surface.Background!).Color);
                }

                window.MouseMove(new Point(190, 90), RawInputModifiers.None);
                runtime.AdvanceMilliseconds(120);
                frames.Advance(TimeSpan.FromMilliseconds(120));
                Dispatcher.UIThread.RunJobs();
                Assert.False(button.IsPointerOver);
                Assert.Equal(0, ((ISolidColorBrush)surface.Background!).Color.A);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [Trait("Req", "PR-SIDEBAR-074")] // UI/Common/Components.swift:23-33
    [InlineData("en_US", 4, "Feb 4")]
    [InlineData("en_GB", 5, "5 Feb")]
    public void Exactly_604800_seconds_uses_the_locale_month_day_branch(string localeName, int day, string expected)
    {
        // :28 is strictly <604800; :31 is .dateTime.month(.abbreviated).day(). Separate dates avoid the
        // source's intentional date-only cache preserving the first locale's value in the second case.
        using var runtime = new TestRuntime();
        using var locale = SwiftFormatters.CurrentLocale.Override(localeName);
        var date = new DateTimeOffset(2026, 2, day, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(expected, runtime.OnMain(() => RelativeTime.Short(date, date.AddSeconds(604800))));
    }

    // STOP UI.Common.Components #1
    // Kind: missing caller dependency. Swift: App/Views/Composer/EffortSlider.swift:65-104.
    // Records: PR-COMPOSER-106.
    // Missing: the shared ProviderIcon test proves artwork only; EffortSlider's whole model chip remains
    // unwritten under that unit's STOP #1. Its file is owned by the composer chat, outside this fix's scope.
    // Needed: caller tests for chipName, pair/fast/effort/chevron composition and unknown-model fallback.
    // The mark assertions are retained; the misleading PR-COMPOSER-106 trait is removed.
    //
    // STOP UI.Common.Components #2
    // Kind: incomplete owning-section proof. Swift: App/Views/Settings/TokenActivitySection.swift:54-87.
    // Records: UI-SETTINGS-022, PR-SETTINGS-131.
    // Missing: shared chip tests do not prove the real mode header; the owning TokenActivitySection.cs also
    // approximates first-baseline alignment, omits the heading trait, and has no separate .smooth(0.25)
    // header animation (:59, :87). Correcting that production file belongs to its owning settings unit.
    // Needed: owner implementation/proofs of those header behaviors and full capsule hit geometry.
    // ComponentsCallerTests adds real mode selection, all chip fonts/accessibility, guarded same-mode
    // clicks and reopening on Daily. Those are explicitly partial R-VIEW-2 proofs, not complete Req proofs.
    // The existing shared assertions are retained under R-VIEW-1, without either full-record Req trait.
}
