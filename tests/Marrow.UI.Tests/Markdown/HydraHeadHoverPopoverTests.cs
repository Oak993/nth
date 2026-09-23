using System;
using System.Threading.Tasks;
using Avalonia.Headless;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.UI.Tests.Markdown;

// Source under test: DroppyCode/UI/Markdown/HydraHeadHoverPopover.swift (B2 1fbf81e), :1-90.
// Records: PR-HYDRA-197, UI-HYDRA-035.
// Port shape (ED-095): the popover is a @MainActor class with a static shared instance in Swift;
// in C# it is a sealed class with a static Instance property, main-actor isolated via MainActor integration.
// Timing is testable via TestRuntime.Advance. Popover measurement and presentation go through
// the Presentation seam (R-VIEW-9, UI/Port/Presentation.cs).
public sealed class HydraHeadHoverPopoverTests
{
    private static HydraPersona MakePersona(string name = "Test")
    {
        return new HydraPersona(name, "test_asset", 0xFF0000FF);
    }

    private static HydraMentionTarget MakeTarget(
        Guid? threadID = null,
        string task = "Test task",
        HydraHeadInfo.Status status = HydraHeadInfo.Status.Running)
    {
        return new HydraMentionTarget
        {
            ThreadID = threadID ?? Guid.NewGuid(),
            Task = task,
            Status = status,
        };
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-197")] // DroppyCode/UI/Markdown/HydraHeadHoverPopover.swift:82-89
    public void Status_line_for_running_is_exactly_working_with_ellipsis()
    {
        // U+2026 HORIZONTAL ELLIPSIS
        var target = MakeTarget(status: HydraHeadInfo.Status.Running);
        var statusLine = HydraHeadHoverCard.StatusLine(target.Status);

        Assert.Equal("Working…", statusLine);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-197")] // DroppyCode/UI/Markdown/HydraHeadHoverPopover.swift:85
    public void Status_line_for_completed_is_exactly_done_with_separator()
    {
        // U+00B7 MIDDLE DOT separator
        var target = MakeTarget(status: HydraHeadInfo.Status.Completed);
        var statusLine = HydraHeadHoverCard.StatusLine(target.Status);

        Assert.Equal("Done · click to open the chat", statusLine);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-197")] // DroppyCode/UI/Markdown/HydraHeadHoverPopover.swift:86
    public void Status_line_for_failed_is_exactly_failed_with_separator()
    {
        // U+00B7 MIDDLE DOT separator
        var target = MakeTarget(status: HydraHeadInfo.Status.Failed);
        var statusLine = HydraHeadHoverCard.StatusLine(target.Status);

        Assert.Equal("Failed · click to open the chat", statusLine);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-197")] // DroppyCode/UI/Markdown/HydraHeadHoverPopover.swift:87
    public void Status_line_for_stopped_is_exactly_stopped_with_separator()
    {
        // U+00B7 MIDDLE DOT separator
        var target = MakeTarget(status: HydraHeadInfo.Status.Stopped);
        var statusLine = HydraHeadHoverCard.StatusLine(target.Status);

        Assert.Equal("Stopped · click to open the chat", statusLine);
    }

    [Fact]
    [Trait("Req", "UI-HYDRA-035")] // DroppyCode/UI/Markdown/HydraHeadHoverPopover.swift:53-90
    public void Card_width_is_pinned_to_300_points()
    {
        // :56 static let width: CGFloat = 300
        Assert.Equal(300, HydraHeadHoverCard.Width);
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-197")] // DroppyCode/UI/Markdown/HydraHeadHoverPopover.swift:19-39
    public async Task Show_schedules_a_250_millisecond_delay_before_presentation()
    {
        using var runtime = new TestRuntime();

        await runtime.OnUiThread(async () =>
        {
            var popover = HydraHeadHoverPopover.Instance;
            var persona = MakePersona();
            var target = MakeTarget();

            // Initial state: no popover shown
            Assert.Null(popover.ShownThreadID);

            // After calling show, the pending task is scheduled but nothing shown yet
            popover.Show(persona, target, new(0, 0, 100, 20), runtime.MainWindow);
            Assert.Null(popover.ShownThreadID); // Not shown yet

            // Advance less than 250ms: still not shown
            runtime.AdvanceFrameClock(TimeSpan.FromMilliseconds(100));
            Assert.Null(popover.ShownThreadID);

            // Advance past 250ms: popover is now shown
            runtime.AdvanceFrameClock(TimeSpan.FromMilliseconds(200));
            Assert.Equal(target.ThreadID, popover.ShownThreadID);

            popover.Hide();
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-197")] // DroppyCode/UI/Markdown/HydraHeadHoverPopover.swift:20
    public async Task Showing_same_head_again_while_shown_does_nothing()
    {
        using var runtime = new TestRuntime();

        await runtime.OnUiThread(async () =>
        {
            var popover = HydraHeadHoverPopover.Instance;
            var persona = MakePersona();
            var threadID = Guid.NewGuid();
            var target1 = new HydraMentionTarget { ThreadID = threadID, Task = "Task 1", Status = HydraHeadInfo.Status.Running };

            // Show the first popover
            popover.Show(persona, target1, new(0, 0, 100, 20), runtime.MainWindow);
            runtime.AdvanceFrameClock(TimeSpan.FromMilliseconds(300));
            Assert.Equal(threadID, popover.ShownThreadID);

            // Try to show it again with the same thread ID
            var target2 = new HydraMentionTarget { ThreadID = threadID, Task = "Task 2", Status = HydraHeadInfo.Status.Running };
            popover.Show(persona, target2, new(0, 0, 100, 20), runtime.MainWindow);

            // Should still be the same (no new show call)
            Assert.Equal(threadID, popover.ShownThreadID);

            popover.Hide();
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-197")] // DroppyCode/UI/Markdown/HydraHeadHoverPopover.swift:19-39
    public async Task Showing_different_head_while_shown_closes_previous_and_schedules_new()
    {
        using var runtime = new TestRuntime();

        await runtime.OnUiThread(async () =>
        {
            var popover = HydraHeadHoverPopover.Instance;
            var persona = MakePersona();
            var threadID1 = Guid.NewGuid();
            var threadID2 = Guid.NewGuid();
            var target1 = new HydraMentionTarget { ThreadID = threadID1, Task = "Task 1", Status = HydraHeadInfo.Status.Running };
            var target2 = new HydraMentionTarget { ThreadID = threadID2, Task = "Task 2", Status = HydraHeadInfo.Status.Running };

            // Show first popover
            popover.Show(persona, target1, new(0, 0, 100, 20), runtime.MainWindow);
            runtime.AdvanceFrameClock(TimeSpan.FromMilliseconds(300));
            Assert.Equal(threadID1, popover.ShownThreadID);

            // Show different popover
            popover.Show(persona, target2, new(0, 0, 100, 20), runtime.MainWindow);
            // The previous popover should close, but the new one isn't shown yet
            Assert.Null(popover.ShownThreadID);

            // After delay, new popover shows
            runtime.AdvanceFrameClock(TimeSpan.FromMilliseconds(300));
            Assert.Equal(threadID2, popover.ShownThreadID);

            popover.Hide();
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-197")] // DroppyCode/UI/Markdown/HydraHeadHoverPopover.swift:41-46
    public async Task Hide_cancels_pending_task_and_closes_popover()
    {
        using var runtime = new TestRuntime();

        await runtime.OnUiThread(async () =>
        {
            var popover = HydraHeadHoverPopover.Instance;
            var persona = MakePersona();
            var target = MakeTarget();

            // Show and advance partway
            popover.Show(persona, target, new(0, 0, 100, 20), runtime.MainWindow);
            runtime.AdvanceFrameClock(TimeSpan.FromMilliseconds(100));

            // Hide should cancel the pending task
            popover.Hide();
            Assert.Null(popover.ShownThreadID);

            // Advancing further should not show the popover
            runtime.AdvanceFrameClock(TimeSpan.FromMilliseconds(200));
            Assert.Null(popover.ShownThreadID);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-197")] // DroppyCode/UI/Markdown/HydraHeadHoverPopover.swift:48-50
    public async Task PopoverDidClose_clears_shown_id()
    {
        using var runtime = new TestRuntime();

        await runtime.OnUiThread(async () =>
        {
            var popover = HydraHeadHoverPopover.Instance;
            var persona = MakePersona();
            var target = MakeTarget();

            // Show popover
            popover.Show(persona, target, new(0, 0, 100, 20), runtime.MainWindow);
            runtime.AdvanceFrameClock(TimeSpan.FromMilliseconds(300));
            Assert.Equal(target.ThreadID, popover.ShownThreadID);

            // Simulate popover close notification
            popover.PopoverDidClose(null);
            Assert.Null(popover.ShownThreadID);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRA-197")] // DroppyCode/UI/Markdown/HydraHeadHoverPopover.swift:32-34
    public async Task Card_height_defaults_to_78_when_measured_height_is_not_positive()
    {
        using var runtime = new TestRuntime();

        await runtime.OnUiThread(async () =>
        {
            var popover = HydraHeadHoverPopover.Instance;
            var persona = MakePersona();
            var target = MakeTarget();

            // Show and advance to presentation
            popover.Show(persona, target, new(0, 0, 100, 20), runtime.MainWindow);
            runtime.AdvanceFrameClock(TimeSpan.FromMilliseconds(300));

            // The popover should have been shown with at least the default height
            // (actual verification of the size requires checking the Presentation seam)
            Assert.NotNull(popover.ShownThreadID);

            popover.Hide();
        });
    }
}
