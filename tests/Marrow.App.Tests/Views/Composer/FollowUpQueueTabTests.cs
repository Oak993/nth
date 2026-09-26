using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Marrow.App;
using Marrow.App.Runtime;
using Marrow.App.Views.Composer;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Composer;

[Collection("Headless")]
public sealed class FollowUpQueueTabTests(HeadlessSession headless)
{
    // Same fixture shape as TerminalPanelTests.MakeFixture: AppModelHarness + store.Main.OnMain(() => new
    // ThreadRuntime(...)), nested INSIDE headless.OnUiThread (never the other way around, and never
    // ThreadRuntimePart12Harness's PrepareBootstrap alongside Avalonia control construction - see this file's own
    // STOP note below on why that combination corrupted the headless platform for later tests).
    private static (AppModel App, ThreadRuntime Runtime, AppModelHarness Store) MakeRuntimeFixture()
    {
        var store = new AppModelHarness();
        var project = AppModelSamples.Project("Follow-ups", store.NewFolder("follow-ups"));
        var thread = AppModelSamples.Thread(project);
        store.WriteLibrary([project], [thread]);
        AppModelHarness.WriteDocument(thread.Id);
        var app = store.MakeModel();
        var runtime = store.Main.OnMain(() => new ThreadRuntime(thread.Id, app));
        return (app, runtime, store);
    }

    // EnqueueFollowUp's own IsEmpty guard calls SwiftCharacter.IsWhitespace, which needs the ICU native library
    // (unavailable on this Linux host - WINDOWS-ONLY, not this lane's concern); a prompt built directly and
    // written straight into FollowUps's private setter never reaches that check.
    private static FollowUpPrompt SeedFollowUp(ThreadRuntime runtime, string text)
    {
        var prompt = new FollowUpPrompt { Text = text };
        var property = typeof(ThreadRuntime).GetProperty(nameof(ThreadRuntime.FollowUps))!;
        property.SetValue(runtime, new ValueList<FollowUpPrompt>([prompt]));
        return prompt;
    }

    private static FollowUpRow MakeRow(FollowUpPrompt prompt, ThreadRuntime runtime, bool isDragged) => new(
        position: 1,
        prompt: prompt,
        runtime: runtime,
        isDragged: isDragged,
        showsRule: false,
        isBundledWithPrevious: false,
        isBundledWithNext: false,
        isPairTarget: false,
        isPairing: false,
        onDragChanged: _ => { },
        onDragEnded: () => { });

    private static SwiftImage FindGrip(FollowUpRow row) =>
        LogicalQuery.SelfAndDescendants<SwiftImage>(row).First(icon => icon.SymbolName == "line.3.horizontal");

    // Same shape as FollowUpEditorTests.Build/Settle: a real attached Window, so AttachedToVisualTree, hit
    // testing and hover tracking all run for real instead of being simulated by hand.
    private static AvaloniaWindow AttachInWindow(Control content)
    {
        var window = new AvaloniaWindow { Content = content, Width = 400, Height = 80 };
        window.Show();
        Settle(window);
        return window;
    }

    private static void Settle(Avalonia.Controls.TopLevel window)
    {
        for (var pass = 0; pass < 5; pass++)
        {
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
    }

    private static Point Center(Visual control, Visual ancestor) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), ancestor)!.Value;

    // Same idiom as ChromeFourthTests/ChromeFifthTests.Effective: alpha folded in from the brush's own Opacity,
    // so a .opacity(x)-only change (no RGB change) is visible in the returned Color's alpha channel.
    private static Color Effective(IBrush? brush)
    {
        if (brush is null) return Color.FromArgb(0, 0, 0, 0);
        var solid = Assert.IsAssignableFrom<ISolidColorBrush>(brush);
        return Color.FromArgb((byte)Math.Round(solid.Color.A * solid.Opacity), solid.Color.R, solid.Color.G, solid.Color.B);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-159")] // App/Views/Composer/FollowUpQueueTab.swift:378-384
    [Trait("Decision", "ED-205")]
    public void The_grip_shows_grabidle_when_idle_and_grabactive_while_this_row_is_the_dragged_one()
    {
        headless.OnUiThread(() =>
        {
            var (app, runtime, store) = MakeRuntimeFixture();
            using var storeDisposable = store;
            store.Main.OnMain(() =>
            {
                var prompt = SeedFollowUp(runtime, "Do the thing");

                var idle = FindGrip(MakeRow(prompt, runtime, isDragged: false));
                Assert.Same(PointerStyleCursors.Get(PointerStyle.GrabIdle, 1), idle.Cursor);

                var dragged = FindGrip(MakeRow(prompt, runtime, isDragged: true));
                Assert.Same(PointerStyleCursors.Get(PointerStyle.GrabActive, 1), dragged.Cursor);
            });
            _ = app;
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-163")] // App/Views/Composer/FollowUpQueueTab.swift:284-296
    [Trait("Decision", "ED-196")]
    public void UpdateState_reverts_the_grip_to_grabidle_once_isDragged_becomes_false_with_no_explicit_pop()
    {
        headless.OnUiThread(() =>
        {
            var (app, runtime, store) = MakeRuntimeFixture();
            using var storeDisposable = store;
            store.Main.OnMain(() =>
            {
                var prompt = SeedFollowUp(runtime, "Do the thing");

                var row = MakeRow(prompt, runtime, isDragged: true);
                Assert.Same(PointerStyleCursors.Get(PointerStyle.GrabActive, 1), FindGrip(row).Cursor);

                row.UpdateState(dragged: false, pairTarget: false, pairing: false,
                    bundledWithPrevious: false, bundledWithNext: false, rule: false);
                Assert.Same(PointerStyleCursors.Get(PointerStyle.GrabIdle, 1), FindGrip(row).Cursor);
            });
            _ = app;
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-159")] // App/Views/Composer/FollowUpQueueTab.swift:378-383
    [Trait("Req", "UI-COMPOSER-052")]
    public void The_grip_brightens_on_hover_via_isHoveringGrip_alone()
    {
        headless.OnUiThread(() =>
        {
            var (app, runtime, store) = MakeRuntimeFixture();
            using var storeDisposable = store;
            store.Main.OnMain(() =>
            {
                var prompt = SeedFollowUp(runtime, "Do the thing");
                var row = MakeRow(prompt, runtime, isDragged: false);
                var window = AttachInWindow(row);

                var idle = Effective(FindGrip(row).Foreground);
                Assert.Equal(Effective(Chrome.SecondaryText.Opacity(0.7).Brush(row)), idle);

                window.MouseMove(Center(FindGrip(row), window), RawInputModifiers.None);
                Settle(window);
                var hovering = Effective(FindGrip(row).Foreground);
                Assert.Equal(Effective(Chrome.SecondaryText.Opacity(1).Brush(row)), hovering);
                Assert.NotEqual(idle, hovering);

                window.MouseMove(new Point(5, 70), RawInputModifiers.None);
                Settle(window);
                Assert.Equal(idle, Effective(FindGrip(row).Foreground));

                window.Close();
            });
            _ = app;
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-159")] // App/Views/Composer/FollowUpQueueTab.swift:386-389
    public void A_press_and_drag_starting_off_the_grip_never_starts_a_reorder()
    {
        headless.OnUiThread(() =>
        {
            var (app, runtime, store) = MakeRuntimeFixture();
            using var storeDisposable = store;
            store.Main.OnMain(() =>
            {
                var prompt = SeedFollowUp(runtime, "Do the thing");
                var changes = 0;
                var row = new FollowUpRow(1, prompt, runtime, isDragged: false, showsRule: false,
                    isBundledWithPrevious: false, isBundledWithNext: false, isPairTarget: false, isPairing: false,
                    onDragChanged: _ => changes++, onDragEnded: () => { });
                var window = AttachInWindow(row);

                var text = LogicalQuery.SelfAndDescendants<TextBlock>(row).First(t => t.Text == prompt.Text);
                var start = Center(text, window);
                window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
                window.MouseMove(start + new Vector(0, 20), RawInputModifiers.LeftMouseButton);
                window.MouseUp(start + new Vector(0, 20), MouseButton.Left, RawInputModifiers.None);
                Settle(window);

                Assert.Equal(0, changes);
                window.Close();
            });
            _ = app;
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-159")] // App/Views/Composer/FollowUpQueueTab.swift:386-389
    public void A_press_and_drag_starting_on_the_grip_still_starts_the_reorder()
    {
        headless.OnUiThread(() =>
        {
            var (app, runtime, store) = MakeRuntimeFixture();
            using var storeDisposable = store;
            store.Main.OnMain(() =>
            {
                var prompt = SeedFollowUp(runtime, "Do the thing");
                var changes = 0;
                var row = new FollowUpRow(1, prompt, runtime, isDragged: false, showsRule: false,
                    isBundledWithPrevious: false, isBundledWithNext: false, isPairTarget: false, isPairing: false,
                    onDragChanged: _ => changes++, onDragEnded: () => { });
                var window = AttachInWindow(row);

                var start = Center(FindGrip(row), window);
                window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
                window.MouseMove(start + new Vector(0, 20), RawInputModifiers.LeftMouseButton);
                window.MouseUp(start + new Vector(0, 20), MouseButton.Left, RawInputModifiers.None);
                Settle(window);

                Assert.True(changes > 0);
                window.Close();
            });
            _ = app;
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-159")] // App/Views/Composer/FollowUpQueueTab.swift:384; Marrow.UI.Port.PointerStyle.cs:44-48
    public void The_grip_cursor_is_rebaked_to_the_windows_real_render_scaling_once_attached()
    {
        headless.OnUiThread(() =>
        {
            var (app, runtime, store) = MakeRuntimeFixture();
            using var storeDisposable = store;
            store.Main.OnMain(() =>
            {
                var prompt = SeedFollowUp(runtime, "Do the thing");
                var row = MakeRow(prompt, runtime, isDragged: false);

                // Baked at construction time, before the row is attached to anything: RenderScaling defaults to
                // 1 (Marrow.UI.Port.PointerStyle.cs:44), same as the two grip tests above.
                Assert.Same(PointerStyleCursors.Get(PointerStyle.GrabIdle, 1), FindGrip(row).Cursor);

                // Show and scale the window BEFORE attaching the row, so the grip's AttachedToVisualTree handler
                // sees the real (non-1) RenderScaling the first time it runs.
                var window = new AvaloniaWindow { Width = 400, Height = 80 };
                window.Show();
                window.SetRenderScaling(2);
                window.Content = row;
                Settle(window);

                Assert.Same(PointerStyleCursors.Get(PointerStyle.GrabIdle, 2), FindGrip(row).Cursor);

                window.Close();
            });
            _ = app;
        });
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-154")] // App/Views/Composer/FollowUpQueueTab.swift:13-25,128-136
    public void Capped_height_passes_through_below_the_row_cap_or_before_any_row_is_measured_and_caps_at_seven_mean_rows()
    {
        var heights = new ValueMap<Guid, double>([
            new KeyValuePair<Guid, double>(Guid.NewGuid(), 30),
            new KeyValuePair<Guid, double>(Guid.NewGuid(), 50),
        ]);

        Assert.Equal(400, FollowUpQueueTab.CappedHeight(400, heights, FollowUpQueueTab.MaxRows));
        Assert.Equal(400, FollowUpQueueTab.CappedHeight(400, ValueMap<Guid, double>.Empty, FollowUpQueueTab.MaxRows + 1));
        Assert.Equal(600, FollowUpQueueTab.CappedHeight(400, heights, FollowUpQueueTab.MaxRows + 1));

        var sevenMeasuredRows = new ValueMap<Guid, double>(
            System.Linq.Enumerable.Range(0, 8).Select(_ => new KeyValuePair<Guid, double>(Guid.NewGuid(), 32d)));
        Assert.Equal(257, FollowUpQueueTab.CappedHeight(33 + 8 * 32, sevenMeasuredRows, FollowUpQueueTab.MaxRows + 1));
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-054")] // App/Views/Composer/FollowUpQueueTab.swift:434-439,512-532
    [Trait("Decision", "ED-059")]
    [Trait("Decision", "ED-062")]
    public void Send_button_uses_the_run_state_help_label_and_is_never_focusable()
    {
        headless.OnUiThread(() =>
        {
            var labels = new[]
            {
                "Send now",
                "Send now (stops the running turn)",
                "Send as soon as the heads report",
            };

            foreach (var label in labels)
            {
                var invoked = false;
                var control = new QueueIconButton("paperplane", label, () => invoked = true);
                var button = Assert.IsType<Button>(control.Child);
                var icon = Assert.IsType<SwiftImage>(button.Content);

                Assert.False(button.Focusable);
                Assert.Equal(22, button.Width);
                Assert.Equal(22, button.Height);
                Assert.Equal(label, ToolTip.GetTip(button));
                Assert.Equal(label, AutomationProperties.GetName(button));
                Assert.Equal("paperplane", icon.SymbolName);
                Assert.Equal(11, Assert.IsType<SwiftFont>(SwiftImage.GetFont(icon)).Resolve().FontSize);
                Assert.Equal(SwiftFont.Weight.Semibold, SwiftImage.GetFont(icon)!.Resolve().FontWeight == FontWeight.SemiBold
                    ? SwiftFont.Weight.Semibold
                    : (SwiftFont.Weight?)null);

                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(invoked);
            }
        });
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-055")] // App/Views/Composer/FollowUpQueueTab.swift:440-449,512-532
    [Trait("Decision", "ED-059")]
    [Trait("Decision", "ED-062")]
    public void Edit_button_uses_the_shared_help_and_accessibility_label_and_is_never_focusable()
    {
        headless.OnUiThread(() =>
        {
            var invoked = false;
            var control = new QueueIconButton("pencil", "Edit follow-up", () => invoked = true);
            var button = Assert.IsType<Button>(control.Child);
            var icon = Assert.IsType<SwiftImage>(button.Content);

            Assert.False(button.Focusable);
            Assert.Equal(22, button.Width);
            Assert.Equal(22, button.Height);
            Assert.Equal("Edit follow-up", ToolTip.GetTip(button));
            Assert.Equal("Edit follow-up", AutomationProperties.GetName(button));
            Assert.Equal("pencil", icon.SymbolName);
            Assert.Equal(11, SwiftImage.GetFont(icon)!.PointSize);
            Assert.Equal(SwiftFont.Weight.Semibold, SwiftImage.GetFont(icon)!.Resolve().FontWeight == FontWeight.SemiBold
                ? SwiftFont.Weight.Semibold
                : (SwiftFont.Weight?)null);

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(invoked);
        });
    }

    [Fact]
    [Trait("Req", "UI-COMPOSER-056")] // App/Views/Composer/FollowUpQueueTab.swift:450-452,512-532
    [Trait("Decision", "ED-059")]
    [Trait("Decision", "ED-062")]
    public void Delete_button_uses_the_shared_help_and_accessibility_label_and_is_never_focusable()
    {
        headless.OnUiThread(() =>
        {
            var invoked = false;
            var control = new QueueIconButton("trash", "Delete follow-up", () => invoked = true);
            var button = Assert.IsType<Button>(control.Child);
            var icon = Assert.IsType<SwiftImage>(button.Content);

            Assert.False(button.Focusable);
            Assert.Equal(22, button.Width);
            Assert.Equal(22, button.Height);
            Assert.Equal("Delete follow-up", ToolTip.GetTip(button));
            Assert.Equal("Delete follow-up", AutomationProperties.GetName(button));
            Assert.Equal("trash", icon.SymbolName);
            Assert.Equal(11, SwiftImage.GetFont(icon)!.PointSize);
            Assert.Equal(SwiftFont.Weight.Semibold, SwiftImage.GetFont(icon)!.Resolve().FontWeight == FontWeight.SemiBold
                ? SwiftFont.Weight.Semibold
                : (SwiftFont.Weight?)null);

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(invoked);
        });
    }
}

// STOP App.Views.Composer.FollowUpQueueTab #1 (helper Claude, 2026-09-25)
// Kind: coverage gap, not a code defect - the implementation (FollowUpQueueTab.cs) has 0 MARROW_STUB sites; every
// member with behaviour is real, non-stub code that traces to the Swift source.
// Tested directly: PR-COMPOSER-154 (CappedHeight), UI-COMPOSER-054/055/056 (QueueIconButton help/accessibility/click),
// PR-COMPOSER-159/UI-COMPOSER-052 (grip cursor idle/active, hover brightening, drag scoped to the grip's own hit
// rect, cursor rebaked once attached to a real RenderScaling), PR-COMPOSER-163 (grip reverts to grab-idle with no
// explicit pop). A38's "CLOUD REPORT A38" commit added the AppModelHarness + store.Main.OnMain(() => new
// ThreadRuntime(...)) pattern (nested inside headless.OnUiThread, matching TerminalPanelTests) that resolves this
// note's own blocker below; the later grip tests reuse it (MakeRuntimeFixture/SeedFollowUp).
// Not independently tested: PR-COMPOSER-151/152/153/155/156/157/158/161/162/164/165/166/168/173 and
// UI-COMPOSER-047 through 051, 053. ThreadRuntime.FollowUps has a public getter and a real, reachable private setter
// (reflection: typeof(ThreadRuntime).GetProperty("FollowUps").SetValue(runtime, new ValueList<FollowUpPrompt>([new
// FollowUpPrompt { Text = "..." }]))), and ThreadRuntimePart12Harness gives a real offline ThreadRuntime, so seeding
// the queue is not the blocker. What is: ThreadRuntime's state mutators (including that property's setter) assert
// MainActor isolation and must run through harness.Main.OnMain(...), while FollowUpQueueTab's constructor calls
// SwiftImage.SystemName, which needs Avalonia's headless platform active via headless.OnUiThread(...) - and tried
// together in either order this session, one broke the other: nesting ThreadRuntimePart12Harness construction (it
// calls LoginEnvironment.Load().Wait(Bound) during PrepareBootstrap) inside headless.OnUiThread deadlocked to its
// 120s bound; running harness.Main.OnMain(...) outside headless.OnUiThread let ThreadRuntime mutate correctly but
// then SwiftImage.SystemName threw "Unable to locate Avalonia.Platform.IAssetLoader" - and, more importantly, left
// the Avalonia headless platform registration broken for the *other* tests in this file that ran afterward in the
// same process (Send/Edit/Delete button tests started failing with the same missing-IAssetLoader error, though they
// need nothing from ThreadRuntime and passed before). Reverted rather than risk that cross-test corruption landing
// on impl/b2. Whoever revisits this needs to find the establishe way this codebase runs Avalonia control
// construction and ThreadRuntime's MainActor-isolated mutation in the same test without one clobbering the other's
// platform/dispatcher state - check for a combined harness pattern in a merged unit whose View takes a ThreadRuntime
// (none was found among currently-merged tests this session).
