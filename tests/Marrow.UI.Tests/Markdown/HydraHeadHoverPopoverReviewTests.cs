using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Marrow.Core.Models;
using Marrow.Testing;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Markdown;

[Collection("Headless")]
public sealed class HydraHeadHoverPopoverReviewTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-HYDRA-197")] // UI/Markdown/HydraHeadHoverPopover.swift:14,19-46
    public void Review_delay_noop_retarget_and_hide_are_observable() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var recorder = new PopoverRecorder();
        using var seam = Presentation.Presenter.Override(recorder);
        var popover = (HydraHeadHoverPopover)Activator.CreateInstance(typeof(HydraHeadHoverPopover), nonPublic: true)!;
        var persona = new HydraPersona("Test", "test_asset", 0xFF0000FF);
        var first = new HydraMentionTarget { ThreadID = Guid.NewGuid(), Task = "First task", Status = HydraHeadInfo.Status.Completed };
        var second = first with { ThreadID = Guid.NewGuid(), Task = "Second task" };
        var anchor = new Control();
        var rect = new Rect(10, 20, 30, 15);
        runtime.OnMain(() => popover.Show(persona, first, rect, anchor));
        runtime.AdvanceMilliseconds(249);
        Assert.Empty(recorder.Calls);
        runtime.AdvanceMilliseconds(1);
        Assert.Equal(first.ThreadID, popover.ShownThreadID);
        var popup = Assert.Single(recorder.Calls).Popup;
        Assert.False(popup.IsLightDismissEnabled);
        Assert.Equal(300, popup.Child!.Width);
        Assert.True(popup.Child.Height > 0);
        runtime.OnMain(() => popover.Show(persona, first, rect, anchor));
        runtime.AdvanceMilliseconds(250);
        Assert.Single(recorder.Calls);
        runtime.OnMain(() => popover.Show(persona, second, rect, anchor));
        runtime.AdvanceMilliseconds(250);
        Assert.Equal(second.ThreadID, popover.ShownThreadID);
        Assert.Equal([true, false, true], recorder.Calls.Select(c => c.Presented));
        runtime.OnMain(popover.Hide);
        Assert.Null(popover.ShownThreadID);
        Assert.False(recorder.Calls[^1].Presented);
    });

    [Fact]
    [Trait("Req", "UI-HYDRA-035")] // UI/Markdown/HydraHeadHoverPopover.swift:56,62-79
    public void Review_long_task_wraps_within_the_remaining_238_points() => headless.OnUiThread(() =>
    {
        var task = string.Join(" ", Enumerable.Repeat("A long task needs readable wrapping", 5));
        var card = new HydraHeadHoverCard(new HydraPersona("Test", "test_asset", 0xFF0000FF),
            new HydraMentionTarget { ThreadID = Guid.NewGuid(), Task = task, Status = HydraHeadInfo.Status.Completed });
        card.Measure(new Size(300, double.PositiveInfinity));
        card.Arrange(new Rect(card.DesiredSize));
        var text = Assert.Single(card.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == task);
        Assert.InRange(text.Bounds.Width, 1, 238);
        Assert.True(text.Bounds.Height > text.FontSize * 2);
        Assert.Equal(3, text.MaxLines);
    });

    [Fact]
    [Trait("Req", "PR-HYDRA-197")] // UI/Markdown/HydraHeadHoverPopover.swift:19-20,37 shown relative to the mention's rectangle
    public void Popup_is_anchored_to_the_mentions_rectangle() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var recorder = new PopoverRecorder();
        using var seam = Presentation.Presenter.Override(recorder);
        var popover = (HydraHeadHoverPopover)Activator.CreateInstance(typeof(HydraHeadHoverPopover), nonPublic: true)!;
        var persona = new HydraPersona("Test", "test_asset", 0xFF0000FF);
        var target = new HydraMentionTarget { ThreadID = Guid.NewGuid(), Task = "Task", Status = HydraHeadInfo.Status.Completed };
        var anchor = new Control();
        var rect = new Rect(10, 20, 30, 15);
        runtime.OnMain(() => popover.Show(persona, target, rect, anchor));
        runtime.AdvanceMilliseconds(250);
        var popup = Assert.Single(recorder.Calls).Popup;
        Assert.Same(anchor, popup.PlacementTarget);
        Assert.Equal(rect, popup.PlacementRect);
    });

    [Fact]
    [Trait("Req", "UI-HYDRA-035")] // UI/Markdown/HydraHeadHoverPopover.swift:68-71 lineLimit(3) truncates the tail with an ellipsis
    public void Long_task_is_tail_truncated_with_an_ellipsis() => headless.OnUiThread(() =>
    {
        var card = new HydraHeadHoverCard(new HydraPersona("Test", "test_asset", 0xFF0000FF),
            new HydraMentionTarget { ThreadID = Guid.NewGuid(), Task = "A task", Status = HydraHeadInfo.Status.Completed });
        var text = Assert.Single(card.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "A task");
        Assert.Equal(Avalonia.Media.TextTrimming.CharacterEllipsis, text.TextTrimming);
    });
}
