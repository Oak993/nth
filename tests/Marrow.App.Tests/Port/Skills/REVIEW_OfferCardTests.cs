using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port.Skills;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.Skills.Detect;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Port.Skills.Offers;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Port.Skills;

// Independent SK-14 acceptance cases: real fail-closed host plus offline runtime, or scripted
// source and manual frames. No Accept on a real downloader and no live provider is invoked.
[Collection("Headless")]
[Trait("Feature", "SK-14")]
[Trait("Spec", "DL-18")]
[Trait("Review", "SK-14")]
[Trait("Rule", "R-TEST-4")]
public sealed class REVIEW_OfferCardTests(HeadlessSession headless)
{
    [Fact]
    public void REVIEW_Unbound_host_keeps_Download_disabled_and_Return_does_nothing()
    {
        var root = Path.Combine(Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR")!, "review-sk14-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var host = SkillsHost.Open(root, SkillsHostOptions.Production());
            Assert.False(host.Downloads.Available);
            using var rig = new SkillsOfferRig(runtime => SkillsOfferFlows.Create(runtime, host));
            rig.Offer();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            headless.OnUiThread(() =>
            {
                var card = new OfferCardControl(rig.Flow, host.Catalogue, () => host.Pipeline.State);
                var window = new Avalonia.Controls.Window { Content = card, Width = 420, Height = 200 };
                window.Show();
                try
                {
                    window.UpdateLayout();
                    Assert.False(card.Primary!.IsEffectivelyEnabled);
                    card.Focus();
                    card.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Enter, Source = card });
                    Assert.Equal(OfferStage.Asked, rig.Flow.Stage);
                }
                finally { window.Close(); }
            });
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void REVIEW_An_additional_need_updates_the_combined_size_before_acceptance() => OfferCardHarness.Run(headless, OfferCardHarness.Asked(), h =>
    {
        var total = h.Catalogue.Items.Where(i => i.Id is "T1" or "T3").Sum(i => i.ApproxSizeMb);
        h.Source.Card = new OfferCard(["T3", "T1"], ["T1", "T3"], total);
        h.Settle();
        Assert.Equal(LibraryWords.Download(total), LibraryButtons.Words(h.Card.Primary!));
    });

    [Fact]
    public void REVIEW_Reduce_Motion_retains_a_short_opacity_fade() => OfferCardHarness.Run(headless, OfferCardHarness.Asked(), h =>
    {
        // ONDEMAND 5.6: movement becomes fades. The locked CSS reduces transitions to .01s;
        // it does not remove the opacity transition. A 5 ms frame must still be intermediate.
        h.Source.Stage = OfferStage.Downloading;
        h.Source.Progress = new OfferProgress(OfferStage.Downloading, 1, 10);
        h.Settle();
        h.Step(5);
        var opacity = h.Card.Bar!.GetVisualAncestors().Prepend(h.Card.Bar!).Select(v => v.Opacity).Aggregate(1.0, (a, b) => a * b);
        Assert.InRange(opacity, 0.001, 0.999);
        h.Step(300);
        Assert.Equal(1, h.Card.Bar!.GetVisualAncestors().Prepend(h.Card.Bar!).Select(v => v.Opacity).Aggregate(1.0, (a, b) => a * b), 6);
    }, motion: true, reduceMotion: true);

    [Theory]
    [InlineData(OfferStage.Done)]
    [InlineData(OfferStage.Continuing)]
    [InlineData(OfferStage.Declined)]
    public void REVIEW_Terminal_state_fades_before_collapsing_the_card(OfferStage terminal) => OfferCardHarness.Run(headless, OfferCardHarness.Asked(), h =>
    {
        var leaving = h.Card.Primary!;
        var before = h.Composer.Bounds.Top;
        h.Source.Stage = terminal;
        h.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.True(h.Card.IsEffectivelyVisible, "The card disappears before its outgoing fade can render.");
        Assert.Equal(before, h.Composer.Bounds.Top, 3);
        h.Step(80);
        var opacity = leaving.GetVisualAncestors().Prepend(leaving).Select(v => v.Opacity).Aggregate(1.0, (a, b) => a * b);
        Assert.InRange(opacity, 0.001, 0.999);
        h.Step(400);
        Assert.False(h.Card.IsEffectivelyVisible);
    }, motion: true);
}
