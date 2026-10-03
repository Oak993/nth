using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// EffortSlider.swift:90-99 the chip's name and effort are plain Texts (no .contentTransition), changed by the slider
// inside withAnimation(.snappy(duration: 0.2)) (:631). SOURCE FACT (reference runtime, 2-chat-use.mov 34.84-34.97 s and
// 35.04-35.19 s, every frame; study effort-title-2026-10-03 chip_34.75-35.35): the chip's label cross-fades from the old
// text to the new one in place ("Sonnet High" under "S... Extra High", fitted weight of the new text 0.37, 0.66, 0.80,
// 0.90, 0.96 at 27, 49, 69, 90 and 112 ms), not letter by letter as the card's title does.
[Collection("Headless")]
public sealed class ModelChipTextCrossfadeTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "UI-COMPOSER-012")]
    public void The_chips_effort_cross_fades_when_it_changes_inside_an_animation_and_swaps_outside_one() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var frameBinding = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var clockBinding = SwiftClock.Provider.Override(clock);
        using var harness = new AppModelHarness(withRuntime: false);
        var thread = new ChatThread(Guid.NewGuid(), ProviderKind.Claude, "sonnet", null, RuntimeMode.Supervised) { Effort = "high" };
        harness.WriteLibrary([], [thread]);
        var model = new AppModel();
        foreach (var provider in SwiftEnum<ProviderKind>.AllCases)
            if (!provider.IsAPIKeyBased) model.Settings.SetBinaryPath(Path.Combine(harness.Scratch, "missing.exe"), provider);
        var chip = new ModelEffortButton(thread, false);
        using var presentation = Presentation.Presenter.Override(new RecordingPresenter());
        var host = new Border { Child = chip };
        AppEnvironment.SetAppModel(host, model);
        var window = new Avalonia.Controls.Window { Content = host, Width = 330, Height = 120 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        void Step(int milliseconds)
        {
            clock.Advance(TimeSpan.FromMilliseconds(milliseconds));
            frames.Advance(TimeSpan.FromMilliseconds(milliseconds));
            Dispatcher.UIThread.RunJobs();
        }
        TextBlock[] Labels(string text) => chip.GetVisualDescendants().OfType<TextBlock>().Where(label => label.Text == text).ToArray();
        try
        {
            Assert.Single(Labels("High"));
            var started = clock.GetUtcNow();
            Transaction.WithAnimation(Animation.Snappy(duration: 0.2), () => model.UpdateThread(thread.Id, current => current with { Effort = "max" }));
            Dispatcher.UIThread.RunJobs();
            // The video's weights, each within 0.06 (round 1 accepted 0.3-0.8 at 49 ms and trailed the video by a frame).
            foreach (var (ms, weight) in new[] { (27, 0.37), (49, 0.66), (69, 0.80), (90, 0.90) })
            {
                Step(ms - (int)Math.Round((clock.GetUtcNow() - started).TotalMilliseconds));
                var leaving = Assert.Single(Labels("High"));
                var arriving = Assert.Single(Labels("Max"));
                Assert.True(Math.Abs(arriving.Opacity - weight) <= 0.06, $"at {ms} ms the new text's weight is {arriving.Opacity:F2}; the video's {weight}");
                Assert.Equal(1 - arriving.Opacity, leaving.Opacity, 3);       // the old one fades as the new one comes
            }
            Step(400);
            Assert.Empty(Labels("High"));
            Assert.Equal(1, Assert.Single(Labels("Max")).Opacity);
            model.UpdateThread(thread.Id, current => current with { Effort = "low" }); // no transaction: a plain swap
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(Labels("Max"));
            Assert.Equal(1, Assert.Single(Labels("Low")).Opacity);
        }
        finally { window.Close(); }
    });
}
