using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Markdown;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792), :130-161, :1401-1410,
// :2156-2162, :2800-2816. These real-input checks retain partial proof for the external STOPs below.
[Collection("Headless")]
public sealed class TimelineRowsUiPointerTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Rule", "R-TEST-4")] // UI-CHAT-010/UI-GIT-049: :130-156; STOP opencode-App.Views.Chat.TimelineRows #2
    public void Real_pointer_prefetches_and_toggles_the_pencil_editor_on_its_actual_anchor() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        var id = Guid.NewGuid();
        UserMessageRow row = null!; Avalonia.Controls.Window window = null!; Button pencil = null!;
        h.Main.OnMain(() =>
        {
            row = new UserMessageRow(User("edit me", id), h.Runtime, true);
            window = Show(row);
            pencil = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(row));
            Assert.Equal("Edit message", ToolTip.GetTip(pencil)); // :157
            Assert.Null(h.Runtime.CachedRevertPreview(id));
            Move(window, Assert.Single(LogicalQuery.SelfAndDescendants<InlineText>(row)));
        });
        h.Main.OnMain(() => Move(window, pencil)); // :143-147: hover, not a fabricated PointerEntered event
        h.Main.OnMain(() =>
        {
            Assert.NotNull(h.Runtime.CachedRevertPreview(id));
            Click(window, pencil); // actual hit test into the 22-point button
        });
        h.Main.OnMain(() =>
        {
            Settle(window);
            var edit = Assert.IsType<PromptEdit>(h.Runtime.MessageEdit);
            Assert.Equal(id, edit.Id); Assert.Equal("edit me", edit.Text);
            var shown = Assert.Single(presenter.Calls, call => call.Presented);
            Assert.Same(Assert.Single(LogicalQuery.SelfAndDescendants<AttachmentAnchorCapture>(pencil.Parent!)), shown.Popup.PlacementTarget);
            Click(window, pencil); // :133 second click closes
        });
        h.Main.OnMain(() => { Assert.Null(h.Runtime.MessageEdit); Close(window); });
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // UI-CHAT-040/UI-GIT-048: :2800-2816; STOP opencode-App.Views.Chat.TimelineRows #2
    public void Real_pointer_arms_review_then_click_delivers_the_buttons_own_anchor() => headless.OnUiThread(() =>
    {
        using var actor = new TestRuntime(); using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var reviewed = new List<Control>(); TurnFileCard card = null!; Button review = null!; Avalonia.Controls.Window window = null!;
        actor.OnMain(() =>
        {
            card = new TurnFileCard(new FileChangeSummary(new ValueList<FileChangeSummary.File>([new("a.cs", 1, 0, false)]), 1, 0), false, () => { }, reviewed.Add);
            window = Show(card);
            review = LogicalQuery.SelfAndDescendants<Button>(card).Last();
            Assert.Equal("Show this turn's changes", ToolTip.GetTip(review)); // :2815
            Assert.Empty(LogicalQuery.SelfAndDescendants<AttachmentAnchorCapture>(card));
            Move(window, review); // :2807-2811 anchor installed only after hover
        });
        actor.OnMain(() =>
        {
            Settle(window);
            var anchor = Assert.Single(LogicalQuery.SelfAndDescendants<AttachmentAnchorCapture>(card));
            Click(window, review);
            Assert.Same(anchor, Assert.Single(reviewed)); // :2802 changes on this button
            Close(window);
        });
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // UI-CHAT-026: :2156-2162; STOP opencode-App.Views.Chat.TimelineRows #4
    public void Real_pointer_toggles_full_output_and_keeps_the_link_below_it_at_both_states() => headless.OnUiThread(() =>
    {
        using var actor = new TestRuntime(); ToolDetailView detail = null!; Avalonia.Controls.Window window = null!;
        var all = string.Join("\n", Enumerable.Range(1, 31).Select(i => "line " + i));
        actor.OnMain(() =>
        {
            detail = new ToolDetailView(new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = "output", ToolCallStatus = ToolCall.Status.Completed, Output = all });
            window = Show(detail); var link = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(detail));
            Assert.Equal("Show full output (31 lines)", Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(link)).Text); // :2157
            BelowOutput(link);
            Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<SelectableTextBlock>(detail), t => t.Text == all);
            Click(window, link);
        });
        actor.OnMain(() =>
        {
            Settle(window); var link = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(detail));
            Assert.Equal("Show less", Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(link)).Text);
            Assert.Equal(all, Assert.Single(LogicalQuery.SelfAndDescendants<SelectableTextBlock>(detail)).Text);
            BelowOutput(link);
            Click(window, link);
        });
        actor.OnMain(() =>
        {
            Assert.Equal("Show full output (31 lines)", Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(Assert.Single(LogicalQuery.SelfAndDescendants<Button>(detail)))).Text);
            Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<SelectableTextBlock>(detail), t => t.Text == all);
            Close(window);
        });
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // UI-CHAT-020/UI-CHAT-021: :1401-1410; STOP opencode-App.Views.Chat.TimelineRows #1
    public void Real_pointer_copies_the_last_code_confirms_Copied_and_adds_one_quote_confirming_Added() => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var h = Harness(); Avalonia.Controls.Window window = null!; PopoverItem copy = null!, add = null!;
        const string text = "```\nfirst\n```\n```\nlast\n```\n```\nunclosed";
        h.Main.OnMain(() =>
        {
            _ = AppCurrentEvent.CurrentWindow;
            var menu = new RowActionItems(AssistantMessageRow.MessageActions(text, TimelineRows.LastCodeBlock(text), h.Runtime));
            window = Show(menu);
            copy = LogicalQuery.SelfAndDescendants<PopoverItem>(menu).Single(item => Title(item) == "Copy last code block");
            add = LogicalQuery.SelfAndDescendants<PopoverItem>(menu).Single(item => Title(item) == "Add to reply");
            var siblings = Assert.IsType<StackPanel>(menu.Child);
            Assert.IsType<PopoverDivider>(siblings.Children[siblings.Children.IndexOf(add) - 1]); // :1408 startsGroup true
            Click(window, Assert.Single(LogicalQuery.SelfAndDescendants<Button>(copy)));
        });
        h.Main.OnMain(() =>
        {
            Settle(window); Assert.Equal("Copied", Title(copy)); // :1403
            Assert.Equal("last", window.Clipboard!.TryGetTextAsync().GetAwaiter().GetResult());
            Click(window, Assert.Single(LogicalQuery.SelfAndDescendants<Button>(add)));
        });
        h.Main.OnMain(() =>
        {
            Settle(window); Assert.Equal("Added", Title(add)); // :1410
            Assert.Equal(text, Assert.Single(h.Runtime.Draft.Quotes).Text);
            Click(window, Assert.Single(LogicalQuery.SelfAndDescendants<Button>(add)));
        });
        h.Main.OnMain(() => { Assert.Equal(text, Assert.Single(h.Runtime.Draft.Quotes).Text); Close(window); });
    });

    private static ThreadRuntimePart12Harness Harness() { Task.Run(() => LoginEnvironment.Load()).GetAwaiter().GetResult(); return new(); }
    private static TimelineEntry User(string text, Guid id) => new(new TimelineItem(id, new TimelineItem.Content.User(new UserMessage { Text = text })));
    private static string Title(PopoverItem item) => Assert.Single(LogicalQuery.SelfAndDescendants<MiddleTruncatingText>(item)).FullText;
    private static Avalonia.Controls.Window Show(Control control)
    {
        var panel = new StackPanel { Margin = new Thickness(40) }; panel.Children.Add(control);
        var window = new Avalonia.Controls.Window { Content = panel, Width = 800, Height = 800 };
        window.Show(); Settle(window); return window;
    }
    private static Point Centre(Avalonia.Controls.Window window, Control control) => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
    private static void BelowOutput(Button link)
    {
        // :2149-2162 VStack: output plate first, then the caption link.
        var stack = Assert.IsType<StackPanel>(link.Parent);
        var output = Assert.IsType<Border>(stack.Children[0]);
        Assert.Same(link, stack.Children[1]);
        Assert.True(link.Bounds.Top >= output.Bounds.Bottom);
        Assert.Equal(10, Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(link)).FontSize); // :2161, ED-059
    }
    private static void Move(Avalonia.Controls.Window window, Control control) { window.MouseMove(Centre(window, control), RawInputModifiers.None); Settle(window); }
    private static void Click(Avalonia.Controls.Window window, Button button)
    {
        var point = Centre(window, button); window.MouseMove(point, RawInputModifiers.None);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None); window.MouseUp(point, MouseButton.Left, RawInputModifiers.None); Settle(window);
    }
    private static void Settle(Avalonia.Controls.Window window) { window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    private static void Close(Avalonia.Controls.Window window) { window.Content = null; window.Close(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
}
