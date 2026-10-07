using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App;
using Marrow.App.Port;
using Marrow.App.Window;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Marrow.App.Views.Usage;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.App.Views.Diff;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;
using static Marrow.App.Tests.Views.Composer.ComposerViewBlitzControlsTests;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/ComposerView.swift (B3 52bc792), :4-112, :139-273, :275-533, :540-607,
// :1037-1101, :1103-1288, :1290-1307; DroppyCode/Core/Models/Provider.swift (B3 52bc792), :306-323. Part 1 of 5 of
// App.Views.Composer.ComposerView: the area, the tab slot, the stats refresh, the pill's layout, placeholder, keys,
// send and recall, the text column and the surface. Part 2 of 5: steer, the caret's suggestion refresh, the file and
// command lists, pick, the panel, command loading, focus and visibility wirings, the file index and attach.
//
// Two runtimes are used. The harness's own runtime has a thread the model resolves; the pill's controls
// (ComposerControls, part 3 of this unit) are built for a resolved thread, so a pill with such a runtime is only
// exercised unattached (its keys through the text view's own key path, its placeholder by calling it). A runtime made
// for a thread id the model does not hold resolves no thread, so the pill leaves its controls out
// (ComposerView.swift:160) and the whole area can be attached and laid out.

[Collection("Headless")]
public sealed partial class ComposerViewTests(HeadlessSession headless)
{
    private const string Plan = "Describe what you want to plan";

    internal static ThreadRuntimePart12Harness RuntimeHarness()
    {
        // FollowUpEditorTests' note: LoginEnvironment.Load is completed once on a pool thread first, so the harness's
        // own Load().Wait() on the headless UI thread returns at once instead of deadlocking.
        System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        var h = new ThreadRuntimePart12Harness();
        // These tests need a stored follow-up, not a Hydra head dispatched from the queue (ThreadRuntime.swift:999-1013; Swift
        // WebsiteCaptures.swift:107 turns hydraQueueHeads off for the same reason).
        h.Main.OnMain(() =>
        {
            h.App.Settings.HydraQueueHeads = false;
            h.App.Settings.HydraAlwaysHeads = false;
        });
        Loaded(h, h.Runtime);
        return h;
    }

    // The runtime installs its thread document when the load lands (ThreadRuntime.swift:539-554), replacing turns and
    // entries, so a test waits for it before rehearsing anything.
    internal static void Loaded(ThreadRuntimePart12Harness h, ThreadRuntime runtime) =>
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);

    internal static ThreadRuntime Unresolved(ThreadRuntimePart12Harness h)
    {
        var runtime = h.Main.OnMain(() => new ThreadRuntime(Guid.NewGuid(), h.App));
        Loaded(h, runtime);
        return runtime;
    }

    internal static AvaloniaWindow Show(Control content, AppModel app, double width)
    {
        var host = new Border
        {
            Child = content,
            Width = width,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        AppEnvironment.SetAppModel(host, app);
        var window = new AvaloniaWindow { Content = host, Width = 1400, Height = 900 };
        window.Show();
        Settle(window);
        return window;
    }

    internal static Border Host(AvaloniaWindow window) => (Border)window.Content!;

    internal static void Settle(AvaloniaWindow window)
    {
        for (var pass = 0; pass < 5; pass++)
        {
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
    }

    // Track reactions land on the next main turn; layout and the text box's posted jobs run on the dispatcher.
    internal static void Pump(ThreadRuntimePart12Harness h, AvaloniaWindow window)
    {
        for (var round = 0; round < 4; round++) h.Main.OnMain(() => Settle(window));
    }

    internal static void Close(ThreadRuntimePart12Harness h, AvaloniaWindow window) => h.Main.OnMain(() =>
    {
        Settle(window);
        window.Close();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    });

    internal static Rect Frame(Visual control, Visual relativeTo) =>
        new(control.TranslatePoint(default, relativeTo)!.Value, control.Bounds.Size);

    internal static T One<T>(ILogical root) where T : class => LogicalQuery.SelfAndDescendants<T>(root).Single();

    internal static IEnumerable<ILogical> Ancestors(ILogical control)
    {
        for (var parent = control.LogicalParent; parent is not null; parent = parent.LogicalParent) yield return parent;
    }

    // The VStack(alignment: .center, spacing: -ThreadChangesTab.overlap) holding the tab slot and the pill (:28-31).
    internal static StackPanel Group(ComposerArea area) => (StackPanel)One<ComposerView>(area).Parent!;

    // The VStack(spacing: 12) holding the approval cards and that group (:24-50).
    internal static StackPanel Stack(ComposerArea area) =>
        Ancestors(Group(area)).OfType<StackPanel>().First(panel => panel.Spacing == 12);

    private static string Patch(string path) =>
        "diff --git a/" + path + " b/" + path + "\nnew file mode 100644\n--- /dev/null\n+++ b/" + path + "\n@@ -0,0 +1 @@\n+new\n";

    // A turn whose provider patch adds one file, then the stats the changes tab shows (ThreadRuntime.swift:1945-1953).
    internal static void GiveStats(ThreadRuntimePart12Harness h, ThreadRuntime runtime, string path = "src/new.txt")
    {
        h.Main.OnMain(() => runtime.RehearseTurn("edit " + path, providerDiff: Patch(path)));
        h.Main.OnMain(runtime.NoteDiffChanged);
        h.Main.Loop.Run(() => runtime.RefreshChangeStats(), AppModelHarness.Bound);
        Assert.NotNull(runtime.ChangeStats);
    }

    internal static bool Press(ComposerView view, Key key)
    {
        var textView = One<ComposerNSTextView>(view);
        var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key };
        textView.RaiseEvent(args);
        return args.Handled;
    }

    internal static ComposerView Pill(ThreadRuntimePart12Harness h, ThreadRuntime runtime) => h.Main.OnMain(() =>
    {
        var view = new ComposerView(runtime, workingDirectory: null);
        AppEnvironment.SetAppModel(view, h.App);
        Drain();
        return view;
    });

    // The text box raises TextChanged from a posted dispatcher job (FollowUpEditorTests' note); that reaches the draft
    // through the text binding, so it must run on the main actor, inside h.Main.OnMain.
    internal static void Drain() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    // ---- ComposerArea (:4-58) ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-001")] // App/Views/Composer/ComposerView.swift:52-55
    [Trait("Req", "UI-COMPOSER-001")] // App/Views/Composer/ComposerView.swift:23-55
    public void The_area_caps_its_content_at_820_wide_20_points_in_from_each_side_14_up_from_the_bottom_and_centred()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = Unresolved(h);
            var area = h.Main.OnMain(() => new ComposerArea(runtime, workingDirectory: null));
            var window = h.Main.OnMain(() => Show(area, h.App, width: 1200));
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                Assert.Equal(1200, area.Bounds.Width);
                var content = Frame(Stack(area), area);
                Assert.Equal(820, content.Width);
                Assert.Equal((1200 - 820) / 2.0, content.X);
                Assert.Equal(0, content.Y);
                Assert.Equal(area.Bounds.Height - 14, content.Bottom);
            });
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-001")] // App/Views/Composer/ComposerView.swift:52-55
    public void Below_820_plus_the_padding_the_content_simply_takes_the_width_left_inside_the_20_point_sides()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = Unresolved(h);
            var area = h.Main.OnMain(() => new ComposerArea(runtime, workingDirectory: null));
            var window = h.Main.OnMain(() => Show(area, h.App, width: 600));
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                var content = Frame(Stack(area), area);
                Assert.Equal(560, content.Width);
                Assert.Equal(20, content.X);
            });
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-002")] // App/Views/Composer/ComposerView.swift:23-31
    public void Without_approvals_the_12_point_stack_holds_only_the_tab_and_pill_group_and_adds_no_spacing()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = Unresolved(h);
            var area = h.Main.OnMain(() => new ComposerArea(runtime, workingDirectory: null));
            var window = h.Main.OnMain(() => Show(area, h.App, width: 900));
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                Assert.Empty(runtime.Approvals);
                var stack = Stack(area);
                Assert.Equal(12, stack.Spacing);
                var only = Assert.Single(stack.Children);
                Assert.Contains(Group(area), LogicalQuery.SelfAndDescendants<StackPanel>(only));
                Assert.Equal(0, Frame(only, stack).Y);
                Assert.Equal(stack.Bounds.Height, only.Bounds.Height);
            });
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-004")] // App/Views/Composer/ComposerView.swift:69-97
    public void With_nothing_queued_and_no_stats_no_slot_exists_so_the_pill_starts_at_the_top_of_its_group()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = Unresolved(h);
            var area = h.Main.OnMain(() => new ComposerArea(runtime, workingDirectory: null));
            var window = h.Main.OnMain(() => Show(area, h.App, width: 900));
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                var slot = One<TabSlot>(area);
                Assert.False(slot.IsVisible);
                Assert.Empty(LogicalQuery.SelfAndDescendants<FollowUpQueueTab>(area));
                Assert.Empty(LogicalQuery.SelfAndDescendants<ThreadChangesTab>(area));
                var group = Group(area);
                Assert.Equal(0, Frame(One<ComposerView>(area), group).Y);
                Assert.Equal(group.Bounds.Height, One<ComposerView>(area).Bounds.Height);
            });
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-004")] // App/Views/Composer/ComposerView.swift:75-81
    public void A_queued_follow_up_puts_the_queue_tab_in_the_slot()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = Unresolved(h);
            var area = h.Main.OnMain(() => new ComposerArea(runtime, workingDirectory: null));
            var window = h.Main.OnMain(() => Show(area, h.App, width: 900));
            // A prompt stays queued behind a running turn; an idle lead would take it out of the queue at once
            // (ThreadRuntime.swift:1003-1007, :1054-1061). The running pill's working dots draw on the frame clock.
            using var frameScope = FrameClock.Source.Override(new ManualFrameSource());
            h.Main.OnMain(() =>
            {
                runtime.RehearseTurn(null);
                runtime.EnqueueFollowUp("queued prompt", []);
            });
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                var slot = One<TabSlot>(area);
                Assert.True(slot.IsVisible);
                Assert.Single(LogicalQuery.SelfAndDescendants<FollowUpQueueTab>(slot));
                Assert.Empty(LogicalQuery.SelfAndDescendants<ThreadChangesTab>(slot));
            });
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-004")] // App/Views/Composer/ComposerView.swift:75-95
    public void With_nothing_queued_the_changes_tab_takes_the_slot_once_there_are_stats()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = h.Runtime;
            var slot = h.Main.OnMain(() => new TabSlot(runtime, new DiffPopoverCoordinator(), showsChanges: true));
            var window = h.Main.OnMain(() => Show(slot, h.App, width: 700));
            Pump(h, window);
            h.Main.OnMain(() => Assert.False(slot.IsVisible));
            GiveStats(h, runtime);
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                Assert.True(slot.IsVisible);
                Assert.Single(LogicalQuery.SelfAndDescendants<ThreadChangesTab>(slot));
                Assert.Empty(LogicalQuery.SelfAndDescendants<FollowUpQueueTab>(slot));
            });
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-004")] // App/Views/Composer/ComposerView.swift:77-82
    public void While_any_follow_up_is_queued_the_queue_tab_holds_the_slot_and_the_changes_tab_stays_hidden()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = h.Runtime;
            GiveStats(h, runtime);
            h.Main.OnMain(() => runtime.EnqueueFollowUp("queued prompt", []));
            var slot = h.Main.OnMain(() => new TabSlot(runtime, new DiffPopoverCoordinator(), showsChanges: true));
            var window = h.Main.OnMain(() => Show(slot, h.App, width: 700));
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                Assert.True(slot.IsVisible);
                Assert.Single(LogicalQuery.SelfAndDescendants<FollowUpQueueTab>(slot));
                Assert.Empty(LogicalQuery.SelfAndDescendants<ThreadChangesTab>(slot));
            });
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-004")] // App/Views/Composer/ComposerView.swift:75, :82
    public void A_box_that_does_not_show_changes_never_raises_the_changes_tab_whatever_the_stats()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = h.Runtime;
            GiveStats(h, runtime);
            var slot = h.Main.OnMain(() => new TabSlot(runtime, new DiffPopoverCoordinator(), showsChanges: false));
            var window = h.Main.OnMain(() => Show(slot, h.App, width: 700));
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                Assert.False(slot.IsVisible);
                Assert.Empty(LogicalQuery.SelfAndDescendants<ThreadChangesTab>(slot));
            });
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-005")] // App/Views/Composer/ComposerView.swift:76-96
    public void When_the_queue_empties_the_queue_tab_fades_out_where_it_stood_under_the_arriving_changes_tab()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var runtime = h.Runtime;
            GiveStats(h, runtime);
            h.Main.OnMain(() => runtime.EnqueueFollowUp("queued prompt", []));
            var slot = h.Main.OnMain(() => new TabSlot(runtime, new DiffPopoverCoordinator(), showsChanges: true));
            var window = h.Main.OnMain(() => Show(slot, h.App, width: 700));
            Pump(h, window);
            var queue = h.Main.OnMain(() => One<FollowUpQueueTab>(slot));
            h.Main.OnMain(() => runtime.RemoveFollowUp(runtime.FollowUps.Single().Id));
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                var changes = One<ThreadChangesTab>(slot);
                var stack = (Panel)Ancestors(changes).OfType<Panel>().First(panel => panel.Children.Count == 2);
                var leaving = stack.Children.Single(child => child == queue || LogicalQuery.SelfAndDescendants<FollowUpQueueTab>(child).Any());
                var arriving = stack.Children.Single(child => LogicalQuery.SelfAndDescendants<ThreadChangesTab>(child).Any());
                Assert.True(stack.Children.IndexOf(arriving) > stack.Children.IndexOf(leaving));
                Assert.Equal(VerticalAlignment.Bottom, leaving.VerticalAlignment);
                Assert.Equal(VerticalAlignment.Bottom, arriving.VerticalAlignment);
            });
            h.Main.Advance(TimeSpan.FromSeconds(0.06));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(0.06)));
            h.Main.OnMain(() =>
            {
                Assert.Single(LogicalQuery.SelfAndDescendants<FollowUpQueueTab>(slot));
                Assert.InRange(queue.Opacity, 0.01, 0.99);
            });
            h.Main.Advance(TimeSpan.FromSeconds(0.07));
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(0.07)));
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                Assert.Empty(LogicalQuery.SelfAndDescendants<FollowUpQueueTab>(slot));
                Assert.Single(LogicalQuery.SelfAndDescendants<ThreadChangesTab>(slot));
            });
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-006")] // App/Views/Composer/ComposerView.swift:28-31
    [Trait("Req", "UI-COMPOSER-001")] // App/Views/Composer/ComposerView.swift:28-31
    public void The_pill_overlaps_the_tab_by_14_points_through_negative_stack_spacing_and_draws_above_it()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = Unresolved(h);
            var area = h.Main.OnMain(() => new ComposerArea(runtime, workingDirectory: null));
            var window = h.Main.OnMain(() => Show(area, h.App, width: 900));
            // A prompt stays queued behind a running turn; an idle lead would take it out of the queue at once
            // (ThreadRuntime.swift:1003-1007, :1054-1061). The running pill's working dots draw on the frame clock.
            using var frameScope = FrameClock.Source.Override(new ManualFrameSource());
            h.Main.OnMain(() =>
            {
                runtime.RehearseTurn(null);
                runtime.EnqueueFollowUp("queued prompt", []);
            });
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                Assert.Equal(14, ThreadChangesTab.Overlap);
                var group = Group(area);
                Assert.Equal(-14, group.Spacing);
                var slot = One<TabSlot>(area);
                var pill = One<ComposerView>(area);
                Assert.True(group.Children.IndexOf(pill) > group.Children.IndexOf(slot));
                Assert.True(slot.Bounds.Height > 14);
                Assert.Equal(Frame(slot, group).Bottom - 14, Frame(pill, group).Y);
            });
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-007")] // App/Views/Composer/ComposerView.swift:104-112
    public void The_stats_refresh_is_a_zero_size_view_that_refreshes_on_appear_and_again_for_each_new_diff_revision()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = h.Runtime;
            h.Main.OnMain(() => runtime.RehearseTurn("first edit", providerDiff: Patch("a.txt")));
            var refresh = h.Main.OnMain(() => new ChangeStatsRefresh(runtime));
            var window = h.Main.OnMain(() => Show(refresh, h.App, width: 200));
            h.Main.Loop.RunUntil(() => runtime.ChangeStats is not null, AppModelHarness.Bound);
            Assert.Single(runtime.ChangeStats!.Files);
            h.Main.OnMain(() =>
            {
                Assert.Equal(0, refresh.Bounds.Width);
                Assert.Equal(0, refresh.Bounds.Height);
            });
            h.Main.OnMain(() => runtime.RehearseTurn("second edit", providerDiff: Patch("b.txt")));
            h.Main.OnMain(runtime.NoteDiffChanged);
            h.Main.Loop.RunUntil(() => runtime.ChangeStats?.Files.Count == 2, AppModelHarness.Bound);
            Assert.Equal(["a.txt", "b.txt"], runtime.ChangeStats!.Files.Select(file => file.Path).OrderBy(path => path, StringComparer.Ordinal).ToArray());
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-007")] // App/Views/Composer/ComposerView.swift:35-38
    public void Only_a_box_that_shows_changes_carries_the_stats_refresh()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = h.Runtime;
            h.Main.OnMain(() =>
            {
                var main = new ComposerArea(runtime, workingDirectory: null);
                Assert.Single(LogicalQuery.SelfAndDescendants<ChangeStatsRefresh>(main));
                var panel = new ComposerArea(runtime, workingDirectory: null, compactModelChip: true, takesFocusOnAppear: false, showsChanges: false);
                Assert.Empty(LogicalQuery.SelfAndDescendants<ChangeStatsRefresh>(panel));
                Drain();
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-008")] // App/Views/Composer/ComposerView.swift:39
    public void On_appear_the_popover_coordinator_closes_a_diff_left_visible_instead_of_opening_it_on_arrival()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = Unresolved(h);
            h.Main.OnMain(() => runtime.IsDiffVisible = true);
            var area = h.Main.OnMain(() => new ComposerArea(runtime, workingDirectory: null));
            var window = h.Main.OnMain(() => Show(area, h.App, width: 900));
            Pump(h, window);
            h.Main.OnMain(() => Assert.False(runtime.IsDiffVisible));
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-008")] // App/Views/Diff/DiffInspector.swift:521-523
    public void The_changes_popover_the_area_owns_is_640_by_540()
    {
        Assert.Equal(640, DiffPopoverCoordinator.Width);
        Assert.Equal(540, DiffPopoverCoordinator.Height);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-009")] // App/Views/Composer/ComposerView.swift:32-34,84-85; App/Views/Diff/DiffInspector.swift:581-586,614-625
    public void The_changes_popover_hangs_from_the_changes_tab_and_from_the_composer_area_while_the_queue_holds_the_slot()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var presenter = new RecordingPresenter();
            using var presentation = Presentation.Presenter.Override(presenter);
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var runtime = h.Runtime;
            GiveStats(h, runtime);
            var area = h.Main.OnMain(() => new ComposerArea(runtime, workingDirectory: null));
            var window = h.Main.OnMain(() => Show(area, h.App, width: 900));
            Pump(h, window);
            static bool Within<T>(Control view) where T : class =>
                Avalonia.VisualTree.VisualExtensions.GetSelfAndVisualAncestors(view).OfType<T>().Any();
            Control Shown() => (Control)presenter.Calls.Last(call => call.Presented).Popup.PlacementTarget!;

            // :84-85 the changes tab is in the slot: the popover hangs from it.
            h.Main.OnMain(() => runtime.IsDiffVisible = true);
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                Assert.Single(LogicalQuery.SelfAndDescendants<ThreadChangesTab>(area));
                Assert.True(Within<ThreadChangesTab>(Shown()), "the popover hangs from the changes tab");
                runtime.IsDiffVisible = false;
            });
            Pump(h, window);

            // :77-81 a queued prompt takes the slot: the popover falls back to the composer area's own rect (:32-34).
            h.Main.OnMain(() =>
            {
                runtime.RehearseTurn(null);
                runtime.EnqueueFollowUp("queued prompt", []);
            });
            Pump(h, window);
            h.Main.Advance(TimeSpan.FromSeconds(2)); // the changes tab's exit transition (.softAppear) runs out
            h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(2)));
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                Assert.Empty(LogicalQuery.SelfAndDescendants<ThreadChangesTab>(area));
                runtime.IsDiffVisible = true;
            });
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                var target = Shown();
                Assert.False(Within<ThreadChangesTab>(target));
                Assert.True(Within<ComposerArea>(target), "the fallback anchor is the composer area's own");
                Assert.IsType<AttachmentAnchorCapture>(target);
            });
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-010")] // App/Views/Composer/ComposerView.swift:86-93
    public void Clicking_the_changes_tab_clears_focus_anchor_and_selection_and_then_makes_the_diff_visible()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = h.Runtime;
            GiveStats(h, runtime);
            var slot = h.Main.OnMain(() => new TabSlot(runtime, new DiffPopoverCoordinator(), showsChanges: true));
            var window = h.Main.OnMain(() => Show(slot, h.App, width: 700));
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                runtime.ShowDiff(new Border(), runtime.Turns[0].Id, focusEdits: [new FileEdit("src/new.txt")]);
                runtime.IsDiffVisible = false;
            });
            h.Main.OnMain(() =>
            {
                Assert.NotEmpty(runtime.DiffFocusEdits);
                Assert.NotNull(runtime.DiffAnchor);
                Assert.NotNull(runtime.DiffSelection);
                One<ThreadChangesTab>(slot).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Empty(runtime.DiffFocusEdits);
                Assert.Null(runtime.DiffAnchor);
                Assert.Null(runtime.DiffSelection);
                Assert.True(runtime.IsDiffVisible);
            });
            Close(h, window);
        });
    }

    // ---- ComposerView layout (:139-189) ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-011")] // App/Views/Composer/ComposerView.swift:145-178
    [Trait("Req", "UI-COMPOSER-002")] // App/Views/Composer/ComposerView.swift:145-178
    public void The_pill_pads_its_row_14_leading_8_trailing_8_top_and_bottom_and_the_text_column_4_more_above_and_below()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var view = Pill(h, h.Runtime);
            h.Main.OnMain(() =>
            {
                var column = One<ComposerTextColumn>(view);
                Assert.Equal(new Thickness(0, 4, 0, 4), column.Margin);
                Assert.Equal(VerticalAlignment.Bottom, column.VerticalAlignment);
                Assert.Contains(Ancestors(column).OfType<Border>(), border => border.Padding == new Thickness(14, 8, 8, 8));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-011")] // App/Views/Composer/ComposerView.swift:145-178
    public void Without_a_resolved_thread_the_controls_are_left_out_and_the_text_column_fills_the_padded_row()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = Unresolved(h);
            var view = h.Main.OnMain(() => new ComposerView(runtime, workingDirectory: null));
            var window = h.Main.OnMain(() => Show(view, h.App, width: 600));
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                Assert.Empty(LogicalQuery.SelfAndDescendants<ComposerControls>(view));
                var column = Frame(One<ComposerTextColumn>(view), view);
                Assert.Equal(14, column.X);
                Assert.Equal(600 - 14 - 8, column.Width);
                Assert.Equal(8 + 4, column.Y);
                Assert.Equal(view.Bounds.Height - 8 - 4, column.Bottom);
            });
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-012")] // App/Views/Composer/ComposerView.swift:1293-1307
    [Trait("Req", "UI-COMPOSER-002")] // App/Views/Composer/ComposerView.swift:1293-1307
    public void In_the_main_chat_the_pill_is_regular_glass_in_a_22_point_continuous_rounded_rectangle_and_is_not_clipped()
    {
        headless.OnUiThread(() =>
        {
            var content = new Border { Width = 120, Height = 44 };
            var surface = new ComposerSurface().Body(content);
            var window = new AvaloniaWindow { Content = new Border { Child = surface }, Width = 300, Height = 200 };
            window.Show();
            var shape = Assert.IsType<Border>(surface);
            Assert.Same(content, shape.Child);
            Assert.Equal(GlassEffect.Regular(GlassShape.RoundedRectangle(22, RoundedCornerStyle.Continuous, circularInBand: true)), SwiftGlass.GetEffect(shape)); // the composer pill: measured circular in OD-0102's band (ED-275 (2))
            Assert.Equal(new CornerRadius(22), shape.CornerRadius);
            Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(shape));
            Assert.False(shape.ClipToBounds);
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-012")] // App/Views/Composer/ComposerView.swift:1299-1302
    public void On_a_glass_panel_the_pill_is_the_panel_control_fill_for_the_appearance_clipped_to_the_same_shape()
    {
        headless.OnUiThread(() =>
        {
            var surface = new ComposerSurface().Body(new Border { Width = 120, Height = 44 });
            var host = new ThemeVariantScope { Child = surface, RequestedThemeVariant = ThemeVariant.Light };
            var window = new AvaloniaWindow { Content = host, Width = 300, Height = 200 };
            window.Show();
            RenderBudget.SetIsOnGlassPanel(host, true);
            var shape = Assert.IsType<Border>(surface);
            Assert.Null(SwiftGlass.GetEffect(shape));
            Assert.True(shape.ClipToBounds);
            Assert.Equal(new CornerRadius(22), shape.CornerRadius);
            Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(shape));
            Assert.Equal(ThemeVariant.Light, shape.ActualThemeVariant);
            Assert.Equal(Chrome.PanelControlFill(isDark: false).Resolve(ThemeVariant.Light), Assert.IsAssignableFrom<ISolidColorBrush>(shape.Background).Color);
            host.RequestedThemeVariant = ThemeVariant.Dark;
            window.UpdateLayout();
            Assert.Equal(ThemeVariant.Dark, shape.ActualThemeVariant);
            Assert.Equal(Chrome.PanelControlFill(isDark: true).Resolve(ThemeVariant.Dark), Assert.IsAssignableFrom<ISolidColorBrush>(shape.Background).Color);
            RenderBudget.SetIsOnGlassPanel(host, false);
            Assert.Equal(GlassEffect.Regular(GlassShape.RoundedRectangle(22, RoundedCornerStyle.Continuous, circularInBand: true)), SwiftGlass.GetEffect(shape)); // the composer pill: measured circular in OD-0102's band (ED-275 (2))
            Assert.False(shape.ClipToBounds);
            window.Close();
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-013")] // App/Views/Composer/ComposerView.swift:187-189
    public void A_change_of_width_moves_the_text_column_with_the_pill_in_the_same_layout_pass()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = Unresolved(h);
            var view = h.Main.OnMain(() => new ComposerView(runtime, workingDirectory: null));
            var window = h.Main.OnMain(() => Show(view, h.App, width: 700));
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                Assert.Equal(700 - 22, One<ComposerTextColumn>(view).Bounds.Width);
                Host(window).Width = 450;
                window.UpdateLayout();
                Assert.Equal(450, view.Bounds.Width);
                Assert.Equal(450 - 22, One<ComposerTextColumn>(view).Bounds.Width);
            });
            Close(h, window);
        });
    }

    // ---- ComposerTextColumn (:540-607) ----

    internal static ComposerTextColumn Column(ThreadRuntime runtime, string? notice) => new(
        runtime,
        new ComposerController(),
        placeholder: "",
        takesFocusOnAppear: false,
        attachmentNotice: notice,
        onKey: _ => false,
        onAttach: _ => { },
        onCursorChange: _ => { },
        onBlur: () => { });

    [Fact]
    [Trait("Req", "PR-COMPOSER-014")] // App/Views/Composer/ComposerView.swift:573-594
    public void The_text_column_stacks_chips_attachments_notice_and_text_view_in_that_order_with_no_spacing()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var column = Column(h.Runtime, "2 files left out");
                var stack = Assert.IsType<StackPanel>(column.Child);
                Assert.Equal(Orientation.Vertical, stack.Orientation);
                Assert.Equal(0, stack.Spacing);
                Assert.Equal(4, stack.Children.Count);
                Assert.IsType<DraftChips>(stack.Children[0]);
                Assert.IsType<DraftAttachments>(stack.Children[1]);
                // The notice's row (a NoticeSlot: UI-COMPOSER-004's fold) holds the line; it sits third in the column.
                var notice = One<TextBlock>(Assert.IsType<NoticeSlot>(stack.Children[2]));
                Assert.Equal("2 files left out", notice.Text);
                Assert.Equal(new Thickness(0, 0, 0, 6), notice.Margin);
                Assert.Equal(1, notice.MaxLines);
                Assert.Equal(SwiftFont.Subheadline.Resolve().FontSize, notice.FontSize);
                Assert.Equal(Chrome.Warning.Resolve(notice.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(notice.Foreground).Color);
                Assert.Single(LogicalQuery.SelfAndDescendants<ComposerTextView>(stack.Children[3]));
                Drain();
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-014")] // App/Views/Composer/ComposerView.swift:573-576, :879, :982
    public void With_no_chips_no_attachments_and_no_notice_those_parts_take_no_room_above_the_text_view()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var column = h.Main.OnMain(() => Column(h.Runtime, notice: null));
            var window = h.Main.OnMain(() => Show(column, h.App, width: 400));
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                var stack = Assert.IsType<StackPanel>(column.Child);
                Assert.Equal(3, stack.Children.Count);
                Assert.Equal(0, One<DraftChips>(column).Bounds.Height);
                Assert.Equal(0, One<DraftAttachments>(column).Bounds.Height);
                Assert.Equal(0, Frame(One<ComposerTextView>(column), column).Y);
            });
            Close(h, window);
        });
    }

    // The five-line ceiling (ComposerView.swift:556-561) with the port's text face, Inter (OD-0009): the line height is
    // ceil(ascender - descender + leading) of the 14 point face, taken here from Inter's own metrics.
    internal static double FiveLineCeiling()
    {
        var font = SwiftFont.SystemFont(14).Resolve();
        Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(font.FontFamily, font.FontStyle, font.FontWeight), out var glyph));
        var scale = 14 / (double)glyph.Metrics.DesignEmHeight;
        var ascender = -glyph.Metrics.Ascent * scale;
        var descender = -glyph.Metrics.Descent * scale;
        var leading = glyph.Metrics.LineGap * scale;
        return Math.Ceiling(ascender - descender + leading) * 5 + 4;
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-015")] // App/Views/Composer/ComposerView.swift:552-565, :599-602
    public void Before_the_text_view_first_measures_the_box_is_20_points_high()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            h.Main.OnMain(() =>
            {
                var column = Column(h.Runtime, notice: null);
                var textView = One<ComposerTextView>(column);
                Assert.Equal(20, textView.Height);
                var clip = Assert.IsType<Border>(textView.Parent);
                Assert.Equal(20, clip.Height);
                Assert.True(clip.ClipToBounds);
                Assert.Equal(VerticalAlignment.Top, textView.VerticalAlignment);
                Drain();
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-015")] // App/Views/Composer/ComposerView.swift:552-565, :599-602
    public void The_box_grows_with_its_lines_up_to_five_lines_of_14_point_text_plus_4_and_the_text_view_always_takes_the_clip_height()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = h.Runtime;
            var column = h.Main.OnMain(() => Column(runtime, notice: null));
            var window = h.Main.OnMain(() => Show(column, h.App, width: 400));
            Pump(h, window);
            double Clip() => ((Border)One<ComposerTextView>(column).Parent!).Height;
            double Inner() => One<ComposerTextView>(column).Height;

            h.Main.OnMain(() => runtime.Draft = new ComposerDraft { Text = "one line" });
            Pump(h, window);
            var one = h.Main.OnMain(Clip);
            Assert.True(one >= 20);
            Assert.Equal(one, h.Main.OnMain(Inner));

            h.Main.OnMain(() => runtime.Draft = new ComposerDraft { Text = "one\ntwo\nthree" });
            Pump(h, window);
            var three = h.Main.OnMain(Clip);
            Assert.True(three > one);
            Assert.True(three < FiveLineCeiling());
            Assert.Equal(three, h.Main.OnMain(Inner));

            h.Main.OnMain(() => runtime.Draft = new ComposerDraft { Text = string.Join("\n", Enumerable.Range(1, 30).Select(line => "line " + line.ToString(System.Globalization.CultureInfo.InvariantCulture))) });
            Pump(h, window);
            Assert.Equal(FiveLineCeiling(), h.Main.OnMain(Clip));
            Assert.Equal(FiveLineCeiling(), h.Main.OnMain(Inner));
            Close(h, window);
        });
    }

    // ---- placeholder (:275-288) ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-020")] // App/Views/Composer/ComposerView.swift:277
    public void Plan_mode_reads_Describe_what_you_want_to_plan_ahead_of_every_Hydra_wording()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var view = Pill(h, h.Runtime);
            h.Main.OnMain(() =>
            {
                h.App.Settings.HydraEnabled = true;
                h.App.Settings.HydraAlwaysHeads = true;
                var thread = h.Thread with { InteractionMode = InteractionMode.Plan, HydraEnabled = true };
                Assert.True(h.App.HydraIsOn(thread));
                Assert.Equal(Plan, view.Placeholder(thread));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-021")] // App/Views/Composer/ComposerView.swift:278-284
    public void With_Hydra_on_and_every_message_sent_to_a_head_it_reads_Goes_to_a_head_and_who_leads()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var view = Pill(h, h.Runtime);
            h.Main.OnMain(() =>
            {
                h.App.Settings.HydraEnabled = true;
                h.App.Settings.HydraAlwaysHeads = true;
                var thread = h.Thread with { HydraEnabled = true };
                Assert.Equal("Goes to a head; Codex leads and reports back", view.Placeholder(thread));
                Assert.Equal("Goes to a head; Claude leads and reports back", view.Placeholder(thread with { Provider = ProviderKind.Claude }));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-022")] // App/Views/Composer/ComposerView.swift:278, :285
    public void With_Hydra_on_but_not_always_heads_it_reads_Ask_the_provider_for_anything_and_big_jobs_go_to_heads()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var view = Pill(h, h.Runtime);
            h.Main.OnMain(() =>
            {
                h.App.Settings.HydraEnabled = true;
                h.App.Settings.HydraAlwaysHeads = false;
                var thread = h.Thread with { HydraEnabled = true };
                Assert.Equal("Ask Codex for anything; big jobs go out to a team of heads", view.Placeholder(thread));
                Assert.Equal("Ask OpenCode Zen for anything; big jobs go out to a team of heads", view.Placeholder(thread with { Provider = ProviderKind.Opencode }));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-023")] // App/Views/Composer/ComposerView.swift:287
    public void With_Hydra_off_it_reads_Ask_the_provider_to_build_fix_or_explain_with_the_at_and_slash_hints()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var view = Pill(h, h.Runtime);
            h.Main.OnMain(() =>
            {
                h.App.Settings.HydraEnabled = false;
                h.App.Settings.HydraAlwaysHeads = true;
                var thread = h.Thread with { HydraEnabled = true };
                Assert.False(h.App.HydraIsOn(thread));
                Assert.Equal("Ask Codex to build, fix or explain. @ for files, / for commands", view.Placeholder(thread));
                Assert.Equal("Ask Z.ai to build, fix or explain. @ for files, / for commands", view.Placeholder(thread with { Provider = ProviderKind.Zai }));
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-023")] // App/Views/Composer/ComposerView.swift:276
    public void With_no_resolved_thread_the_placeholder_is_empty()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = Unresolved(h);
            var view = h.Main.OnMain(() => new ComposerView(runtime, workingDirectory: null));
            var window = h.Main.OnMain(() => Show(view, h.App, width: 600));
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                Assert.Equal("", view.Placeholder(null));
                Assert.Equal("", One<ComposerNSTextView>(view).Placeholder);
            });
            Close(h, window);
        });
    }

    // ---- keys (:311-368), send (:370-382), recall (:400-422) ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-025")] // App/Views/Composer/ComposerView.swift:342-354
    public void Escape_stops_a_running_turn_and_leaves_the_terminal_and_the_diff_alone()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = h.Runtime;
            var view = Pill(h, runtime);
            h.Main.OnMain(() =>
            {
                runtime.RehearseTurn("working");
                runtime.Draft = new ComposerDraft { Text = "typed while it ran" };
                runtime.IsTerminalVisible = true;
                runtime.IsDiffVisible = true;
            });
            Assert.True(runtime.IsRunning);
            Assert.True(h.Main.OnMain(() => Press(view, Key.Escape)));
            h.Main.Loop.RunUntil(() => !runtime.IsRunning, AppModelHarness.Bound);
            Assert.True(runtime.IsTerminalVisible);
            Assert.True(runtime.IsDiffVisible);
            Assert.Equal("typed while it ran", runtime.Draft.Text);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-025")] // App/Views/Composer/ComposerView.swift:347-348
    public void Escape_with_nothing_running_closes_the_terminal_first_and_leaves_the_diff()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = h.Runtime;
            var view = Pill(h, runtime);
            h.Main.OnMain(() =>
            {
                runtime.IsTerminalVisible = true;
                runtime.IsDiffVisible = true;
            });
            Assert.True(h.Main.OnMain(() => Press(view, Key.Escape)));
            Assert.False(runtime.IsTerminalVisible);
            Assert.True(runtime.IsDiffVisible);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-025")] // App/Views/Composer/ComposerView.swift:349-350
    public void Escape_with_only_the_diff_open_toggles_the_diff_closed()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = h.Runtime;
            var view = Pill(h, runtime);
            h.Main.OnMain(() => runtime.ShowDiff(new Border(), null, focusEdits: [new FileEdit("a.txt")]));
            Assert.True(runtime.IsDiffVisible);
            Assert.NotEmpty(runtime.DiffFocusEdits);
            Assert.True(h.Main.OnMain(() => Press(view, Key.Escape)));
            Assert.False(runtime.IsDiffVisible);
            Assert.Empty(runtime.DiffFocusEdits);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-025")] // App/Views/Composer/ComposerView.swift:351-352
    public void Escape_with_nothing_to_stop_or_close_is_not_handled()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var view = Pill(h, h.Runtime);
            Assert.False(h.Main.OnMain(() => Press(view, Key.Escape)));
        });
    }

    private static void SendPrompts(ThreadRuntimePart12Harness h, params string[] prompts) => h.Main.OnMain(() =>
    {
        foreach (var prompt in prompts) h.Runtime.RehearseTurn(prompt);
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-027")] // App/Views/Composer/ComposerView.swift:400-410
    public void Up_walks_back_through_the_non_empty_sent_prompts_and_holds_at_the_oldest()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = h.Runtime;
            var view = Pill(h, runtime);
            SendPrompts(h, "first", "", "second");
            Assert.Equal(["first", "", "second"], runtime.SentPrompts);
            Assert.True(h.Main.OnMain(() => Press(view, Key.Up)));
            Assert.Equal("second", runtime.Draft.Text);
            Assert.True(h.Main.OnMain(() => Press(view, Key.Up)));
            Assert.Equal("first", runtime.Draft.Text);
            Assert.True(h.Main.OnMain(() => Press(view, Key.Up)));
            Assert.Equal("first", runtime.Draft.Text);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-027")] // App/Views/Composer/ComposerView.swift:411-421
    public void Down_walks_forward_and_past_the_newest_clears_the_text_and_forgets_the_place()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = h.Runtime;
            var view = Pill(h, runtime);
            SendPrompts(h, "first", "second");
            h.Main.OnMain(() => Press(view, Key.Up));
            h.Main.OnMain(() => Press(view, Key.Up));
            Assert.Equal("first", runtime.Draft.Text);
            Assert.True(h.Main.OnMain(() => Press(view, Key.Down)));
            Assert.Equal("second", runtime.Draft.Text);
            Assert.True(h.Main.OnMain(() => Press(view, Key.Down)));
            Assert.Equal("", runtime.Draft.Text);
            Assert.False(h.Main.OnMain(() => Press(view, Key.Down)));
            Assert.Equal("", runtime.Draft.Text);
            Assert.True(h.Main.OnMain(() => Press(view, Key.Up)));
            Assert.Equal("second", runtime.Draft.Text);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-027")] // App/Views/Composer/ComposerView.swift:403-405
    public void Text_that_is_neither_empty_nor_the_recalled_prompt_lets_the_arrows_through()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = h.Runtime;
            var view = Pill(h, runtime);
            SendPrompts(h, "first", "second");
            h.Main.OnMain(() => runtime.Draft = new ComposerDraft { Text = "typed" });
            Assert.False(h.Main.OnMain(() => Press(view, Key.Up)));
            Assert.False(h.Main.OnMain(() => Press(view, Key.Down)));
            Assert.Equal("typed", runtime.Draft.Text);
            h.Main.OnMain(() => runtime.Draft = new ComposerDraft());
            Assert.True(h.Main.OnMain(() => Press(view, Key.Up)));
            Assert.Equal("second", runtime.Draft.Text);
            h.Main.OnMain(() => runtime.Draft = runtime.Draft with { Text = "second, edited" });
            Assert.False(h.Main.OnMain(() => Press(view, Key.Up)));
            Assert.Equal("second, edited", runtime.Draft.Text);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-027")] // App/Views/Composer/ComposerView.swift:401-402
    public void Recall_never_runs_with_an_attachment_in_the_draft_or_with_nothing_sent()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = h.Runtime;
            var view = Pill(h, runtime);
            Assert.False(h.Main.OnMain(() => Press(view, Key.Up)));
            Assert.Equal("", runtime.Draft.Text);
            SendPrompts(h, "first");
            var attachment = new Attachment { Name = "a.txt", Path = @"C:\no-such-folder\a.txt", MimeType = "text/plain" };
            h.Main.OnMain(() => runtime.Draft = new ComposerDraft { Attachments = new ValueList<Attachment>([attachment]) });
            Assert.False(h.Main.OnMain(() => Press(view, Key.Up)));
            Assert.Equal("", runtime.Draft.Text);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-028")] // App/Views/Composer/ComposerView.swift:357-366
    public void Delete_at_the_start_takes_the_command_chip_first_then_the_last_quote_and_otherwise_passes()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = h.Runtime;
            var view = Pill(h, runtime);
            var first = new ReplyQuote { Text = "first quote" };
            var second = new ReplyQuote { Text = "second quote" };
            h.Main.OnMain(() => runtime.Draft = new ComposerDraft
            {
                Command = new DraftCommand { Name = "plan", Detail = "Turn plan mode on or off", IsBuiltIn = true },
                Quotes = new ValueList<ReplyQuote>([first, second]),
            });
            Assert.True(h.Main.OnMain(() => Press(view, Key.Back)));
            Assert.Null(runtime.Draft.Command);
            Assert.Equal([first, second], runtime.Draft.Quotes);
            Assert.True(h.Main.OnMain(() => Press(view, Key.Back)));
            Assert.Equal([first], runtime.Draft.Quotes);
            Assert.True(h.Main.OnMain(() => Press(view, Key.Back)));
            Assert.Empty(runtime.Draft.Quotes);
            Assert.False(h.Main.OnMain(() => Press(view, Key.Back)));
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-029")] // App/Views/Composer/ComposerView.swift:355-356
    public void Tab_is_never_handled_while_the_suggestion_list_is_closed()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = h.Runtime;
            var view = Pill(h, runtime);
            SendPrompts(h, "first");
            Assert.False(h.Main.OnMain(() => Press(view, Key.Tab)));
            Assert.Equal("", runtime.Draft.Text);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-030")] // App/Views/Composer/ComposerView.swift:370-382
    public void Return_with_nothing_running_sends_the_draft()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = h.Runtime;
            var view = Pill(h, runtime);
            h.Main.OnMain(() => runtime.Draft = new ComposerDraft { Text = "build it" });
            Assert.False(runtime.IsRunning);
            Assert.True(h.Main.OnMain(() => Press(view, Key.Enter)));
            Assert.True(runtime.Draft.IsEmpty);
            h.Main.Loop.RunUntil(() => runtime.Phase == RuntimePhase.Idle && runtime.Turns.Count == 1, AppModelHarness.Bound);
            Assert.Equal(["build it"], runtime.SentPrompts);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-030")] // App/Views/Composer/ComposerView.swift:374-378
    public void Return_while_a_turn_runs_captures_the_draft_and_interrupts_the_turn()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = h.Runtime;
            var view = Pill(h, runtime);
            h.Main.OnMain(() =>
            {
                runtime.RehearseTurn("working");
                runtime.Draft = new ComposerDraft { Text = "change of plan" };
            });
            var running = runtime.Turns.Single().Id;
            Assert.True(h.Main.OnMain(() => Press(view, Key.Enter)));
            Assert.True(runtime.Draft.IsEmpty);
            Assert.Empty(runtime.FollowUps);
            h.Main.Loop.RunUntil(() => runtime.Turns.Single(turn => turn.Id == running).Status != TurnStatus.Running, AppModelHarness.Bound);
            Assert.Equal(TurnStatus.Interrupted, runtime.Turns.Single(turn => turn.Id == running).Status);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-030")] // App/Views/Composer/ComposerView.swift:371
    public void Return_with_an_empty_draft_is_taken_but_sends_nothing()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = h.Runtime;
            var view = Pill(h, runtime);
            h.Main.OnMain(() => runtime.Draft = new ComposerDraft { Text = "  \n " });
            Assert.True(h.Main.OnMain(() => Press(view, Key.Enter)));
            Assert.Equal("  \n ", runtime.Draft.Text);
            Assert.Empty(runtime.Turns);
            Assert.False(runtime.IsRunning);
        });
    }

    // ======== Part 2 of 5 (ComposerView.swift :190-218, :228-273, :311-330, :384-398, :424-524, :1037-1101,
    // :1103-1288; Provider.swift :306-323): steer, the suggestion list, the command list, pick, the file index and
    // attach ========

    // The key the text view reports for a chord (ComposerTextView.swift:329-357), raised as part 1's Press does.
    internal static bool Press(ComposerView view, Key key, KeyModifiers modifiers)
    {
        var textView = One<ComposerNSTextView>(view);
        var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers };
        textView.RaiseEvent(args);
        return args.Handled;
    }

    // The text box as a user leaves it: the text, then the caret. The text view reports both (ComposerTextView.swift:
    // 317-327), the text through the draft binding.
    internal static void Type(ComposerView view, string text, int? caret = null)
    {
        var box = One<ComposerNSTextView>(view);
        box.Text = text;
        // The text box raises TextChanged from a posted dispatcher job; it runs here, before the caret moves, so the
        // draft holds the text when the caret is reported, as a keystroke delivers them (ComposerTextView.swift:317-327).
        Drain();
        var at = caret ?? text.Length;
        box.SelectionStart = at;
        box.SelectionEnd = at;
    }

    // Pumps the main loop, the dispatcher and layout until the condition holds: a search or an index build runs off main
    // and comes back to it.
    internal static void Until(ThreadRuntimePart12Harness h, AvaloniaWindow? window, Func<bool> condition) =>
        h.Main.Loop.RunUntil(() =>
        {
            if (window is null) Drain();
            else Settle(window);
            return condition();
        }, AppModelHarness.Bound);

    // A pill that is not on screen, with a working directory, for the resolved harness thread: the controls a resolved
    // thread draws (ComposerControls) are part 3's, so such a pill is exercised unattached, as part 1 does.
    internal static ComposerView Pill(ThreadRuntimePart12Harness h, ThreadRuntime runtime, string? workingDirectory) => h.Main.OnMain(() =>
    {
        var view = new ComposerView(runtime, workingDirectory);
        AppEnvironment.SetAppModel(view, h.App);
        Drain();
        return view;
    });

    // A pill on screen beside another focusable control, its text view focused. Its runtime resolves no thread, so the
    // controls are left out (ComposerView.swift:160) and the file list, which needs no thread, can be shown.
    internal static (ComposerView View, AvaloniaWindow Window, Button Other, StackPanel Stack) OnScreen(
        ThreadRuntimePart12Harness h, ThreadRuntime runtime, string? workingDirectory, bool takesFocusOnAppear = true)
    {
        var (view, window, other, stack) = h.Main.OnMain(() =>
        {
            var view = new ComposerView(runtime, workingDirectory, takesFocusOnAppear: takesFocusOnAppear);
            var other = new Button { Content = "Other" };
            var stack = new StackPanel
            {
                Width = 600,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            stack.Children.Add(view);
            stack.Children.Add(other);
            AppEnvironment.SetAppModel(stack, h.App);
            var window = new AvaloniaWindow { Content = stack, Width = 1400, Height = 900 };
            window.Show();
            Settle(window);
            return (view, window, other, stack);
        });
        Pump(h, window);
        h.Main.OnMain(() => One<ComposerNSTextView>(view).Focus());
        Pump(h, window);
        return (view, window, other, stack);
    }

    // Empty files at the given relative paths ("/" separated) under root.
    internal static void Touch(string root, params string[] relative)
    {
        foreach (var path in relative)
        {
            var full = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, []);
        }
    }

    // Builds the shared index for a directory before a composer asks for it (ComposerView.swift:1121-1127: a composer
    // that finds its directory built is served at once).
    internal static void Indexed(ThreadRuntimePart12Harness h, string directory)
    {
        var index = h.Main.OnMain(() => new FileIndex());
        h.Main.Loop.Run(() => index.Prepare(directory), AppModelHarness.Bound);
    }

    // What the controller last handed the popover (ComposerTextView.swift:51-84): the fixed frame holding the menu.
    internal static Border Shown(RecordingPresenter presenter)
    {
        var (popup, presented) = presenter.Calls[^1];
        Assert.True(presented);
        return Assert.IsType<Border>(PopoverHost.Content(popup)); // the site's frame, not the card the presenter hangs it on (OD-0095)
    }

    private static string HeaderText(Control menu) => Assert.IsType<TextBlock>(One<PopoverSectionHeader>(menu).Child).Text!;

    internal static string[] Titles(ComposerView view) => view.Suggestions.Items.Select(item => item.Title).ToArray();

    private static ShortcutStore FreshStore() => (ShortcutStore)Activator.CreateInstance(typeof(ShortcutStore), nonPublic: true)!;

    // ED-211: use an offline CLI under the allowed scratch root; never use a system-installed executable.
    internal static string FailingCommandCLI() =>
        ProviderProbeStandIns.CopyFor("composer-command-discovery-failure").SetExitCode(1).ExecutablePath;

    internal static Attachment Fake(int index) => new()
    {
        Name = "held" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".txt",
        Path = @"C:\no-such-folder\held" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".txt",
        MimeType = "text/plain",
    };


    // ---- steer (:384-398) ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-031")] // App/Views/Composer/ComposerView.swift:388-397
    public void Command_Return_with_nothing_running_sends_the_draft_because_there_is_nothing_to_steer_behind()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            using var shared = ShortcutStore.Shared.Override(FreshStore());
            var runtime = h.Runtime;
            var view = Pill(h, runtime);
            h.Main.OnMain(() => runtime.Draft = new ComposerDraft { Text = "steer me" });
            Assert.True(h.Main.OnMain(() => Press(view, Key.Enter, KeyModifiers.Control)));
            Assert.True(runtime.Draft.IsEmpty);
            Assert.Empty(runtime.FollowUps);
            h.Main.Loop.RunUntil(() => runtime.Phase == RuntimePhase.Idle && runtime.Turns.Count == 1, AppModelHarness.Bound);
            Assert.Equal(["steer me"], runtime.SentPrompts);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-031")] // App/Views/Composer/ComposerView.swift:392-394
    public void Command_Return_while_a_turn_runs_with_the_default_queue_chord_queues_the_draft_as_a_follow_up()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            using var shared = ShortcutStore.Shared.Override(FreshStore());
            var runtime = h.Runtime;
            var view = Pill(h, runtime);
            h.Main.OnMain(() =>
            {
                runtime.RehearseTurn("working");
                runtime.Draft = new ComposerDraft { Text = "after that, the tests" };
            });
            var running = runtime.Turns.Single().Id;
            Assert.True(h.Main.OnMain(() => Press(view, Key.Enter, KeyModifiers.Control)));
            Assert.True(runtime.Draft.IsEmpty);
            Assert.Equal(["after that, the tests"], runtime.FollowUps.Select(prompt => prompt.Text).ToArray());
            Assert.True(runtime.IsRunning);
            Assert.Equal(TurnStatus.Running, runtime.Turns.Single(turn => turn.Id == running).Status);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-031")] // App/Views/Composer/ComposerView.swift:392-397
    public void Once_the_queue_chord_is_rebound_Command_Return_falls_back_to_send_which_leaves_a_running_turn_and_the_draft_alone()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var store = FreshStore();
            using var shared = ShortcutStore.Shared.Override(store);
            var runtime = h.Runtime;
            var view = Pill(h, runtime);
            h.Main.OnMain(() =>
            {
                store.Set(new KeyChord(0x1F, ChordModifiers.Control), AppShortcut.QueueChat);
                runtime.RehearseTurn("working");
                runtime.Draft = new ComposerDraft { Text = "after that, the tests" };
            });
            Assert.True(h.Main.OnMain(() => Press(view, Key.Enter, KeyModifiers.Control)));
            Assert.Empty(runtime.FollowUps);
            Assert.Equal("after that, the tests", runtime.Draft.Text);
            Assert.True(runtime.IsRunning);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-031")] // App/Views/Composer/ComposerView.swift:389
    public void Command_Return_with_an_empty_draft_is_taken_and_does_nothing()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            using var shared = ShortcutStore.Shared.Override(FreshStore());
            var runtime = h.Runtime;
            var view = Pill(h, runtime);
            h.Main.OnMain(() => runtime.RehearseTurn("working"));
            Assert.True(h.Main.OnMain(() => Press(view, Key.Enter, KeyModifiers.Control)));
            Assert.Empty(runtime.FollowUps);
            Assert.True(runtime.IsRunning);
        });
    }

    // An idle lead with a head at work can queue: canQueue is `isRunning || hasWorkingHeads` (ThreadRuntime.swift:
    // 771-773), so the default chord takes queueDraftAsFollowUp (ComposerView.swift:392-394), which queues the draft
    // and clears the box (ThreadRuntime.swift:786-787, :1000). The queue never waits behind an idle lead, though:
    // enqueueFollowUp ends with flushFollowUpsIfIdle (:1003-1007), whose Task (:1056-1061) drains the queue as after a
    // completed turn (:1066-1071), so the prompt leaves the queue and starts a turn of its own (:1116-1137), counted as
    // one that started on its own (:1134-1135), which send() never counts (:743-755). The head is put to work after
    // the model is made, since a loaded library stops a running head (AppModel.swift:132-136).
    [Fact]
    [Trait("Req", "PR-COMPOSER-031")] // App/Views/Composer/ComposerView.swift:392-394; App/Runtime/ThreadRuntime.swift:664-666, :771-773, :786-787
    [Trait("Req", "PR-RUNTIME-197")] // App/Runtime/ThreadRuntime.swift:1003-1007, :1056-1071, :1116-1137
    public void Command_Return_on_an_idle_lead_with_a_head_at_work_queues_the_draft_and_the_idle_lead_takes_it_at_once()
    {
        headless.OnUiThread(() =>
        {
            System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
            using var h = new AppModelHarness();
            using var shared = ShortcutStore.Shared.Override(FreshStore());
            var project = AppModelSamples.Project("Thread runtime", h.NewFolder("thread-runtime"));
            var lead = AppModelSamples.Thread(project);
            var head = new ChatThread(project.Id, ProviderKind.Codex, null, null, RuntimeMode.Supervised)
            {
                Title = "head",
                ParentThreadID = lead.Id,
                IsInPanel = true,
                Hydra = AppModelSamples.Head(HydraHeadInfo.Status.Stopped),
            };
            h.WriteLibrary([project], [lead, head]);
            AppModelHarness.WriteDocument(lead.Id);
            AppModelHarness.WriteDocument(head.Id);
            var model = h.MakeModel();
            h.PrepareBootstrap(model); // R-TEST-10: the turn's CLI is a path that does not exist, so no real CLI runs.
            var runtime = h.Main.OnMain(() => new ThreadRuntime(lead.Id, model));
            h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
            var view = h.Main.OnMain(() =>
            {
                model.UpdateThread(head.Id, thread => thread with { Hydra = thread.Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Running } });
                var pill = new ComposerView(runtime, workingDirectory: null);
                AppEnvironment.SetAppModel(pill, model);
                Drain();
                runtime.Draft = new ComposerDraft { Text = "after the heads report" };
                return pill;
            });
            Assert.False(runtime.IsRunning);
            Assert.True(h.Main.OnMain(() => runtime.HasWorkingHeads));

            h.Main.OnMain(() =>
            {
                Assert.True(Press(view, Key.Enter, KeyModifiers.Control));
                // The press itself, before the main actor runs the Task the flush scheduled (:1060): queued behind
                // nothing but an idle lead (:786, :1000), the box cleared (:787), nothing sent yet.
                Assert.True(runtime.Draft.IsEmpty);
                Assert.Equal(["after the heads report"], runtime.FollowUps.Select(prompt => prompt.Text).ToArray());
                Assert.Empty(runtime.SentPrompts);
                Assert.Equal(0L, runtime.AutoStartedTurnToken); // :226
            });

            // :1066-1071, :1116-1137 the flush's Task takes the prompt out of the queue and starts its turn on it; the
            // missing CLI then ends that turn as failed (:1376-1380).
            h.Main.Loop.RunUntil(() => runtime.Phase == RuntimePhase.Idle, AppModelHarness.Bound);
            h.Main.OnMain(() =>
            {
                Assert.Single(runtime.Turns);
                Assert.Equal(["after the heads report"], runtime.SentPrompts);
                Assert.Empty(runtime.FollowUps);
                Assert.Equal(1L, runtime.AutoStartedTurnToken); // :1134-1135
                Assert.True(runtime.Draft.IsEmpty);
            });
        });
    }

    // ---- caret moves and the warm-up (:426-437) ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-035")] // App/Views/Composer/ComposerView.swift:323-326, :429-436
    public void Escape_latches_the_list_shut_and_the_next_caret_move_clears_the_latch_so_it_opens_again()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var view = Pill(h, h.Runtime, workingDirectory: null);
            h.Main.OnMain(() => Type(view, "/"));
            Until(h, null, () => view.Suggestions.IsVisible);
            Assert.True(h.Main.OnMain(() => Press(view, Key.Escape)));
            Until(h, null, () => true);
            Assert.False(view.Suggestions.IsVisible);
            h.Main.OnMain(() => Type(view, "/p"));
            Until(h, null, () => view.Suggestions.IsVisible);
            Assert.Equal(["/plan", "/compact"], Titles(view));
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-035")] // App/Views/Composer/ComposerView.swift:432-436
    public void Each_caret_report_cancels_the_refresh_before_it_and_leaves_one_refresh_running()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var view = Pill(h, h.Runtime, workingDirectory: null);
            var (first, second) = h.Main.OnMain(() =>
            {
                var box = One<ComposerNSTextView>(view);
                box.Text = "/p";
                box.SelectionStart = box.SelectionEnd = 1;
                var earlier = view.Controller.SuggestionRefresh;
                box.SelectionStart = box.SelectionEnd = 2;
                return (earlier, view.Controller.SuggestionRefresh);
            });
            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.NotSame(first, second);
            Assert.True(first!.IsCancelled);
            Assert.False(second!.IsCancelled);
            Until(h, null, () => view.Suggestions.IsVisible);
            Assert.Equal(["/plan", "/compact"], Titles(view));
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-036")] // App/Views/Composer/ComposerView.swift:431; App/Runtime/ThreadRuntime.swift:1416-1427
    public void Typing_into_an_idle_thread_warms_its_session_which_settles_the_model_before_any_send()
    {
        headless.OnUiThread(() =>
        {
            // Claude's catalog starts from its seed, so the warm-up's ensureModel has a default to settle on
            // (ThreadRuntime.swift:1423); its CLI is the harness's missing path, so the start that follows fails quietly.
            System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
            using var h = new ThreadRuntimePart12Harness(ProviderKind.Claude);
            var runtime = h.Runtime;
            Loaded(h, runtime);
            Assert.Null(h.App.Thread(h.Thread.Id)!.Model);
            Assert.NotNull(h.App.Providers.DefaultModel(ProviderKind.Claude));
            var view = Pill(h, runtime, workingDirectory: null);
            h.Main.OnMain(() => Type(view, "h"));
            Until(h, null, () => h.App.Thread(h.Thread.Id)!.Model is not null);
            Assert.Equal(h.App.Providers.DefaultModel(ProviderKind.Claude)!.Id, h.App.Thread(h.Thread.Id)!.Model);
            Assert.Empty(runtime.Turns);
            Assert.Equal(RuntimePhase.Idle, runtime.Phase);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-036")] // App/Views/Composer/ComposerView.swift:431; App/Runtime/ThreadRuntime.swift:1418
    public void Typing_into_an_archived_thread_warms_nothing()
    {
        headless.OnUiThread(() =>
        {
            System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
            using var h = new ThreadRuntimePart12Harness(ProviderKind.Claude);
            Loaded(h, h.Runtime);
            Assert.NotNull(h.App.Providers.DefaultModel(ProviderKind.Claude));
            h.Main.OnMain(() => h.App.UpdateThread(h.Thread.Id, thread => thread with { IsArchived = true }));
            var view = Pill(h, h.Runtime, workingDirectory: null);
            h.Main.OnMain(() => Type(view, "h"));
            for (var round = 0; round < 20; round++) h.Main.OnMain(Drain);
            Assert.Null(h.App.Thread(h.Thread.Id)!.Model);
        });
    }

    // ---- the word before the caret (:439-473) ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-037")] // App/Views/Composer/ComposerView.swift:445-452
    public void The_word_runs_back_from_the_caret_to_the_last_space_newline_or_tab()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var presenter = new RecordingPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            var directory = h.Store.NewFolder("words");
            Touch(directory, "main.swift");
            Indexed(h, directory);
            var (view, window, _, _) = OnScreen(h, Unresolved(h), directory);

            h.Main.OnMain(() => Type(view, "one two\n@ma"));
            Until(h, window, () => view.Suggestions.IsVisible);
            Assert.Equal((8, 3), view.Suggestions.Range);

            h.Main.OnMain(() => Type(view, "a\t@mai"));
            Until(h, window, () => view.Suggestions.Range == (2, 4));

            h.Main.OnMain(() => Type(view, "a b @main"));
            Until(h, window, () => view.Suggestions.Range == (4, 5));

            h.Main.OnMain(() => Type(view, "x@ma"));
            Until(h, window, () => !view.Suggestions.IsVisible);
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-037")] // App/Views/Composer/ComposerView.swift:440-452
    public void Offsets_are_UTF_16_units_as_NSString_counts_them()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var presenter = new RecordingPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            var directory = h.Store.NewFolder("utf16");
            Touch(directory, "main.swift");
            Indexed(h, directory);
            var (view, window, _, _) = OnScreen(h, Unresolved(h), directory);
            // U+00E9, then U+1F642 (two UTF-16 units), then a space: the word starts at unit 4.
            var text = "\U000000E9\U0001F642 @ma"; // U+00E9 LATIN SMALL LETTER E WITH ACUTE, U+1F642 SLIGHTLY SMILING FACE
            h.Main.OnMain(() => Type(view, text));
            Until(h, window, () => view.Suggestions.IsVisible);
            Assert.Equal((4, 3), view.Suggestions.Range);
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-037")] // App/Views/Composer/ComposerView.swift:441-443
    public void A_caret_at_the_very_start_closes_an_open_list()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var view = Pill(h, h.Runtime, workingDirectory: null);
            h.Main.OnMain(() => Type(view, "/pl"));
            Until(h, null, () => view.Suggestions.IsVisible);
            h.Main.OnMain(() =>
            {
                var box = One<ComposerNSTextView>(view);
                box.SelectionStart = box.SelectionEnd = 0;
            });
            Until(h, null, () => !view.Suggestions.IsVisible);
            Assert.Equal("/pl", h.Runtime.Draft.Text);
        });
    }

    // ---- @ file mentions (:455-462) ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-038")] // App/Views/Composer/ComposerView.swift:455-462, :1179-1183
    public void An_at_word_offers_at_most_eight_paths_each_titled_by_its_last_component_with_the_path_as_detail_and_the_doc_symbol()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var presenter = new RecordingPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            var directory = h.Store.NewFolder("mentions");
            var made = Enumerable.Range(0, 10).Select(n => "src/file" + n.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".txt").ToArray();
            Touch(directory, made);
            Indexed(h, directory);
            var (view, window, _, _) = OnScreen(h, Unresolved(h), directory);
            h.Main.OnMain(() => Type(view, "@file"));
            Until(h, window, () => view.Suggestions.IsVisible);
            var suggestions = view.Suggestions;
            Assert.Equal(SuggestionState.Kind.File, suggestions.SuggestionKind);
            Assert.Equal(8, suggestions.Items.Count);
            var relative = made.Select(path => path.Replace('/', '\\')).ToHashSet(StringComparer.Ordinal);
            foreach (var item in suggestions.Items)
            {
                Assert.Contains(item.Detail, relative);
                Assert.Equal(SwiftPath.LastPathComponent(item.Detail), item.Title);
                Assert.StartsWith("file", item.Title, StringComparison.Ordinal);
                Assert.Equal("@" + item.Detail + " ", item.Value);
                Assert.Equal("doc", item.Symbol);
                Assert.Null(item.Command);
                Assert.Equal(item.Value, item.Id);
            }
            Assert.Equal(8, suggestions.Items.Select(item => item.Detail).Distinct(StringComparer.Ordinal).Count());
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-038")] // App/Views/Composer/ComposerView.swift:467-470
    public void An_at_word_matching_no_path_offers_nothing()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var presenter = new RecordingPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            var directory = h.Store.NewFolder("nomatch");
            Touch(directory, "main.swift");
            Indexed(h, directory);
            var (view, window, _, _) = OnScreen(h, Unresolved(h), directory);
            h.Main.OnMain(() => Type(view, "@ma"));
            Until(h, window, () => view.Suggestions.IsVisible && presenter.Calls.Count > 0);
            var shown = presenter.Calls[^1].Popup;
            h.Main.OnMain(() => Type(view, "@qqq"));
            Until(h, window, () => !view.Suggestions.IsVisible && !presenter.Calls[^1].Presented);
            Assert.Same(shown, presenter.Calls[^1].Popup);
            Close(h, window);
        });
    }

    // ---- / commands (:463-466, :475-485) ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-039")] // App/Views/Composer/ComposerView.swift:463-465
    public void A_slash_word_at_the_very_start_of_the_draft_offers_commands()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var view = Pill(h, h.Runtime, workingDirectory: null);
            h.Main.OnMain(() => Type(view, "/"));
            Until(h, null, () => view.Suggestions.IsVisible);
            Assert.Equal(SuggestionState.Kind.Command, view.Suggestions.SuggestionKind);
            Assert.Equal((0, 1), view.Suggestions.Range);
            Assert.Contains("/plan", Titles(view));
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-039")] // App/Views/Composer/ComposerView.swift:463, :467-470
    public void A_slash_word_anywhere_but_the_start_offers_nothing_at_all()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var view = Pill(h, h.Runtime, workingDirectory: null);
            h.Main.OnMain(() => Type(view, "/pl"));
            Until(h, null, () => view.Suggestions.IsVisible);
            h.Main.OnMain(() => Type(view, "run /pl"));
            Until(h, null, () => !view.Suggestions.IsVisible);
            h.Main.OnMain(() => Type(view, " /pl"));
            Until(h, null, () => true);
            Assert.False(view.Suggestions.IsVisible);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-040")] // App/Views/Composer/ComposerView.swift:477, :482-484
    public void The_command_list_always_holds_plan_built_in_with_its_detail_and_the_command_symbol()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var view = Pill(h, h.Runtime, workingDirectory: null);
            h.Main.OnMain(() => Type(view, "/pla"));
            Until(h, null, () => view.Suggestions.IsVisible);
            var plan = Assert.Single(view.Suggestions.Items);
            Assert.Equal("/plan ", plan.Value);
            Assert.Equal("/plan", plan.Title);
            Assert.Equal("Turn plan mode on or off", plan.Detail);
            Assert.Equal("command", plan.Symbol);
            Assert.Equal(new SlashCommand { Name = "plan", Detail = "Turn plan mode on or off", IsBuiltIn = true }, plan.Command);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-041")] // App/Views/Composer/ComposerView.swift:478-480; Core/Models/Provider.swift:149-154
    public void Compact_is_offered_on_the_seven_providers_that_support_it_and_on_no_other()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var view = Pill(h, h.Runtime, workingDirectory: null);
            ProviderKind[] supporting =
            [
                ProviderKind.Codex, ProviderKind.Claude, ProviderKind.Copilot, ProviderKind.Deepseek, ProviderKind.Meta,
                ProviderKind.Zai, ProviderKind.Pi, ProviderKind.OpenRouter, ProviderKind.Local, // ED-234, ED-410
            ];
            foreach (var provider in SwiftEnum<ProviderKind>.AllCases)
            {
                h.Main.OnMain(() =>
                {
                    h.App.UpdateThread(h.Thread.Id, thread => thread with { Provider = provider });
                    Type(view, "");
                });
                Until(h, null, () => !view.Suggestions.IsVisible);
                h.Main.OnMain(() => Type(view, "/"));
                Until(h, null, () => view.Suggestions.IsVisible);
                var compact = view.Suggestions.Items.Where(item => item.Title == "/compact").ToArray();
                if (supporting.Contains(provider))
                {
                    var only = Assert.Single(compact);
                    Assert.Equal("Summarize the conversation to free up context", only.Detail);
                    Assert.Equal("command", only.Symbol);
                    Assert.True(only.Command!.IsBuiltIn);
                }
                else
                {
                    Assert.Empty(compact);
                }
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-042")] // App/Views/Composer/ComposerView.swift:481-482; Core/Models/Provider.swift:306-323
    public void Provider_commands_join_the_built_ins_and_rank_by_prefix_then_recency_then_name()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var directory = h.Store.NewFolder("commands");
            var view = Pill(h, h.Runtime, directory);
            h.Main.OnMain(() =>
            {
                h.App.Providers.UpdateCommands(new ValueList<SlashCommand>(
                [
                    new SlashCommand { Name = "review", Detail = "Review the diff" },
                    new SlashCommand { Name = "prereview", Detail = "Before the review" },
                    new SlashCommand { Name = "rebase", Detail = "Rebase the branch" },
                    new SlashCommand { Name = "Refactor", Detail = "Tidy the code" },
                    new SlashCommand { Name = "zzz", Detail = "Sleep" },
                    new SlashCommand { Name = "plan", Detail = "A provider plan" },
                ]), ProviderKind.Codex, directory);
                h.App.Providers.RecordCommand("prereview", ProviderKind.Codex);
                h.App.Providers.RecordCommand("rebase", ProviderKind.Codex);
                Type(view, "/re");
            });
            Until(h, null, () => view.Suggestions.IsVisible);
            Assert.Equal(["/rebase", "/Refactor", "/review", "/prereview"], Titles(view));

            h.Main.OnMain(() => Type(view, "/"));
            Until(h, null, () => view.Suggestions.Items.Count > 4);
            // An empty query: every name is a prefix match, so recency, then the lowercased name, decide.
            Assert.Equal(["/rebase", "/prereview", "/compact", "/plan", "/Refactor", "/review", "/zzz"], Titles(view));
            // The built-in plan came first, so the provider's plan is the duplicate that is dropped.
            var plan = view.Suggestions.Items.Single(item => item.Title == "/plan");
            Assert.Equal("Turn plan mode on or off", plan.Detail);
            Assert.Equal("command", plan.Symbol);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-042")] // Core/Models/Provider.swift:306-323
    public void Suggestions_match_a_lowercased_substring_drop_later_duplicates_and_rank_a_name_by_its_most_recent_use()
    {
        SlashCommand Command(string name) => new() { Name = name, Detail = name + " detail" };
        var commands = new[] { Command("Deploy"), Command("undeploy"), Command("Deploy"), Command("docs"), Command("build") };
        var matched = SlashCommand.Suggestions(commands, "DEP", ["undeploy", "docs", "undeploy"]);
        Assert.Equal(["Deploy", "undeploy"], matched.Select(command => command.Name).ToArray());
        Assert.Same(commands[0], matched[0]);

        // Recency: "docs" at 1 and again at 3 keeps rank 1; "build" at 0 is the most recent.
        var all = SlashCommand.Suggestions(commands, "", ["build", "docs", "zzz", "docs"]);
        Assert.Equal(["build", "docs", "Deploy", "undeploy"], all.Select(command => command.Name).ToArray());

        Assert.Empty(SlashCommand.Suggestions(commands, "nothing", []));
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-043")] // App/Views/Composer/ComposerView.swift:482-484
    public void A_provider_command_row_carries_slash_name_space_as_value_slash_name_as_title_its_detail_and_the_sparkles_symbol()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var directory = h.Store.NewFolder("rows");
            var view = Pill(h, h.Runtime, directory);
            var review = new SlashCommand { Name = "review", Detail = "Review the diff", SkillPath = "C:/skills/review" };
            h.Main.OnMain(() =>
            {
                h.App.Providers.UpdateCommands(new ValueList<SlashCommand>([review]), ProviderKind.Codex, directory);
                Type(view, "/rev");
            });
            Until(h, null, () => view.Suggestions.IsVisible);
            var row = Assert.Single(view.Suggestions.Items);
            Assert.Equal("/review ", row.Value);
            Assert.Equal("/review", row.Title);
            Assert.Equal("Review the diff", row.Detail);
            Assert.Equal("sparkles", row.Symbol);
            Assert.Equal(review, row.Command);
        });
    }

    // ---- the highlighted row (:471-472) and the keys while the list is open (:311-330) ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-044")] // App/Views/Composer/ComposerView.swift:471-472
    public void Typing_on_in_the_same_word_keeps_the_highlight_clamped_and_a_word_elsewhere_starts_at_the_first_row()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var presenter = new RecordingPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            var directory = h.Store.NewFolder("highlight");
            Touch(directory, "aa1.txt", "aa2.txt", "aa3.txt", "ab1.txt", "ab2.txt", "ab3.txt");
            Indexed(h, directory);
            var (view, window, _, _) = OnScreen(h, Unresolved(h), directory);
            h.Main.OnMain(() => Type(view, "@a"));
            Until(h, window, () => view.Suggestions.Items.Count == 6);
            for (var step = 0; step < 4; step++) Assert.True(h.Main.OnMain(() => Press(view, Key.Down)));
            Assert.Equal(4, view.Suggestions.Selected);

            h.Main.OnMain(() => Type(view, "@aa"));
            Until(h, window, () => view.Suggestions.Items.Count == 3);
            Assert.Equal(2, view.Suggestions.Selected);
            Assert.Equal(0, view.Suggestions.Range.Location);

            h.Main.OnMain(() => Type(view, "@aa @a"));
            Until(h, window, () => view.Suggestions.Range.Location == 4);
            Assert.Equal(0, view.Suggestions.Selected);
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-045")] // App/Views/Composer/ComposerView.swift:312-319
    public void While_the_list_is_open_Up_and_Down_move_the_highlight_within_bounds_and_history_is_never_recalled()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = h.Runtime;
            h.Main.OnMain(() => runtime.RehearseTurn("an earlier prompt"));
            var view = Pill(h, runtime, workingDirectory: null);
            h.Main.OnMain(() => Type(view, "/"));
            Until(h, null, () => view.Suggestions.IsVisible);
            Assert.Equal(["/compact", "/plan"], Titles(view));
            Assert.True(h.Main.OnMain(() => Press(view, Key.Up)));
            Assert.Equal(0, view.Suggestions.Selected);
            Assert.True(h.Main.OnMain(() => Press(view, Key.Down)));
            Assert.Equal(1, view.Suggestions.Selected);
            Assert.True(h.Main.OnMain(() => Press(view, Key.Down)));
            Assert.Equal(1, view.Suggestions.Selected);
            Assert.True(h.Main.OnMain(() => Press(view, Key.Up)));
            Assert.Equal(0, view.Suggestions.Selected);
            Assert.Equal("/", runtime.Draft.Text);
        });
    }

    [Theory]
    [InlineData(Key.Tab, KeyModifiers.None)]
    [InlineData(Key.Enter, KeyModifiers.None)]
    [InlineData(Key.Enter, KeyModifiers.Control)]
    [Trait("Req", "PR-COMPOSER-045")] // App/Views/Composer/ComposerView.swift:320-322
    public void Tab_Return_and_Command_Return_each_commit_the_highlighted_row(Key key, KeyModifiers modifiers)
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            using var shared = ShortcutStore.Shared.Override(FreshStore());
            var runtime = h.Runtime;
            var view = Pill(h, runtime, workingDirectory: null);
            h.Main.OnMain(() => Type(view, "/"));
            Until(h, null, () => view.Suggestions.IsVisible);
            Assert.True(h.Main.OnMain(() => Press(view, Key.Down)));
            Assert.True(h.Main.OnMain(() => Press(view, key, modifiers)));
            Until(h, null, () => runtime.Draft.Text.Length == 0);
            Assert.Equal(new DraftCommand { Name = "plan", Detail = "Turn plan mode on or off", IsBuiltIn = true }, runtime.Draft.Command);
            Assert.False(view.Suggestions.IsVisible);
            Assert.Empty(runtime.Turns);
            Assert.Empty(runtime.FollowUps);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-045")] // App/Views/Composer/ComposerView.swift:323-326
    public void Escape_closes_the_open_list_and_is_consumed_leaving_the_terminal_open()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = h.Runtime;
            var view = Pill(h, runtime, workingDirectory: null);
            h.Main.OnMain(() => runtime.IsTerminalVisible = true);
            h.Main.OnMain(() => Type(view, "/"));
            Until(h, null, () => view.Suggestions.IsVisible);
            Assert.True(h.Main.OnMain(() => Press(view, Key.Escape)));
            Assert.False(view.Suggestions.IsVisible);
            Assert.True(runtime.IsTerminalVisible);
            Assert.Equal("/", runtime.Draft.Text);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-045")] // App/Views/Composer/ComposerView.swift:327-328
    public void Backspace_at_the_start_is_not_consumed_while_the_list_is_open_so_the_chip_stays()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = h.Runtime;
            var view = Pill(h, runtime, workingDirectory: null);
            var chip = new DraftCommand { Name = "plan", Detail = "Turn plan mode on or off", IsBuiltIn = true };
            h.Main.OnMain(() => Type(view, "/"));
            Until(h, null, () => view.Suggestions.IsVisible);
            h.Main.OnMain(() => runtime.Draft = runtime.Draft with { Command = chip });
            // The caret goes to the start and Backspace follows in the same turn, before the refresh that closes the
            // list has run.
            var handled = h.Main.OnMain(() =>
            {
                var box = One<ComposerNSTextView>(view);
                box.SelectionStart = box.SelectionEnd = 0;
                Assert.True(view.Suggestions.IsVisible);
                return Press(view, Key.Back);
            });
            Assert.False(handled);
            Assert.Equal(chip, runtime.Draft.Command);
        });
    }

    // ---- the panel (:239-262) ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-048")] // App/Views/Composer/ComposerView.swift:190-196, :244
    [Trait("Req", "UI-COMPOSER-019")] // App/Views/Composer/ComposerView.swift:244
    public void The_file_list_is_shown_in_the_panel_under_the_header_Files()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var presenter = new RecordingPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            var directory = h.Store.NewFolder("header");
            Touch(directory, "main.swift", "mail.txt");
            Indexed(h, directory);
            var (view, window, _, _) = OnScreen(h, Unresolved(h), directory);
            h.Main.OnMain(() => Type(view, "@mai"));
            Until(h, window, () => view.Suggestions.IsVisible && presenter.Calls.Count > 0);
            var frame = Shown(presenter);
            Assert.Equal("Files", HeaderText(frame));
            Assert.Equal(2, LogicalQuery.SelfAndDescendants<SuggestionPopoverRow>(frame).Count());
            Assert.Equal(44 + 2 * 26, frame.Height); // ComposerTextView.swift:70 with itemCount 2
            Assert.Empty(LogicalQuery.SelfAndDescendants<PopoverNote>(frame));
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-048")] // App/Views/Composer/ComposerView.swift:244
    [Trait("Req", "UI-COMPOSER-019")] // App/Views/Composer/ComposerView.swift:244
    public void The_command_list_is_headed_Commands_middle_dot_Recently_used_first()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var view = Pill(h, h.Runtime, workingDirectory: null);
            h.Main.OnMain(() => Type(view, "/"));
            Until(h, null, () => view.Suggestions.IsVisible);
            var menu = h.Main.OnMain(() => view.SuggestionMenu());
            Assert.Equal("Commands \U000000B7 Recently used first", HeaderText(menu)); // U+00B7 MIDDLE DOT
            Assert.Equal(2, LogicalQuery.SelfAndDescendants<SuggestionPopoverRow>(menu).Count());
        });
    }

    // ---- pick (:487-501) ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-049")] // App/Views/Composer/ComposerView.swift:487-497
    public void Picking_a_command_deletes_the_typed_slash_word_and_puts_a_chip_with_its_name_detail_and_flag_on_the_draft()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var directory = h.Store.NewFolder("pick");
            var runtime = h.Runtime;
            var view = Pill(h, runtime, directory);
            h.Main.OnMain(() =>
            {
                h.App.Providers.UpdateCommands(new ValueList<SlashCommand>([new SlashCommand { Name = "review", Detail = "Review the diff" }]), ProviderKind.Codex, directory);
                Type(view, "/rev");
            });
            Until(h, null, () => view.Suggestions.IsVisible);
            Assert.True(h.Main.OnMain(() => Press(view, Key.Enter)));
            Assert.False(view.Suggestions.IsVisible);
            Until(h, null, () => runtime.Draft.Text.Length == 0);
            Assert.Equal("", One<ComposerNSTextView>(view).Text);
            Assert.Equal(new DraftCommand { Name = "review", Detail = "Review the diff", IsBuiltIn = false }, runtime.Draft.Command);
            Assert.Empty(runtime.Turns);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-049")] // App/Views/Composer/ComposerView.swift:487-490, :499-500
    public void Picking_a_file_replaces_the_typed_at_word_with_at_path_space_and_leaves_the_caret_after_it()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var presenter = new RecordingPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            var directory = h.Store.NewFolder("pickfile");
            Touch(directory, "main.swift");
            Indexed(h, directory);
            var runtime = Unresolved(h);
            var (view, window, _, _) = OnScreen(h, runtime, directory);
            h.Main.OnMain(() => Type(view, "see @mai and"));
            h.Main.OnMain(() =>
            {
                var box = One<ComposerNSTextView>(view);
                box.SelectionStart = box.SelectionEnd = 8;
            });
            Until(h, window, () => view.Suggestions.IsVisible);
            Assert.Equal((4, 4), view.Suggestions.Range);
            Assert.True(h.Main.OnMain(() => Press(view, Key.Tab)));
            Until(h, window, () => runtime.Draft.Text == "see @main.swift  and");
            var box = h.Main.OnMain(() => One<ComposerNSTextView>(view));
            Assert.Equal("see @main.swift ".Length, h.Main.OnMain(() => box.SelectionStart));
            Assert.Null(runtime.Draft.Command);
            Assert.True(h.Main.OnMain(() => box.IsFocused));
            Close(h, window);
        });
    }

    // ---- command loading and its failure (:245-248, :264-273) ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-050")] // App/Views/Composer/ComposerView.swift:245-248
    [Trait("Req", "UI-COMPOSER-021")] // App/Views/Composer/ComposerView.swift:245-246
    [Trait("Req", "UI-COMPOSER-022")] // App/Views/Composer/ComposerView.swift:247
    public void A_failed_command_load_shows_its_description_then_a_Retry_loading_commands_row_with_the_arrow_clockwise_symbol()
    {
        headless.OnUiThread(() =>
        {
            System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
            using var h = new ThreadRuntimePart12Harness(ProviderKind.Claude);
            Loaded(h, h.Runtime);
            var directory = h.Store.NewFolder("failing");
            h.Main.OnMain(() => h.App.Settings.SetBinaryPath(FailingCommandCLI(), ProviderKind.Claude));
            var view = Pill(h, h.Runtime, directory);
            h.Main.Loop.Run(() => view.LoadCommands(), AppModelHarness.Bound);
            var key = ProviderRegistry.CommandKey(ProviderKind.Claude, directory);
            // ClaudeSession.swift:208: the process ended before it answered the handshake.
            Assert.Equal("Claude's command discovery did not finish. Try again.", h.App.Providers.CommandErrors[key]);

            h.Main.OnMain(() => Type(view, "/"));
            Until(h, null, () => view.Suggestions.IsVisible);
            var menu = h.Main.OnMain(() => view.SuggestionMenu());
            var note = One<PopoverNote>(menu);
            Assert.Equal("Claude's command discovery did not finish. Try again.", Assert.IsType<TextBlock>(note.Child).Text);
            var retry = One<PopoverItem>(menu);
            var button = One<Button>(retry);
            Assert.Equal("Retry loading commands", Avalonia.Automation.AutomationProperties.GetName(button));
            var glyph = LogicalQuery.SelfAndDescendants<SwiftImage>(retry).Single(image => image.SymbolName is not null);
            Assert.Equal("arrow.clockwise", glyph.SymbolName);
            // In order: the header, the note, the retry row, then the rows.
            var order = LogicalQuery.SelfAndDescendants<Control>(menu)
                .Where(control => control is PopoverSectionHeader or PopoverNote or PopoverItem or SuggestionPopoverRow)
                .Select(control => control.GetType().Name).ToArray();
            Assert.Equal(["PopoverSectionHeader", "PopoverNote", "PopoverItem", "SuggestionPopoverRow", "SuggestionPopoverRow"], order);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-050")] // App/Views/Composer/ComposerView.swift:245
    [Trait("Req", "UI-COMPOSER-022")] // App/Views/Composer/ComposerView.swift:245-247: the retry row only with the error and only in the command list
    [Trait("Rule", "R-TEST-4")] // The row's symbol, label and place: A_failed_command_load_shows_its_description_then_a_Retry_...; the forced reload: B3_retry_enters_a_new_command_load_even_when_the_cached_commands_are_fresh
    public void The_failure_rows_belong_to_the_command_list_only()
    {
        headless.OnUiThread(() =>
        {
            System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
            using var h = new ThreadRuntimePart12Harness(ProviderKind.Claude);
            Loaded(h, h.Runtime);
            var directory = h.Store.NewFolder("failing-files");
            Touch(directory, "main.swift");
            h.Main.OnMain(() => h.App.Settings.SetBinaryPath(FailingCommandCLI(), ProviderKind.Claude));
            var view = Pill(h, h.Runtime, directory);
            h.Main.Loop.Run(() => view.LoadCommands(), AppModelHarness.Bound);
            Assert.NotEmpty(h.App.Providers.CommandErrors);
            h.Main.OnMain(() => Type(view, "/"));
            Until(h, null, () => view.Suggestions.IsVisible);
            h.Main.OnMain(() => Type(view, "/zzzz"));
            Until(h, null, () => !view.Suggestions.IsVisible);
            var menu = h.Main.OnMain(() => view.SuggestionMenu());
            // With the list closed the kind is the default, file: no failure rows.
            Assert.Empty(LogicalQuery.SelfAndDescendants<PopoverNote>(menu));
            Assert.Empty(LogicalQuery.SelfAndDescendants<PopoverItem>(menu));
            Assert.Equal("Files", HeaderText(menu));
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-051")] // App/Views/Composer/ComposerView.swift:264-273
    public void Loading_commands_asks_the_registry_for_the_thread_provider_in_the_working_directory()
    {
        headless.OnUiThread(() =>
        {
            System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
            using var h = new ThreadRuntimePart12Harness(ProviderKind.Claude);
            Loaded(h, h.Runtime);
            var directory = h.Store.NewFolder("keyed");
            h.Main.OnMain(() => h.App.Settings.SetBinaryPath(FailingCommandCLI(), ProviderKind.Claude));
            var view = Pill(h, h.Runtime, directory);
            h.Main.Loop.Run(() => view.LoadCommands(), AppModelHarness.Bound);
            Assert.Equal([ProviderRegistry.CommandKey(ProviderKind.Claude, directory)], h.App.Providers.CommandErrors.Keys.ToArray());
            Assert.Equal("claude\n", ProviderRegistry.CommandKey(ProviderKind.Claude, directory)[..7]);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-051")] // App/Views/Composer/ComposerView.swift:265, :270
    public void Without_a_working_directory_no_commands_are_loaded()
    {
        headless.OnUiThread(() =>
        {
            System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
            using var h = new ThreadRuntimePart12Harness(ProviderKind.Claude);
            Loaded(h, h.Runtime);
            h.Main.OnMain(() => h.App.Settings.SetBinaryPath(FailingCommandCLI(), ProviderKind.Claude));
            var view = Pill(h, h.Runtime, workingDirectory: null);
            h.Main.Loop.Run(() => view.LoadCommands(), AppModelHarness.Bound);
            Assert.Empty(h.App.Providers.CommandErrors);
            Assert.Empty(h.App.Providers.Commands);
        });
    }

    // ---- command loading: a CLI that cannot be launched, a failing retry, one launch for many composers (:245-248, :264-273) ----

    private const string ListingOf = "{\"type\":\"control_response\",\"response\":{\"request_id\":\"handshake\",\"response\":{\"commands\":[{\"name\":\"proof-shared\",\"description\":\"Shared fixture command\"}]}}}\n";
    private const string ListingWithoutCommands = "{\"type\":\"control_response\",\"response\":{\"request_id\":\"handshake\",\"response\":{\"models\":[]}}}\n";

    private static System.Collections.IDictionary InFlightCommandLoads(ThreadRuntimePart12Harness h) =>
        (System.Collections.IDictionary)typeof(ProviderRegistry).GetField("commandLoads", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(h.App.Providers)!;

    // The catalogue handshake the mounted model picker would start is not what these tests are about.
    private static void SkipCatalogHandshake(ThreadRuntimePart12Harness h) =>
        ((HashSet<ProviderKind>)typeof(ProviderRegistry).GetField("attemptedCatalogs", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(h.App.Providers)!).Add(ProviderKind.Claude);

    [Fact]
    [Trait("Req", "PR-COMPOSER-050")] // App/Views/Composer/ComposerView.swift:245-248; Services/Providers/ProviderRegistry.swift:855-858
    [Trait("Req", "PR-COMPOSER-051")] // App/Views/Composer/ComposerView.swift:264-273
    public void A_command_list_for_a_folder_that_is_gone_shows_the_launch_failure_and_a_Retry_row_instead_of_ending_the_app()
    {
        headless.OnUiThread(() =>
        {
            System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
            using var h = new ThreadRuntimePart12Harness(ProviderKind.Claude);
            Loaded(h, h.Runtime);
            var gone = Path.Combine(h.Store.NewFolder("folder-gone"), "deleted"); // never created: the thread's folder is not there
            h.Main.OnMain(() => h.App.Settings.SetBinaryPath(FailingCommandCLI(), ProviderKind.Claude));
            // The text of that launch failure, taken from the very launch run directly (a locale proof: it is never typed here).
            var launchFailure = h.Main.Loop.Run<string?>(async () =>
            {
                try
                {
                    await ClaudeSession.Handshake(h.App.Providers.Executable(ProviderKind.Claude)!,
                        SwiftURL.Create("marrow-native-path:" + gone.Replace('\\', '/'))!, h.App.Providers.Environment(ProviderKind.Claude));
                    return null;
                }
                catch (Exception error) { return error.Message; }
            }, AppModelHarness.Bound);
            Assert.False(string.IsNullOrEmpty(launchFailure));

            var view = Pill(h, h.Runtime, gone);
            h.Main.Loop.Run(() => view.LoadCommands(), AppModelHarness.Bound); // used to throw out of the load and end the process
            var key = ProviderRegistry.CommandKey(ProviderKind.Claude, gone);
            Assert.Equal(launchFailure, h.App.Providers.CommandErrors[key]);
            Assert.Empty(InFlightCommandLoads(h));

            h.Main.OnMain(() => Type(view, "/"));
            Until(h, null, () => view.Suggestions.IsVisible);
            var menu = h.Main.OnMain(() => view.SuggestionMenu());
            Assert.Equal(launchFailure, Assert.IsType<TextBlock>(One<PopoverNote>(menu).Child).Text);
            Assert.Equal("Retry loading commands", Avalonia.Automation.AutomationProperties.GetName(One<Button>(One<PopoverItem>(menu))));
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-050")] // App/Views/Composer/ComposerView.swift:245-248, :269-273; Services/Providers/ProviderRegistry.swift:855-858
    public void A_failing_retry_replaces_the_note_with_the_new_description()
    {
        headless.OnUiThread(() =>
        {
            System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
            using var h = new ThreadRuntimePart12Harness(ProviderKind.Claude);
            Loaded(h, h.Runtime);
            // The recording of the stand-in is taken under its folder lock (WaitForRelease, then released at once, so no listing is held): a listing and
            // the runtime's session warm-up are separate processes, and without the lock two of them can claim one call number and lose a record.
            var script = ProviderProbeStandIns.CopyFor("composer-retry-fails-again").SetExitCode(1).WaitForRelease();
            script.Release();
            var directory = h.Store.NewFolder("retry-fails-again");
            h.Main.OnMain(() => h.App.Settings.SetBinaryPath(script.ExecutablePath, ProviderKind.Claude));
            var view = Pill(h, h.Runtime, directory);
            h.Main.Loop.Run(() => view.LoadCommands(force: true), AppModelHarness.Bound);
            var key = ProviderRegistry.CommandKey(ProviderKind.Claude, directory);
            Assert.Equal("Claude's command discovery did not finish. Try again.", h.App.Providers.CommandErrors[key]); // ClaudeSession.swift:208

            h.Main.OnMain(() => Type(view, "/"));
            Until(h, null, () => view.Suggestions.IsVisible);
            var menu = h.Main.OnMain(() => view.SuggestionMenu());
            Assert.Equal("Claude's command discovery did not finish. Try again.", Assert.IsType<TextBlock>(One<PopoverNote>(menu).Child).Text);

            // Opening the panel asked again, as the list is not fresh (:213-215): that is the second listing; let it end before the retry.
            var loads = InFlightCommandLoads(h);
            Until(h, null, () => script.CommandListings().Count == 2 && loads.Count == 0);
            var listed = script.CommandListings().Count;
            // The retry fails again, in another way: the CLI answers the handshake with no commands (ClaudeSession.swift:203-205).
            script.SetStdout(ListingWithoutCommands);
            h.Main.OnMain(() => Click(One<Button>(One<PopoverItem>(menu))));
            Until(h, null, () => loads.Count > 0);
            Until(h, null, () => loads.Count == 0);
            Assert.Equal(listed + 1, script.CommandListings().Count); // the retry is one more listing
            Assert.Equal("Claude could not load its commands.", h.App.Providers.CommandErrors[key]);
            var next = h.Main.OnMain(() => view.SuggestionMenu());
            // The note is replaced, not added to, and the Retry row is still the one row of its kind.
            Assert.Equal("Claude could not load its commands.", Assert.IsType<TextBlock>(One<PopoverNote>(next).Child).Text);
            Assert.Equal("Retry loading commands", Avalonia.Automation.AutomationProperties.GetName(One<Button>(One<PopoverItem>(next))));
        });
    }

    // REVIEW (defects-priority, round 3). PR-COMPOSER-050's clause "Activating it reloads with force, ignoring the ten-minute freshness window" was proven only by
    // a test tagged UI-COMPOSER-022 (B3_retry_enters_a_new_command_load_even_when_the_cached_commands_are_fresh): the mutant "Retry loads without force" survived every
    // test tagged PR-COMPOSER-050, because the failing-retry tests above hold no fresh list, so a load that is not forced runs for them too. This one holds a list that
    // is fresh and counts the launches of the CLI, the side effect the record names ("A forced reload launches the provider CLI to list commands").
    [Fact]
    [Trait("Req", "PR-COMPOSER-050")] // App/Views/Composer/ComposerView.swift:247 PopoverItem("Retry loading commands") { Task { await loadCommands(force: true) } }; Services/Providers/ProviderRegistry.swift:840-845 (force asks again whatever the list's age)
    [Trait("Req", "UI-COMPOSER-022")] // App/Views/Composer/ComposerView.swift:247
    public void Activating_Retry_loading_commands_launches_the_CLI_to_list_again_although_the_commands_are_still_within_their_ten_minutes()
    {
        headless.OnUiThread(() =>
        {
            System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
            using var h = new ThreadRuntimePart12Harness(ProviderKind.Claude);
            Loaded(h, h.Runtime);
            // The stand-in's recording is taken under its folder lock (see A_failing_retry_replaces_the_note_with_the_new_description).
            var script = ProviderProbeStandIns.CopyFor("composer-retry-forces").SetExitCode(1).WaitForRelease();
            script.Release();
            var directory = h.Store.NewFolder("retry-forces");
            var key = ProviderRegistry.CommandKey(ProviderKind.Claude, directory);
            h.Main.OnMain(() =>
            {
                h.App.Settings.SetBinaryPath(script.ExecutablePath, ProviderKind.Claude);
                SkipCatalogHandshake(h);
                h.App.Providers.UpdateCommands(new ValueList<SlashCommand>([new() { Name = "cached", Detail = "Cached command" }]), ProviderKind.Claude, directory); // fetched just now
            });
            var view = Pill(h, h.Runtime, directory);

            // The list is fresh: a load that is not forced is skipped, nothing is launched (ProviderRegistry.swift:845).
            h.Main.Loop.Run(() => view.LoadCommands(), AppModelHarness.Bound);
            Assert.Empty(script.CommandListings());
            Assert.Empty(h.App.Providers.CommandErrors);

            // A forced load that fails puts its error beside the fresh list: the state in which the Retry row shows. That is the first launch.
            h.Main.Loop.Run(() => view.LoadCommands(force: true), AppModelHarness.Bound);
            Assert.Single(script.CommandListings());
            Assert.Equal("Claude's command discovery did not finish. Try again.", h.App.Providers.CommandErrors[key]);
            h.Main.Loop.Run(() => view.LoadCommands(), AppModelHarness.Bound); // still fresh: skipped again, so only force gets past the window
            Assert.Single(script.CommandListings());

            h.Main.OnMain(() => Type(view, "/"));
            Until(h, null, () => view.Suggestions.IsVisible);
            var menu = h.Main.OnMain(() => view.SuggestionMenu());
            Assert.Equal("Claude's command discovery did not finish. Try again.", Assert.IsType<TextBlock>(One<PopoverNote>(menu).Child).Text);
            var retry = One<Button>(One<PopoverItem>(menu));
            Assert.Equal("Retry loading commands", Avalonia.Automation.AutomationProperties.GetName(retry));
            Assert.Single(script.CommandListings()); // opening the panel did not list again: the list is fresh

            // Activating Retry lists again: a second launch, although the list is still within its ten minutes.
            var loads = InFlightCommandLoads(h);
            h.Main.OnMain(() => Click(retry));
            h.Main.Loop.RunUntil(() => { Drain(); return script.CommandListings().Count == 2 && loads.Count == 0; }, TimeSpan.FromSeconds(30));
            Assert.Equal(2, script.CommandListings().Count);
            Assert.Equal("Claude's command discovery did not finish. Try again.", h.App.Providers.CommandErrors[key]); // the retry failed in the same way: the note is the new load's
            Assert.Equal("cached", h.App.Providers.Commands[key][0].Name); // the list it did not replace stays
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-051")] // App/Views/Composer/ComposerView.swift:264-273; Services/Providers/ProviderRegistry.swift:762-784 (a second caller awaits the first task), ED-033 (one key for two spellings of a folder)
    public void Two_composers_on_one_provider_and_folder_launch_the_CLI_once_and_two_spellings_of_the_folder_share_the_entry()
    {
        headless.OnUiThread(() =>
        {
            System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
            using var h = new ThreadRuntimePart12Harness(ProviderKind.Claude);
            Loaded(h, h.Runtime);
            var frames = new ManualFrameSource();
            using var frameSeam = FrameClock.Source.Override(frames);
            var script = ProviderProbeStandIns.CopyFor("composer-two-composers").WaitForRelease().SetExitCode(0).SetStdout(ListingOf);
            h.Main.OnMain(() => h.App.Settings.SetBinaryPath(script.ExecutablePath, ProviderKind.Claude));
            SkipCatalogHandshake(h);
            var directory = h.Store.NewFolder("two-composers");
            var key = ProviderRegistry.CommandKey(ProviderKind.Claude, directory);
            var loads = InFlightCommandLoads(h);
            var (first, firstWindow, _, _) = OnScreen(h, h.Runtime, directory);
            AvaloniaWindow? secondWindow = null, thirdWindow = null;
            try
            {
                Until(h, firstWindow, () => script.CommandListings().Count == 1 && loads.Contains(key)); // the first composer's mount starts the listing
                // A second composer, for the same folder spelled another way (upper case with a trailing separator): it asks while the first listing is
                // held and joins it, as the key is one and the listing is in flight.
                var spelling = directory.ToUpperInvariant() + Path.DirectorySeparatorChar;
                Assert.NotEqual(directory, spelling);
                Assert.Equal(key, ProviderRegistry.CommandKey(ProviderKind.Claude, spelling));
                var (second, window2, _, _) = OnScreen(h, h.Runtime, spelling);
                secondWindow = window2;
                Pump(h, secondWindow);
                Assert.Single(script.CommandListings()); // still one launch: the second awaits the held one
                Assert.True(loads.Contains(key));
                script.Release();
                Until(h, firstWindow, () => !loads.Contains(key) && h.App.Providers.Commands.ContainsKey(key));
                Assert.Equal(["proof-shared"], h.App.Providers.Commands[key].Select(command => command.Name));
                Assert.Single(script.CommandListings());
                // Inside the 600 s the cache is fresh: a third composer asks and costs nothing.
                var (third, window3, _, _) = OnScreen(h, h.Runtime, directory);
                thirdWindow = window3;
                Pump(h, thirdWindow);
                Assert.Single(script.CommandListings());
                Assert.Single(h.App.Providers.Commands.Keys, candidate => candidate.StartsWith("claude\n", StringComparison.Ordinal));
            }
            finally
            {
                script.Release();
                if (thirdWindow is not null) Close(h, thirdWindow);
                if (secondWindow is not null) Close(h, secondWindow);
                Close(h, firstWindow);
            }
        });
    }

    [Theory]
    [InlineData(ProviderKind.Copilot)]
    [InlineData(ProviderKind.Cursor)]
    [InlineData(ProviderKind.Deepseek)]
    [Trait("Req", "PR-COMPOSER-051")] // App/Views/Composer/ComposerView.swift:264-273; Services/Providers/ProviderRegistry.swift:763 (only Codex and Claude list commands)
    public void A_composer_for_a_provider_other_than_codex_or_claude_launches_nothing(ProviderKind provider)
    {
        headless.OnUiThread(() =>
        {
            System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
            using var h = new ThreadRuntimePart12Harness(provider);
            Loaded(h, h.Runtime);
            var frames = new ManualFrameSource();
            using var frameSeam = FrameClock.Source.Override(frames);
            var script = ProviderProbeStandIns.CopyFor("composer-other-provider").SetExitCode(0).SetStdout(ListingOf);
            // Every provider's executable points at the stand-in, so that nothing but the provider guard (ProviderRegistry.swift:763) stands between
            // this composer and a launch: if it launched one, the stand-in would record it.
            h.Main.OnMain(() =>
            {
                h.App.Settings.SetBinaryPath(script.ExecutablePath, ProviderKind.Claude);
                h.App.Settings.SetBinaryPath(script.ExecutablePath, ProviderKind.Codex);
                h.App.Settings.SetBinaryPath(script.ExecutablePath, provider);
            });
            var presenter = new RecordingPresenter();
            using var presentation = Presentation.Presenter.Override(presenter);
            var (view, window, _, _) = OnScreen(h, h.Runtime, h.Store.NewFolder("other-provider"));
            try
            {
                Pump(h, window);
                h.Main.OnMain(() => Type(view, "/"));
                // The composer asks for the commands as soon as the slash is typed, in a main-actor job that one pump runs; LoadCommands registers its load
                // in commandLoads before its first await (ProviderRegistry.swift:781), so a launch that was attempted is in the map by now: there is nothing to
                // wait for, and no clock to wait on. (A launch that completed and left the map would have been recorded by the stand-in instead.)
                Pump(h, window);
                var loads = InFlightCommandLoads(h);
                Assert.Empty(loads);
                Assert.Empty(script.CommandListings());
                Assert.DoesNotContain(script.RecordedCalls(), call => call.Contains("--no-session-persistence"));
                Assert.Empty(h.App.Providers.Commands);
                Assert.Empty(h.App.Providers.CommandErrors);
            }
            finally { Close(h, window); }
        });
    }

    // ---- focus and visibility (:197-203, :228-237) ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-053")] // App/Views/Composer/ComposerView.swift:228-237
    public void Focus_leaving_for_a_control_outside_the_panel_clears_the_list_and_closes_the_panel()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var presenter = new RecordingPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            var directory = h.Store.NewFolder("blur");
            Touch(directory, "main.swift");
            Indexed(h, directory);
            var (view, window, other, _) = OnScreen(h, Unresolved(h), directory);
            h.Main.OnMain(() => Type(view, "@ma"));
            Until(h, window, () => view.Suggestions.IsVisible && presenter.Calls.Count > 0);
            h.Main.OnMain(() => other.Focus());
            Until(h, window, () => !view.Suggestions.IsVisible && !presenter.Calls[^1].Presented);
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-054")] // App/Views/Composer/ComposerView.swift:197
    public void The_panel_closes_when_the_composer_leaves_the_screen()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var presenter = new RecordingPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            var directory = h.Store.NewFolder("disappear");
            Touch(directory, "main.swift");
            Indexed(h, directory);
            var (view, window, _, stack) = OnScreen(h, Unresolved(h), directory);
            h.Main.OnMain(() => Type(view, "@ma"));
            Until(h, window, () => view.Suggestions.IsVisible && presenter.Calls.Count > 0);
            var shown = presenter.Calls[^1].Popup;
            h.Main.OnMain(() => stack.Children.Remove(view));
            Pump(h, window);
            Assert.Equal((shown, false), presenter.Calls[^1]);
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-054")] // App/Views/Composer/ComposerView.swift:201-203
    public void Hiding_the_chat_column_closes_the_panel_and_showing_it_again_gives_the_text_the_keyboard()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var presenter = new RecordingPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            var directory = h.Store.NewFolder("column");
            Touch(directory, "main.swift");
            Indexed(h, directory);
            var (view, window, other, stack) = OnScreen(h, Unresolved(h), directory);
            h.Main.OnMain(() => Type(view, "@ma"));
            Until(h, window, () => view.Suggestions.IsVisible && presenter.Calls.Count > 0);
            var shown = presenter.Calls[^1].Popup;
            h.Main.OnMain(() => Marrow.App.Views.RootView.SetChatColumnIsShown(stack, false));
            Pump(h, window);
            Assert.Equal((shown, false), presenter.Calls[^1]);

            h.Main.OnMain(() => other.Focus());
            Pump(h, window);
            var box = h.Main.OnMain(() => One<ComposerNSTextView>(view));
            Assert.False(h.Main.OnMain(() => box.IsFocused));
            h.Main.OnMain(() => Marrow.App.Views.RootView.SetChatColumnIsShown(stack, true));
            Pump(h, window);
            Assert.True(h.Main.OnMain(() => box.IsFocused));
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-054")] // App/Views/Composer/ComposerView.swift:202
    public void A_composer_that_does_not_take_focus_on_appear_leaves_the_keyboard_where_it_is_when_its_column_returns()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var (view, window, other, stack) = OnScreen(h, Unresolved(h), workingDirectory: null, takesFocusOnAppear: false);
            h.Main.OnMain(() => Marrow.App.Views.RootView.SetChatColumnIsShown(stack, false));
            Pump(h, window);
            h.Main.OnMain(() => other.Focus());
            Pump(h, window);
            h.Main.OnMain(() => Marrow.App.Views.RootView.SetChatColumnIsShown(stack, true));
            Pump(h, window);
            Assert.False(h.Main.OnMain(() => One<ComposerNSTextView>(view).IsFocused));
            Assert.True(h.Main.OnMain(() => other.IsFocused));
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-055")] // App/Views/Composer/ComposerView.swift:249-256, :1096-1099
    public void Hovering_a_row_highlights_it_so_Return_commits_that_row()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var presenter = new RecordingPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            var directory = h.Store.NewFolder("hover");
            Touch(directory, "ma1.txt", "ma2.txt", "ma3.txt");
            Indexed(h, directory);
            var runtime = Unresolved(h);
            var (view, window, _, _) = OnScreen(h, runtime, directory);
            h.Main.OnMain(() => Type(view, "@ma"));
            Until(h, window, () => view.Suggestions.Items.Count == 3 && presenter.Calls.Count > 0);
            Assert.Equal(0, view.Suggestions.Selected);
            var rows = LogicalQuery.SelfAndDescendants<SuggestionPopoverRow>(Shown(presenter)).ToArray();
            Assert.Equal(3, rows.Length);
            h.Main.OnMain(() =>
            {
                var button = One<Button>(rows[2]);
                button.RaiseEvent(new PointerEventArgs(
                    InputElement.PointerEnteredEvent, button, null!, button, default, 0, new PointerPointProperties(), KeyModifiers.None));
            });
            Until(h, window, () => view.Suggestions.Selected == 2);
            var third = view.Suggestions.Items[2];
            Assert.True(h.Main.OnMain(() => Press(view, Key.Enter)));
            Until(h, window, () => runtime.Draft.Text == third.Value);
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-055")] // App/Views/Composer/ComposerView.swift:1088-1091
    [Trait("Req", "UI-COMPOSER-020")] // App/Views/Composer/ComposerView.swift:1069-1099
    public void The_highlighted_row_is_filled_with_the_overlay_at_a_tenth_and_the_others_are_clear()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            var presenter = new RecordingPresenter();
            using var seam = Presentation.Presenter.Override(presenter);
            var directory = h.Store.NewFolder("fill");
            Touch(directory, "ma1.txt", "ma2.txt");
            Indexed(h, directory);
            var (view, window, _, _) = OnScreen(h, Unresolved(h), directory);
            h.Main.OnMain(() => Type(view, "@ma"));
            Until(h, window, () => view.Suggestions.Items.Count == 2 && presenter.Calls.Count > 0);
            Assert.True(h.Main.OnMain(() => Press(view, Key.Down)));
            Until(h, window, () => view.Suggestions.Selected == 1);
            Pump(h, window);
            var rows = LogicalQuery.SelfAndDescendants<SuggestionPopoverRow>(Shown(presenter)).ToArray();
            Color Fill(SuggestionPopoverRow row) =>
                Assert.IsAssignableFrom<ISolidColorBrush>(LogicalQuery.SelfAndDescendants<Border>(row).First(border => border.Background is not null).Background).Color;
            Assert.Equal(0, Fill(rows[0]).A);
            Assert.Equal(Chrome.Overlay(0.1).Resolve(rows[1].ActualThemeVariant), Fill(rows[1]));
            Close(h, window);
        });
    }

    // ---- the file index (:1103-1288) ----

    private static AppModelHarness IndexStore()
    {
        System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        return new AppModelHarness();
    }

    private static ValueList<string> Search(AppModelHarness store, FileIndex index, string query, long limit = 8) =>
        store.Main.Loop.Run(() => index.Search(query, limit), AppModelHarness.Bound);

    private static FileIndex Prepared(AppModelHarness store, string directory)
    {
        var index = store.Main.OnMain(() => new FileIndex());
        store.Main.Loop.Run(() => index.Prepare(directory), AppModelHarness.Bound);
        return index;
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-056")] // App/Views/Composer/ComposerView.swift:1121-1135
    public void A_built_index_is_shared_so_a_second_composer_in_the_same_directory_is_served_without_a_rebuild()
    {
        using var store = IndexStore();
        var directory = store.NewFolder("shared");
        Touch(directory, "one.txt");
        var first = Prepared(store, directory);
        Assert.Equal(["one.txt"], Search(store, first, "one"));
        Touch(directory, "two.txt");
        var second = Prepared(store, directory);
        Assert.Empty(Search(store, second, "two"));
        Assert.Equal(["one.txt"], Search(store, second, "one"));
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-056")] // App/Views/Composer/ComposerView.swift:1127, :1133-1157
    public void An_index_is_rebuilt_only_once_it_is_more_than_60_seconds_old()
    {
        using var store = IndexStore();
        var directory = store.NewFolder("fresh");
        Touch(directory, "one.txt");
        Prepared(store, directory);
        Touch(directory, "two.txt");
        store.Main.Advance(TimeSpan.FromSeconds(60));
        Assert.Empty(Search(store, Prepared(store, directory), "two"));
        store.Main.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(["two.txt"], Search(store, Prepared(store, directory), "two"));
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-056")] // App/Views/Composer/ComposerView.swift:1131-1132, :1155
    public void A_prepare_whose_directory_changed_under_it_abandons_its_result_and_the_build_is_still_shared()
    {
        using var store = IndexStore();
        var first = store.NewFolder("first");
        var second = store.NewFolder("second");
        Touch(first, "alpha.txt");
        Touch(second, "beta.txt");
        var index = store.Main.OnMain(() => new FileIndex());
        store.Main.Loop.Run(() => System.Threading.Tasks.Task.WhenAll(index.Prepare(first), index.Prepare(second)), AppModelHarness.Bound);
        Assert.Empty(Search(store, index, "alpha"));
        Assert.Equal(["beta.txt"], Search(store, index, "beta"));
        Assert.Equal(["alpha.txt"], Search(store, Prepared(store, first), "alpha"));
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-056")] // App/Views/Composer/ComposerView.swift:1130-1131, :1155
    public void A_cancelled_prepare_leaves_the_index_as_it_was()
    {
        using var store = IndexStore();
        var kept = store.NewFolder("kept");
        var abandoned = store.NewFolder("abandoned");
        Touch(kept, "kept.txt");
        Touch(abandoned, "gone.txt");
        var index = Prepared(store, kept);
        store.Main.OnMain(() =>
        {
            var task = MainActor.Spawn(() => index.Prepare(abandoned));
            task.Cancel();
        });
        store.Main.RunUntilIdle();
        Assert.Equal(["kept.txt"], Search(store, index, "kept"));
    }

    // The building map is a static shared by every composer (:1125-1126), and a build leaves it only when its task,
    // spawned on the main actor (:1146-1152), runs to its end there. A test that ends with its composer's build still
    // under way must not leave that build to a main loop nobody pumps again: every later composer in the directory
    // joins it (:1140-1141), and once two such builds are left, every composer in a new directory waits on one of
    // them (:1142-1144). Here prepare runs alone, so its build is registered and its start is still queued when the
    // harness goes.
    [Fact]
    [Trait("Rule", "R-TEST-5")] // App/Views/Composer/ComposerView.swift:1125-1126, :1140-1152
    public void A_build_still_queued_when_its_harness_is_disposed_is_finished_so_a_later_composer_is_served_from_it()
    {
        string directory;
        using (var store = IndexStore())
        {
            directory = store.NewFolder("left");
            Touch(directory, "left.txt");
            var index = store.Main.OnMain(() => new FileIndex());
            System.Threading.Tasks.Task? preparing = null;
            store.Main.Loop.Enqueue(() => preparing = index.Prepare(directory));
            store.Main.Loop.RunUntil(() => preparing is not null, AppModelHarness.Bound);
            Assert.False(preparing!.IsCompleted);
        }
        // :1133-1135 the index built before the folder went is served at once, and nothing waits on the build.
        using var later = IndexStore();
        var served = later.Main.OnMain(() => new FileIndex());
        later.Main.Loop.Run(() => served.Prepare(directory), TimeSpan.FromSeconds(20));
        Assert.Equal(["left.txt"], Search(later, served, "left"));
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-057")] // App/Views/Composer/ComposerView.swift:1257-1262
    public void In_a_git_repository_the_index_holds_the_tracked_and_the_untracked_unignored_files()
    {
        using var store = IndexStore();
        var directory = store.NewFolder("repo");
        var repository = AppModelGitRepo.WithCommit(directory);
        File.WriteAllBytes(Path.Combine(directory, ".gitignore"), System.Text.Encoding.UTF8.GetBytes("*.log\n"));
        Touch(directory, "sub/b.txt", "ignored.log", "node_modules/kept-by-git.js");
        Assert.Equal("true", repository.Git("rev-parse", "--is-inside-work-tree").Trim());
        var index = Prepared(store, directory);
        Assert.Equal([".gitignore", "a.txt", "node_modules/kept-by-git.js", "sub/b.txt"], Search(store, index, "").OrderBy(path => path, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-057")] // App/Views/Composer/ComposerView.swift:1266-1286
    public void Outside_a_repository_the_walk_skips_the_seven_names_and_hidden_entries_and_lists_files_only()
    {
        using var store = IndexStore();
        var directory = store.NewFolder("walk");
        Touch(directory,
            "keep.txt", "src/a.txt", "node_modules/x.js", "build/y.txt", "dist/z.txt", ".build/w.txt", "DerivedData/q.txt",
            "Pods/p.txt", "target/t.txt", "src/target", "hidden.txt", "cloak/inner.txt", "empty/.keep-me-visible");
        File.SetAttributes(Path.Combine(directory, "hidden.txt"), FileAttributes.Hidden);
        var cloak = new DirectoryInfo(Path.Combine(directory, "cloak"));
        cloak.Attributes |= FileAttributes.Hidden;
        var index = Prepared(store, directory);
        Assert.Equal([@"empty\.keep-me-visible", "keep.txt", @"src\a.txt"], Search(store, index, "").OrderBy(path => path, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-057")] // App/Views/Composer/ComposerView.swift:1278
    public void The_walk_lists_files_down_to_level_16_and_nothing_deeper()
    {
        using var store = IndexStore();
        var directory = store.NewFolder("deep");
        var fifteen = string.Join("/", Enumerable.Range(1, 15).Select(n => "d" + n.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Touch(directory, fifteen + "/level16.txt", fifteen + "/d16/level17.txt");
        var index = Prepared(store, directory);
        var found = Search(store, index, "level");
        Assert.Equal([fifteen.Replace('/', '\\') + @"\level16.txt"], found);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-057")] // App/Views/Composer/ComposerView.swift:1284
    public void The_walk_stops_at_20000_files()
    {
        using var store = IndexStore();
        var directory = store.NewFolder("many");
        for (var n = 0; n < 20_001; n++)
            File.WriteAllBytes(Path.Combine(directory, "f" + n.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".txt"), []);
        var index = Prepared(store, directory);
        Assert.Equal(20_000, Search(store, index, "", limit: 30_000).Count);
    }

    // A repository with no commit: git lists the untracked files sorted by path, which is the index's order.
    private static FileIndex GitIndexed(AppModelHarness store, string label, params string[] files)
    {
        var directory = store.NewFolder(label);
        AppModelGitRepo.Empty(directory);
        Touch(directory, files);
        return Prepared(store, directory);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-058")] // App/Views/Composer/ComposerView.swift:1196-1214, :1169
    public void Matches_rank_exact_name_then_name_prefix_then_name_substring_then_path_substring_then_subsequence_less_the_length()
    {
        using var store = IndexStore();
        var index = GitIndexed(store, "scores", "a1b2c3", "abc", "abc.txt", "abczzzz/zz", "nomatch", "xabcx", "zz/abc");
        // abc 1000-3; zz/abc 1000-6; abc.txt 800-7; xabcx 600-5; abczzzz/zz 400-10; a1b2c3 100-6.
        Assert.Equal(["abc", "zz/abc", "abc.txt", "xabcx", "abczzzz/zz", "a1b2c3"], Search(store, index, "ABC"));
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-058")] // App/Views/Composer/ComposerView.swift:1169, :1210
    public void The_length_penalty_stops_at_99_characters()
    {
        using var store = IndexStore();
        var longName = "abc" + new string('x', 100) + ".txt";
        var index = GitIndexed(store, "penalty", longName, "xabc");
        // The long prefix match scores 800-99, still above the short substring match at 600-4.
        Assert.Equal([longName, "xabc"], Search(store, index, "abc"));
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-058")] // App/Views/Composer/ComposerView.swift:1211-1214
    public void Equal_scores_keep_the_index_order_and_only_the_best_limit_are_kept()
    {
        using var store = IndexStore();
        var index = GitIndexed(store, "ties", "b/abc9", "c/abc1", "d/abc5", "e/xabc");
        Assert.Equal(["b/abc9", "c/abc1", "d/abc5", "e/xabc"], Search(store, index, "abc"));
        Assert.Equal(["b/abc9", "c/abc1"], Search(store, index, "abc", limit: 2));
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-058")] // App/Views/Composer/ComposerView.swift:1181, :1189
    public void An_empty_query_returns_the_first_eight_entries_unscored_and_a_limit_of_zero_returns_nothing()
    {
        using var store = IndexStore();
        var names = Enumerable.Range(0, 10).Select(n => "f" + n.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + new string('y', 10 - n)).ToArray();
        var index = GitIndexed(store, "empty", names);
        Assert.Equal(names.OrderBy(name => name, StringComparer.Ordinal).Take(8).ToArray(), Search(store, index, ""));
        Assert.Empty(Search(store, index, "f", limit: 0));
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-058")] // App/Views/Composer/ComposerView.swift:1235-1254
    public void A_subsequence_walks_bytes_for_ASCII_but_a_carriage_return_sends_it_to_the_Character_walk()
    {
        Assert.True(FileIndex.IsSubsequence("ace", "abcde", ascii: true));
        Assert.False(FileIndex.IsSubsequence("aec", "abcde", ascii: true));
        Assert.True(FileIndex.IsSubsequence("", "abcde", ascii: true));
        Assert.True(FileIndex.IsSubsequence("", "", ascii: false));
        // "\r\n" is one Character: a lone "\r" is not in it, although its byte is.
        Assert.False(FileIndex.IsSubsequence("\r", "a\r\nb", ascii: true));
        Assert.False(FileIndex.IsSubsequence("\r", "a\r\nb", ascii: false));
        Assert.True(FileIndex.IsSubsequence("ab", "a\r\nb", ascii: true));
        Assert.True(FileIndex.IsSubsequence("\r\n", "a\r\nb", ascii: true));
        // The Character walk compares Characters: U+00E9 matches its decomposed spelling.
        Assert.True(FileIndex.IsSubsequence("\U000000E9", "cafe\U00000301", ascii: false)); // U+00E9 LATIN SMALL LETTER E WITH ACUTE, U+0301 COMBINING ACUTE ACCENT
    }

    // ---- attach (:505-524) ----
    // The notices are read from the pill's own state: a pill on screen draws the draft's attachments in DraftAttachments'
    // strip, which is part 3's (MARROW_STUB at :990), so these pills stay off screen.

    [Fact]
    [Trait("Req", "PR-COMPOSER-059")] // App/Views/Composer/ComposerView.swift:507-518
    public void Attaching_more_files_than_fit_attaches_what_fits_and_says_how_many_were_left_out()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = h.Runtime;
            var folder = h.Store.NewFolder("attach");
            Touch(folder, "a.txt", "b.txt", "c.txt", "d.txt");
            var view = Pill(h, runtime, workingDirectory: null);
            h.Main.OnMain(() => runtime.Draft = new ComposerDraft { Attachments = new ValueList<Attachment>(Enumerable.Range(0, 6).Select(Fake)) });
            h.Main.OnMain(() => One<ComposerNSTextView>(view).OnAttach!(
                new[] { "a.txt", "b.txt", "c.txt", "d.txt" }.Select(name => (AttachmentSource)new AttachmentSource.File(Path.Combine(folder, name))).ToList()));
            Assert.Equal("2 files were left out: a message holds 8 attachments", view.AttachmentNotice);
            Until(h, null, () => runtime.Draft.Attachments.Count == 8);
            Assert.Equal(["a.txt", "b.txt"], runtime.Draft.Attachments.Skip(6).Select(attachment => attachment.Name).ToArray());
        });
    }

    [Theory]
    [InlineData(7, false, "One file was left out: a message holds 8 attachments")]
    [InlineData(8, true, "The image was left out: a message holds 8 attachments")]
    [InlineData(8, false, "One file was left out: a message holds 8 attachments")]
    [Trait("Req", "PR-COMPOSER-059")] // App/Views/Composer/ComposerView.swift:511-517
    [Trait("Req", "UI-COMPOSER-004")] // App/Views/Composer/ComposerView.swift:511-517
    public void One_source_left_out_is_named_as_a_file_or_as_the_pasted_image(int held, bool pasted, string notice)
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = h.Runtime;
            var folder = h.Store.NewFolder("one");
            Touch(folder, "a.txt", "b.txt");
            var view = Pill(h, runtime, workingDirectory: null);
            h.Main.OnMain(() => runtime.Draft = new ComposerDraft { Attachments = new ValueList<Attachment>(Enumerable.Range(0, held).Select(Fake)) });
            IReadOnlyList<AttachmentSource> sources = pasted
                ? [new AttachmentSource.Image([0x89, 0x50, 0x4E, 0x47], AttachmentSource.PngMimeType)]
                : held == 8
                    ? [new AttachmentSource.File(Path.Combine(folder, "a.txt"))]
                    : [new AttachmentSource.File(Path.Combine(folder, "a.txt")), new AttachmentSource.File(Path.Combine(folder, "b.txt"))];
            h.Main.OnMain(() => One<ComposerNSTextView>(view).OnAttach!(sources));
            Assert.Equal(notice, view.AttachmentNotice);
            Until(h, null, () => runtime.Draft.Attachments.Count == 8);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-059")] // App/Views/Composer/ComposerView.swift:511
    public void Nothing_left_out_says_nothing()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = h.Runtime;
            var folder = h.Store.NewFolder("fits");
            Touch(folder, "a.txt");
            var view = Pill(h, runtime, workingDirectory: null);
            h.Main.OnMain(() => runtime.Draft = new ComposerDraft { Attachments = new ValueList<Attachment>(Enumerable.Range(0, 7).Select(Fake)) });
            h.Main.OnMain(() => One<ComposerNSTextView>(view).OnAttach!([new AttachmentSource.File(Path.Combine(folder, "a.txt"))]));
            Assert.Null(view.AttachmentNotice);
            Until(h, null, () => runtime.Draft.Attachments.Count == 8);
            Assert.Null(view.AttachmentNotice);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-059")] // App/Views/Composer/ComposerView.swift:519
    public void An_attach_hands_the_keyboard_back_to_the_text()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = Unresolved(h);
            var (view, window, other, _) = OnScreen(h, runtime, workingDirectory: null);
            h.Main.OnMain(() => other.Focus());
            Pump(h, window);
            var box = h.Main.OnMain(() => One<ComposerNSTextView>(view));
            Assert.False(h.Main.OnMain(() => box.IsFocused));
            // A file that cannot be copied: the attach still counts its slot and takes the keyboard back, and the draft
            // gains nothing for the strip to draw.
            var missing = Path.Combine(h.Store.NewFolder("missing"), "gone.txt");
            h.Main.OnMain(() => box.OnAttach!([new AttachmentSource.File(missing)]));
            Pump(h, window);
            Assert.True(h.Main.OnMain(() => box.IsFocused));
            Assert.Empty(runtime.Draft.Attachments);
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-059")] // App/Views/Composer/ComposerView.swift:137; Services/Store/Storage.swift:153
    public void The_limit_is_the_storage_attachment_limit_of_eight()
    {
        Assert.Equal(8, ComposerView.MaxAttachments);
        Assert.Equal(Storage.AttachmentLimit, ComposerView.MaxAttachments);
    }
    internal static ApprovalRequest Approval(string id) => new(id, ApprovalRequest.Kind.Tool, id,
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
    [Fact]
    [Trait("Req", "PR-COMPOSER-056")] // App/Views/Composer/ComposerView.swift:1125
    public void B3_shared_file_indexes_evict_the_oldest_two_entry_generation()
    {
        using var store = IndexStore();
        var a = store.NewFolder("cache-a"); var b = store.NewFolder("cache-b"); var c = store.NewFolder("cache-c");
        Touch(a, "old.txt"); Touch(b, "b.txt"); Touch(c, "c.txt");
        Prepared(store, a); Prepared(store, b); Prepared(store, c);
        // Core/Support/RecentCache.swift:47-54: limit 2 is per generation; the fifth insert discards the oldest.
        var d = store.NewFolder("cache-d"); var e = store.NewFolder("cache-e");
        Touch(d, "d.txt"); Touch(e, "e.txt");
        Prepared(store, d); Prepared(store, e);
        Touch(a, "new.txt");
        Assert.Equal(["new.txt"], Search(store, Prepared(store, a), "new"));
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-058")] // App/Views/Composer/ComposerView.swift:1219-1232
    public void B3_non_ASCII_substring_search_matches_canonically_equal_Characters()
    {
        using var store = IndexStore();
        var directory = store.NewFolder("canonical");
        Touch(directory, "a cafe\u0301.txt");
        Assert.Equal(["a cafe\u0301.txt"], Search(store, Prepared(store, directory), "f\u00e9"));
        var ranked = store.NewFolder("canonical-ranked");
        Touch(ranked, "a cafe\u0301.txt", "f0\u00e9.txt");
        Assert.Equal(["a cafe\u0301.txt", "f0\u00e9.txt"], Search(store, Prepared(store, ranked), "f\u00e9"));
    }
    internal static Button Named(Control root, string name) => LogicalQuery.SelfAndDescendants<Button>(root).Single(b => AutomationProperties.GetName(b) == name);
    internal static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    internal static void InvokePrivate(object target, string method, params object[] args) => target.GetType().GetMethod(method, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(target, args);

    [Fact]
    [Trait("Req", "PR-COMPOSER-060")] // App/Views/Composer/ComposerView.swift:219-225
    [Trait("Req", "UI-COMPOSER-004")] // App/Views/Composer/ComposerView.swift:219-225,576-583
    public void B3_attachment_notice_uses_a_five_second_string_key_and_equal_notices_do_not_restart_it()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var frames = new ManualFrameSource(); using var fs = FrameClock.Source.Override(frames);
            var runtime = Unresolved(h);
            var view = Pill(h, runtime);
            var window = h.Main.OnMain(() => Show(view, h.App, 500));
            h.Main.OnMain(() => InvokePrivate(view, "Note", "first")); Pump(h, window);
            h.Main.Advance(TimeSpan.FromSeconds(4)); Pump(h, window); Assert.Equal("first", view.AttachmentNotice);
            h.Main.OnMain(() => InvokePrivate(view, "Note", "first")); Pump(h, window);
            h.Main.Advance(TimeSpan.FromMilliseconds(999)); Pump(h, window); Assert.Equal("first", view.AttachmentNotice);
            h.Main.Advance(TimeSpan.FromMilliseconds(1)); Pump(h, window); Assert.Null(view.AttachmentNotice);
            h.Main.OnMain(() => InvokePrivate(view, "Note", "second")); Pump(h, window);
            h.Main.Advance(TimeSpan.FromSeconds(4)); Pump(h, window);
            h.Main.OnMain(() => InvokePrivate(view, "Note", "replacement")); Pump(h, window);
            h.Main.Advance(TimeSpan.FromSeconds(1)); Pump(h, window); Assert.Equal("replacement", view.AttachmentNotice);
            h.Main.Advance(TimeSpan.FromSeconds(4)); Pump(h, window); Assert.Null(view.AttachmentNotice);
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-063")] // App/Views/Composer/ComposerView.swift:526-533
    [Trait("Req", "UI-COMPOSER-029")] // App/Views/Composer/ComposerView.swift:526-533
    public void B3_file_chooser_is_multiple_files_only_with_Attach_as_accept_label_and_cancel_leaves_the_draft()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness(); var dialog = new ComposerDialogProbe();
            using var ds = ComposerFileDialog.Current.Override(dialog);
            var view = Pill(h, h.Runtime);
            h.Main.OnMain(() => { h.Runtime.Draft = new ComposerDraft { Text = "keep" }; InvokePrivate(view, "ChooseFiles"); });
            h.Main.RunUntilIdle();
            Assert.Equal((true, false, "Attach"), dialog.Options);
            Assert.Equal("keep", h.Runtime.Draft.Text); Assert.Empty(h.Runtime.Draft.Attachments);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-063")] // App/Views/Composer/ComposerView.swift:526-533
    [Trait("Req", "UI-COMPOSER-029")] // App/Views/Composer/ComposerView.swift:527-532
    public void B3_attach_default_dialog_reuses_OpenPanel_options_and_cancel()
    {
        // Test-safety precondition: the red run must fail before it could open the legacy native dialog.
        // The review asks for removal of this duplicate COM interface as well as reuse of the shared runner.
        Assert.Null(typeof(ComposerFileDialog).Assembly.GetType("Marrow.App.Port.IComposerShellItemArray"));
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runner = new RecordingOpenPanelRunner(null);
            using var seam = OpenPanel.Runner.Override(runner);
            var view = Pill(h, h.Runtime);
            h.Main.OnMain(() => { h.Runtime.Draft = new ComposerDraft { Text = "keep" }; InvokePrivate(view, "ChooseFiles"); });
            h.Main.RunUntilIdle();
            Assert.Equal((false, true, true, "Attach", ""), Assert.Single(runner.Shown));
            Assert.Equal("keep", h.Runtime.Draft.Text);
            Assert.Empty(h.Runtime.Draft.Attachments);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-063")] // App/Views/Composer/ComposerView.swift:532-533
    public void B3_attach_default_dialog_preserves_shared_panel_selected_file_paths_and_order()
    {
        // Never enter the unadapted COM dialog in the red run; OpenPanel.Runner must own presentation.
        Assert.Null(typeof(ComposerFileDialog).Assembly.GetType("Marrow.App.Port.IComposerShellItemArray"));
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var directory = h.Store.NewFolder("shared-panel-files");
            Touch(directory, "caf\u00e9 % file.txt", "second.txt");
            var paths = new[] { Path.Combine(directory, "caf\u00e9 % file.txt"), Path.Combine(directory, "second.txt") };
            var runner = new RecordingOpenPanelRunner(paths.Select(path => new Project("fixture", path).Url).ToArray());
            using var seam = OpenPanel.Runner.Override(runner);
            var view = Pill(h, h.Runtime);
            h.Main.OnMain(() => { h.Runtime.Draft = new ComposerDraft { Text = "keep" }; InvokePrivate(view, "ChooseFiles"); });
            Until(h, null, () => h.Runtime.Draft.Attachments.Count == 2);
            Assert.Single(runner.Shown);
            Assert.Equal(paths.Select(Path.GetFileName), h.Runtime.Draft.Attachments.Select(file => file.Name));
            Assert.All(h.Runtime.Draft.Attachments, file => Assert.True(File.Exists(file.Path)));
            Assert.Equal("keep", h.Runtime.Draft.Text);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-067")] // App/Views/Composer/ComposerView.swift:990-1023
    [Trait("Req", "UI-COMPOSER-009")] // App/Views/Composer/ComposerView.swift:1000-1023
    [Trait("Req", "UI-COMPOSER-010")] // App/Views/Composer/ComposerView.swift:1005-1016
    public void B3_attachment_strip_has_48_point_thumbnails_2_top_8_trailing_and_named_remove_badges()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var files = new ValueList<Attachment>([Fake(1), Fake(2)]);
            h.Main.OnMain(() => h.Runtime.Draft = new ComposerDraft { Attachments = files });
            var strip = h.Main.OnMain(() => new DraftAttachments(h.Runtime));
            var window = h.Main.OnMain(() => Show(strip, h.App, 400)); Pump(h, window);
            h.Main.OnMain(() =>
            {
                var scroll = One<ScrollViewer>(strip);
                Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, scroll.HorizontalScrollBarVisibility);
                var thumbnails = LogicalQuery.SelfAndDescendants<Marrow.App.Views.Chat.AttachmentThumbnail>(strip).ToArray();
                Assert.Equal(2, thumbnails.Length);
                foreach (var thumb in thumbnails) { Assert.Equal(48, thumb.Bounds.Width); Assert.Equal(48, thumb.Bounds.Height); }
                foreach (var file in files) { var remove = Named(strip, "Remove " + file.Name); Assert.False(remove.Focusable); Assert.Equal(new Thickness(4), remove.Padding); }
                Assert.Equal(new Thickness(0, 0, 0, 8), strip.Child!.Margin);
            });
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-068")] // App/Views/Composer/ComposerView.swift:1005-1006,1028-1031
    public void B3_attachment_remove_leaves_the_copied_file_and_closes_its_preview()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness(); var presenter = new RecordingPresenter(); using var ps = Presentation.Presenter.Override(presenter);
            var path = System.IO.Path.Combine(h.Store.NewFolder("preview"), "kept.txt"); System.IO.File.WriteAllText(path, "file");
            var file = Fake(1) with { Path = path };
            h.Main.OnMain(() => h.Runtime.Draft = new ComposerDraft { Attachments = new ValueList<Attachment>([file]) });
            var strip = h.Main.OnMain(() => new DraftAttachments(h.Runtime)); var window = h.Main.OnMain(() => Show(strip, h.App, 400)); Pump(h, window);
            h.Main.OnMain(() => Click(LogicalQuery.SelfAndDescendants<Button>(One<Marrow.App.Views.Chat.AttachmentThumbnail>(strip)).First())); Pump(h, window);
            Assert.Contains(presenter.Calls, call => call.Presented);
            h.Main.OnMain(() => Click(Named(strip, "Remove " + file.Name))); Pump(h, window);
            Assert.Empty(h.Runtime.Draft.Attachments); Assert.True(System.IO.File.Exists(path)); Assert.False(presenter.Calls[^1].Presented);
            Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-077")] // App/Views/Composer/ComposerView.swift:627-646
    [Trait("Req", "UI-COMPOSER-011")] // App/Views/Composer/ComposerView.swift:627-637
    public void B3_attach_action_uses_the_setting_and_never_takes_focus()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness(); var presenter = new RecordingPresenter(); using var ps = Presentation.Presenter.Override(presenter);
            foreach (var recent in new[] { true, false })
            {
                var shown = false; var chosen = 0;
                // the attachment button moved out of ComposerControls to the pill's left (V-14.1, owner-ruled): the same button, built on its own
                var controls = h.Main.OnMain(() => new ComposerAttachButton(ComposerAttachPlace.Inside, recent, new Binding<bool>(() => shown, v => shown = v), _ => { }, () => chosen++));
                var window = h.Main.OnMain(() => Show(controls, h.App, 400)); Pump(h, window);
                h.Main.OnMain(() => { var button = Named(controls, "Attach files"); Assert.False(button.Focusable); Assert.Equal("Attach files", ToolTip.GetTip(button)); Click(button); }); Pump(h, window);
                Assert.Equal(recent, shown); Assert.Equal(recent ? 0 : 1, chosen);
                Close(h, window);
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-086")] // App/Views/Composer/ComposerView.swift:882-906
    [Trait("Req", "UI-COMPOSER-005")] // App/Views/Composer/ComposerView.swift:882-906
    [Trait("Req", "UI-COMPOSER-006")] // App/Views/Composer/ComposerView.swift:890-902
    public void B3_command_chip_has_verbatim_name_detail_symbol_and_removes_only_the_command()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource(); using var fs = FrameClock.Source.Override(frames);
            using var h = RuntimeHarness(); h.Main.OnMain(() => h.Runtime.Draft = new ComposerDraft { Text = "keep", Command = new DraftCommand { Name = "plan", Detail = "Turn plan mode on or off", IsBuiltIn = true } });
            var chips = h.Main.OnMain(() => new DraftChips(h.Runtime)); var window = h.Main.OnMain(() => Show(chips, h.App, 400)); Pump(h, window);
            h.Main.OnMain(() =>
            {
                Assert.Equal("/plan", One<TextBlock>(chips).Text); Assert.Contains(LogicalQuery.SelfAndDescendants<SwiftImage>(chips), image => image.SymbolName == "command");
                Assert.Contains(LogicalQuery.SelfAndDescendants<Control>(chips), control => Equals(ToolTip.GetTip(control), "Turn plan mode on or off"));
                var remove = Named(chips, "Remove command"); Assert.False(remove.Focusable); Click(remove);
            }); Pump(h, window); Assert.Null(h.Runtime.Draft.Command); Assert.Equal("keep", h.Runtime.Draft.Text); Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-087")] // App/Views/Composer/ComposerView.swift:908-935
    [Trait("Req", "UI-COMPOSER-007")] // App/Views/Composer/ComposerView.swift:908-935
    [Trait("Req", "UI-COMPOSER-008")] // App/Views/Composer/ComposerView.swift:919-931
    public void B3_quote_chip_shows_excerpt_one_line_capped_at_260_and_removes_by_id()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource(); using var fs = FrameClock.Source.Override(frames);
            using var h = RuntimeHarness(); var a = new ReplyQuote { Text = "\n  alpha   beta\nsecond" }; var b = new ReplyQuote { Text = "other" };
            h.Main.OnMain(() => h.Runtime.Draft = new ComposerDraft { Quotes = new ValueList<ReplyQuote>([a, b]) });
            var chips = h.Main.OnMain(() => new DraftChips(h.Runtime)); var window = h.Main.OnMain(() => Show(chips, h.App, 400)); Pump(h, window);
            // ComposerView.swift:965-968 ChipChrome `Capsule(style: .continuous)` (ED-181 closed): each chip's radius is half its height, the circular arc.
            h.Main.OnMain(() => { var capsules = LogicalQuery.SelfAndDescendants<Border>(chips).Where(border => SwiftShapes.GetCornerStyle(border) == RoundedCornerStyle.Continuous).ToArray(); Assert.Equal(2, capsules.Length); Assert.All(capsules, capsule => ContinuousCornerChecks.Drawn(capsule, expectApple: false)); });
            h.Main.OnMain(() => { var text = LogicalQuery.SelfAndDescendants<TextBlock>(chips).First(); Assert.Equal("alpha beta", text.Text); Assert.Equal(260, text.MaxWidth); Assert.Equal(1, text.MaxLines); Assert.Equal(TextTrimming.CharacterEllipsis, text.TextTrimming); Click(LogicalQuery.SelfAndDescendants<Button>(chips).First()); });
            Pump(h, window); Assert.Equal([b.Id], h.Runtime.Draft.Quotes.Select(q => q.Id)); Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-088")] // App/Views/Composer/ComposerView.swift:880-881,939-957
    public void B3_chip_row_scrolls_without_indicators_and_collapse_clips_from_the_top()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource(); using var fs = FrameClock.Source.Override(frames);
            using var h = RuntimeHarness(); h.Main.OnMain(() => h.Runtime.Draft = new ComposerDraft { Quotes = new ValueList<ReplyQuote>([new() { Text = "quote" }]) });
            var chips = h.Main.OnMain(() => new DraftChips(h.Runtime)); var window = h.Main.OnMain(() => Show(chips, h.App, 400)); Pump(h, window);
            h.Main.OnMain(() => { Assert.Equal(6, ChipControls(chips).Single().Margin.Right); /* the 6 point gap: each chip's place keeps it after the chip and the row takes the last back (ED-282-12); the gap between two chips is measured in The_command_chip_names_... */ Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, One<ScrollViewer>(chips).HorizontalScrollBarVisibility); var collapsed = Assert.IsType<Border>(new ChipRowCollapse(true).Body(new Border())); Assert.Equal(0, collapsed.Height); Assert.Equal(0, collapsed.Opacity); Assert.True(collapsed.ClipToBounds); }); Close(h, window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-089")] // App/Views/Composer/ComposerView.swift:963-969
    [Trait("Req", "UI-COMPOSER-005")] // App/Views/Composer/ComposerView.swift:963-969
    public void B3_chip_capsule_uses_9_7_5_padding_and_12_percent_primary_fill()
    {
        headless.OnUiThread(() => { using var h = RuntimeHarness(); h.Main.OnMain(() => { var chrome = Assert.IsType<Border>(new ChipChrome().Body(new TextBlock())); Assert.Equal(new Thickness(9,5,7,5), chrome.Padding); Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(chrome)); Assert.Equal(Chrome.Overlay(0.12).Resolve(chrome.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(chrome.Background).Color); }); });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-090")] // App/Views/Composer/ComposerView.swift:625-655
    public void B3_controls_are_attach_model_context_send_two_apart_and_compact_omits_context()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            foreach (var compact in new[] { false, true })
            {
                var controls = h.Main.OnMain(() => new ComposerControls(h.Runtime,h.Thread,compact,false,false,null,() => {}));
                // the attachment button, first in the source (:626-646), is the pill's left now (V-14.1, owner-ruled): the controls are the chip, the ring and send
                var row = Assert.IsType<StackPanel>(controls.Child); Assert.Equal(2,row.Spacing); Assert.Equal(compact ? 2 : 3,row.Children.Count); Assert.IsType<ModelEffortSlot>(row.Children[0]); if (!compact) Assert.IsType<ContextMeter>(row.Children[1]); Assert.IsType<SendButton>(row.Children[^1]);
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-091")] // App/Views/Composer/ComposerView.swift:704-713
    [Trait("Req", "PR-USAGE-001")] // App/Views/Composer/ComposerView.swift:704-713
    [Trait("Req", "UI-USAGE-001")] // App/Views/Composer/ComposerView.swift:704-713
    public void B3_context_ring_uses_fraction_limits_or_key_and_is_absent_while_popped_out()
    {
        headless.OnUiThread(() =>
        {
            // The ring is an if-branch: it leaves and returns through the opacity transition (UI-COMPOSER-013), so its end is read once the fade has run.
            var frames = new ManualFrameSource(); using var frameScope = FrameClock.Source.Override(frames);
            using var h = RuntimeHarness(); var meter = h.Main.OnMain(() => new ContextMeter(h.Runtime, ProviderKind.Codex,null)); var window = h.Main.OnMain(() => Show(meter,h.App,200)); Pump(h,window); Assert.Single(LogicalQuery.SelfAndDescendants<Button>(meter));
            void Fade() { h.Main.Advance(TimeSpan.FromSeconds(2)); h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(2))); Pump(h,window); }
            h.Main.OnMain(() => h.App.Settings.ShowsUsagePanel=true); Pump(h,window); Fade(); Assert.Empty(LogicalQuery.SelfAndDescendants<Button>(meter)); h.Main.OnMain(() => h.App.Settings.ShowsUsagePanel=false); Pump(h,window); Fade(); Assert.Single(LogicalQuery.SelfAndDescendants<Button>(meter)); Close(h,window);
            var noLimits = h.Main.OnMain(() => new ContextMeter(h.Runtime,ProviderKind.Cursor,null)); var other = h.Main.OnMain(() => Show(noLimits,h.App,200)); Pump(h,other); Assert.Empty(LogicalQuery.SelfAndDescendants<Button>(noLimits));
            h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.Usage(new ContextUsage { UsedTokens=5,WindowTokens=10 }))); Pump(h,other); Assert.Single(LogicalQuery.SelfAndDescendants<Button>(noLimits)); Close(h,other);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-092")] // App/Views/Composer/ComposerView.swift:718-731
    [Trait("Req", "PR-USAGE-002")] // App/Views/Composer/ComposerView.swift:718-731
    [Trait("Req", "UI-COMPOSER-013")] // App/Views/Composer/ComposerView.swift:718-731
    [Trait("Req", "UI-USAGE-001")] // App/Views/Composer/ComposerView.swift:718-731
    public void B3_context_ring_is_15_square_with_25_strokes_round_cap_and_6_5_hit_padding()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness(); var meter=h.Main.OnMain(() => new ContextMeter(h.Runtime,ProviderKind.Codex,null)); var window=h.Main.OnMain(() => Show(meter,h.App,200)); Pump(h,window);
            h.Main.OnMain(() => { var button=One<Button>(meter); Assert.False(button.Focusable); Assert.Equal(new Thickness(6,5),button.Padding); var ring=Assert.IsType<Grid>(button.Content); Assert.Equal(15,ring.Width); Assert.Equal(15,ring.Height); var track=Assert.IsType<Avalonia.Controls.Shapes.Path>(ring.Children[0]); Assert.Equal(new Rect(0,0,15,15),Assert.IsType<EllipseGeometry>(track.Data).Rect); /* the circle's centre line is the 15 point frame's edge, as the arc's (VIDEO static.composer-context-ring: radius 7.53) */ var arc=Assert.IsType<Avalonia.Controls.Shapes.Path>(ring.Children[1]); Assert.Equal(2.5,track.StrokeThickness); Assert.Equal(2.5,arc.StrokeThickness); Assert.Equal(PenLineCap.Round,arc.StrokeLineCap); Assert.Equal(-90,Assert.IsType<RotateTransform>(arc.RenderTransform).Angle); }); Close(h,window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-093")] // App/Views/Composer/ComposerView.swift:732-736,764-772
    public void B3_ring_percentage_truncates_and_accessibility_name_stays_Context_window()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness(); h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.Usage(new ContextUsage { UsedTokens=859,WindowTokens=1000 })));
            var meter=h.Main.OnMain(() => new ContextMeter(h.Runtime,ProviderKind.Codex,null)); var window=h.Main.OnMain(() => Show(meter,h.App,200)); Pump(h,window);
            h.Main.OnMain(() => { var button=One<Button>(meter); Assert.Equal("85% of the context window used",ToolTip.GetTip(button)); Assert.Equal("Context window",AutomationProperties.GetName(button)); Assert.Equal("85% of the context window used",AutomationProperties.GetHelpText(button)); }); Close(h,window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-094")] // App/Views/Composer/ComposerView.swift:737-759
    [Trait("Req", "PR-USAGE-008")] // App/Views/Composer/ComposerView.swift:748-753
    [Trait("Req", "UI-COMPOSER-015")] // App/Views/Composer/ComposerView.swift:748-753
    [Trait("Req", "UI-USAGE-004")] // App/Views/Composer/ComposerView.swift:748-753
    public void B3_ring_popover_header_popout_hides_ring_and_enables_floating_usage()
    {
        headless.OnUiThread(() =>
        {
            var frames=new ManualFrameSource(); using var frameScope=FrameClock.Source.Override(frames); // the ring's fade (UI-COMPOSER-013) runs on the frame clock
            using var h=RuntimeHarness(); var presenter=new RecordingPresenter(); using var ps=Presentation.Presenter.Override(presenter);
            var meter=h.Main.OnMain(() => new ContextMeter(h.Runtime,ProviderKind.Codex,null)); var window=h.Main.OnMain(() => Show(meter,h.App,200)); Pump(h,window);
            h.Main.OnMain(() => Click(One<Button>(meter))); Pump(h,window); var popup=presenter.Calls[^1].Popup; Assert.Equal(Avalonia.Controls.PlacementMode.Bottom,popup.Placement); Assert.NotNull(popup.Child);
            var content=popup.Child!; Assert.Contains(LogicalQuery.SelfAndDescendants<TextBlock>(content), t=>t.Text=="Usage");
            h.Main.OnMain(() => Click(Named(content,"Open as a floating panel beside the chat"))); Pump(h,window); h.Main.Advance(TimeSpan.FromSeconds(2)); h.Main.OnMain(() => frames.Advance(TimeSpan.FromSeconds(2))); Pump(h,window); Assert.True(h.App.Settings.ShowsUsagePanel); Assert.False(presenter.Calls[^1].Presented); Assert.Empty(LogicalQuery.SelfAndDescendants<Button>(meter)); Close(h,window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-095")] // App/Views/Composer/ComposerView.swift:301-307
    public void B3_heads_usage_provider_is_nil_when_it_equals_the_lead_or_Hydra_is_off()
    {
        headless.OnUiThread(() => { using var h=RuntimeHarness(); var view=Pill(h,h.Runtime); h.Main.OnMain(() => { var method=typeof(ComposerView).GetMethod("HeadsProvider",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!; h.App.Settings.HydraEnabled=false; Assert.Null(method.Invoke(view,[h.Thread])); }); });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-096")] // App/Views/Composer/ComposerView.swift:825-841
    [Trait("Req", "PR-COMPOSER-100")] // App/Views/Composer/ComposerView.swift:843
    [Trait("Req", "UI-COMPOSER-016")] // App/Views/Composer/ComposerView.swift:825-843
    public void B3_send_is_28_circle_4_leading_and_disabled_until_draft_then_named_Send_to_a_head()
    {
        headless.OnUiThread(() =>
        {
            var frames=new ManualFrameSource(); using var frameScope=FrameClock.Source.Override(frames); // the disc's fill eases on the frame clock (UI-COMPOSER-016)
            using var h=RuntimeHarness(); var send=h.Main.OnMain(() => new SendDraftState(h.Runtime,false,false,true,false,()=>{})); var window=h.Main.OnMain(() => Show(send,h.App,200)); Pump(h,window);
            var button=One<Button>(send); Assert.False(button.IsEnabled); Assert.False(button.Focusable); Assert.Equal("Send to a head",AutomationProperties.GetName(button)); Assert.Equal(new Thickness(4,0,0,0),button.Margin);
            h.Main.OnMain(() => h.Runtime.Draft=new ComposerDraft{Text="message"}); Pump(h,window); Assert.True(button.IsEnabled); var label=Assert.IsType<Border>(button.Content); Assert.Equal(28,label.Width); Assert.Equal(28,label.Height); Assert.Equal("arrow.up",One<SwiftImage>(label).SymbolName); Close(h,window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-097")] // App/Views/Composer/ComposerView.swift:828-829
    public void B3_stop_click_is_plain_interrupt_and_never_returns_the_turn_text_to_the_draft()
    {
        headless.OnUiThread(() =>
        {
            using var h=RuntimeHarness(); Loaded(h,h.Runtime); h.Main.OnMain(() => h.Runtime.RehearseTurn("sent prompt"));
            var send=h.Main.OnMain(() => new SendDraftState(h.Runtime,true,false,false,false,()=> throw new Exception("idle send invoked"))); var window=h.Main.OnMain(() => Show(send,h.App,200)); Pump(h,window);
            h.Main.OnMain(() => { Assert.Equal("Stop",AutomationProperties.GetName(One<Button>(send))); Click(One<Button>(send)); }); Pump(h,window); Assert.Equal("",h.Runtime.Draft.Text); Assert.Single(h.Runtime.Turns); Close(h,window);
        });
    }

    [Theory]
    [InlineData(true,"Heads are at work: your message goes to a head")]
    [InlineData(false,"Heads are at work: your message waits for the lead")]
    [Trait("Req", "PR-COMPOSER-098")] // App/Views/Composer/ComposerView.swift:848-860
    public void B3_running_heads_tooltip_names_the_exact_message_destination(bool queueHeads,string expected)
    {
        headless.OnUiThread(() => { using var h=RuntimeHarness(); h.Main.OnMain(() => { var send=new SendDraftState(h.Runtime,true,true,false,queueHeads,()=>{}); Assert.Equal(expected,ToolTip.GetTip(One<Button>(send))); }); });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-099")] // App/Views/Composer/ComposerView.swift:862-864
    public void B3_idle_help_uses_the_cached_queue_chord_and_literal_Return()
    {
        headless.OnUiThread(() => { using var h=RuntimeHarness(); h.Main.OnMain(() => { var send=new SendDraftState(h.Runtime,false,false,true,false,()=>{}); var tail=SendShortcutText.Queue is {} q ? " · "+q+" queues while running" : ""; Assert.Equal("Send to a head (Return) · the lead reports back"+tail,ToolTip.GetTip(One<Button>(send))); }); });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-101")] // App/Views/Composer/ComposerView.swift:664-672
    public void B3_working_dots_take_bottom_two_fifths_inside_22_point_clip()
    {
        headless.OnUiThread(() =>
        {
            var frames = new ManualFrameSource(); using var fs = FrameClock.Source.Override(frames);
            using var h=RuntimeHarness(); h.Main.OnMain(() => h.Runtime.RehearseTurn("working")); var slot=h.Main.OnMain(() => new ComposerWorkingDotsSlot(h.Runtime)); var window=h.Main.OnMain(() => Show(slot,h.App,400)); h.Main.OnMain(() => slot.Height=100); Pump(h,window);
            h.Main.OnMain(() => { var dots=One<ComposerWorkingDots>(slot); Assert.Equal(40,dots.Bounds.Height); Assert.Equal(60,Frame(dots,slot).Y); var clip=Assert.IsType<Border>(slot.Child); Assert.Equal(new CornerRadius(22),clip.CornerRadius); Assert.True(clip.ClipToBounds); }); Close(h,window);
        });
    }

    [Fact]
    [Trait("Req", "PR-TERM-073")] // App/Views/Composer/ComposerView.swift:342-354
    public void B3_Escape_closes_terminal_before_diff_without_terminating_shells()
    {
        headless.OnUiThread(() => { using var h=RuntimeHarness(); var view=Pill(h,h.Runtime); h.Main.OnMain(() => { h.Runtime.IsTerminalVisible=true; h.Runtime.IsDiffVisible=true; Assert.True(Press(view,Key.Escape)); Assert.False(h.Runtime.IsTerminalVisible); Assert.True(h.Runtime.IsDiffVisible); }); });
    }

    [Theory]
    [InlineData(850, false)]
    [InlineData(851, true)]
    [Trait("Req", "PR-USAGE-003")] // App/Views/Composer/ComposerView.swift:721-722
    [Trait("Req", "UI-USAGE-001")] // App/Views/Composer/ComposerView.swift:721-722
    public void B3_context_warning_threshold_is_strict_and_uses_the_ambient_tint(int used, bool warns)
    {
        headless.OnUiThread(() =>
        {
            using var h=RuntimeHarness();
            h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.Usage(new ContextUsage { UsedTokens=used,WindowTokens=1000 })));
            var meter=h.Main.OnMain(() => new ContextMeter(h.Runtime,ProviderKind.Codex,null));
            var window=h.Main.OnMain(() => { Popovers.SetTint(meter, SwiftColor.White); return Show(meter,h.App,200); }); Pump(h,window);
            h.Main.OnMain(() => { var arc=LogicalQuery.SelfAndDescendants<Avalonia.Controls.Shapes.Path>(meter).Last(); Assert.Equal((warns ? Chrome.Warning : SwiftColor.White).Resolve(arc.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(arc.Stroke).Color); }); Close(h,window);
        });
    }

    [Theory]
    [InlineData(ProviderKind.Codex,859,false,"Context window","85% of the context window used","85% of the context window used")]
    [InlineData(ProviderKind.Codex,999,false,"Context window","99% of the context window used","99% of the context window used")]
    [InlineData(ProviderKind.Codex,-1,false,"Usage limits","Usage limits","")]
    [InlineData(ProviderKind.Deepseek,-1,true,"Remaining credits","Remaining credits","")]
    [Trait("Req", "PR-USAGE-004")] // App/Views/Composer/ComposerView.swift:732,764-767
    [Trait("Req", "PR-USAGE-005")] // App/Views/Composer/ComposerView.swift:733-736,769-772
    [Trait("Req", "UI-USAGE-002")] // App/Views/Composer/ComposerView.swift:732-736,764-772
    public void B3_context_accessibility_keeps_label_and_read_only_value_separate(ProviderKind provider,int used,bool credits,string label,string tip,string value)
    {
        headless.OnUiThread(() =>
        {
            using var h=RuntimeHarness();
            h.Main.OnMain(() => { if(credits) h.App.Settings.SetAPIKeyInput("fixture-only",provider); if(used>=0) h.Runtime.Rehearse(new ProviderEvent.Usage(new ContextUsage { UsedTokens=used,WindowTokens=1000 })); });
            var meter=h.Main.OnMain(() => new ContextMeter(h.Runtime,provider,null)); var window=h.Main.OnMain(() => Show(meter,h.App,200)); Pump(h,window);
            h.Main.OnMain(() => { var button=One<Button>(meter); Assert.Equal(label,AutomationProperties.GetName(button)); Assert.Equal(tip,ToolTip.GetTip(button)); var peer=ControlAutomationPeer.CreatePeerForElement(button); var reading=Assert.IsAssignableFrom<IValueProvider>(peer); Assert.True(reading.IsReadOnly); Assert.Equal(value,reading.Value); Assert.Throws<InvalidOperationException>(()=>reading.SetValue("other")); Assert.IsAssignableFrom<IInvokeProvider>(peer); }); Close(h,window);
        });
    }

    [Fact]
    [Trait("Req", "PR-USAGE-006")] // App/Views/Composer/ComposerView.swift:714-716,737,759
    [Trait("Req", "PR-USAGE-007")] // App/Views/Composer/ComposerView.swift:738-756
    [Trait("Req", "PR-USAGE-009")] // App/Views/Composer/ComposerView.swift:757; App/Views/Usage/UsagePanel.swift:19-24,68-70
    [Trait("Req", "UI-COMPOSER-014")] // App/Views/Composer/ComposerView.swift:743-757
    [Trait("Req", "UI-USAGE-003")] // App/Views/Composer/ComposerView.swift:743-746,755-756
    public void B3_usage_popover_toggles_and_preserves_header_geometry_and_fixed_panel_width()
    {
        headless.OnUiThread(() =>
        {
            using var h=RuntimeHarness(); var presenter=new RecordingPresenter(); using var ps=Presentation.Presenter.Override(presenter);
            var meter=h.Main.OnMain(()=>new ContextMeter(h.Runtime,ProviderKind.Codex,null)); var window=h.Main.OnMain(()=>Show(meter,h.App,200)); Pump(h,window);
            h.Main.OnMain(()=>Click(One<Button>(meter))); Pump(h,window);
            h.Main.OnMain(()=>
            {
                var popup=presenter.Calls[^1].Popup; Assert.True(presenter.Calls[^1].Presented); Assert.Equal(Avalonia.Controls.PlacementMode.Bottom,popup.Placement);
                var title=LogicalQuery.SelfAndDescendants<TextBlock>(popup.Child!).Single(t=>t.Text=="Usage"); Assert.Equal(12,title.FontSize); Assert.Equal(FontWeight.SemiBold,title.FontWeight); Assert.Equal(Chrome.SecondaryText.Resolve(title.ActualThemeVariant),Assert.IsAssignableFrom<ISolidColorBrush>(title.Foreground).Color);
                var header=Assert.IsType<Grid>(title.GetLogicalParent()); Assert.Equal(new Thickness(16,10,16,0),header.Margin); Assert.Equal(8,header.ColumnDefinitions[1].MinWidth);
                var panel=One<UsagePanel>(popup.Child!); var frame=Assert.IsType<Border>(panel.Child); Assert.Equal(360,frame.Width); Assert.Equal(new Thickness(16),frame.Padding);
                Assert.True(Popovers.GetIsCapsuleButtonBorderShape(PopoverHost.Content(popup)!)); Assert.Equal(ThemeManager.Spec.Accent,Popovers.GetTint(PopoverHost.Content(popup)!)); Click(One<Button>(meter));
            }); Pump(h,window); Assert.False(presenter.Calls[^1].Presented); Close(h,window);
        });
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-005")] // App/Views/Composer/ComposerView.swift:883-906,939,963-969
    [Trait("Req", "UI-COMPOSER-006")] // App/Views/Composer/ComposerView.swift:890-902
    [Trait("Req", "UI-COMPOSER-007")] // App/Views/Composer/ComposerView.swift:909-935; App/Runtime/ThreadRuntime.swift:54-64
    [Trait("Req", "UI-COMPOSER-008")] // App/Views/Composer/ComposerView.swift:919-931
    public void B3_chip_fonts_and_remove_labels_follow_each_text_style_and_quote_excerpt_is_72_Characters()
    {
        headless.OnUiThread(() =>
        {
            using var h=RuntimeHarness(); var text=new string('a',73); h.Main.OnMain(()=>h.Runtime.Draft=new ComposerDraft { Command=new DraftCommand { Name="review",Detail="Full detail",IsBuiltIn=false },Quotes=new ValueList<ReplyQuote>([new(){Text="\n  "+text+"\nignored"}]) });
            var chips=h.Main.OnMain(()=>new DraftChips(h.Runtime)); var window=h.Main.OnMain(()=>Show(chips,h.App,500)); Pump(h,window);
            h.Main.OnMain(()=>
            {
                var titles=LogicalQuery.SelfAndDescendants<TextBlock>(chips).ToArray(); Assert.Equal(["/review",new string('a',72)+"…"],titles.Select(t=>t.Text)); Assert.All(titles,t=>Assert.Equal(11,t.FontSize)); Assert.Equal(FontWeight.Medium,titles[0].FontWeight); Assert.Equal(FontWeight.Normal,titles[1].FontWeight);
                foreach(var title in titles) { var row=Assert.IsType<StackPanel>(title.GetLogicalParent()); Assert.Equal(5,row.Spacing); Assert.Equal(Chrome.PrimaryText.Opacity(0.9).Resolve(title.ActualThemeVariant),Assert.IsAssignableFrom<ISolidColorBrush>(title.Foreground).Color); var glyph=Assert.IsType<SwiftImage>(row.Children[0]); var font=glyph.GetValue(SwiftImage.FontProperty)!.Resolve(); Assert.Equal(10,font.FontSize); Assert.Equal(FontWeight.SemiBold,font.FontWeight); }
                foreach(var label in new[]{"Remove command","Remove quote"}) { var button=Named(chips,label); Assert.False(button.Focusable); Assert.Equal(label,ToolTip.GetTip(button)); var glyph=One<SwiftImage>(button); Assert.Equal("xmark",glyph.SymbolName); Assert.Equal(10,glyph.GetValue(SwiftImage.FontProperty)!.Resolve().FontSize); Assert.Equal(FontWeight.Bold,glyph.GetValue(SwiftImage.FontProperty)!.Resolve().FontWeight); }
            }); Close(h,window);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-067")] // App/Views/Composer/ComposerView.swift:1001-1023
    public void B3_surviving_attachment_keeps_its_frame_reader_when_its_metadata_changes()
    {
        headless.OnUiThread(() =>
        {
            using var h=RuntimeHarness(); var file=Fake(1); h.Main.OnMain(()=>h.Runtime.Draft=new ComposerDraft { Attachments=new ValueList<Attachment>([file]) });
            var strip=h.Main.OnMain(()=>new DraftAttachments(h.Runtime)); var window=h.Main.OnMain(()=>Show(strip,h.App,300)); Pump(h,window); var arrival=One<AttachmentArrival>(strip); var reader=One<ScreenFrameReader>(arrival);
            h.Main.OnMain(()=>h.Runtime.Draft=h.Runtime.Draft with { Attachments=new ValueList<Attachment>([file with {Name="renamed.txt"}]) }); Pump(h,window);
            Assert.Same(arrival,One<AttachmentArrival>(strip)); Assert.Same(reader,One<ScreenFrameReader>(arrival)); Assert.Equal("Remove renamed.txt",AutomationProperties.GetName(Named(strip,"Remove renamed.txt"))); Close(h,window);
        });
    }

    [Fact]
    [Trait("Req", "UI-USAGE-001")] // App/Views/Composer/ComposerView.swift:717-728
    public void B3_ring_padded_hit_rectangle_measures_27_by_25_and_retains_a_15_point_ring()
    {
        headless.OnUiThread(()=>
        {
            using var h=RuntimeHarness(); var meter=h.Main.OnMain(()=>new ContextMeter(h.Runtime,ProviderKind.Codex,null)); var window=h.Main.OnMain(()=>Show(meter,h.App,200)); Pump(h,window);
            h.Main.OnMain(()=>{ var button=One<Button>(meter); Assert.Equal(new Size(27,25),button.DesiredSize); var ring=Assert.IsType<Grid>(button.Content); Assert.Equal(new Size(15,15),ring.Bounds.Size); Assert.Equal(new Point(6,5),Frame(ring,button).Position); }); Close(h,window);
        });
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-010")] // App/Views/Composer/ComposerView.swift:1008-1015
    public void B3_attachment_badge_reserves_4_point_padding_around_the_glyph_and_offsets_6_minus6()
    {
        headless.OnUiThread(()=>
        {
            using var h=RuntimeHarness(); var file=Fake(1); h.Main.OnMain(()=>h.Runtime.Draft=new ComposerDraft { Attachments=new ValueList<Attachment>([file]) }); var strip=h.Main.OnMain(()=>new DraftAttachments(h.Runtime)); var window=h.Main.OnMain(()=>Show(strip,h.App,300)); Pump(h,window);
            h.Main.OnMain(()=>{ var remove=Named(strip,"Remove "+file.Name); var image=One<SwiftImage>(remove); Assert.Equal(image.DesiredSize.Width+8,remove.DesiredSize.Width); Assert.Equal(image.DesiredSize.Height+8,remove.DesiredSize.Height); var offset=Assert.IsType<TranslateTransform>(remove.RenderTransform); Assert.Equal(6,offset.X); Assert.Equal(-6,offset.Y); }); Close(h,window);
        });
    }

    [Fact]
    [Trait("Req", "UI-USAGE-004")] // App/Views/Composer/ComposerView.swift:748-753; UI/Chrome/Chrome.swift:413-436
    public void B3_popout_uses_32_point_interactive_glass_and_12_semibold_arrow_with_092_to1_hover()
    {
        headless.OnUiThread(()=>
        {
            var frames=new ManualFrameSource(); using var fs=FrameClock.Source.Override(frames); using var h=RuntimeHarness(); var presenter=new RecordingPresenter(); using var ps=Presentation.Presenter.Override(presenter);
            var meter=h.Main.OnMain(()=>new ContextMeter(h.Runtime,ProviderKind.Codex,null)); var window=h.Main.OnMain(()=>Show(meter,h.App,200)); Pump(h,window); h.Main.OnMain(()=>Click(One<Button>(meter))); Pump(h,window); var content=presenter.Calls[^1].Popup.Child!;
            var surface=One<ChromeCircleButton>(content); var other=h.Main.OnMain(()=>{ Assert.IsType<Grid>(surface.GetLogicalParent()).Children.Remove(surface); return Show(surface,h.App,200); }); Pump(h,other);
            h.Main.OnMain(()=>
            {
                var button=Named(surface,"Open as a floating panel beside the chat"); Assert.Equal("Open as a floating panel beside the chat",ToolTip.GetTip(button)); var label=Assert.IsType<Panel>(button.Content); Assert.Equal(new Size(32,32),label.Bounds.Size); var image=One<SwiftImage>(surface); Assert.Equal("arrow.up.right",image.SymbolName); var font=image.GetValue(SwiftImage.FontProperty)!.Resolve(); Assert.Equal(12,font.FontSize); Assert.Equal(FontWeight.SemiBold,font.FontWeight);
                Assert.Equal(Chrome.PrimaryText.Opacity(0.92).Resolve(image.ActualThemeVariant),Assert.IsAssignableFrom<ISolidColorBrush>(image.Foreground).Color);
                var glass=LogicalQuery.SelfAndDescendants<Control>(surface).Select(SwiftGlass.GetEffect).Single(effect=>effect is not null)!; Assert.Equal(GlassShape.Circle,glass.Shape); Assert.Equal(0.3,glass.TintOpacity); Assert.True(glass.IsInteractive);
                var hover=button.GetLogicalParent() as Control ?? throw new Exception("Missing glass wrapper"); hover.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent,hover,null!,hover,default,0,new PointerPointProperties(),KeyModifiers.None));
            }); Pump(h,other); h.Main.Advance(TimeSpan.FromSeconds(0.1)); h.Main.OnMain(()=>frames.Advance(TimeSpan.FromSeconds(0.1))); Pump(h,other);
            h.Main.OnMain(()=>{ var image=One<SwiftImage>(surface); Assert.Equal(Chrome.PrimaryText.Resolve(image.ActualThemeVariant),Assert.IsAssignableFrom<ISolidColorBrush>(image.Foreground).Color); }); Close(h,other); Close(h,window);
        });
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-022")] // App/Views/Composer/ComposerView.swift:247; Services/Providers/ProviderRegistry.swift:762-784
    public void B3_retry_enters_a_new_command_load_even_when_the_cached_commands_are_fresh()
    {
        headless.OnUiThread(()=>
        {
            System.Threading.Tasks.Task.Run(()=>Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult(); using var h=new ThreadRuntimePart12Harness(ProviderKind.Claude); Loaded(h,h.Runtime); var directory=h.Store.NewFolder("fresh-retry");
            h.Main.OnMain(()=>{ h.App.Settings.SetBinaryPath(FailingCommandCLI(),ProviderKind.Claude); h.App.Providers.UpdateCommands(new ValueList<SlashCommand>([new(){Name="cached",Detail="Cached command"}]),ProviderKind.Claude,directory); }); var view=Pill(h,h.Runtime,directory);
            h.Main.Loop.Run(()=>view.LoadCommands(force:true),AppModelHarness.Bound); Assert.NotEmpty(h.App.Providers.CommandErrors); h.Main.OnMain(()=>Type(view,"/")); Until(h,null,()=>view.Suggestions.IsVisible); var menu=h.Main.OnMain(()=>view.SuggestionMenu());
            var loads=typeof(ProviderRegistry).GetField("commandLoads",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            h.Main.OnMain(()=>Click(One<Button>(One<PopoverItem>(menu)))); Until(h,null,()=>((System.Collections.IDictionary)loads.GetValue(h.App.Providers)!).Count>0); Until(h,null,()=>((System.Collections.IDictionary)loads.GetValue(h.App.Providers)!).Count==0);
            Assert.NotEmpty(h.App.Providers.CommandErrors); Assert.Equal("cached",h.App.Providers.Commands[ProviderRegistry.CommandKey(ProviderKind.Claude,directory)][0].Name);
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-052")] // App/Views/Composer/ComposerView.swift:205-207
    public void B3_commands_landing_refresh_the_focused_on_screen_composer()
    {
        headless.OnUiThread(()=>
        {
            var frames=new ManualFrameSource(); using var fs=FrameClock.Source.Override(frames); using var h=RuntimeHarness(); var presenter=new RecordingPresenter(); using var ps=Presentation.Presenter.Override(presenter); var directory=h.Store.NewFolder("landing-commands"); var (view,window,_,_)=OnScreen(h,h.Runtime,directory); h.Main.OnMain(()=>Type(view,"/")); Until(h,window,()=>view.Suggestions.IsVisible); Assert.True(h.Main.OnMain(()=>view.Controller.HasFocus));
            try { h.Main.OnMain(()=>h.App.Providers.UpdateCommands(new ValueList<SlashCommand>([new(){Name="review",Detail="Review diff"}]),ProviderKind.Codex,directory)); h.Main.Loop.RunUntil(()=> { Settle(window); return view.Suggestions.Items.Any(item=>item.Title=="/review"); },TimeSpan.FromSeconds(20)); Assert.True(h.Main.OnMain(()=>view.Controller.HasFocus)); }
            finally { Close(h,window); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-COMPOSER-052")] // App/Views/Composer/ComposerView.swift:204-212; Services/Providers/ClaudeSession.swift:198-208
    public void B3_delayed_listing_refreshes_the_focused_open_panel_with_commands_or_a_late_error(bool fails)
    {
        headless.OnUiThread(() =>
        {
            System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
            using var h = new ThreadRuntimePart12Harness(ProviderKind.Claude);
            Loaded(h, h.Runtime);
            var frames = new ManualFrameSource();
            using var fs = FrameClock.Source.Override(frames);
            var presenter = new RecordingPresenter();
            using var ps = Presentation.Presenter.Override(presenter);
            var script = ProviderProbeStandIns.CopyFor("composer-delayed-listing").WaitForRelease().SetExitCode(fails ? 1 : 0);
            if (!fails) script.SetStdout("{\"type\":\"control_response\",\"response\":{\"request_id\":\"handshake\",\"response\":{\"commands\":[{\"name\":\"proof-late\",\"description\":\"Delayed fixture command\"}]}}}\n");
            h.Main.OnMain(() =>
            {
                h.App.Settings.SetBinaryPath(script.ExecutablePath, ProviderKind.Claude);
                // The mounted model picker must not start a separate catalogue handshake on this command fixture.
                ((HashSet<ProviderKind>)typeof(ProviderRegistry).GetField("attemptedCatalogs",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(h.App.Providers)!).Add(ProviderKind.Claude);
            });
            var directory = h.Store.NewFolder("delayed-listing");
            var key = ProviderRegistry.CommandKey(ProviderKind.Claude, directory);
            var (view, window, _, _) = OnScreen(h, h.Runtime, directory);
            var loads = (System.Collections.IDictionary)typeof(ProviderRegistry)
                .GetField("commandLoads", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(h.App.Providers)!;
            try
            {
                Until(h, window, () => script.CommandListings().Count == 1 && loads.Contains(key));
                h.Main.OnMain(() => Type(view, "/"));
                Until(h, window, () => view.Suggestions.IsVisible && presenter.Calls.Count > 0 && presenter.Calls[^1].Presented);
                Pump(h, window); // Let the opening-triggered task join the still-held listing before releasing it.
                Assert.True(loads.Contains(key));
                Assert.Single(script.CommandListings());
                Assert.True(h.Main.OnMain(() => view.Controller.HasFocus));
                Assert.True(h.Main.OnMain(() => One<ComposerNSTextView>(view).IsFocused));
                var before = presenter.Calls.Count;
                var popup = presenter.Calls[^1].Popup;
                var panel = Shown(presenter);
                Assert.Empty(LogicalQuery.SelfAndDescendants<PopoverNote>(panel));
                Assert.DoesNotContain("/proof-late", Titles(view));
                Assert.False(h.App.Providers.Commands.ContainsKey(key));
                Assert.False(h.App.Providers.CommandErrors.ContainsKey(key));
                script.Release(); // Only now may the real offline listing answer or fail.
                if (fails)
                {
                    Until(h, window, () => presenter.Calls.Count > before && presenter.Calls[^1].Presented
                        && LogicalQuery.SelfAndDescendants<PopoverNote>(Shown(presenter)).Any());
                    Assert.Equal("Claude's command discovery did not finish. Try again.",
                        Assert.IsType<TextBlock>(One<PopoverNote>(Shown(presenter)).Child).Text);
                    Assert.Equal("Retry loading commands", AutomationProperties.GetName(One<Button>(One<PopoverItem>(Shown(presenter)))));
                }
                else
                {
                    Until(h, window, () => presenter.Calls.Count > before && presenter.Calls[^1].Presented
                        && LogicalQuery.SelfAndDescendants<MiddleTruncatingText>(Shown(presenter)).Any(text => text.FullText == "/proof-late"));
                    Assert.Contains("/proof-late", Titles(view));
                    Assert.Contains(LogicalQuery.SelfAndDescendants<TextBlock>(Shown(presenter)), text => text.Text == "Delayed fixture command");
                }
                Assert.Same(popup, presenter.Calls[^1].Popup);
                Assert.NotSame(panel, Shown(presenter)); // Read the newly presented content, not a freshly constructed menu.
                Assert.All(presenter.Calls.Skip(before), call => Assert.True(call.Presented)); // The panel stayed open.
                Assert.True(view.Suggestions.IsVisible);
                Assert.True(h.Main.OnMain(() => view.Controller.HasFocus));
                Assert.True(h.Main.OnMain(() => One<ComposerNSTextView>(view).IsFocused));
                Until(h, window, () => !loads.Contains(key));
                Assert.Single(script.CommandListings());
            }
            finally
            {
                script.Release();
                Until(h, window, () => !loads.Contains(key));
                Close(h, window);
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-052")] // App/Views/Composer/ComposerView.swift:213-215; Services/Providers/ProviderRegistry.swift:44,844-848
    public void B3_opening_the_command_panel_starts_a_stale_load_and_reopening_uses_the_fresh_cache()
    {
        headless.OnUiThread(() =>
        {
            System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
            using var h = new ThreadRuntimePart12Harness(ProviderKind.Claude);
            Loaded(h, h.Runtime);
            var frames = new ManualFrameSource();
            using var fs = FrameClock.Source.Override(frames);
            var presenter = new RecordingPresenter();
            using var ps = Presentation.Presenter.Override(presenter);
            var script = ProviderProbeStandIns.CopyFor("composer-opening-load").WaitForRelease().SetExitCode(0)
                .SetStdout("{\"type\":\"control_response\",\"response\":{\"request_id\":\"handshake\",\"response\":{\"commands\":[{\"name\":\"proof-late\",\"description\":\"Delayed fixture command\"}]}}}\n");
            var directory = h.Store.NewFolder("opening-load");
            var key = ProviderRegistry.CommandKey(ProviderKind.Claude, directory);
            h.Main.OnMain(() =>
            {
                h.App.Settings.SetBinaryPath(script.ExecutablePath, ProviderKind.Claude);
                h.App.Providers.UpdateCommands(new([new() { Name = "cached", Detail = "Cached command" }]), ProviderKind.Claude, directory);
                ((HashSet<ProviderKind>)typeof(ProviderRegistry).GetField("attemptedCatalogs",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(h.App.Providers)!).Add(ProviderKind.Claude);
            });
            var (view, window, _, _) = OnScreen(h, h.Runtime, directory);
            var loads = (System.Collections.IDictionary)typeof(ProviderRegistry)
                .GetField("commandLoads", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(h.App.Providers)!;
            try
            {
                Pump(h, window); // The mounted commandKey task is complete; the fresh cache suppresses its CLI read.
                Assert.False(view.Suggestions.IsVisible);
                Assert.Empty(script.CommandListings());
                Assert.False(loads.Contains(key));
                h.Main.Advance(TimeSpan.FromSeconds(601)); // :44 freshness = 600 seconds. No commandKey change.
                Assert.Empty(script.CommandListings());
                h.Main.OnMain(() => Type(view, "/")); // The visible observer alone must now ask for the listing.
                Until(h, window, () => view.Suggestions.IsVisible && script.CommandListings().Count == 1 && loads.Contains(key));
                Assert.True(h.Main.OnMain(() => view.Controller.HasFocus));
                Assert.Contains(LogicalQuery.SelfAndDescendants<MiddleTruncatingText>(Shown(presenter)), text => text.FullText == "/cached");
                script.Release();
                Until(h, window, () => !loads.Contains(key) && Titles(view).Contains("/proof-late")
                    && LogicalQuery.SelfAndDescendants<MiddleTruncatingText>(Shown(presenter)).Any(text => text.FullText == "/proof-late"));
                h.Main.OnMain(() => Type(view, ""));
                Until(h, window, () => !view.Suggestions.IsVisible);
                h.Main.OnMain(() => Type(view, "/"));
                Until(h, window, () => view.Suggestions.IsVisible && presenter.Calls[^1].Presented);
                Pump(h, window);
                Assert.Single(script.CommandListings()); // Reopening asks again but the new list is still fresh.
                Assert.Contains(LogicalQuery.SelfAndDescendants<MiddleTruncatingText>(Shown(presenter)), text => text.FullText == "/proof-late");
                Assert.True(h.Main.OnMain(() => view.Controller.HasFocus));
            }
            finally
            {
                script.Release();
                Until(h, window, () => !loads.Contains(key));
                Close(h, window);
            }
        });
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-019")] // App/Views/Composer/ComposerView.swift:244; UI/Chrome/Popovers.swift:117-133
    [Trait("Req", "UI-COMPOSER-021")] // App/Views/Composer/ComposerView.swift:245-246; UI/Chrome/Popovers.swift:135-150
    public void B3_suggestion_header_and_error_note_use_the_source_fonts_margins_and_secondary_colour()
    {
        headless.OnUiThread(()=>
        {
            using var h=RuntimeHarness(); h.Main.OnMain(()=>
            {
                var header=Assert.IsType<TextBlock>(new PopoverSectionHeader("Commands · Recently used first").Child); Assert.Equal(11,header.FontSize); Assert.Equal(FontWeight.SemiBold,header.FontWeight); Assert.Equal(new Thickness(8,6,8,3),header.Margin);
                var note=Assert.IsType<TextBlock>(new PopoverNote("Failed load").Child); Assert.Equal(12,note.FontSize); Assert.Equal(FontWeight.Normal,note.FontWeight); Assert.Equal(new Thickness(8,5),note.Margin); foreach(var text in new[]{header,note}) Assert.Equal(Chrome.SecondaryText.Resolve(text.ActualThemeVariant),Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color);
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-020")] // App/Views/Composer/ComposerView.swift:1069-1095
    public void B3_suggestion_row_preserves_26_height_8_insets_16_symbol_16_spacer_and_middle_title()
    {
        headless.OnUiThread(()=>
        {
            using var h=RuntimeHarness(); var row=h.Main.OnMain(()=>new SuggestionPopoverRow(new(){Value="@foo ",Title="foo",Detail="long/path/foo",Symbol="doc"},false,()=>{},()=>{})); var window=h.Main.OnMain(()=>Show(row,h.App,300)); Pump(h,window);
            h.Main.OnMain(()=>
            {
                var button=One<Button>(row); Assert.False(button.Focusable); var plate=Assert.IsType<Border>(button.Content); Assert.Equal(26,plate.Height); Assert.Equal(new Thickness(8,0),plate.Padding); Assert.Equal(new CornerRadius(6),plate.CornerRadius); Assert.Equal(RoundedCornerStyle.Continuous,SwiftShapes.GetCornerStyle(plate)); var grid=Assert.IsType<Grid>(plate.Child); Assert.Equal(8,grid.ColumnSpacing); Assert.Equal(16,grid.ColumnDefinitions[0].Width.Value); Assert.Equal(16,grid.ColumnDefinitions[2].Width.Value); Assert.Equal(13,One<MiddleTruncatingText>(grid).FontSize); var detail=LogicalQuery.SelfAndDescendants<TextBlock>(grid).Single(t=>t.Text=="long/path/foo"); Assert.Equal(12,detail.FontSize); Assert.Equal(1,detail.MaxLines); Assert.Equal(12,One<SwiftImage>(grid).GetValue(SwiftImage.FontProperty)!.Resolve().FontSize);
            }); Close(h,window);
        });
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-029")] // App/Views/Composer/ComposerView.swift:526-533
    [Trait("Req", "PR-COMPOSER-063")] // App/Views/Composer/ComposerView.swift:526-533
    public void B3_accepting_the_chooser_attaches_both_selected_files_and_preserves_text()
    {
        headless.OnUiThread(()=>
        {
            using var h=RuntimeHarness(); var directory=h.Store.NewFolder("chosen-files"); Touch(directory,"a.txt","b.txt"); var dialog=new ComposerDialogProbe { Result=new ValueList<string>([Path.Combine(directory,"a.txt"),Path.Combine(directory,"b.txt")]) }; using var ds=ComposerFileDialog.Current.Override(dialog); var view=Pill(h,h.Runtime);
            h.Main.OnMain(()=>{ h.Runtime.Draft=new ComposerDraft { Text="keep" }; InvokePrivate(view,"ChooseFiles"); }); Until(h,null,()=>h.Runtime.Draft.Attachments.Count==2); Assert.Equal("keep",h.Runtime.Draft.Text); Assert.Equal(["a.txt","b.txt"],h.Runtime.Draft.Attachments.Select(file=>file.Name)); Assert.All(h.Runtime.Draft.Attachments,file=>Assert.True(File.Exists(file.Path)));
        });
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-004")] // App/Views/Composer/ComposerView.swift:576-582
    public void B3_attachment_notice_is_an_11_point_one_line_warning_with_6_bottom_margin()
    {
        headless.OnUiThread(()=>
        {
            var frames=new ManualFrameSource(); using var fs=FrameClock.Source.Override(frames); using var h=RuntimeHarness(); var view=Pill(h,h.Runtime); var window=h.Main.OnMain(()=>Show(view,h.App,500)); Pump(h,window); h.Main.OnMain(()=>InvokePrivate(view,"Note","One file was left out: a message holds 8 attachments")); Pump(h,window); h.Main.OnMain(()=>
            {
                var column=One<ComposerTextColumn>(view); var text=LogicalQuery.SelfAndDescendants<TextBlock>(column).Single(t=>t.Text=="One file was left out: a message holds 8 attachments"); Assert.Equal(11,text.FontSize); Assert.Equal(1,text.MaxLines); Assert.Equal(new Thickness(0,0,0,6),text.Margin); Assert.Equal(Chrome.Warning.Resolve(text.ActualThemeVariant),Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color);
            }); Close(h,window);
        });
    }
}

// STOP #1 and #2 below are historical, resolved in B3 part 1 after impl/b2 supplied their dependencies.
// STOP App.Views.Composer.ComposerView #1
// Kind: missing dependency
// Swift: DroppyCode/App/Views/Composer/ComposerView.swift:604 (B3 52bc792)
//   .animation(liveResize.isReshaping ? nil : .smooth(duration: 0.28), value: composerHeight)
// Requirements: PR-COMPOSER-016
// What is unclear or missing: ComposerTextColumn reads @Environment(WindowLiveResize.self) (:541). In the tree
// WindowLiveResize (src/Marrow.App/Window/WindowManager.cs) has a MARROW_STUB constructor and isReshaping, and no
// environment key carries it to a view (HostedEnvironment, which would attach it, is MARROW_STUB too; RootView.cs and
// ThreadTimeline.cs record the same gap). Without it the clip's height cannot choose between the 0.28 s smooth curve
// and no animation.
// Readings or options: (a) always animate -> the live-resize lag the source comment removes comes back; (b) never
// animate -> the growth loses its 0.28 s curve. Neither is the source.
// Needed to proceed: unit App.Window.WindowManager (WindowLiveResize and HostedEnvironment) plus the environment key
// that hands WindowLiveResize to a view.
// Done so far: nothing implemented at :604; the clip takes composerHeight at once. The heights themselves are
// implemented and tested (PR-COMPOSER-015).
//
// STOP App.Views.Composer.ComposerView #2
// Kind: historical dependency blocker, resolved (RequestCards is real). PR-COMPOSER-002 is proven (container spacing 8, the 12 point
// gaps, runtime order through a removal). PR-COMPOSER-003: the curve (spring response 0.32, damping 0.9; 0.18 s ease-out under
// reduce motion), the id-list key and the arrival and resolution fades are proven (A_card_arriving_and_a_card_resolved_...,
// Changing_a_request_without_changing_the_id_list_...). RESOLVED (unit rec-composer): the stack's own re-layout glides (WindowGlide, in window
// coordinates: the area is bottom-pinned, so the cards above a new one rise and the pill stays) and a resolved card leaves the layout at once,
// fading where it stood (ComposerAreaApprovalGlideTests).
//
// STOP App.Views.Composer.ComposerView #3
// Kind: historical (untestable), resolved: DiffPopoverCoordinator presents through Presentation.Presenter (R-VIEW-9), so the
// area's wirings are observed with RecordingPresenter: The_area_opens_reopens_and_closes_the_changes_popover_... (PR-COMPOSER-008:
// the flag, the open request, the hidden column, disappearance, 640 by 540) and the placement edge and held desire tests
// (PR-COMPOSER-009). The thread-change arm (:40-41) cannot fire: ThreadRuntime.threadID is `let` (ThreadRuntime.swift:169).
//
// STOP App.Views.Composer.ComposerView #4
// Kind: historical dependency blocker, resolved; command-observer proof gaps resolved by notify2-proofs.
// Swift: DroppyCode/App/Views/Composer/ComposerView.swift:204-215 (B3 52bc792)
// Requirements: PR-COMPOSER-051, PR-COMPOSER-052
// Resolution: ComposerControls is implemented, and a resolved thread's pill can be mounted and focused. The
// B3_delayed_listing_refreshes_the_focused_open_panel_with_commands_or_a_late_error cases now hold an offline
// CLI listing until the panel is already open, then prove the presented command/error content and retained focus.
// B3_opening_the_command_panel_starts_a_stale_load_and_reopening_uses_the_fresh_cache proves the visible observer
// starts the listing without a direct LoadCommands call, then reopens from the fresh cache without a new listing.
// The delayed cases also reach the commandKey task on mount (:204). Existing PR-COMPOSER-051 tests retain the
// thread-provider/directory and missing-directory assertions. Nothing remains blocked at these observer sites.
// This uses the R-VIEW-9 recording presenter; native popup hit-testing and scrolling remain separately in STOP #5.
//
// STOP App.Views.Composer.ComposerView #5
// Kind: resolved for (a), (b) and (c).
// (a) PR-COMPOSER-053: RESOLVED (unit rec-composer) on a real window: ComposerSuggestionsNativeTests.A_press_inside_the_open_panel_keeps_the_list_and_
// a_press_on_the_transcript_clears_it (a focus loss with the real pointer over the panel's HWND keeps the list, a real press on a row keeps it and
// picks, a real press on the transcript's text clears it and cancels its refresh); headless proves a refresh still searching is cancelled by the blur.
// (b) PR-COMPOSER-055: resolved, Selecting_a_row_far_down_a_long_list_scrolls_it_into_view hosts each show's frame in a window.
// (c) PR-COMPOSER-050: resolved, a retry that fails again replaces the note (A_retry_that_fails_again_...).
//
// STOP App.Views.Composer.ComposerView #7
// Kind: resolved for PR-COMPOSER-088's clauses and for UI-COMPOSER-007. DraftChips keeps one row, one scroller and its chips, applies
// them through the keyed ForEach under panelSlide when the ChipKey changes and asks for no frame on a text edit. A leaving chip fades
// where it stands while its place (ChipSlot) closes under panelSlide and the chips after it follow, on the layout, under reduce motion
// too (ED-282-12; Removing_the_middle_quote_fades_it_where_it_stands_...).
//
// STOP App.Views.Composer.ComposerView #11: RESOLVED (unit rec-composer). UI-COMPOSER-029 (the attach dialog, ComposerView.swift:526-533,
// PromptEditor.swift:117-124). The options reach the runner (multiple true, directories false, "Attach") and cancel and accept are seam-tested
// (B3_file_chooser_..., B3_attach_default_dialog_..., B3_accepting_the_chooser_...); on a real window the real common dialog is driven from both
// sites by OpenPanelNativeTests: OK reads "Attach", two files multi-select and both reach the draft, a folder's name and OK open it and nothing
// is chosen, Cancel leaves the draft. The caption an empty SetTitle("") gives (OpenPanel.cs:74) is the system's "Open" (recorded, ED-333).
//
// STOP App.Views.Composer.ComposerView #12: RESOLVED 2026-10-04 (unit rec-tlr, UI-CHAT-052). The foundation's SoftAppear modifier had no animatable data, so
// SwiftTransition applied the identity state at once (SwiftAnimation.cs Gather: `to.Update(body)`): every SoftAppear site arrived at full opacity at its first frame
// where SwiftUI animates the opacity and the 4 point offset inside the modifier's body over smooth(0.32). SoftAppearModifier now carries a progress that the transition
// interpolates (Chrome.cs), so the shared SoftAppear and SettleGlide arrive on that curve. The attachment notice still plays its arrival itself (NoticeSlot.Arrive).
//
// STOP App.Views.Composer.ComposerView #13: RESOLVED (unit rec-composer). UI-COMPOSER-002's record was amended to 46 (0f85fb3a1, ED-282-11), the source's
// arithmetic and the reference video's measure (An_empty_pill_is_46_points_high_...); the pill's glass is drawn (ED-320; ComposerAreaGlassFrostTests,
// GlassMaterialNativeTests). Its r 22 corner on the 46 pt pill is the circular arc the video measures (ED-275 (2); OD-0102 keeps the composer as measured).
//
// STOP App.Views.Composer.ComposerView #10
// Kind: resolved for the container (SwiftGlass.SetContainerSpacing is implemented and asserted: PR-COMPOSER-002, UI-COMPOSER-001);
// the surviving approval rows' movement glides, see #2. Continuous-corner rendering is ED-181, closed 2026-10-04.

// B3 resolution of historical STOP #5(c): forced retry is now proven by observing the in-flight registry load after
// clicking the actual retry row while a fetched command list is still fresh. #5(a) and (b) are resolved too (see #5).
