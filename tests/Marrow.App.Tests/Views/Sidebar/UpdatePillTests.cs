using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Port.Updates;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Sidebar;
using Marrow.Core.Port;
using Marrow.Core.Port.Updates;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using static Marrow.App.Tests.Views.Sidebar.SidebarViewTests;

namespace Marrow.App.Tests.Views.Sidebar;

// UE-5 (BUILD-PLAN.md 2.4 UE-5; OD-0100 pick: place D, "a glass pill above Settings in the main window, shown only when something is out of date, with the run's progress ring and a
// popover list", words U1; OD-0076: every change is transitioned; ED-320: the app's GlassMaterial). The service is given fake items (nothing runs, nothing is reached) and the frame
// clock is a manual one, so each frame of every transition can be looked at. The existing UpdateAvailableDot tests (SidebarLeftoversTests, SidebarRowUpdateDotReviewTests) are not changed.
[Collection("Headless")]
public sealed class UpdatePillTests(HeadlessSession headless)
{
    // These are headless UI tests: make the OS animation preference an explicit input, as SidebarViewTests does.
    // No assertion changes, and no SystemParametersInfo call is needed on the test host.
    private void OnUiThread(Action body) => headless.OnUiThread(() =>
    {
        using var preference = Chrome.ReduceMotionPlatform.Override(new RenderingGeniePlatform());
        body();
    });

    private static FakeUpdateItem Cli(string id, string title, string installed = "1.0.0", string? latest = "1.1.0") => new(id, title, UpdateItemKind.ProviderCli, installed, latest);

    // Layout is run before the pill is looked at: geometry is read, and the headless window lays out on its own schedule.
    private static UpdatePill PillOf(SidebarView view)
    {
        (TopLevel.GetTopLevel(view) as Avalonia.Controls.Window)?.UpdateLayout();
        return LogicalQuery.SelfAndDescendants<UpdatePill>(view).Single();
    }

    private static double ShownOpacity(Visual visual) => visual.GetSelfAndVisualAncestors().OfType<Visual>().Aggregate(1d, (o, v) => o * v.Opacity);

    private static UpdatePopoverContent Hosted(AppModel model, UpdatePill pill)
    {
        var content = pill.BuildPopover();
        var host = new Border { Child = content };
        AppEnvironment.SetAppModel(host, model);
        var window = new Avalonia.Controls.Window { Content = host, Width = 520, Height = 600 };
        window.Show();
        window.UpdateLayout();
        return content;
    }

    private static void Tick(AppModelHarness harness, ManualFrameSource frames, int ms)
    {
        harness.Main.AdvanceMilliseconds(ms);
        harness.Main.OnMain(() => frames.Advance(TimeSpan.FromMilliseconds(ms)));
    }

    private static void Check(AppModelHarness harness, UpdateEverythingService service)
    {
        harness.Main.OnMain(() => service.CheckNow());
        harness.Main.RunUntilIdle();
    }

    [Fact]
    [Trait("Decision", "ED-308")]
    public void While_nothing_is_out_of_date_the_sidebar_has_a_pill_slot_with_no_height_and_nothing_to_press() => OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness();
        var service = harness.Main.OnMain(() => new UpdateEverythingService([Cli("c", "Claude Code", "1.1.0", "1.1.0")], () => { }));
        using var shared = UpdateEverythingService.OverrideShared(service);
        var model = harness.MakeModel();
        SidebarView view = null!;
        try
        {
            harness.Main.OnMain(() => view = Attached(model));
            Check(harness, service);
            harness.Main.OnMain(() =>
            {
                var pill = PillOf(view);
                Assert.False(pill.IsShown);
                Assert.Equal(0, pill.Bounds.Height);
                Assert.False(pill.Button.IsHitTestVisible);
                Assert.False(pill.Button.Focusable);
                Assert.Equal(0, pill.Revealed);
            });
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    [Fact]
    [Trait("Decision", "ED-308")]
    public void An_out_of_date_item_brings_the_pill_in_above_the_Add_project_and_Settings_rows_left_aligned_with_the_list() => OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        using var harness = new AppModelHarness();
        var service = harness.Main.OnMain(() => new UpdateEverythingService([Cli("c", "Claude Code"), Cli("x", "Codex", "0.157.1", "0.160.0")], () => { }));
        using var shared = UpdateEverythingService.OverrideShared(service);
        var model = harness.MakeModel();
        SidebarView view = null!;
        try
        {
            harness.Main.OnMain(() => view = Attached(model));
            Check(harness, service);
            Tick(harness, frames, 1000);
            harness.Main.OnMain(() =>
            {
                var pill = PillOf(view);
                Assert.True(pill.IsShown);
                Assert.Equal("2 updates", pill.LabelText);
                var pillFrame = Frame(pill.Button, view);
                var addProject = Frame(RowNamed(view, "Add project"), view);
                var settings = Frame(RowNamed(view, "Settings"), view);
                Assert.True(pillFrame.Bottom <= addProject.Top + 0.5, $"the pill ({pillFrame}) is above Add project ({addProject})");
                Assert.True(addProject.Bottom <= settings.Top + 1.5);
                Assert.Equal(Chrome.ListInset, pillFrame.Left, 0.5);
                Assert.Equal(UpdatePill.PillHeight, pillFrame.Height, 0.5);
                Assert.Equal(1, pill.Revealed);
                Assert.Equal("2 updates", AutomationProperties.GetName(pill.Button));
            });
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    [Theory]
    [Trait("Decision", "ED-308")] // OD-0076: nothing cuts
    [InlineData(false)]
    [InlineData(true)]
    public void The_pill_arrives_and_leaves_by_easing_and_a_reduced_motion_setting_stills_it(bool reduceMotion) => OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        using var reduction = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = reduceMotion });
        using var harness = new AppModelHarness();
        var item = Cli("c", "Claude Code");
        var service = harness.Main.OnMain(() => new UpdateEverythingService([item], () => { }));
        using var shared = UpdateEverythingService.OverrideShared(service);
        var model = harness.MakeModel();
        SidebarView view = null!;
        try
        {
            harness.Main.OnMain(() => view = Attached(model));
            Check(harness, service);
            Tick(harness, frames, 16);
            if (reduceMotion)
            {
                harness.Main.OnMain(() => Assert.Equal(1, PillOf(view).Revealed)); // stilled: taken at once
                return;
            }
            var heights = new List<double>();
            var opacities = new List<double>();
            harness.Main.OnMain(() => { var pill = PillOf(view); heights.Add(pill.Bounds.Height); opacities.Add(ShownOpacity(pill.Button)); });
            for (var step = 0; step < 6; step++)
            {
                Tick(harness, frames, 16);
                harness.Main.OnMain(() => { var pill = PillOf(view); heights.Add(pill.Bounds.Height); opacities.Add(ShownOpacity(pill.Button)); });
            }
            Assert.Contains(heights, h => h > 0 && h < UpdatePill.PillHeight + UpdatePill.GapBelow - 1); // part way: the height eased, it did not jump
            Assert.Contains(opacities, o => o > 0 && o < 1);
            Tick(harness, frames, 1500);
            harness.Main.OnMain(() =>
            {
                Assert.Equal(1, PillOf(view).Revealed);
                Assert.Equal(UpdatePill.PillHeight + UpdatePill.GapBelow, PillOf(view).Bounds.Height, 0.5);
            });

            // the pill leaves the same way once nothing is out of date: the item catches up
            item.Latest = "1.0.0";
            Check(harness, service);
            var leaving = new List<double>();
            for (var step = 0; step < 6; step++)
            {
                Tick(harness, frames, 16);
                harness.Main.OnMain(() => leaving.Add(PillOf(view).Bounds.Height));
            }
            Assert.Contains(leaving, h => h > 0 && h < UpdatePill.PillHeight + UpdatePill.GapBelow - 1);
            Tick(harness, frames, 1500);
            harness.Main.OnMain(() =>
            {
                Assert.Equal(0, PillOf(view).Bounds.Height);
                Assert.False(PillOf(view).Button.IsHitTestVisible);
            });
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    [Fact]
    [Trait("Decision", "ED-320")] // the app's GlassMaterial
    public void The_pill_is_the_apps_glass_capsule() => OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness();
        var service = harness.Main.OnMain(() => new UpdateEverythingService([Cli("c", "Claude Code")], () => { }));
        using var shared = UpdateEverythingService.OverrideShared(service);
        var model = harness.MakeModel();
        SidebarView view = null!;
        try
        {
            harness.Main.OnMain(() => view = Attached(model));
            Check(harness, service);
            harness.Main.OnMain(() =>
            {
                var pill = PillOf(view);
                var glass = LogicalQuery.SelfAndDescendants<Border>(pill).Select(b => SwiftGlass.GetEffect(b)).Where(e => e is not null).ToArray();
                var effect = Assert.Single(glass)!;
                Assert.Equal(GlassShapeKind.Capsule, effect.Shape.Kind);
                Assert.True(effect.IsInteractive);
            });
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    [Fact]
    [Trait("Decision", "ED-308")]
    public void The_ring_and_the_words_follow_the_run_one_item_at_a_time() => OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        using var harness = new AppModelHarness();
        var gate = new TaskCompletionSource();
        var claude = Cli("c", "Claude Code");
        var codex = Cli("x", "Codex", "0.157.1", "0.160.0");
        codex.Gate = gate.Task;
        codex.Steps = [0.5];
        var service = harness.Main.OnMain(() => new UpdateEverythingService([claude, codex], () => { }));
        using var shared = UpdateEverythingService.OverrideShared(service);
        var model = harness.MakeModel();
        SidebarView view = null!;
        try
        {
            harness.Main.OnMain(() => view = Attached(model));
            Check(harness, service);
            harness.Main.OnMain(() => service.Perform(UpdatePillAction.UpdateEverything));
            harness.Main.RunUntilIdle();
            Tick(harness, frames, 1500);
            harness.Main.OnMain(() =>
            {
                var pill = PillOf(view);
                Assert.Equal("Updating 2 of 2", pill.LabelText);
                Assert.Equal((1 + 0.5) / 2, pill.Ring.Target, 6); // one item done, the other half through
                Assert.Equal(1, pill.LeadOpacity(UpdatePillPhase.Running), 3);
                Assert.Equal(0, pill.LeadOpacity(UpdatePillPhase.Available), 3);
            });
            gate.SetResult();
            harness.Main.RunUntilIdle();
            Tick(harness, frames, 1500);
            harness.Main.OnMain(() =>
            {
                Assert.True(PillOf(view).IsShown); // nothing to restart: the design's done phase stays until it has been seen
                Assert.Equal("2 updated", PillOf(view).LabelText);
                Assert.Equal(1, PillOf(view).LeadOpacity(UpdatePillPhase.Done), 3);
            });
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    [Fact]
    [Trait("Decision", "ED-308")] // OD-0076
    public void A_change_of_words_crossfades_and_the_capsule_eases_to_its_new_width() => OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        using var harness = new AppModelHarness();
        var gate = new TaskCompletionSource();
        var claude = Cli("c", "Claude Code");
        claude.Gate = gate.Task;
        var service = harness.Main.OnMain(() => new UpdateEverythingService([claude], () => { }));
        using var shared = UpdateEverythingService.OverrideShared(service);
        var model = harness.MakeModel();
        SidebarView view = null!;
        try
        {
            harness.Main.OnMain(() => view = Attached(model));
            Check(harness, service);
            Tick(harness, frames, 1500);
            double before = 0;
            harness.Main.OnMain(() => before = PillOf(view).Button.Bounds.Width);
            harness.Main.OnMain(() => service.Perform(UpdatePillAction.UpdateEverything));
            harness.Main.RunUntilIdle();
            harness.Main.OnMain(() => PillOf(view)); // the layout pass that sees the new words and starts the width's ease
            Tick(harness, frames, 40);
            harness.Main.OnMain(() =>
            {
                var pill = PillOf(view);
                Assert.Equal("Updating 1 of 1", pill.LabelText);
                var layers = LogicalQuery.SelfAndDescendants<TextBlock>(pill.Button).Select(t => (t.Text, t.Opacity)).ToArray();
                Assert.Contains(layers, l => l.Text == "1 update" && l.Opacity is > 0 and < 1); // the old words still fading out
                Assert.Contains(layers, l => l.Text == "Updating 1 of 1" && l.Opacity is > 0 and < 1); // the new ones fading in
                var width = pill.Button.Bounds.Width;
                Assert.True(width > before && width < before + 200, $"the width {width} is on its way from {before}");
            });
            Tick(harness, frames, 600);
            double after = 0;
            harness.Main.OnMain(() => after = PillOf(view).Button.Bounds.Width);
            Assert.True(after > before + 20);
            Tick(harness, frames, 5);
            harness.Main.OnMain(() => Assert.Equal(after, PillOf(view).Button.Bounds.Width, 0.5)); // and it rests
            gate.SetResult();
            harness.Main.RunUntilIdle();
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    [Fact]
    [Trait("Decision", "ED-308")]
    public void A_press_presents_the_popover_on_the_pill_and_a_second_press_dismisses_it() => OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var recorder = new RecordingPresenter();
        using var presenter = Presentation.Presenter.Override(recorder);
        using var harness = new AppModelHarness();
        var service = harness.Main.OnMain(() => new UpdateEverythingService([Cli("c", "Claude Code")], () => { }));
        using var shared = UpdateEverythingService.OverrideShared(service);
        var model = harness.MakeModel();
        SidebarView view = null!;
        try
        {
            harness.Main.OnMain(() => view = Attached(model));
            Check(harness, service);
            harness.Main.OnMain(() => PillOf(view).Button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)));
            harness.Main.RunUntilIdle();
            var shown = Assert.Single(recorder.Calls, c => c.Presented);
            Assert.Same(PillOf(view).Popover, shown.Popup);
            Assert.Same(PillOf(view).Button, shown.Popup.PlacementTarget);
            Assert.Equal(PlacementMode.TopEdgeAlignedLeft, shown.Popup.Placement); // the card's left edge on the pill's, as the page draws it (.dpop left 14 px)
            Assert.True(shown.Popup.IsLightDismissEnabled);
            harness.Main.OnMain(() => PillOf(view).Button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)));
            harness.Main.RunUntilIdle();
            Assert.Contains(recorder.Calls, c => !c.Presented);
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    [Fact]
    [Trait("Decision", "ED-308")]
    public void The_popover_closes_when_the_pill_goes() => OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var recorder = new RecordingPresenter();
        using var presenter = Presentation.Presenter.Override(recorder);
        using var harness = new AppModelHarness();
        var item = Cli("c", "Claude Code");
        var service = harness.Main.OnMain(() => new UpdateEverythingService([item], () => { }));
        using var shared = UpdateEverythingService.OverrideShared(service);
        var model = harness.MakeModel();
        SidebarView view = null!;
        try
        {
            harness.Main.OnMain(() => view = Attached(model));
            Check(harness, service);
            harness.Main.OnMain(() => PillOf(view).Button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)));
            harness.Main.RunUntilIdle();
            Assert.True(harness.Main.OnMain(() => PillOf(view).IsPresented));
            item.Latest = "1.0.0";
            Check(harness, service);
            Assert.False(harness.Main.OnMain(() => PillOf(view).IsPresented));
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    [Fact]
    [Trait("Decision", "ED-308")]
    public void The_popover_lists_the_installed_items_with_their_versions_and_the_one_button() => OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness();
        IUpdateItem[] items =
        [
            Cli("c", "Claude Code", "2.1.283", "2.1.290"), Cli("x", "Codex", "0.157.1", "0.160.0"), Cli("o", "OpenCode", "1.4.2", "1.4.2"), Cli("g", "Gone", "1.0", "1.0"),
            new FakeUpdateItem("m", "nth", UpdateItemKind.Marrow, "1.0.0", "1.1.0"),
        ];
        var service = harness.Main.OnMain(() => new UpdateEverythingService(items, () => { }));
        using var shared = UpdateEverythingService.OverrideShared(service);
        var model = harness.MakeModel();
        SidebarView view = null!;
        try
        {
            harness.Main.OnMain(() => view = Attached(model));
            Check(harness, service);
            harness.Main.OnMain(() =>
            {
                var content = Hosted(model, PillOf(view));
                var texts = LogicalQuery.SelfAndDescendants<TextBlock>(content).Select(t => t.Text).ToArray();
                Assert.Contains("3 updates available", texts);
                Assert.Contains("Claude Code 2.1.290, Codex 0.160.0 and nth 1.1.0.", texts);
                var rows = content.RowViews.ToArray();
                Assert.Equal(["nth", "Claude Code", "Codex", "OpenCode", "Gone"], rows.Select(r => r.NameText)); // Marrow first, as the page lists it
                Assert.Equal(["1.0.0 → 1.1.0", "2.1.283 → 2.1.290", "0.157.1 → 0.160.0", "1.4.2", "1.0"], rows.Select(r => r.VersionsText));
                Assert.Equal(["Update available", "Update available", "Update available", "Up to date", "Up to date"], rows.Select(r => r.StatusText));
                Assert.Equal(["The app", "", "", "", ""], rows.Select(r => r.SubText)); // ids that are not a provider CLI's have no command to show
                var button = LogicalQuery.SelfAndDescendants<Button>(content.ActionControl!).Single();
                Assert.Equal("Update everything", AutomationProperties.GetName(button));
                Assert.Null(content.Banner);
                Close(content);
            });
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    [Fact]
    [Trait("Decision", "ED-308")]
    public void The_popovers_button_runs_Update_everything_and_the_rows_follow_the_run_with_a_bar_on_the_one_in_hand() => OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        using var harness = new AppModelHarness();
        var gate = new TaskCompletionSource();
        var claude = Cli("c", "Claude Code");
        var codex = Cli("x", "Codex", "0.157.1", "0.160.0");
        codex.Gate = gate.Task;
        codex.Steps = [0.4];
        var service = harness.Main.OnMain(() => new UpdateEverythingService([claude, codex], () => { }));
        using var shared = UpdateEverythingService.OverrideShared(service);
        var model = harness.MakeModel();
        SidebarView view = null!;
        try
        {
            harness.Main.OnMain(() => view = Attached(model));
            Check(harness, service);
            UpdatePopoverContent content = null!;
            harness.Main.OnMain(() =>
            {
                content = Hosted(model, PillOf(view));
                var button = LogicalQuery.SelfAndDescendants<Button>(content.ActionControl!).Single();
                button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            });
            harness.Main.RunUntilIdle();
            Tick(harness, frames, 1500);
            harness.Main.OnMain(() =>
            {
                var rows = content.RowViews.ToDictionary(r => r.Id);
                Assert.Equal("Updated", rows["c"].StatusText);
                Assert.Equal("Updating…", rows["x"].StatusText);
                Assert.True(rows["x"].BarOpen);
                Assert.False(rows["c"].BarOpen);
                Assert.Equal(0.4, rows["x"].Bar.Target);
                var button = LogicalQuery.SelfAndDescendants<Button>(content.ActionControl!).Single();
                Assert.Equal("Updating…", AutomationProperties.GetName(button));
                Assert.False(button.IsEnabled);
                Assert.Contains("Updating Codex… 2 of 2", LogicalQuery.SelfAndDescendants<TextBlock>(content).Select(t => t.Text));
            });
            gate.SetResult();
            harness.Main.RunUntilIdle();
            Tick(harness, frames, 1500);
            harness.Main.OnMain(() =>
            {
                Assert.True(PillOf(view).IsShown); // the done phase stays: "N updated" in the pill and in the card
                Assert.Equal("2 updated", PillOf(view).LabelText);
                Assert.Contains("2 updated", LogicalQuery.SelfAndDescendants<TextBlock>(content).Where(t => t.Opacity > 0.5).Select(t => t.Text));
                Assert.Contains("Updated Claude Code and Codex.", LogicalQuery.SelfAndDescendants<TextBlock>(content).Where(t => t.Opacity > 0.5).Select(t => t.Text));
                Assert.Equal("Updated", content.RowViews.Single(r => r.Id == "x").StatusText);
                Close(content);
            });
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    [Fact]
    [Trait("Decision", "ED-308")]
    public void A_failed_item_puts_a_banner_in_the_popover_and_Retry_in_its_button() => OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        using var harness = new AppModelHarness();
        var codex = Cli("x", "Codex", "0.157.1", "0.160.0");
        codex.Fails = true;
        var service = harness.Main.OnMain(() => new UpdateEverythingService([Cli("c", "Claude Code"), codex], () => { }));
        using var shared = UpdateEverythingService.OverrideShared(service);
        var model = harness.MakeModel();
        SidebarView view = null!;
        try
        {
            harness.Main.OnMain(() => view = Attached(model));
            Check(harness, service);
            UpdatePopoverContent content = null!;
            harness.Main.OnMain(() =>
            {
                content = Hosted(model, PillOf(view));
                service.Perform(UpdatePillAction.UpdateEverything);
            });
            harness.Main.RunUntilIdle();
            Tick(harness, frames, 1500);
            harness.Main.OnMain(() =>
            {
                Assert.Equal("Codex didn’t update", PillOf(view).LabelText);
                Assert.NotNull(content.Banner);
                var texts = LogicalQuery.SelfAndDescendants<TextBlock>(content.Banner!).Where(t => t.Opacity > 0.5).Select(t => t.Text).ToArray();
                Assert.Contains("Codex didn’t update", texts);
                Assert.Contains("the updater failed", texts);
                Assert.Equal("Didn’t update", content.RowViews.Single(r => r.Id == "x").StatusText);
                var button = LogicalQuery.SelfAndDescendants<Button>(content.ActionControl!).Single();
                Assert.Equal("Retry", AutomationProperties.GetName(button));
                codex.Fails = false;
                button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            });
            harness.Main.RunUntilIdle();
            Tick(harness, frames, 1500);
            harness.Main.OnMain(() =>
            {
                Assert.Equal(2, codex.Updates);
                Assert.Equal("2 updated", PillOf(view).LabelText);
                Close(content);
            });
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    [Fact]
    [Trait("Decision", "ED-308")]
    public void When_Marrow_waits_for_a_restart_the_pill_and_the_popover_offer_Restart_Marrow() => OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var bound = FrameClock.Source.Override(frames);
        using var harness = new AppModelHarness();
        var restarts = 0;
        var marrow = new FakeUpdateItem("marrow", "nth", UpdateItemKind.Marrow, "1.0.0", "1.1.0") { NeedsRestart = true };
        var service = harness.Main.OnMain(() => new UpdateEverythingService([Cli("c", "Claude Code"), marrow], () => restarts++));
        using var shared = UpdateEverythingService.OverrideShared(service);
        var model = harness.MakeModel();
        SidebarView view = null!;
        try
        {
            harness.Main.OnMain(() => view = Attached(model));
            Check(harness, service);
            harness.Main.OnMain(() => service.Perform(UpdatePillAction.UpdateEverything));
            harness.Main.RunUntilIdle();
            Tick(harness, frames, 1500);
            UpdatePopoverContent content = null!;
            harness.Main.OnMain(() =>
            {
                var pill = PillOf(view);
                Assert.True(pill.IsShown);
                Assert.Equal("Restart to finish", pill.LabelText);
                Assert.Equal(1, pill.LeadOpacity(UpdatePillPhase.Restart), 3);
                content = Hosted(model, pill);
                Assert.Equal("Restart to finish", content.RowViews.Single(r => r.Id == "marrow").StatusText);
                var button = LogicalQuery.SelfAndDescendants<Button>(content.ActionControl!).Single();
                Assert.Equal("Restart nth", AutomationProperties.GetName(button));
                button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Close(content);
            });
            Assert.Equal(1, restarts);
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });

    [Fact]
    [Trait("Decision", "ED-308")]
    public void The_floating_sidebars_pill_builds_its_popover_after_the_one_it_is_in_has_opened() => OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var harness = new AppModelHarness();
        var service = harness.Main.OnMain(() => new UpdateEverythingService([Cli("c", "Claude Code")], () => { }));
        using var shared = UpdateEverythingService.OverrideShared(service);
        var model = harness.MakeModel();
        SidebarView view = null!;
        try
        {
            harness.Main.OnMain(() => view = Attached(model, inPopover: true));
            Check(harness, service);
            harness.Main.OnMain(() =>
            {
                var pill = PillOf(view);
                Assert.True(pill.IsShown);
                Assert.Null(pill.Popover.Child); // nothing is built until it is asked for
            });
        }
        finally { harness.Main.OnMain(() => Close(view)); }
    });
}
