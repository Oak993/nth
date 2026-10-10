using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Port.Skills;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Testing;
using Marrow.UI.Chrome;

namespace Marrow.App.Tests.Port.Skills;

// PORT-ONLY (no Swift twin): SK-14 (DL-18). The real timeline draws the offer card and its caption inline in the reply (Offer A). It lays out real text, which needs the Windows text stack (ICU), so it is a Native test:
// the Linux slices leave it out and the Windows check runs it; SkillsOfferMountTests proves the same placement on the rows' own panel.
[Collection("Headless")]
public sealed class SkillsOfferTimelineNativeTests(HeadlessSession headless)
{
    private static void LoadLogin() => System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();

    private static void Settle(Avalonia.Controls.Window window)
    {
        for (var pass = 0; pass < 6; pass++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    }

    private static (ThreadTimeline Timeline, Avalonia.Controls.Window Window) MountTimeline(ThreadRuntimeTurnRig rig)
    {
        rig.Send("fixture"); // a thread with no entries draws the new-thread prompt, not the timeline
        var timeline = rig.H.Main.OnMain(() =>
        {
            var view = new ThreadTimeline(rig.Runtime, new ChromeScrollModel(), new TimelineScrollState(), "Project", null, false, 480);
            var host = new Avalonia.Controls.Border { Child = view };
            AppEnvironment.SetAppModel(host, rig.App);
            return (view, host);
        });
        var window = new Avalonia.Controls.Window { Content = timeline.host, Width = 900, Height = 500 };
        AppEnvironment.SetWindowLiveResize(timeline.host, new Marrow.App.Window.WindowLiveResize(new Avalonia.Controls.Window()));
        rig.H.Main.OnMain(() => { window.Show(); Settle(window); });
        return (timeline.view, window);
    }

    [Fact]
    [Trait("Decision", "ED-326")]
    public void A_chat_with_an_offer_flow_draws_its_card_and_caption_inline_in_the_reply_after_the_rows_and_the_card_shows_an_offer_made_for_it() => headless.OnUiThread(() =>
    {
        LoadLogin();
        using var frames = Marrow.UI.Port.FrameClock.Source.Override(new Marrow.UI.Port.ManualFrameSource()); // the timeline's animations ask for frames; Marrow.App binds the clock at start-up
        using var reduce = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = false });
        using var r = new SkillsOfferRig();
        var (timeline, window) = MountTimeline(r.Turn);
        var card = Assert.Single(r.Turn.H.Main.OnMain(() => timeline.GetVisualDescendants().OfType<OfferCardControl>().ToArray()));
        var caption = Assert.Single(r.Turn.H.Main.OnMain(() => timeline.GetVisualDescendants().OfType<OfferCaptionControl>().ToArray()));
        r.Turn.H.Main.OnMain(() =>
        {
            var body = Assert.IsType<Avalonia.Controls.StackPanel>(card.GetVisualParent());
            Assert.Same(body, caption.GetVisualParent());
            var order = body.Children.ToList();
            Assert.True(order.IndexOf(card) > 0 && order.IndexOf(caption) == order.IndexOf(card) + 1 && order.IndexOf(caption) < order.Count - 1, "the card follows the rows and the caption follows the card, before the end sentinel");
        });
        Assert.Equal("none", r.Turn.H.Main.OnMain(() => card.Kind));
        r.Offer();
        r.Turn.H.Main.OnMain(() => Settle(window));
        Assert.Equal("asked", r.Turn.H.Main.OnMain(() => card.Kind));
        r.Turn.H.Main.OnMain(() => window.Close());
    });

    [Fact]
    [Trait("Decision", "ED-326")]
    public void A_chat_without_an_offer_flow_draws_no_card_or_caption() => headless.OnUiThread(() =>
    {
        LoadLogin();
        using var frames = Marrow.UI.Port.FrameClock.Source.Override(new Marrow.UI.Port.ManualFrameSource()); // the timeline's animations ask for frames; Marrow.App binds the clock at start-up
        using var reduce = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = false });
        using var h = new ThreadRuntimeTurnRig();
        var (timeline, window) = MountTimeline(h);
        Assert.Empty(h.H.Main.OnMain(() => timeline.GetVisualDescendants().Where(v => v is OfferCardControl or OfferCaptionControl).ToArray()));
        h.H.Main.OnMain(() => window.Close());
    });
}
