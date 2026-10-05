using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Composer;

/// <summary>
/// The composer pill's working dot field (App/Views/Composer/ComposerWorkingDots.swift:12-190 and ComposerView.swift:659-675, B3 52bc792):
/// dots 2 points across on an 8-point pitch, at most three rows, one extra column past the width, each row up fainter by 1/rowCount, the
/// pulse's column and row delays, and the slot that gives it the pill's bottom 40 percent inside a continuous 22-point clip.
/// UI-COMPOSER-017 is proven by these tests together with ComposerWorkingDotsReviewTests (hidden from assistive technology and hit testing,
/// static at 0.28 under reduced motion, no rebuild when the motion flag changes), ComposerWorkingDotsAccentTests and
/// ComposerWorkingDotsEaseTests (the accent colour follows a pick, a theme and the system accent without a restart).
/// </summary>
[Collection("Headless")]
public sealed class ComposerWorkingDotsFieldTests(HeadlessSession headless)
{
    private static Ellipse[] StaticDots(DotFieldControl field) => Assert.IsType<Canvas>(field.Layer).Children.Cast<Ellipse>().ToArray();

    private static double RenderedOpacity(DotFieldControl control, int row, int column)
    {
        var frame = control.GetType().GetProperty("LastRenderedFrame", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(control);
        Assert.NotNull(frame);
        return Assert.IsType<double>(frame.GetType().GetMethod("OpacityAt")!.Invoke(frame, [row, column]));
    }

    private static void Render(AvaloniaWindow window)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var pixels = window.CaptureRenderedFrame();
        Assert.NotNull(pixels);
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-017")] // ComposerWorkingDots.swift:67-71, :127-148: 2 points across, 8-point pitch, at most 3 rows, one extra column, each row up fainter by 1/rowCount
    [Trait("Rule", "R-TEST-4")] // partial proof of UI-COMPOSER-017: the field's layout (the render-thread clause: ComposerWorkingDotsNativeTests, STOP cloud-verify #3 closed); the slot, the pulse, the colour and the accessibility clauses are the tests named in this class's summary
    public void The_field_lays_out_two_point_dots_on_an_eight_point_pitch_in_at_most_three_rows_with_one_extra_column() => headless.OnUiThread(() =>
    {
        // A pill field 200 wide and 100 tall: 26 columns (25 to cover the width and one past it) and 3 rows, though 13 would fit.
        var tall = new DotFieldControl(new SwiftColor(1, 0, 0), false, false);
        tall.Measure(new Size(200, 100));
        tall.Arrange(new Rect(0, 0, 200, 100));
        var dots = StaticDots(tall);
        Assert.Equal(26 * 3, dots.Length);
        Assert.All(dots, dot => { Assert.Equal(2, dot.Width); Assert.Equal(2, dot.Height); });
        for (var row = 0; row < 3; row++)
            for (var column = 0; column < 26; column++)
            {
                var dot = dots[row * 26 + column];
                Assert.Equal(4 + column * 8 - 1, Canvas.GetLeft(dot), 8); // a 2-point dot centred 4 points in, then every 8
                Assert.Equal(100 - (4 + row * 8) - 1, Canvas.GetTop(dot), 8); // rows rise from the bottom edge, 8 apart
            }
        // Each row up is fainter by 1/rowCount: 0.28 x (1 - row/3).
        Assert.Equal(0.28, dots[0].Opacity, 8);
        Assert.Equal(0.28 * 2 / 3, dots[26].Opacity, 8);
        Assert.Equal(0.28 / 3, dots[52].Opacity, 8);

        // A short field has fewer rows, and the fade steps by its own row count.
        var short2 = new DotFieldControl(new SwiftColor(1, 0, 0), false, false);
        short2.Measure(new Size(16, 12));
        short2.Arrange(new Rect(0, 0, 16, 12));
        var twoRows = StaticDots(short2);
        Assert.Equal(3 * 2, twoRows.Length); // ceil(16/8)+1 columns, ceil(12/8) rows
        Assert.Equal(0.28, twoRows[0].Opacity, 8);
        Assert.Equal(0.28 * 1 / 2, twoRows[3].Opacity, 8);
    });

    [Fact]
    [Trait("Req", "UI-COMPOSER-017")] // ComposerWorkingDots.swift:142-148, :177-190: pulse 0.08 to 0.75 over 2.2 s, column delay 0.045, row delay 0.09, each row up fainter by 1/rowCount
    [Trait("Rule", "R-TEST-4")] // partial proof of UI-COMPOSER-017: the pulse's delays and the row fade while it runs (the render-thread clause: ComposerWorkingDotsNativeTests)
    public void The_running_pulse_peaks_later_by_the_column_and_row_delays_and_each_row_peaks_lower_by_one_over_the_row_count() => headless.OnUiThread(() =>
    {
        using var rt = new TestRuntime();
        using var executor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var frames = new ManualFrameSource();
        using var scope = FrameClock.Source.Override(frames);
        var field = new DotFieldControl(new SwiftColor(1), true, false);
        var window = new AvaloniaWindow { Width = 24, Height = 24, Content = field };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // The pulse peaks 0.35 x 2.2 = 0.77 s into its cycle (the shared clock starts one second in, as the Swift's beginTime does).
        void At(double seconds)
        {
            rt.Advance(TimeSpan.FromSeconds(seconds));
            frames.Advance(TimeSpan.FromSeconds(seconds));
            Render(window);
        }
        At(1.77);
        Assert.Equal(0.75, RenderedOpacity(field, 0, 0), 8); // row 0, column 0 at its peak
        At(0.045);
        Assert.Equal(0.75, RenderedOpacity(field, 0, 1), 8); // column 1 peaks one column delay (0.045 s) later
        At(0.045);
        Assert.Equal(0.75, RenderedOpacity(field, 0, 2), 8); // column 2 two delays later
        // Row r peaks r x 0.09 s after row 0 and a factor (1 - r/3) lower: row 1 is at its peak now (1.77 + 0.09), row 2 0.09 s on.
        Assert.Equal(0.75 * (1 - 1.0 / 3), RenderedOpacity(field, 1, 0), 8);
        At(0.09);
        Assert.Equal(0.75 * (1 - 2.0 / 3), RenderedOpacity(field, 2, 0), 8);
        window.Close();
    });

    // :664-674 the slot the pill mounts: while a turn runs, the dots take the bottom 40 percent of the pill, clipped to a 22-point continuous
    // rounded rectangle.
    [Fact]
    [Trait("Req", "UI-COMPOSER-017")] // ComposerView.swift:664-672 and ComposerWorkingDots.swift:12-20: the bottom 40 percent, clipped to a continuous 22-point rounded rectangle
    [Trait("Rule", "R-TEST-4")] // partial proof of UI-COMPOSER-017: the slot's geometry and the clip's continuous corner (ED-181, closed: ContinuousCornerChecks); the capture comparison (C 92.00) is ComposerWorkingDotsFrameTests and the render-thread clause ComposerWorkingDotsNativeTests (STOP cloud-verify #3 closed)
    public void The_slot_gives_the_dots_the_pills_bottom_40_percent_inside_a_continuous_22_point_clip() => headless.OnUiThread(() =>
    {
        System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        var frames = new ManualFrameSource();
        using var scope = FrameClock.Source.Override(frames);
        using var h = new ThreadRuntimePart12Harness();
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        h.Main.OnMain(() => h.Runtime.RehearseTurn("working"));
        var slot = h.Main.OnMain(() => new ComposerWorkingDotsSlot(h.Runtime));
        var host = new Border { Child = slot, Width = 400, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
        AppEnvironment.SetAppModel(host, h.App);
        var window = new AvaloniaWindow { Content = host, Width = 1400, Height = 900 };
        h.Main.OnMain(() => { window.Show(); Settle(window); });
        void Settle(AvaloniaWindow target) { for (var pass = 0; pass < 5; pass++) { target.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); } }
        foreach (var height in new[] { 100.0, 60.0 })
        {
            h.Main.OnMain(() => slot.Height = height);
            for (var round = 0; round < 4; round++) h.Main.OnMain(() => Settle(window));
            h.Main.OnMain(() =>
            {
                var dots = LogicalQuery.SelfAndDescendants<ComposerWorkingDots>(slot).Single();
                Assert.Equal(height * 0.4, dots.Bounds.Height, 6); // the bottom 40 percent
                var top = dots.TranslatePoint(default, slot)!.Value.Y;
                Assert.Equal(height - height * 0.4, top, 6); // resting on the pill's bottom edge
                var clip = Assert.IsType<Border>(slot.Child);
                Assert.True(clip.ClipToBounds);
                Assert.Equal(new CornerRadius(22), clip.CornerRadius);
                // The outline is the shared continuous-corner helper's (SwiftShapes, every `.continuous` site goes through it). ED-275 (2), (3b):
                // while Apple's corner fits (2 x 1.528665 x 22 = 67.3 <= the height, here 100) the Border is cut to the curve by its Visual.Clip
                // (ContinuousClip); below that (60) the shape is the circular arc of radius 22, which the Border's own ClipToBounds draws with
                // its CornerRadius (Avalonia clips a Border's child to its rounded rectangle), so no Visual.Clip is set.
                ContinuousCornerChecks.Drawn(clip, expectApple: height > 2 * 22 * 1.528665);
                // The slot is the composer pill's overlay (ComposerView.swift:671, the same r 22), so it keeps the pill's circular arc in OD-0102's band (CircularInBand,
                // ED-275 (2): the video measures the pill circular), and is Apple's corner only from 67.3 pt up.
                var expected = SwiftShapes.RoundedRectangleGeometry(new Rect(0, 0, 400, height), 22, RoundedCornerStyle.Continuous, circularInBand: true);
                var apple = SwiftShapes.HasContinuousCorner(new Size(400, height), new CornerRadius(22), RoundedCornerStyle.Continuous, circularInBand: true);
                Assert.True(SwiftShapes.GetCircularInBand(clip));
                Assert.Equal(height >= 2 * 1.528665 * 22, apple);
                if (apple)
                {
                    Assert.NotNull(clip.Clip);
                    for (var x = 0.25; x < 24; x += 0.5)
                        for (var y = 0.25; y < 24; y += 0.5)
                            Assert.Equal(expected.FillContains(new Point(x, y)), clip.Clip!.FillContains(new Point(x, y)));
                    Assert.False(clip.Clip!.FillContains(new Point(1, 1))); // the corner is cut away
                    Assert.True(clip.Clip!.FillContains(new Point(200, height / 2.0)));
                }
                else
                {
                    Assert.Null(clip.Clip);
                    Assert.Contains(clip.GetType().GetInterfaces(), i => i.Name == "IVisualWithRoundRectClip"); // Avalonia's rounded ClipToBounds of a Border
                    var circle = new Point(22, 22);
                    for (var x = 0.25; x < 24; x += 0.5)
                        for (var y = 0.25; y < 24; y += 0.5)
                        {
                            var p = new Point(x, y);
                            var inCircle = x >= 22 || y >= 22 || (p - circle).X * (p - circle).X + (p - circle).Y * (p - circle).Y <= 22 * 22;
                            if (Math.Abs(Math.Sqrt((x - 22) * (x - 22) + (y - 22) * (y - 22)) - 22) < 0.05) continue; // on the edge itself
                            Assert.Equal(inCircle, expected.FillContains(p)); // the shape is the Border's own circular corner of 22
                        }
                }
            });
        }
        h.Main.OnMain(() => { Settle(window); window.Close(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); });
    });
}
