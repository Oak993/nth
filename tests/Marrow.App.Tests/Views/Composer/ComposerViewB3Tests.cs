using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Automation;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Primitives;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Window;
using Marrow.App.Views.Composer;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/ComposerView.swift (B3 52bc792), :1-1307.
public sealed partial class ComposerViewTests
{
    private static ApprovalRequest Approval(string id) => new(id, ApprovalRequest.Kind.Tool, id,
        new ValueList<ApprovalRequest.Option>([new("no", "Decline", ApprovalRequest.Option.Role.Decline)]));

    [Fact]
    [Trait("Req", "PR-COMPOSER-002")] // App/Views/Composer/ComposerView.swift:23-31
    public void B3_approval_cards_precede_the_pill_in_runtime_order_with_12_point_gaps()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = Unresolved(h);
            h.Main.OnMain(() => { runtime.Rehearse(new ProviderEvent.Approval(Approval("first"))); runtime.Rehearse(new ProviderEvent.Approval(Approval("second"))); });
            var area = h.Main.OnMain(() => new ComposerArea(runtime, null, showsChanges: false));
            var window = h.Main.OnMain(() => Show(area, h.App, 500));
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                var cards = LogicalQuery.SelfAndDescendants<ApprovalCard>(area).ToArray();
                Assert.Equal(2, cards.Length);
                var stack = Stack(area);
                Assert.Equal(12, stack.Spacing);
                Assert.Equal(12, Frame(cards[1], stack).Y - Frame(cards[0], stack).Bottom, 5);
                Assert.Equal(12, Frame(One<ComposerView>(area), stack).Y - Frame(cards[1], stack).Bottom, 5);
            });
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-003")] // App/Views/Composer/ComposerView.swift:56
    public void B3_new_approval_ids_arrive_under_panelSlide_instead_of_appearing_at_full_opacity()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var frames = new ManualFrameSource();
            using var scope = FrameClock.Source.Override(frames);
            var runtime = Unresolved(h);
            var area = h.Main.OnMain(() => new ComposerArea(runtime, null, showsChanges: false));
            var window = h.Main.OnMain(() => Show(area, h.App, 500));
            Pump(h, window);
            h.Main.OnMain(() => runtime.Rehearse(new ProviderEvent.Approval(Approval("new"))));
            Pump(h, window);
            var card = h.Main.OnMain(() => One<ApprovalCard>(area));
            Assert.Equal(0, h.Main.OnMain(() => ((Control)card.Parent!).Opacity));
            h.Main.Advance(TimeSpan.FromSeconds(Chrome.PanelSlide.Duration + 0.01));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(Chrome.PanelSlide.Duration + 0.01)));
            Assert.Equal(1, h.Main.OnMain(() => ((Control)card.Parent!).Opacity));
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-016")] // App/Views/Composer/ComposerView.swift:599-604
    public void B3_clip_grows_on_smooth_028_while_text_is_immediate_and_live_resize_is_immediate()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var frames = new ManualFrameSource();
            using var scope = FrameClock.Source.Override(frames);
            var column = h.Main.OnMain(() => Column(h.Runtime, null));
            var window = h.Main.OnMain(() => Show(column, h.App, 400));
            var resize = h.Main.OnMain(() => new WindowLiveResize(window));
            h.Main.OnMain(() => AppEnvironment.SetWindowLiveResize(column, resize));
            Pump(h, window);
            var text = One<ComposerTextView>(column);
            var clip = (Border)text.Parent!;
            var before = clip.Height;
            h.Main.OnMain(() => h.Runtime.Draft = new ComposerDraft { Text = "one\ntwo\nthree" });
            Pump(h, window);
            var target = text.Height;
            Assert.True(target > before);
            Assert.Equal(before, clip.Height);
            h.Main.Advance(TimeSpan.FromSeconds(0.14));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(0.14)));
            Assert.Equal(before + (target - before) * Animation.Smooth(0.28).Progress(0.14), clip.Height, 5);
            h.Main.OnMain(() => { resize.IsPaneResizing = true; h.Runtime.Draft = new ComposerDraft { Text = "one\ntwo\nthree\nfour" }; });
            Pump(h, window);
            Assert.Equal(text.Height, clip.Height);
            Close(h, window);
        });
    }
}
