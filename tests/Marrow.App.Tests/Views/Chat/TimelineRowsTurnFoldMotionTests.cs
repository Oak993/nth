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

// PR-CHAT-104, the finished turn's fold (DroppyCode/App/Views/Chat/TimelineRows.swift:2609-2616 the header's action is
// `withAnimation(.snappy(duration: 0.24)) { isExpanded.toggle(); ... }`, :2623 the chevron's rotation in the same transaction, :2631 and
// :2661 the `if isExpanded { steps } else if derived.hasResponse { answer }` branches, which SwiftUI animates inside that transaction;
// OD-0076 and the lead's ruling of 2026-10-04): the body's height glides from one branch's to the other's along snappy(0.24) in both
// directions, the chevron turns on the same clock, the incoming branch is revealed by a clipped height and its rows fade in one by one, and the
// outgoing branch is gone at once: the two are never on screen together and no opacity lies on the whole body (a whole-layer fade over a
// body that can run to thousands of points is what ThreadTimeline.swift:1907-1926 `.transition(.identity)` avoids).
[Collection("Headless")]
public sealed class TimelineRowsTurnFoldMotionTests(HeadlessSession headless)
{
    private static readonly Animation Snappy = Animation.Snappy(duration: 0.24);

    private static TurnFinishedBlock Block(ThreadRuntime runtime, Guid turn, params TimelineEntry[] content) =>
        new(runtime, turn, Summary(turn), ValueList<TimelineEntry>.Empty, new ValueList<TimelineEntry>(content), null, canUndo: false, chronological: false);

    private static HeightFoldHost Body(Control block) => Assert.Single(Mounted<HeightFoldHost>(block));

    private static Button Header(Control block) => Mounted<Button>(block).First();

    private static double Angle(Control block) =>
        ((RotateTransform?)Mounted<SwiftImage>(block).First(image => image.SymbolName == "chevron.right").RenderTransform)?.Angle ?? 0;

    private static Border? Card(Control block) => Mounted<Border>(block).SingleOrDefault(border => border.Padding == new Thickness(14));

    private static FoldedAnswerView? Answer(Control block) => Mounted<FoldedAnswerView>(block).SingleOrDefault();

    // The rows the incoming branch brings: the card's groups, or the folded answer's column.
    private static Control[] Rows(Control block) =>
        Card(block) is { Child: StackPanel steps } ? [.. steps.Children] : Answer(block) is { } answer ? [.. ((StackPanel)answer.GetVisualParent()!).Children] : [];

    [Fact]
    [Trait("Req", "PR-CHAT-104")] // TimelineRows.swift:2609-2616 snappy(0.24), :2623 the chevron, :2631/:2661 both branches; OD-0076; the lead's ruling of 2026-10-04 (E_rulings.md PR-CHAT-104 implement 1)
    public void The_fold_glides_the_body_height_and_turns_the_chevron_on_snappy_024_both_ways_and_the_two_branches_are_never_on_screen_together() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        TurnFinishedBlock block = null!;
        AvaloniaWindow window = null!;
        h.Main.OnMain(() =>
        {
            var turn = Guid.NewGuid();
            block = Block(h.Runtime, turn, ToolEntry(Call(ToolCall.Kind.Command, "make"), turn), Reply("the answer " + new string('x', 30)));
            window = Show(new Border { Child = block });
        });
        void Step(int milliseconds)
        {
            for (var elapsed = 0; elapsed < milliseconds; elapsed += 10)
            {
                h.Main.AdvanceMilliseconds(10);
                h.Main.OnMain(() => { manual.Advance(TimeSpan.FromMilliseconds(10)); Settle(window); }); // the frames and the layout they cause run on the main actor
            }
        }
        // One direction of the fold: `from` and `to` are the body's natural heights, `turn` the chevron's angle at the start and the end.
        void Fold(Action click, double from, double to, double fromAngle, double toAngle, Func<Control, bool> incomingOnShow)
        {
            h.Main.OnMain(click);
            h.Main.OnMain(() => { }); // the body runs again under the transaction the click made
            h.Main.OnMain(() => Settle(window)); // the layout pass that reads the new height under the animation it was made in
            double At(double seconds) => from + (to - from) * Snappy.Progress(seconds);
            double AngleAt(double seconds) => fromAngle + (toAngle - fromAngle) * Snappy.Progress(seconds);
            var previousFade = 0d;
            for (var step = 0; step <= 40; step++) // 0 to 400 ms in 10 ms frames
            {
                var seconds = step * 0.010;
                h.Main.OnMain(() =>
                {
                    var body = Body(block);
                    // The height is the animation's value at this frame (one frame either way for the clock's quantum).
                    var low = Math.Min(At(Math.Max(0, seconds - 0.010)), At(seconds + 0.010)) - 1.0;
                    var high = Math.Max(At(Math.Max(0, seconds - 0.010)), At(seconds + 0.010)) + 1.0;
                    Assert.True(body.Bounds.Height >= Math.Min(low, high) && body.Bounds.Height <= Math.Max(low, high), $"at {step * 10} ms the body is {body.Bounds.Height:0.00} tall, snappy 0.24 has it between {Math.Min(low, high):0.00} and {Math.Max(low, high):0.00} ({from:0.0} -> {to:0.0})");
                    var angleLow = Math.Min(AngleAt(Math.Max(0, seconds - 0.010)), AngleAt(seconds + 0.010)) - 1.0;
                    var angleHigh = Math.Max(AngleAt(Math.Max(0, seconds - 0.010)), AngleAt(seconds + 0.010)) + 1.0;
                    Assert.True(Angle(block) >= Math.Min(angleLow, angleHigh) && Angle(block) <= Math.Max(angleLow, angleHigh), $"at {step * 10} ms the chevron is at {Angle(block):0.00}, snappy 0.24 has it between {Math.Min(angleLow, angleHigh):0.00} and {Math.Max(angleLow, angleHigh):0.00}");
                    // One branch at a time: the outgoing one is gone at once, the incoming one is there from the first frame.
                    Assert.True((Answer(block) is not null) ^ (Card(block) is not null), "both branches or neither are on screen");
                    Assert.True(incomingOnShow(block), "the incoming branch is not the one on show");
                    // No opacity on the body or on the branch's root: only its rows fade.
                    Assert.Equal(1, body.Opacity);
                    Assert.Equal(1, body.Content.Opacity);
                    Assert.Equal(1, (Card(block) ?? (Control)Answer(block)!.GetVisualParent()!).Opacity);
                    var rows = Rows(block);
                    Assert.NotEmpty(rows);
                    var fade = rows.Min(row => row.Opacity);
                    Assert.InRange(fade, 0, 1);
                    Assert.True(fade + 1e-9 >= previousFade, "a row faded back out while it was coming in");
                    previousFade = fade;
                    if (step == 0) Assert.Equal(0, fade, 3); // the incoming rows start clear
                    if (step == 0) Assert.True(body.ClipToBounds); // the height reveal clips while it runs ...
                });
                if (step < 40) Step(10);
            }
            h.Main.OnMain(() =>
            {
                Assert.Equal(to, Body(block).Bounds.Height, 0.5); // settled on the other branch's own height
                Assert.All(Rows(block), row => Assert.Equal(1, row.Opacity));
                Assert.Equal(toAngle, Angle(block));
                Assert.False(Body(block).ClipToBounds); // ... and not once it has settled
            });
        }

        double collapsed = 0, expanded = 0;
        h.Main.OnMain(() =>
        {
            Assert.NotNull(Answer(block));
            collapsed = Body(block).Bounds.Height;
            Assert.Equal(0, Angle(block));
        });
        // The steps' natural height: open the block once and let it settle, then fold it back, so the runs below start from the folded one.
        h.Main.OnMain(() => Click(window, Header(block)));
        Step(600);
        h.Main.OnMain(() => expanded = Body(block).Bounds.Height);
        h.Main.OnMain(() => Click(window, Header(block)));
        Step(600);
        h.Main.OnMain(() => Assert.Equal(collapsed, Body(block).Bounds.Height, 0.5));
        Assert.True(expanded > collapsed + 20, $"the open body is {expanded:0.0} against {collapsed:0.0} folded");

        Fold(() => Click(window, Header(block)), collapsed, expanded, 0, 90, shown => Card(shown) is not null);
        Fold(() => Click(window, Header(block)), expanded, collapsed, 90, 0, shown => Answer(shown) is not null);
        h.Main.OnMain(() => Close(window));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-104")] // TimelineRows.swift:2589-2715 the body reads the turn's entries; a change made outside an animation is not animated (OD-0076 transitions changes; it does not invent motion for a data refresh)
    public void A_change_of_the_turns_content_made_outside_an_animation_is_followed_at_once() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        TurnFinishedBlock block = null!;
        AvaloniaWindow window = null!;
        var turn = Guid.NewGuid();
        double before = 0;
        h.Main.OnMain(() =>
        {
            block = Block(h.Runtime, turn, Reply("short"));
            window = Show(new Border { Child = block });
            before = Body(block).Bounds.Height;
            var longer = Reply(string.Join("\n\n", Enumerable.Repeat("a paragraph of the answer", 6)));
            block.Update(Summary(turn), ValueList<TimelineEntry>.Empty, new ValueList<TimelineEntry>([longer]), null, nextCanUndo: false);
        });
        h.Main.AdvanceMilliseconds(50); // the block's body runs again; no frame is delivered
        h.Main.OnMain(() =>
        {
            Settle(window);
            var body = Body(block);
            Assert.True(body.Bounds.Height > before + 20, $"{before:0.0} -> {body.Bounds.Height:0.0}");
            Assert.Equal(body.Content.DesiredSize.Height, body.Bounds.Height, 0.5); // no frame has run: it is already there
            Assert.False(body.ClipToBounds);
            Close(window);
        });
    });
}
