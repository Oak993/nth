using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// REVIEW (adversarial review of unit/blitz-effortslider, 2026-10-04): PR-COMPOSER-108 says the chip holds its width while
// the effort or the model changes with its popover open (EffortSlider.swift:105-117), and the record's verification is
// "dragging the slider from Low to Extra High with the popover open". SwiftUI keeps the chip's @State isPresented through
// the composer's re-render (ComposerView.swift:647,688 the same structural identity), so the popover stays open while the
// slider writes thread.effort (:204-210). In the port, ComposerView.ShowControls draws a new ComposerControls (and so a new
// ModelEffortButton) whenever the thread record changes, and the old chip's DetachedFromVisualTree dismisses its popover:
// moving the slider in the real composer closes the popover. The unit's chip tests mount the chip alone and never see it.
[Collection("Headless")]
public sealed class EffortChipInComposerReviewTests(HeadlessSession headless)
{
    [Fact]
    public void REVIEW_An_effort_change_with_the_chips_popover_open_keeps_the_chip_and_its_popover_in_the_composer() => headless.OnUiThread(() =>
    {
        Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        using var harness = new ThreadRuntimePart12Harness();
        harness.Main.Loop.Run(() => harness.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var presenter = new RecordingPresenter(); using var presentation = Presentation.Presenter.Override(presenter);
        ComposerView view = null!; Avalonia.Controls.Window window = null!;
        void Pump() { for (var round = 0; round < 4; round++) harness.Main.OnMain(() => { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }); }
        harness.Main.OnMain(() =>
        {
            view = new ComposerView(harness.Runtime, workingDirectory: null);
            var host = new Border { Child = view, Width = 600 };
            AppEnvironment.SetAppModel(host, harness.App);
            window = new Avalonia.Controls.Window { Content = host, Width = 900, Height = 600 };
            window.Show();
        });
        Pump();
        try
        {
            var chip = harness.Main.OnMain(() => Assert.Single(view.GetVisualDescendants().OfType<ModelEffortButton>()));
            harness.Main.OnMain(() => Assert.Single(chip.GetVisualDescendants().OfType<Button>()).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            Pump();
            Assert.True(presenter.Calls[^1].Presented, "the chip's popover opened");
            var calls = presenter.Calls.Count;
            // what the open slider writes (EffortSlider.swift:204-210, 631)
            harness.Main.OnMain(() => harness.App.UpdateThread(harness.Thread.Id, current => current with { Effort = current.Effort == "low" ? "high" : "low" }));
            Pump();
            Assert.DoesNotContain(presenter.Calls.Skip(calls), call => !call.Presented); // the popover is not dismissed
            harness.Main.OnMain(() => Assert.Same(chip, Assert.Single(view.GetVisualDescendants().OfType<ModelEffortButton>())));
        }
        finally { harness.Main.OnMain(() => window.Close()); }
    });
}
