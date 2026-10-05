using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Port.Updates;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Sidebar;
using Marrow.Core.Port;
using Marrow.Core.Port.Updates;
using Marrow.UI.Port;
using Xunit;
using static Marrow.App.Tests.Views.Sidebar.SidebarViewTests;

namespace Marrow.App.Tests.Views.Sidebar;

// UE-5 review (lead decisions on the first review): the popover hangs from the pill's left edge as the design draws it (.dpop left 14 px), the failure banner folds its height in frame by
// frame with the card following, and the done phase stays until its popover has been closed. The popover is opened for real here, in the window's overlay layer (ReviewOverlayPresenter).
[Collection("Headless")]
public sealed class UpdatePillPopoverTests(HeadlessSession headless)
{
    private static FakeUpdateItem Cli(string id, string title, string installed = "1.0.0", string? latest = "1.1.0") => new(id, title, UpdateItemKind.ProviderCli, installed, latest);

    private static void Tick(AppModelHarness harness, ManualFrameSource frames, int ms)
    {
        harness.Main.AdvanceMilliseconds(ms);
        harness.Main.OnMain(() => frames.Advance(TimeSpan.FromMilliseconds(ms)));
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    [Trait("Decision", "ED-308")]
    public void The_popover_opens_on_a_real_window_with_its_left_edge_on_the_pills_left_edge_and_inside_the_window() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        var presenter = new ReviewOverlayPresenter(Presentation.Presenter.Current);
        using var seam = Presentation.Presenter.Override(presenter);
        using var harness = new AppModelHarness();
        var service = harness.Main.OnMain(() => new UpdateEverythingService([Cli("c", "Claude Code"), Cli("x", "Codex", "0.157.1", "0.160.0")], () => { }));
        using var shared = UpdateEverythingService.OverrideShared(service);
        var model = harness.MakeModel();
        var host = new Border { Width = 280, HorizontalAlignment = HorizontalAlignment.Left };
        AppEnvironment.SetAppModel(host, model);
        var view = new SidebarView();
        host.Child = view;
        var window = new Avalonia.Controls.Window { Content = host, Width = 900, Height = 640 };
        SettingsProviderPopupLayers.Install(window);
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            harness.Main.OnMain(() => service.CheckNow());
            harness.Main.RunUntilIdle();
            Tick(harness, frames, 1000);
            var pill = LogicalQuery.SelfAndDescendants<UpdatePill>(view).Single();
            window.UpdateLayout();
            var pillBox = new Rect(pill.Button.TranslatePoint(default, window)!.Value, pill.Button.Bounds.Size);
            harness.Main.OnMain(() => pill.Button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)));
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Tick(harness, frames, 1000); // the card grows in from its tip (PopoverAppear): look at it once it has arrived
            window.UpdateLayout();
            var popup = Assert.Single(presenter.Popups);
            Assert.True(popup.IsOpen);
            Assert.Equal(PlacementMode.TopEdgeAlignedLeft, popup.Placement);
            var card = Assert.IsType<PopoverCard>(popup.Child);
            var cardBox = new Rect(card.TranslatePoint(default, window)!.Value, card.Bounds.Size);
            // the card keeps a few points round itself for its shadow, so its edge is that far from the pill's
            Assert.InRange(cardBox.Left, pillBox.Left - 12, pillBox.Left + 12);
            Assert.True(cardBox.Left >= 0, $"the card ({cardBox}) starts inside the window");
            Assert.True(cardBox.Right <= window.Width, $"the card ({cardBox}) ends inside the window");
            Assert.True(cardBox.Bottom <= pillBox.Top + 4, $"the card ({cardBox}) is above the pill ({pillBox})");
            Assert.True(cardBox.Width > pillBox.Width * 2, "the card is the wide list, not the pill's width");
            harness.Main.OnMain(() => popup.IsOpen = false);
        }
        finally { harness.Main.OnMain(() => window.Close()); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    [Trait("Decision", "ED-308")] // OD-0076: the card does not jump when the banner arrives
    public void The_failure_banner_folds_its_height_in_frame_by_frame_and_the_card_follows_without_a_jump() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        using var harness = new AppModelHarness();
        var codex = Cli("x", "Codex", "0.157.1", "0.160.0");
        codex.Fails = true;
        var service = harness.Main.OnMain(() => new UpdateEverythingService([codex], () => { }));
        using var shared = UpdateEverythingService.OverrideShared(service);
        var model = harness.MakeModel();
        harness.Main.OnMain(() => service.CheckNow());
        harness.Main.RunUntilIdle();
        var pillHost = new Border();
        AppEnvironment.SetAppModel(pillHost, model);
        var pill = new UpdatePill(service);
        pillHost.Child = pill;
        var content = pill.BuildPopover();
        var inner = new Border { Child = content };
        AppEnvironment.SetAppModel(inner, model);
        var window = new Avalonia.Controls.Window { Content = inner, Width = 520, Height = 700 };
        try
        {
            window.Show();
            window.UpdateLayout();
            var before = content.DesiredSize.Height;
            harness.Main.OnMain(() => service.Perform(UpdatePillAction.UpdateEverything));
            harness.Main.RunUntilIdle();
            window.UpdateLayout();
            var heights = new List<double> { content.DesiredSize.Height };
            for (var step = 0; step < 30; step++)
            {
                Tick(harness, frames, 16);
                window.UpdateLayout();
                heights.Add(content.DesiredSize.Height);
            }
            var after = heights[^1];
            var delta = after - before;
            Assert.True(delta > 30, $"the banner added {delta} pt ({before} to {after})");
            Assert.NotNull(content.Banner);
            Assert.Equal(1, content.BannerFold, 3);
            var largestStep = heights.Zip(heights.Skip(1), (a, b) => b - a).Max();
            Assert.True(largestStep < delta * 0.5, $"no frame takes half the growth at once (largest step {largestStep} of {delta}): {string.Join(", ", heights.Select(h => Math.Round(h, 1)))}");
            Assert.Equal(heights.Order(), heights); // it only grows
            Assert.True(heights.Count(h => h > before + 1 && h < after - 1) >= 4, "it passes through several frames part way");
            Assert.True(heights[0] < before + delta * 0.5, $"the first frame does not already hold the banner ({heights[0]} of {before} to {after})");
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    [Trait("Decision", "ED-308")]
    public void The_done_phase_stays_until_the_popover_has_been_opened_and_closed() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        using var presenter = Presentation.Presenter.Override(new RecordingPresenter());
        using var harness = new AppModelHarness();
        var service = harness.Main.OnMain(() => new UpdateEverythingService([Cli("c", "Claude Code")], () => { }));
        using var shared = UpdateEverythingService.OverrideShared(service);
        var model = harness.MakeModel();
        SidebarView view = null!;
        try
        {
            harness.Main.OnMain(() => view = Attached(model));
            harness.Main.OnMain(() => service.CheckNow());
            harness.Main.OnMain(() => service.Perform(UpdatePillAction.UpdateEverything));
            harness.Main.RunUntilIdle();
            Tick(harness, frames, 1500);
            var pill = LogicalQuery.SelfAndDescendants<UpdatePill>(view).Single();
            Assert.Equal("1 updated", pill.LabelText);
            Assert.True(pill.IsShown);
            Tick(harness, frames, 5000);
            Assert.True(pill.IsShown); // it does not time out
            harness.Main.OnMain(() => pill.Button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent))); // open
            harness.Main.RunUntilIdle();
            Assert.True(pill.IsShown);
            harness.Main.OnMain(() => pill.Button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent))); // close: seen
            harness.Main.RunUntilIdle();
            Tick(harness, frames, 1500);
            Assert.False(pill.IsShown);
            Assert.Equal(0, pill.Revealed);
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });
}
