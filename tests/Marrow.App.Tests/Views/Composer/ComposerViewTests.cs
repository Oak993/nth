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
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.App.Views.Diff;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/ComposerView.swift (B3 52bc792), :4-112, :139-189, :275-422, :540-607,
// :1048, :1290-1307. Part 1 of 5 of App.Views.Composer.ComposerView: the area, the tab slot, the stats refresh, the
// pill's layout, placeholder, keys, send and recall, the text column and the surface.
//
// Two runtimes are used. The harness's own runtime has a thread the model resolves; the pill's controls
// (ComposerControls, part 3 of this unit) are built for a resolved thread, so a pill with such a runtime is only
// exercised unattached (its keys through the text view's own key path, its placeholder by calling it). A runtime made
// for a thread id the model does not hold resolves no thread, so the pill leaves its controls out
// (ComposerView.swift:160) and the whole area can be attached and laid out.

[Collection("Headless")]
public sealed class ComposerViewTests(HeadlessSession headless)
{
    private const string Plan = "Describe what you want to plan";

    private static ThreadRuntimePart12Harness RuntimeHarness()
    {
        // FollowUpEditorTests' note: LoginEnvironment.Load is completed once on a pool thread first, so the harness's
        // own Load().Wait() on the headless UI thread returns at once instead of deadlocking.
        System.Threading.Tasks.Task.Run(() => Marrow.Core.Support.LoginEnvironment.Load()).GetAwaiter().GetResult();
        var h = new ThreadRuntimePart12Harness();
        Loaded(h, h.Runtime);
        return h;
    }

    // The runtime installs its thread document when the load lands (ThreadRuntime.swift:539-554), replacing turns and
    // entries, so a test waits for it before rehearsing anything.
    private static void Loaded(ThreadRuntimePart12Harness h, ThreadRuntime runtime) =>
        h.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);

    private static ThreadRuntime Unresolved(ThreadRuntimePart12Harness h)
    {
        var runtime = h.Main.OnMain(() => new ThreadRuntime(Guid.NewGuid(), h.App));
        Loaded(h, runtime);
        return runtime;
    }

    private static AvaloniaWindow Show(Control content, AppModel app, double width)
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

    private static Border Host(AvaloniaWindow window) => (Border)window.Content!;

    private static void Settle(AvaloniaWindow window)
    {
        for (var pass = 0; pass < 5; pass++)
        {
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
    }

    // Track reactions land on the next main turn; layout and the text box's posted jobs run on the dispatcher.
    private static void Pump(ThreadRuntimePart12Harness h, AvaloniaWindow window)
    {
        for (var round = 0; round < 4; round++) h.Main.OnMain(() => Settle(window));
    }

    private static void Close(ThreadRuntimePart12Harness h, AvaloniaWindow window) => h.Main.OnMain(() =>
    {
        Settle(window);
        window.Close();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    });

    private static Rect Frame(Visual control, Visual relativeTo) =>
        new(control.TranslatePoint(default, relativeTo)!.Value, control.Bounds.Size);

    private static T One<T>(ILogical root) where T : class => LogicalQuery.SelfAndDescendants<T>(root).Single();

    private static IEnumerable<ILogical> Ancestors(ILogical control)
    {
        for (var parent = control.LogicalParent; parent is not null; parent = parent.LogicalParent) yield return parent;
    }

    // The VStack(alignment: .center, spacing: -ThreadChangesTab.overlap) holding the tab slot and the pill (:28-31).
    private static StackPanel Group(ComposerArea area) => (StackPanel)One<ComposerView>(area).Parent!;

    // The VStack(spacing: 12) holding the approval cards and that group (:24-50).
    private static StackPanel Stack(ComposerArea area) =>
        Ancestors(Group(area)).OfType<StackPanel>().First(panel => panel.Spacing == 12);

    private static string Patch(string path) =>
        "diff --git a/" + path + " b/" + path + "\nnew file mode 100644\n--- /dev/null\n+++ b/" + path + "\n@@ -0,0 +1 @@\n+new\n";

    // A turn whose provider patch adds one file, then the stats the changes tab shows (ThreadRuntime.swift:1945-1953).
    private static void GiveStats(ThreadRuntimePart12Harness h, ThreadRuntime runtime, string path = "src/new.txt")
    {
        h.Main.OnMain(() => runtime.RehearseTurn("edit " + path, providerDiff: Patch(path)));
        h.Main.OnMain(runtime.NoteDiffChanged);
        h.Main.Loop.Run(() => runtime.RefreshChangeStats(), AppModelHarness.Bound);
        Assert.NotNull(runtime.ChangeStats);
    }

    private static bool Press(ComposerView view, Key key)
    {
        var textView = One<ComposerNSTextView>(view);
        var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key };
        textView.RaiseEvent(args);
        return args.Handled;
    }

    private static ComposerView Pill(ThreadRuntimePart12Harness h, ThreadRuntime runtime) => h.Main.OnMain(() =>
    {
        var view = new ComposerView(runtime, workingDirectory: null);
        AppEnvironment.SetAppModel(view, h.App);
        Drain();
        return view;
    });

    // The text box raises TextChanged from a posted dispatcher job (FollowUpEditorTests' note); that reaches the draft
    // through the text binding, so it must run on the main actor, inside h.Main.OnMain.
    private static void Drain() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    // ---- ComposerArea (:4-58) ----

    [Fact]
    [Trait("Req", "PR-COMPOSER-001")] // App/Views/Composer/ComposerView.swift:52-55
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
            h.Main.OnMain(() => runtime.EnqueueFollowUp("queued prompt", []));
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
    public void The_pill_overlaps_the_tab_by_14_points_through_negative_stack_spacing_and_draws_above_it()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var runtime = Unresolved(h);
            var area = h.Main.OnMain(() => new ComposerArea(runtime, workingDirectory: null));
            var window = h.Main.OnMain(() => Show(area, h.App, width: 900));
            h.Main.OnMain(() => runtime.EnqueueFollowUp("queued prompt", []));
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
            Assert.Equal(GlassEffect.Regular(GlassShape.RoundedRectangle(22, RoundedCornerStyle.Continuous)), SwiftGlass.GetEffect(shape));
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
            Assert.Equal(GlassEffect.Regular(GlassShape.RoundedRectangle(22, RoundedCornerStyle.Continuous)), SwiftGlass.GetEffect(shape));
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

    private static ComposerTextColumn Column(ThreadRuntime runtime, string? notice) => new(
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
                var notice = Assert.IsType<TextBlock>(stack.Children[2]);
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
    private static double FiveLineCeiling()
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
}

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
// Kind: missing dependency
// Swift: DroppyCode/App/Views/Composer/ComposerView.swift:25-27, :56 (B3 52bc792)
// Requirements: PR-COMPOSER-002, PR-COMPOSER-003
// What is unclear or missing: the approval cards are ApprovalCard (App.Views.Composer.RequestCards), whose constructor
// is MARROW_STUB in the tree, so no card can be built and neither the order and 12 point spacing of real cards nor the
// Chrome.panelSlide re-layout keyed on their ids can be observed. The site itself is written (keyed rows under
// Transaction.WithAnimation(Chrome.PanelSlide) when the id list changes).
// Needed to proceed: unit App.Views.Composer.RequestCards.
// Done so far: the empty case of PR-COMPOSER-002 is tested and passes; PR-COMPOSER-003 has no test.
//
// STOP App.Views.Composer.ComposerView #3
// Kind: untestable
// Swift: DroppyCode/App/Views/Composer/ComposerView.swift:32-34, :40-49, :84-85 (B3 52bc792)
// Requirements: PR-COMPOSER-008, PR-COMPOSER-009
// What is unclear or missing: DiffPopoverCoordinator (DiffInspector.cs) opens its Popup by setting IsOpen directly,
// and Avalonia 12.1.2 headless throws "Unable to create IPopupImpl and no overlay layer is found for the target
// control" for it (probed on this machine for this unit, with and without a VisualLayerManager). The open, reopen and
// close wirings and which anchor the popover hangs from are therefore not observable in a test.
// Needed to proceed: a decision on how DiffPopoverCoordinator presents (the R-VIEW-9 seam), in its own unit.
// Done so far: the sites are written; the arrival rule on appear and the 640 by 540 size are tested and pass.
