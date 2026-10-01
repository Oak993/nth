using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/EffortSlider.swift (B3 52bc792), :1232-1335; ED-213.
[Collection("Headless")]
public sealed class EffortSliderFusionTests(HeadlessSession headless)
{
    // DEVIATION OD-0048 / OD-0051: the source fusion (:1232-1335, ED-213) is replaced by the liquid tube; its tests
    // moved to EffortSliderLiquidTests.

    [Fact]
    [Trait("Req", "PR-COMPOSER-116")] // App/Views/Composer/EffortSlider.swift:650
    [Trait("Req", "UI-COMPOSER-037")] // App/Views/Composer/EffortSlider.swift:650
    public void The_pair_caption_enters_with_the_shared_soft_appear_transition() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource(); using var frameBinding = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider(); using var clockBinding = SwiftClock.Provider.Override(clock);
        using var harness = new AppModelHarness(withRuntime: false);
        var model = new AppModel();
        var pair = new EffortPairLook(new HydraPair(ProviderKind.Codex) { WorkerEffort = "high" }, model.Providers, null);
        var card = new EffortSliderCard("Lead", ProviderKind.Codex, new ValueList<string>(["low", "high"]), "low", false,
            new Binding<string?>(() => "low", _ => { }), new Binding<bool>(() => false, _ => { }), () => { }, pair);
        var window = new Avalonia.Controls.Window { Content = card, Width = 330, Height = 220 };
        window.Show(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var caption = Assert.Single(card.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == pair.Caption);
        Assert.Contains(caption.GetVisualAncestors().OfType<Control>(), ancestor => ancestor.Opacity == 0);
        clock.Advance(TimeSpan.FromSeconds(Animation.SoftAppear.Duration));
        frames.Advance(TimeSpan.FromSeconds(Animation.SoftAppear.Duration)); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.All(caption.GetVisualAncestors().OfType<Control>(), ancestor => Assert.Equal(1, ancestor.Opacity));
        window.Close();
    });
}
