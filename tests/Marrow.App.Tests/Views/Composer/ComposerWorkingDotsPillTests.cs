using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.App.Fixtures;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Testing;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/ComposerWorkingDots.swift (B3 52bc792), :67-71, :124-149, :152-157, and ComposerView.swift:179-185, :659-675.
// PR-COMPOSER-102 and PR-COMPOSER-104 ask for a capture comparison of a one-line and a five-line composer (and one with reduced motion). No frame shows
// reduced motion or a five-line composer, so for those OD-0098 makes the Swift the proof; the one-line working composer IS in the reference video
// (C 87.9-126.4) and ComposerWorkingDotsFrameTests compares it with C 92.00; the windows_path clause (no UI-thread ticking) is ComposerWorkingDotsNativeTests (STOP cloud-verify #3 closed). The dot field is laid over the mounted pill's
// bottom two-fifths, and its rows are min(3, max(1, ceil(height / 8))) at 8 points a row, however tall the draft makes the pill. This mounts the real pill
// (ComposerView on a running thread) with a one-line draft and with a draft past the five-line ceiling, and reads the dots the field drew.
[Collection("Headless")]
public sealed class ComposerWorkingDotsPillTests(HeadlessSession headless)
{
    private static void Settle(AvaloniaWindow window)
    {
        for (var pass = 0; pass < 5; pass++) { window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
    }

    private static (int Rows, int Columns, double Height, double Width, Ellipse[] Dots) Field(ComposerWorkingDots dots)
    {
        var field = Assert.Single(LogicalQuery.SelfAndDescendants<DotFieldControl>(dots));
        var layer = Assert.IsType<Canvas>(field.Layer);
        var all = layer.Children.Cast<Ellipse>().ToArray();
        var rows = all.Select(Canvas.GetTop).Distinct().Count();
        return (rows, all.Length / Math.Max(1, rows), field.Bounds.Height, field.Bounds.Width, all);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-102")] // ComposerWorkingDots.swift:124-149 through ComposerView.swift:179-185,664-672: a one-line pill and a five-line pill
    [Trait("Req", "PR-COMPOSER-104")] // ComposerWorkingDots.swift:13-19,152-157 reduced motion: the dots are drawn at 0.28, still faded by row
    [Trait("Rule", "R-TEST-4")] // partial proof of PR-COMPOSER-102 (the mounted rows); the C 92.00 comparison is ComposerWorkingDotsFrameTests, the render-thread clause ComposerWorkingDotsNativeTests
    public void A_mounted_pill_draws_at_most_three_rows_of_dots_for_a_one_line_and_for_a_five_line_draft_and_each_row_up_is_fainter() => headless.OnUiThread(() =>
    {
        System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var motion = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
        using var h = new ThreadRuntimePart12Harness();
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        h.Main.OnMain(() => h.Runtime.RehearseTurn("working"));
        var view = h.Main.OnMain(() => { var pill = new ComposerView(h.Runtime, workingDirectory: null); AppEnvironment.SetAppModel(pill, h.App); return pill; });
        var host = new Border { Child = view, Width = 600, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
        AppEnvironment.SetAppModel(host, h.App);
        EnvironmentValues.SetAccessibilityReduceMotion(host, true); // :13-19 the environment's reduce-motion value reaches the dots through the tree
        var window = new AvaloniaWindow { Content = host, Width = 1400, Height = 900 };
        h.Main.OnMain(() => { window.Show(); Settle(window); });
        void Pump() { for (var round = 0; round < 4; round++) h.Main.OnMain(() => Settle(window)); }
        Pump();

        var results = new List<(int Rows, int Columns, double Height, double Width, Ellipse[] Dots)>();
        var pillHeights = new List<double>();
        foreach (var text in new[] { "one line", string.Join("\n", Enumerable.Range(1, 30).Select(line => "line " + line.ToString(System.Globalization.CultureInfo.InvariantCulture))) })
        {
            h.Main.OnMain(() => h.Runtime.Draft = new ComposerDraft { Text = text });
            Pump();
            h.Main.OnMain(() =>
            {
                var dots = Assert.Single(LogicalQuery.SelfAndDescendants<ComposerWorkingDots>(view));
                results.Add(Field(dots));
                pillHeights.Add(view.Bounds.Height);
            });
        }
        h.Main.OnMain(() =>
        {
            Assert.True(pillHeights[1] > pillHeights[0], "the five-line pill is taller");
            foreach (var (rows, columns, height, width, dots) in results)
            {
                Assert.Equal(Math.Min(3, Math.Max(1, (int)Math.Ceiling(height / 8))), rows); // :129-132 rowCount
                Assert.Equal((int)Math.Ceiling(width / 8) + 1, columns); // :127 columnCount: ceil(width / 8) + 1
                Assert.All(dots, dot => { Assert.Equal(2, dot.Width); Assert.Equal(2, dot.Height); });
                var byRow = dots.GroupBy(Canvas.GetTop).OrderByDescending(group => group.Key).ToArray(); // the bottom row first
                for (var row = 0; row < rows; row++)
                    Assert.All(byRow[row], dot => Assert.Equal(0.28 * (1 - row / (double)rows), dot.Opacity, 8)); // :155 0.28, :149 each row up fainter by 1 / rowCount
            }
            Assert.True(results[1].Height > results[0].Height); // the field is the pill's bottom 40 percent, so it grows with the pill
            Assert.Equal(3, results[1].Rows); // the five-line pill never gets past three rows
            window.Close();
        });
        h.Main.OnMain(() => Avalonia.Threading.Dispatcher.UIThread.RunJobs());
    });
}
