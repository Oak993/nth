using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Chat;

// REVIEW (rec-e1, PR-CHAT-104): the header clicked again while the fold is half way. SwiftUI retargets an animation from where it is (no jump); the port's height, the chevron and the rows'
// fades must continue from where they are, one branch on show at a time, and settle on the branch the last click chose.
[Collection("Headless")]
public sealed class TimelineRowsTurnFoldReversalReviewTests(HeadlessSession headless)
{
    private static HeightFoldHost Body(Control block) => Assert.Single(Mounted<HeightFoldHost>(block));

    private static double Angle(Control block) =>
        ((RotateTransform?)Mounted<SwiftImage>(block).First(image => image.SymbolName == "chevron.right").RenderTransform)?.Angle ?? 0;

    private static Border? Card(Control block) => Mounted<Border>(block).SingleOrDefault(border => border.Padding == new Thickness(14));

    private static FoldedAnswerView? Answer(Control block) => Mounted<FoldedAnswerView>(block).SingleOrDefault();

    [Fact]
    [Trait("Req", "PR-CHAT-104")]
    public void REVIEW_a_second_click_half_way_through_the_fold_turns_height_chevron_and_fades_round_without_a_jump_or_two_branches() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        TurnFinishedBlock block = null!;
        AvaloniaWindow window = null!;
        var turn = Guid.NewGuid();
        h.Main.OnMain(() =>
        {
            block = new TurnFinishedBlock(h.Runtime, turn, Summary(turn), ValueList<TimelineEntry>.Empty,
                new ValueList<TimelineEntry>([ToolEntry(Call(ToolCall.Kind.Command, "make"), turn), Reply("the answer " + new string('x', 30))]), null, canUndo: false, chronological: false);
            window = Show(new Border { Child = block });
        });
        void Step(int milliseconds)
        {
            for (var elapsed = 0; elapsed < milliseconds; elapsed += 10)
            {
                h.Main.AdvanceMilliseconds(10);
                h.Main.OnMain(() => { manual.Advance(TimeSpan.FromMilliseconds(10)); Settle(window); });
            }
        }
        var header = Mounted<Button>(block).First();
        double collapsed = 0, expanded = 0;
        h.Main.OnMain(() => collapsed = Body(block).Bounds.Height);
        h.Main.OnMain(() => Click(window, header)); Step(600);
        h.Main.OnMain(() => expanded = Body(block).Bounds.Height);
        h.Main.OnMain(() => Click(window, header)); Step(600);
        var failures = new List<string>();
        foreach (var halfway in new[] { 60, 120 })
        {
            // open, turn round after `halfway` ms, then close again.
            h.Main.OnMain(() => Click(window, header)); Step(halfway);
            double heightBefore = 0, angleBefore = 0;
            h.Main.OnMain(() => { heightBefore = Body(block).Bounds.Height; angleBefore = Angle(block); });
            h.Main.OnMain(() => Click(window, header));
            h.Main.OnMain(() => { }); h.Main.OnMain(() => Settle(window));
            var previous = heightBefore; var previousAngle = angleBefore;
            for (var step = 0; step <= 40; step++)
            {
                h.Main.OnMain(() =>
                {
                    var body = Body(block);
                    var height = body.Bounds.Height; var angle = Angle(block);
                    if (Math.Abs(height - previous) > 40) failures.Add($"turn-round at {halfway} ms: height jumped {previous:0.0} -> {height:0.0} at +{step * 10} ms");
                    if (Math.Abs(angle - previousAngle) > 40) failures.Add($"turn-round at {halfway} ms: chevron jumped {previousAngle:0.0} -> {angle:0.0} at +{step * 10} ms");
                    if ((Answer(block) is not null) == (Card(block) is not null)) failures.Add($"turn-round at {halfway} ms: both or neither branch at +{step * 10} ms");
                    previous = height; previousAngle = angle;
                });
                if (step < 40) Step(10);
            }
            h.Main.OnMain(() =>
            {
                if (Math.Abs(Body(block).Bounds.Height - collapsed) > 0.5) failures.Add($"settled at {Body(block).Bounds.Height:0.0}, not {collapsed:0.0}");
                if (Angle(block) != 0) failures.Add($"chevron settled at {Angle(block):0.0}");
                if (Body(block).ClipToBounds) failures.Add("still clipping");
            });
            Step(300);
        }
        h.Main.OnMain(() => Close(window));
        Assert.True(failures.Count == 0, string.Join(" | ", failures.Take(6)));
    });
}
