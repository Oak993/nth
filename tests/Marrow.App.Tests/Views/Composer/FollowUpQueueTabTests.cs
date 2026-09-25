using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Marrow.App.Views.Composer;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Composer;

[Collection("Headless")]
public sealed class FollowUpQueueTabTests(HeadlessSession headless)
{
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
    [Trait("Req", "UI-COMPOSER-054")] // App/Views/Composer/FollowUpQueueTab.swift:449-454,527-547
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
    [Trait("Req", "UI-COMPOSER-055")] // App/Views/Composer/FollowUpQueueTab.swift:455-464,527-547
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
    [Trait("Req", "UI-COMPOSER-056")] // App/Views/Composer/FollowUpQueueTab.swift:465-467,527-547
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
// Tested directly: PR-COMPOSER-154 (CappedHeight), UI-COMPOSER-054/055/056 (QueueIconButton help/accessibility/click).
// Not independently tested: PR-COMPOSER-151/152/153/155/156/157/158/159/161/162/163/164/165/166/168/173 and
// UI-COMPOSER-047 through 053. ThreadRuntime.FollowUps has a public getter and a real, reachable private setter
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
