using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Port.FirstRun;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Port.FirstRun;

// REVIEW (GR-2 adversarial pass, 2026-10-04): OD-0076 (nothing cuts) at the frame the sheet leaves the tree, and the wrapped line
// under the title (ED-306 (7)) leaving one word alone on its second line.
[Collection("Headless")]
public sealed class GettingReadyReviewTests(HeadlessSession headless)
{
    private static void Mounted(HeadlessSession headless, Action<GettingReadyRootRig> body) => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        harness.Main.OnMain(() =>
        {
            using var rig = new GettingReadyRootRig(model);
            body(rig);
        });
    });

    [Fact] // GR-2.review
    [Trait("Decision", "ED-288")]
    public void The_top_of_the_pane_does_not_step_on_the_frame_the_sheet_and_its_shadow_are_taken_out() => Mounted(headless, rig =>
    {
        var origin = rig.Root.DetailPane.TranslatePoint(new Point(0, 0), rig.Window)!.Value;
        var x = (int)(origin.X + rig.Root.DetailPane.Bounds.Width / 2);
        var ys = new[] { 1, 6, 14, 24, 40 };
        rig.AllDone();
        _ = rig.Sheet.EndAsync(new FirstRunOutcome(FirstRunOutcomeKind.Prepared));
        var log = new System.Text.StringBuilder();
        double[]? last = null;
        var worstBefore = 0.0;
        var atRemoval = 0.0;
        var removed = false;
        rig.Run(1.5, time =>
        {
            var levels = FramePixels.Levels(rig.Window, ys.Select(y => (x, y)).ToArray());
            var inTree = rig.Root.GetVisualDescendants().Contains(rig.Sheet);
            if (last is not null)
            {
                var step = levels.Zip(last, (a, b) => Math.Abs(a - b)).Max();
                if (!removed && !inTree) { atRemoval = step; removed = true; }
                else if (inTree && time > 0.9) worstBefore = Math.Max(worstBefore, step);
            }
            if (time > 1.07 && time < 1.12) log.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"{time:0.000} hand {rig.Sheet.Hand:0.000} in {inTree}: {string.Join(" ", levels.Select(v => v.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)))}");
            last = levels;
        });
        Assert.True(removed);
        Assert.True(atRemoval <= worstBefore + 0.004, $"the top of the pane steps {atRemoval:0.0000} when the sheet is taken out (before: at most {worstBefore:0.0000}); levels at y 1, 6, 14, 24, 40: {log.ToString().Replace(Environment.NewLine, " ; ", StringComparison.Ordinal)}");
    });

    [Fact] // GR-2.review
    [Trait("Decision", "ED-288")]
    public void The_line_under_the_title_does_not_leave_one_word_alone_on_a_line() => headless.OnUiThread(() =>
    {
        using var rig = new GettingReadyRig();
        var line = (TextBlock)rig.Sheet.LineText;
        var layout = line.TextLayout;
        var text = GettingReadyLook.Line;
        var lines = layout.TextLines.Select(l => text.Substring(l.FirstTextSourceIndex, Math.Min(l.Length, text.Length - l.FirstTextSourceIndex)).Trim()).ToList();
        var width = layout.TextLines.Select(l => l.WidthIncludingTrailingWhitespace).ToList();
        Assert.True(lines.Count == 1 || lines[^1].Split(' ').Length > 1, $"lines: {string.Join(" | ", lines.Select((l, i) => $"[{l}] {width[i]:0.0}"))}; box {rig.Rect(line)}");
    });
}
