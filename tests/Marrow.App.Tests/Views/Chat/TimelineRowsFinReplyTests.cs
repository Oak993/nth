using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinRig;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792), :31-54 the pasteboard half and lastCodeBlock,
// :1353-1429 AssistantMessageRow and its menu; :56-62, :165-178, :1228-1231, :1406-1407 the menus' weak captures.
[Collection("Headless")]
public sealed class TimelineRowsFinReplyTests(HeadlessSession headless)
{
    private const string Fenced = "```swift\nlet x = 1\n```";

    private static TimelineEntry Streaming(string text) =>
        new(new TimelineItem(null, new TimelineItem.Content.Assistant(new AssistantMessage { Text = text, IsStreaming = true })));

    private static StackPanel HoverLine(Control row) => Mounted<StackPanel>(row).Single(stack => stack.Height == 22 && double.IsNaN(stack.Width));

    [Fact]
    [Trait("Req", "UI-CHAT-019")] // TimelineRows.swift:1361-1390
    public void A_reply_keeps_a_22_point_hover_line_two_points_below_its_text_and_fades_the_duration_in_over_0_12_seconds() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var locale = SwiftFormatters.CurrentLocale.Override("en_US");
        AssistantMessageRow settled = null!, streaming = null!, middle = null!;
        AvaloniaWindow window = null!;
        h.Main.OnMain(() =>
        {
            var summary = Summary(Guid.NewGuid()) with { Duration = 64 };
            settled = new AssistantMessageRow(Reply(Fenced), summary, h.Runtime);
            streaming = new AssistantMessageRow(Streaming("```\nstill typing"), summary, h.Runtime);
            middle = new AssistantMessageRow(Reply("```\nmiddle\n```"), null, h.Runtime); // only the turn's last reply carries a summary
            var panel = new StackPanel { Spacing = 60 };
            foreach (var row in new Control[] { settled, streaming, middle }) panel.Children.Add(row);
            window = Show(panel);
            var markdown = Assert.Single(Mounted<MarkdownView>(settled));
            var line = HoverLine(settled);
            // :1363 VStack(spacing: 2); :1377 .frame(height: hoverLineHeight); :1388 the room is taken back from the row's height.
            Assert.Equal(22, line.Height);
            Assert.Equal(2, line.TranslatePoint(default, window)!.Value.Y - (markdown.TranslatePoint(default, window)!.Value.Y + markdown.Bounds.Height), 0.5);
            Assert.Equal(markdown.Bounds.Height, settled.Bounds.Height, 0.5);
            // :1368-1372 the summary label: Font.chat(.caption), Chrome.secondaryText, on the leading edge of the line.
            var label = Assert.Single(Mounted<TextBlock>(line));
            Assert.Equal("Worked for 1m 4s", label.Text);
            Face(label, 10, FontWeight.Normal, ".chat(.caption)");
            Paints(label.Foreground, ChromeTokens.PrimaryText.Opacity(0.7), label, "Chrome.secondaryText");
            Assert.Equal(Left(settled, window), Left(label, window), 0.5);
            // :1367 HStack(spacing: 8) centres the text in the 22-point line (an HStack's default alignment).
            OnOneCentreLine(window, line, label);
            // :1378 hidden until hovered.
            Assert.True(line.Opacity == 0, "hover line hidden before hover");
            // Only the last reply has a summary: the others draw a hover line with nothing in it.
            Assert.Empty(Mounted<TextBlock>(HoverLine(middle)));
            Hover(window, markdown);
        });
        // :1381 withAnimation(.easeOut(duration: 0.12)): part way at 60 ms, fully in by 120 ms.
        frames.Advance(TimeSpan.Zero);
        h.Main.AdvanceMilliseconds(60);
        frames.Advance(TimeSpan.FromMilliseconds(60));
        Assert.InRange(HoverLine(settled).Opacity, 0.01, 0.99);
        h.Main.AdvanceMilliseconds(60);
        frames.Advance(TimeSpan.FromMilliseconds(60));
        Assert.Equal(1, HoverLine(settled).Opacity);
        // :1378 `isHovering && !message.isStreaming`: a streaming reply never shows the line.
        h.Main.OnMain(() => Hover(window, Assert.Single(Mounted<MarkdownView>(streaming))));
        h.Main.AdvanceMilliseconds(300);
        frames.Advance(TimeSpan.FromMilliseconds(300));
        Assert.Equal(0, HoverLine(streaming).Opacity);
        h.Main.OnMain(() => Close(window));
    });

    [Fact]
    [Trait("Req", "UI-CHAT-020")] // TimelineRows.swift:1401-1405 Copy last code block, :38-54 lastCodeBlock
    public void The_last_complete_fenced_block_is_a_menu_item_that_copies_its_code_and_reads_Copied_in_place() => headless.OnUiThread(() =>
    {
        // :38-54 a line-prefix test on three backticks: indented fences, tildes and an open fence yield nothing; an info string is fine.
        Assert.Null(TimelineRows.LastCodeBlock("no code"));
        Assert.Null(TimelineRows.LastCodeBlock("```\nopen"));
        Assert.Null(TimelineRows.LastCodeBlock("~~~\ncode\n~~~"));
        Assert.Null(TimelineRows.LastCodeBlock("``\ncode\n``"));
        Assert.Null(TimelineRows.LastCodeBlock("  ```\ncode\n  ```"));
        Assert.Equal("let x = 1", TimelineRows.LastCodeBlock(Fenced));
        Assert.Equal("second", TimelineRows.LastCodeBlock("```\nfirst\n```\ntext\n```js\nsecond\n```\n```\nopen"));
        Assert.Equal("a\nb", TimelineRows.LastCodeBlock("```\na\nb\n```"));
        Assert.Equal("", TimelineRows.LastCodeBlock("```\n```")); // an empty block is still a whole fenced block

        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        AvaloniaWindow window = null!, menu = null!;
        PopoverItem[] items = [];
        Task<string?> read = null!;
        h.Main.OnMain(() =>
        {
            _ = AppCurrentEvent.CurrentWindow; // starts the app-wide event watch before the click
            var withCode = new AssistantMessageRow(Reply("answer\n\n" + Fenced), null, h.Runtime);
            var without = new AssistantMessageRow(Reply("answer\n\n```\nunfinished"), null, h.Runtime);
            var panel = new StackPanel { Spacing = 80 };
            panel.Children.Add(withCode);
            panel.Children.Add(without);
            window = Show(panel);
            var (_, plain) = RightClick(presenter, window, Assert.Single(Mounted<MarkdownView>(without)));
            Assert.Equal(["Copy message", "Copy as markdown", "Add to reply"], ItemTitles(plain));
            var (popup, actions) = RightClick(presenter, window, Assert.Single(Mounted<MarkdownView>(withCode)));
            // :1397-1405 the two copies, then the last code block, with its symbol; :1406 Add to reply follows in its own group.
            Assert.Equal(["Copy message", "Copy as markdown", "Copy last code block", "Add to reply"], ItemTitles(actions));
            Assert.Equal("chevron.left.forwardslash.chevron.right", ItemSymbols(actions)[2]);
            var column = Assert.IsType<StackPanel>(actions.Child);
            Assert.IsType<PopoverDivider>(column.Children[3]);
            menu = HostMenu(popup);
            items = LogicalQuery.SelfAndDescendants<PopoverItem>(actions).ToArray();
            ClickButton(menu, Assert.Single(LogicalQuery.SelfAndDescendants<Button>(items[2])));
        });
        h.Main.OnMain(() =>
        {
            Settle(menu);
            // "after activation the label is replaced in place by Copied".
            Assert.True(items[2].IsConfirmed);
            Assert.Equal("Copied", Title(items[2]));
            Assert.Equal(["Copy message", "Copy as markdown", "Copied", "Add to reply"], items.Select(Title));
            // :1401-1410 PopoverItem's hover surface, `.background(..., in: RoundedRectangle(cornerRadius: 6, style: .continuous))` (Chrome.swift:322-326;
            // ED-181 closed): a 26 pt row holds the 18 pt two run-ins of a 6 pt corner, so each is cut to Apple's corner.
            foreach (var item in items) ContinuousCornerChecks.MenuItemSurface(Mounted<Border>(item).First(border => SwiftShapes.GetCornerStyle(border) is not null));
            read = window.Clipboard!.TryGetTextAsync();
        });
        h.Main.OnMain(() =>
        {
            Assert.Equal("let x = 1", read.GetAwaiter().GetResult()); // the code alone, no fence lines
            Close(menu);
            Close(window);
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-021")] // TimelineRows.swift:1406-1410 Add to reply
    public void Add_to_reply_quotes_the_reply_into_the_draft_once_and_reads_Added_in_place() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        AvaloniaWindow window = null!, menu = null!;
        PopoverItem add = null!;
        h.Main.OnMain(() =>
        {
            var row = new AssistantMessageRow(Reply("the answer words"), null, h.Runtime);
            window = Show(row);
            var (popup, actions) = RightClick(presenter, window, Assert.Single(Mounted<MarkdownView>(row)));
            // :1406 symbol arrow.turn.down.left, starts a group (a divider before it).
            Assert.Equal(["Copy message", "Copy as markdown", "Add to reply"], ItemTitles(actions));
            Assert.Equal("arrow.turn.down.left", ItemSymbols(actions)[2]);
            Assert.IsType<PopoverDivider>(Assert.IsType<StackPanel>(actions.Child).Children[2]);
            menu = HostMenu(popup);
            add = LogicalQuery.SelfAndDescendants<PopoverItem>(actions).Last();
            Assert.Empty(h.Runtime.Draft.Quotes);
            ClickButton(menu, Assert.Single(LogicalQuery.SelfAndDescendants<Button>(add)));
        });
        h.Main.OnMain(() =>
        {
            Settle(menu);
            // :1409 the reply is appended to the thread's draft as a quote; the confirmation is Added, not Copied.
            Assert.Equal(["the answer words"], h.Runtime.Draft.Quotes.Select(quote => quote.Text));
            Assert.True(add.IsConfirmed);
            Assert.Equal("Added", Title(add));
            // :1408 a duplicate quote is silently not added.
            ClickButton(menu, Assert.Single(LogicalQuery.SelfAndDescendants<Button>(add)));
        });
        h.Main.OnMain(() =>
        {
            Assert.Equal(["the answer words"], h.Runtime.Draft.Quotes.Select(quote => quote.Text));
            // :1408 the same words from a fresh menu are silently not added either; other words are.
            AssistantMessageRow.MessageActions("the answer words", null, h.Runtime).Last().Action();
            Assert.Single(h.Runtime.Draft.Quotes);
            AssistantMessageRow.MessageActions("another answer", null, h.Runtime).Last().Action();
            Assert.Equal(["the answer words", "another answer"], h.Runtime.Draft.Quotes.Select(quote => quote.Text));
            // :1401-1405 an empty fenced block still offers Copy last code block.
            Assert.Equal(4, AssistantMessageRow.MessageActions("x", "", h.Runtime).Count);
            Close(menu);
            Close(window);
        });
    });

    // The row, its entry and its text are made and dropped inside this method, so no local of the test pins them.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Row, WeakReference Entry) MountAndScrollAway(ThreadRuntimePart12Harness h, AvaloniaWindow window, Panel panel, int kind)
    {
        Control row;
        TimelineEntry entry;
        var turn = Guid.NewGuid();
        switch (kind)
        {
            case 0:
                entry = Reply("a reply that scrolls away " + Guid.NewGuid());
                row = new AssistantMessageRow(entry, null, h.Runtime);
                break;
            case 1:
                entry = UserEntry("a message that scrolls away " + Guid.NewGuid(), turn);
                row = new UserMessageRow(entry, h.Runtime, canRevert: true);
                break;
            default:
                entry = Tool(ToolCall.Kind.Edit, ToolCall.Status.Completed, new ValueList<FileEdit>([new FileEdit("src/a.swift", "@@ -1 +1 @@\n-a\n+b\n", 1, 1)]));
                row = new ToolRow(entry, h.Runtime);
                break;
        }
        panel.Children.Insert(0, row);
        Settle(window);
        // The pointer on the row mounts its hover responders and its anchor views (the pencil's, the tool label's).
        Hover(window, row);
        Settle(window);
        var weak = (new WeakReference(row), new WeakReference(entry));
        panel.Children.Remove(row); // the row scrolls out of the virtualized list
        window.MouseMove(new Point(2, 690), RawInputModifiers.None);
        Settle(window);
        return weak;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Runtime, IReadOnlyList<RowAction> Menu) MenuOverAShortLivedRuntime(ThreadRuntimePart12Harness h)
    {
        var runtime = new ThreadRuntime(h.Thread.Id, h.App);
        return (new WeakReference(runtime), AssistantMessageRow.MessageActions("the answer", null, runtime));
    }

    [Fact]
    [Trait("Req", "PR-CHAT-161")] // TimelineRows.swift:165-178 hover binding, :1228-1231 anchor handlers, :1678-1687 tool row, :1761-1763 anchor
    public void A_row_that_scrolls_away_is_released_whatever_its_hover_responders_and_anchor_views_were_doing() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var panel = new StackPanel { Spacing = 20 };
        var window = Show(panel);
        var kept = new List<(WeakReference Row, WeakReference Entry)>();
        h.Main.OnMain(() =>
        {
            for (var kind = 0; kind < 3; kind++) kept.Add(MountAndScrollAway(h, window, panel, kind));
        });
        // The runtime and the window outlive the rows; only the rows scroll away.
        for (var pass = 0; pass < 4; pass++)
        {
            h.Main.OnMain(() => Settle(window));
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        h.Main.OnMain(() =>
        {
            Assert.False(kept[0].Row.IsAlive, "the reply row is pinned");
            Assert.False(kept[0].Entry.IsAlive, "the reply's entry is pinned");
            Assert.False(kept[1].Row.IsAlive, "the sent-message row is pinned");
            Assert.False(kept[1].Entry.IsAlive, "the sent message's entry is pinned");
            Assert.False(kept[2].Row.IsAlive, "the tool row is pinned");
            Assert.False(kept[2].Entry.IsAlive, "the tool row's entry is pinned");
            Close(window);
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-161")] // TimelineRows.swift:1406-1407 Add to reply captures the runtime weakly; :56-62 intents travel through a request object
    public void A_menu_action_holds_its_runtime_weakly_and_the_edit_intent_travels_through_a_request_object() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        WeakReference runtime = null!;
        IReadOnlyList<RowAction> menu = null!;
        h.Main.OnMain(() => { (runtime, menu) = MenuOverAShortLivedRuntime(h); });
        // A new runtime's own start-up work may still sit on a shared background queue that another test keeps busy; that
        // work holds the runtime only until it runs. So collect until the runtime is gone, for up to five seconds: a strong
        // reference from the menu would keep it alive throughout.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        for (var pass = 0; pass < 4 || (runtime.IsAlive && DateTime.UtcNow < deadline); pass++)
        {
            h.Main.OnMain(() => Avalonia.Threading.Dispatcher.UIThread.RunJobs());
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (pass >= 4 && runtime.IsAlive) Thread.Sleep(20);
        }
        h.Main.OnMain(() =>
        {
            // The menu outlives the right click, and with it every action's closure; none of them keeps the runtime alive.
            Assert.False(runtime.IsAlive, "the Add to reply action holds its runtime strongly");
            // :1407 guard let runtime else { return }: the action is a no-op once the runtime is gone.
            Assert.Equal("Add to reply", menu[^1].Title);
            menu[^1].Action();
            GC.KeepAlive(menu);
        });
    });
}
