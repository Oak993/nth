using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// EffortSlider.swift:120-123 `.popover(isPresented: $isPresented, arrowEdge: .bottom) { ModelEffortPanel(...) }` on the
// composer's chip. SOURCE FACT (reference runtime, 2-chat-use.mov 33.74-33.94 s every frame, and stills at 34-50 s and
// 183-194 s; study E:\Droppy\work\reference-video\study\effort-title-2026-10-03): the popover opens ABOVE the chip,
// its arrow on the card's bottom edge pointing down at the chip, and grows out of that arrow. The same arrowEdge
// .bottom popovers of the main window (Chrome.swift:405 ChromeMenuButton, :509 ChromeTextMenu, the diff panel's scope
// and "..." menus at 19.28 s and 21.73 s) open above their buttons with room to spare below them, so it is not a flip
// for want of room: the card hangs above. Where there is no room above, the system flips it below (NSPopover).
[Collection("Headless")]
public sealed class ModelEffortPopoverPlacementTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-COMPOSER-109")]
    public void The_chip_popover_opens_above_the_chip_and_flips_below_only_without_room() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness(withRuntime: false);
        var thread = new ChatThread(Guid.NewGuid(), ProviderKind.Codex, null, null, RuntimeMode.Supervised);
        harness.WriteLibrary([], [thread]);
        var model = new AppModel();
        foreach (var provider in SwiftEnum<ProviderKind>.AllCases)
            if (!provider.IsAPIKeyBased) model.Settings.SetBinaryPath(Path.Combine(harness.Scratch, "missing.exe"), provider);
        var chip = new ModelEffortButton(thread, false);
        var presenter = new RecordingPresenter();
        using var presentation = Presentation.Presenter.Override(presenter);
        var host = new Border { Child = chip };
        AppEnvironment.SetAppModel(host, model);
        var window = new Avalonia.Controls.Window { Content = host, Width = 330, Height = 220 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var button = Assert.Single(chip.GetVisualDescendants().OfType<Button>());
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var (popup, presented) = presenter.Calls[^1];
            Assert.True(presented);
            Assert.Same(button, popup.PlacementTarget);
            Assert.Equal(PlacementMode.Top, popup.Placement); // above the chip, the arrow on the card's bottom edge
            Assert.True(popup.PlacementConstraintAdjustment.HasFlag(PopupPositionerConstraintAdjustment.FlipY)); // no room above: below
        }
        finally { window.Close(); }
    });
}
