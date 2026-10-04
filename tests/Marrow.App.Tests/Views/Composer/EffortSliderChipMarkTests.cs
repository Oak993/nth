using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Port;
using static Marrow.App.Tests.Views.Composer.EffortSliderModelListTests;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/EffortSlider.swift (B3 52bc792), :66-75 the chip's mark: `.id(pair.id)` keeps the fused mark's view (and its
// state) while the pair stays the same, so a change of effort or fast mode does not play its fuse-in again; HydraFusedPairIcon.swift:37-42 its
// `.task(id: "\(lead)/\(heads)")` is what plays it.
[Collection("Headless")]
public sealed class EffortSliderChipMarkTests(HeadlessSession headless)
{
    private static bool Fused(HydraFusedPairIcon icon) => (bool)typeof(HydraFusedPairIcon).GetField("isFused", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(icon)!;

    [Fact]
    [Trait("Req", "UI-COMPOSER-012")] // App/Views/Composer/EffortSlider.swift:73 `.id(pair.id)`: the fused mark is kept across effort and fast changes
    public void The_chips_fused_mark_is_kept_while_the_pair_stays_and_made_again_for_another_pair() => headless.OnUiThread(() =>
    {
        var pair = new HydraPair(ProviderKind.Codex) { OrchestratorModel = "c1", Name = "Duo" };
        var other = new HydraPair(ProviderKind.Codex) { OrchestratorModel = "c1", Name = "Other" };
        using var env = Make(() => Thread(ProviderKind.Codex, "c1", hydra: true, pair: pair.Id), model =>
        {
            model.Settings.HydraEnabled = true; model.Settings.AddHydraPair(pair); model.Settings.AddHydraPair(other);
        }, (ProviderKind.Codex, [Option("c1", "C1", fast: true)]));
        var chip = new ModelEffortButton(env.Thread, false);
        var window = env.Show(chip);
        HydraFusedPairIcon Mark() => Assert.Single(chip.GetVisualDescendants().OfType<HydraFusedPairIcon>());
        var first = Mark();
        env.Step(700); // the 30 ms wait and the 0.55 s spring of the fuse-in have run
        Assert.True(Fused(first));
        // the effort moves, then fast mode: the same mark, still fused, still in the tree, and no new fuse-in starts
        env.Model.UpdateThread(env.Thread.Id, chat => chat with { Effort = "high" }); Dispatcher.UIThread.RunJobs();
        Assert.Same(first, Mark()); Assert.True(Fused(first));
        env.Model.UpdateThread(env.Thread.Id, chat => chat with { FastMode = true }); Dispatcher.UIThread.RunJobs();
        Assert.Same(first, Mark()); Assert.True(Fused(first)); Assert.True(first.IsAttachedToVisualTree());
        env.Step(40);
        Assert.True(Fused(first));
        // another pair is another mark (its own id)
        env.Model.UpdateThread(env.Thread.Id, chat => chat with { HydraPairID = other.Id }); Dispatcher.UIThread.RunJobs();
        Assert.NotSame(first, Mark());
        // leaving the pair: the provider's own icon, and no mark
        env.Model.UpdateThread(env.Thread.Id, chat => chat with { HydraPairID = null }); Dispatcher.UIThread.RunJobs();
        Assert.Empty(chip.GetVisualDescendants().OfType<HydraFusedPairIcon>());
        Assert.Single(chip.GetVisualDescendants().OfType<ProviderIcon>());
        window.Close();
    });
}
