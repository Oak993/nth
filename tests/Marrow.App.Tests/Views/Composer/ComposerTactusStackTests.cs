using Avalonia.Controls;
using Marrow.App.Port.PcControl;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.UI.Port;
using Marrow.UI.Port.PcControl;
using Chrome = Marrow.UI.Chrome.Chrome;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Views.Composer;

// PR-COMPOSER-002: ComposerView.swift:23-31; OD-0145, ED-NEW-pcctl-27: Tactus sits after the cards, above the group.
// Drive its real feed while reduced motion settles its height deterministically; its existing strip tests pin the height transition.
[Collection("Headless")]
public sealed class ComposerTactusStackTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-COMPOSER-002")] // App/Views/Composer/ComposerView.swift:23-31
    [Trait("Decision", "ED-NEW-pcctl-27")]
    public void A_live_Tactus_strip_keeps_the_card_order_and_12_point_gaps_and_leaves_no_gap_when_its_stream_ends() => headless.OnUiThread(() =>
    {
        using var h = RuntimeHarness();
        using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = true });
        var frames = new ManualFrameSource();
        using var frameScope = FrameClock.Source.Override(frames);
        var clock = new PcManualClock();
        var control = new PcRunControl();
        using var pause = new PcPauseControl(clock, control);
        using var broker = new PcAskBroker(clock, control);
        using var feed = new PcChatFeed(pause, new PcStopControl(control), broker, clock);
        using var bridge = new TactusChatBridge(feed, broker, new PillAskBridge(broker), new TactusFakeChats(), () => false);
        var previous = TactusChatBridge.Current;
        var runtime = Unresolved(h);
        Avalonia.Controls.Window? window = null;
        try
        {
            TactusChatBridge.Current = bridge;
            var area = h.Main.OnMain(() => new ComposerArea(runtime, null, showsChanges: false));
            window = h.Main.OnMain(() => Show(area, h.App, 500));
            Pump(h, window);
            var strip = One<TactusStrip>(area);
            var stack = Stack(area);
            var groupRow = Assert.Single(stack.Children, child => child is not TactusStrip);
            Assert.Same(stack, strip.Parent);
            Assert.False(strip.IsVisible);
            Assert.Equal(0, Frame(groupRow, stack).Y);

            using var joined = pause.Join(runtime.ThreadID.ToString(), @"exe:c:\office\excel.exe", new PcWaitState());
            Pump(h, window);
            Assert.True(strip.IsVisible);
            Assert.Equal(TactusStrip.StripHeight, strip.Bounds.Height);
            Assert.Equal(12, Frame(groupRow, stack).Y - Frame(strip, stack).Bottom, 5);
            h.Main.OnMain(() =>
            {
                foreach (var id in new[] { "a", "b", "c" }) runtime.Rehearse(new ProviderEvent.Approval(Approval(id)));
            });
            Pump(h, window);
            void CheckRows(string[] ids)
            {
                var cards = LogicalQuery.SelfAndDescendants<ApprovalCard>(area).ToArray();
                Assert.Equal(ids, cards.Select(card => LogicalQuery.SelfAndDescendants<TextBlock>(card).Single(text => ids.Contains(text.Text!)).Text));
                for (var index = 1; index < cards.Length; index++)
                    Assert.Equal(12, Frame(cards[index], stack).Y - Frame(cards[index - 1], stack).Bottom, 5);
                Assert.Equal(12, Frame(strip, stack).Y - Frame(cards[^1], stack).Bottom, 5);
                Assert.Equal(12, Frame(groupRow, stack).Y - Frame(strip, stack).Bottom, 5);
            }
            h.Main.Advance(TimeSpan.FromSeconds(1));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(1)));
            Pump(h, window);
            CheckRows(["a", "b", "c"]);
            h.Main.OnMain(() => runtime.Rehearse(new ProviderEvent.RequestResolved("b")));
            Pump(h, window);
            h.Main.Advance(TimeSpan.FromSeconds(1));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(1)));
            Pump(h, window);
            CheckRows(["a", "c"]);
            joined.Dispose();
            pause.EndTurn(runtime.ThreadID.ToString());
            Pump(h, window);
            Assert.False(strip.IsVisible);
            Assert.Equal(0, strip.Height);
            var lastCard = LogicalQuery.SelfAndDescendants<ApprovalCard>(area).Last();
            Assert.Equal(12, Frame(groupRow, stack).Y - Frame(lastCard, stack).Bottom, 5);
            Assert.Equal(3, stack.Children.Count(child => child is not TactusStrip));
        }
        finally
        {
            if (window is not null) Close(h, window);
            TactusChatBridge.Current = previous;
        }
    });
}
