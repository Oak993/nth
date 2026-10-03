using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/EffortSlider.swift (B3 52bc792), :364-477 HydraPairListRow and ModelListRow: an
// HStack(spacing: 10) of [icon 18] [VStack(spacing: 1): name 14, detail .callout] [Spacer(minLength: 12)] [bolt .footnote]
// [checkmark .body semibold], .padding(.horizontal, 10), .frame(height:), a radius 9 continuous hover plate, 0.45 when locked.
// SwiftUI's HStack puts its spacing between EVERY two neighbours, the Spacer included, so the gap from the text to whatever
// follows the Spacer is 10 + 12 + 10 and the checkmark sits 10 after the bolt.
[Collection("Headless")]
public sealed class EffortSliderModelRowTests(HeadlessSession headless)
{
    private static Avalonia.Controls.Window Show(Control content, double width = 318)
    {
        var window = new Avalonia.Controls.Window { Width = width, Height = 220, Content = new StackPanel { Children = { content } } }; // the list's rows sit in a vertical stack
        window.Show(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); return window;
    }

    private static double X(Control control, Avalonia.Controls.Window window) => control.TranslatePoint(default, window)!.Value.X;
    private static double Right(Control control, Avalonia.Controls.Window window) => X(control, window) + control.Bounds.Width;

    private static Point Centre(Control control, Avalonia.Controls.Window window) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

    private static void Click(Avalonia.Controls.Window window, Point point)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); // pointer hit testing consumes the rendered scene, not just layout
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static Color Colour(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    [Theory]
    [Trait("Req", "PR-COMPOSER-143")] // App/Views/Composer/EffortSlider.swift:422-477 ModelListRow; ED-059 and ED-182(Q2) font mapping
    [InlineData(true, true, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(false, true, false, true)]
    [InlineData(false, false, true, true)]
    [InlineData(true, true, false, false)]
    [InlineData(false, false, false, false)]
    public void Model_row_renders_source_geometry_order_fonts_colours_and_pointer_states(bool showsIcon, bool fast, bool selected, bool enabled)
    {
        headless.OnUiThread(() =>
        {
            var time = new ManualTimeProvider(); var frames = new ManualFrameSource();
            using var clock = SwiftClock.Provider.Override(time); using var source = FrameClock.Source.Override(frames);
            var calls = 0;
            var option = new ModelOption("test", "gpt-test-model", null, null, null, fastTier: fast ? "fast" : null);
            var detail = showsIcon ? (enabled ? "Codex" : "New chats only") : null;
            var row = new ModelListRow(new ModelCatalog.Entry(ProviderKind.Codex, option), detail, showsIcon, showsIcon ? 46 : 34, selected, enabled, () => calls++);
            var window = Show(row);
            var button = Assert.IsType<Button>(row.Child); var plate = Assert.IsType<Border>(button.Content);
            var grid = Assert.IsType<Grid>(plate.Child);

            // :435-461 the HStack's children in order: the icon (only when providers are shown), the text column, the bolt (only on a
            // model with a fast tier), the checkmark (always, so the names never shift)
            var next = 0;
            ProviderIcon? icon = showsIcon ? Assert.IsType<ProviderIcon>(grid.Children[next++]) : null;
            var texts = Assert.IsType<StackPanel>(grid.Children[next++]);
            SwiftImage? bolt = fast ? Assert.IsType<SwiftImage>(grid.Children[next++]) : null;
            var check = Assert.IsType<SwiftImage>(grid.Children[next++]);
            Assert.Equal(next, grid.Children.Count);

            // :435 HStack(spacing: 10): the gap goes between every two neighbours, :451 Spacer(minLength: 12) included
            var expected = new List<string>();
            if (showsIcon) expected.AddRange(["18", "10"]);
            expected.AddRange(["*", "10", "12"]);
            if (fast) expected.AddRange(["10", "A"]);
            expected.AddRange(["10", "A"]);
            Assert.Equal(expected, grid.ColumnDefinitions.Select(column => column.Width.IsStar ? "*" : column.Width.IsAuto ? "A" : FormattableString.Invariant($"{column.Width.Value}")));
            var column = 0;
            if (icon is not null) { Assert.Equal(0, Grid.GetColumn(icon)); column = 2; }
            Assert.Equal(column, Grid.GetColumn(texts)); column += 3;
            if (bolt is not null) { column += 1; Assert.Equal(column, Grid.GetColumn(bolt)); column += 1; }
            column += 1; Assert.Equal(column, Grid.GetColumn(check));

            // :436-439 the provider mark: 14 pt artwork in an 18 point column, tinted secondary
            if (icon is not null)
            {
                Assert.Equal(18, icon.Width); Assert.Equal(14, icon.Size);
                Assert.Equal(Chrome.SecondaryText.Resolve(icon.ActualThemeVariant), Colour(Assert.IsType<SwiftImage>(icon.Child).Foreground));
            }

            // :440-450 the name at a fixed 14 point in primary text (a leading "gpt-" dropped, the hyphens spaces), the detail .callout in secondary, 1 point apart
            Assert.Equal(1, texts.Spacing);
            var name = Assert.IsType<TextBlock>(texts.Children[0]);
            Assert.Equal("test model", name.Text); Assert.Equal(14, name.FontSize); Assert.Equal(FontWeight.Normal, name.FontWeight);
            Assert.Equal(Chrome.PrimaryText.Resolve(name.ActualThemeVariant), Colour(name.Foreground));
            if (detail is null) Assert.Single(texts.Children);
            else
            {
                var line = Assert.IsType<TextBlock>(Assert.Single(texts.Children.Skip(1)));
                Assert.Equal(detail, line.Text); Assert.Equal(12, line.FontSize); Assert.Equal(FontWeight.Normal, line.FontWeight);
                Assert.Equal(Chrome.PrimaryText.Opacity(0.7).Resolve(line.ActualThemeVariant), Colour(line.Foreground)); // Chrome.swift:79, ED-062
                Assert.Equal(X(name, window), X(line, window));
                Assert.True(name.TranslatePoint(default, window)!.Value.Y < line.TranslatePoint(default, window)!.Value.Y);
            }

            // :452-455 the bolt: SF "bolt" at .footnote (10 pt) in secondary text, only when the model has a fast tier
            if (bolt is not null)
            {
                Assert.Equal("bolt", bolt.SymbolName);
                Assert.Equal(10, SwiftImage.GetFont(bolt)!.Resolve().FontSize);
                Assert.Equal(Chrome.SecondaryText.Resolve(bolt.ActualThemeVariant), Colour(bolt.Foreground));
            }
            else Assert.DoesNotContain(grid.Children.OfType<SwiftImage>(), image => image.SymbolName == "bolt");

            // :456-460 the checkmark at .body semibold (13 pt) in primary text, at full opacity only when selected; it keeps its column either way
            Assert.Equal("checkmark", check.SymbolName);
            var checkFont = SwiftImage.GetFont(check)!.Resolve(); Assert.Equal(13, checkFont.FontSize); Assert.Equal(FontWeight.SemiBold, checkFont.FontWeight);
            Assert.Equal(Chrome.PrimaryText.Resolve(check.ActualThemeVariant), Colour(check.Foreground));
            Assert.Equal(selected ? 1 : 0, check.Opacity);
            Assert.True(check.Bounds.Width > 0);

            // :435-461 the horizontal layout, measured: .padding(.horizontal, 10) at both ends, the icon 18 wide, 10 before the text
            var left = X(plate, window); var rightEdge = Right(plate, window);
            Assert.Equal(318, plate.Bounds.Width, 0.5);
            Assert.Equal(new Thickness(10, 0), plate.Padding);
            Assert.Equal(rightEdge - 10, Right(check, window), 0.5);
            if (icon is not null) { Assert.Equal(left + 10, X(icon, window), 0.5); Assert.Equal(left + 10 + 18 + 10, X(texts, window), 0.5); }
            else Assert.Equal(left + 10, X(texts, window), 0.5);
            // the Spacer(minLength: 12) with the spacing on both sides: 10 + 12 + 10 between the text column and what follows it
            var following = bolt ?? check;
            Assert.Equal(32, X(following, window) - Right(texts, window), 0.5);
            if (bolt is not null) Assert.Equal(10, X(check, window) - Right(bolt, window), 0.5);

            // :463-467 the plate: 46 or 34 tall, a radius 9 continuous rounded rectangle, clear at rest
            Assert.Equal(showsIcon ? 46 : 34, plate.Height); Assert.Equal(plate.Height, row.Bounds.Height, 0.5);
            Assert.Equal(new CornerRadius(9), plate.CornerRadius); Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(plate));
            var presenter = Assert.IsType<Avalonia.Controls.Presenters.ContentPresenter>(Assert.Single(button.GetVisualChildren()));
            Assert.Same(plate, presenter.Child); // :470 .buttonStyle(.plain) draws only the label
            Assert.Equal(0, Colour(plate.Background).A);

            // :471-472 .disabled(!isEnabled) and .opacity(isEnabled ? 1 : 0.45)
            Assert.Equal(enabled, button.IsEnabled); Assert.Equal(enabled ? 1 : 0.45, button.Opacity);

            // :466 the hover plate: Chrome.overlay(0.1) only while enabled, eased by Chrome.hover (easeOut 0.1)
            var point = Centre(button, window);
            window.MouseMove(point); Dispatcher.UIThread.RunJobs();
            time.Advance(TimeSpan.FromMilliseconds(50)); frames.Advance(TimeSpan.FromMilliseconds(50)); Dispatcher.UIThread.RunJobs();
            var alpha = enabled ? (int)Math.Round(Chrome.PrimaryText.Opacity(0.1).Resolve(plate.ActualThemeVariant).A * Animation.EaseOut(0.1).Progress(0.05)) : 0;
            Assert.InRange((int)Colour(plate.Background).A, alpha - 1, alpha + 1);
            time.Advance(TimeSpan.FromMilliseconds(60)); frames.Advance(TimeSpan.FromMilliseconds(60)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(enabled ? Chrome.PrimaryText.Opacity(0.1).Resolve(plate.ActualThemeVariant) : SwiftColor.Clear.Resolve(plate.ActualThemeVariant), Colour(plate.Background));

            // :468 .contentShape(.rect): the whole row takes a press, the unpainted rounded corners included; a locked row takes none
            Click(window, point); Assert.Equal(enabled ? 1 : 0, calls);
            Click(window, plate.TranslatePoint(new Point(1, 1), window)!.Value); Assert.Equal(enabled ? 2 : 0, calls);
            window.MouseMove(new Point(317, 219)); Dispatcher.UIThread.RunJobs();
            time.Advance(TimeSpan.FromSeconds(1)); frames.Advance(TimeSpan.FromSeconds(1)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, Colour(plate.Background).A);
            window.Close();
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // partial: UI-COMPOSER-042's typography, truncation and list spacing; the pair row's other clauses are PR-COMPOSER-147's theory and the panel scenarios in EffortSliderModelListTests
    public void A_pair_rows_title_and_detail_are_one_line_cut_with_an_ellipsis_at_the_tail()
    {
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource());
            var longDetail = "Claude lead, Codex heads, with a detail line far longer than the row can ever show in one line";
            var row = new HydraPairListRow(new HydraPair(ProviderKind.Claude) { WorkerProvider = ProviderKind.Codex }, "Opus 4.5 + Codex Max With A Rather Long Name", longDetail, 46, false, true, () => { });
            var window = Show(row);
            var grid = Assert.IsType<Grid>(Assert.IsType<Border>(Assert.IsType<Button>(row.Child).Content).Child);
            var texts = Assert.IsType<StackPanel>(grid.Children[1]);
            foreach (var line in texts.Children.OfType<TextBlock>())
            {
                Assert.Equal(1, line.MaxLines); // :393 :397 .lineLimit(1)
                Assert.Equal(TextTrimming.CharacterEllipsis, line.TextTrimming); // the default truncation mode is the tail
                Assert.Equal(TextWrapping.NoWrap, line.TextWrapping);
                Assert.Single(line.TextLayout.TextLines);
                Assert.True(line.TextLayout.TextLines[0].HasCollapsed, $"'{line.Text}' is cut with an ellipsis, not run past the row");
                Assert.True(line.Bounds.Width <= texts.Bounds.Width + 0.5);
            }
            Assert.Equal(46, row.Bounds.Height, 0.5);
            window.Close();
        });
    }
}
