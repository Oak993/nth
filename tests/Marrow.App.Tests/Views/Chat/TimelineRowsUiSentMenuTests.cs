using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
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
using Marrow.Services.Providers;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792), :64-234 UserMessageRow and its
// messageActions(text:canRevert:confirmRevert:). Job TimelineRows-ui (successor pass): these five records
// (UI-CHAT-011, UI-CHAT-012, UI-CHAT-013, UI-CHAT-014, UI-GIT-051) had Req-tagged tests in TimelineRowsTests.cs
// already, but none of them independently caught the exact title/symbol/divider clauses without also breaking a
// different record's test, and the "Edit from here" -> confirmation flow was only proven by driving each half
// separately (a reflection call into MessageActions, and a manually-set ConfirmRevert flag), never through one
// real right-click and one real button click. This file closes both gaps with genuine pointer input (MouseDown/
// MouseUp through real hit testing, per the MUTATION STANDARD), in a file of its own so no existing assertion,
// trait or test is touched.
[Collection("Headless")]
public sealed class TimelineRowsUiSentMenuTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "UI-CHAT-007")] // TimelineRows.swift:165-178 rightClickPopover, MessagePopoverMenu.swift:11-19, :56-71: a right-click or a control-click on the sent bubble opens the message menu at the pointer
    public void A_right_click_and_a_control_click_on_the_sent_bubble_open_the_message_menu_anchored_at_the_pointer() => headless.OnUiThread(() =>
    {
        using var h = RuntimeHarness();
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        h.Main.OnMain(() =>
        {
            // Two rows, one click each: a row whose menu is already up is not presented again by a second click.
            var rightRow = new UserMessageRow(UserEntry("hello there, a sent message"), h.Runtime, canRevert: true);
            var controlRow = new UserMessageRow(UserEntry("and another sent message"), h.Runtime, canRevert: true);
            var window = Show(new StackPanel { Margin = new Thickness(20), Spacing = 60, Children = { rightRow, controlRow } });
            try
            {
                Point Origin(Control row) => Assert.Single(LogicalQuery.SelfAndDescendants<InlineText>(row)).TranslatePoint(default, window)!.Value;
                foreach (var (modifiers, button, at) in new[]
                {
                    (RawInputModifiers.None, MouseButton.Right, new Point(Origin(rightRow).X + 9, Origin(rightRow).Y + 5)),
                    (RawInputModifiers.Control, MouseButton.Left, new Point(Origin(controlRow).X + 31, Origin(controlRow).Y + 7)),
                })
                {
                    var before = presenter.Calls.Count(call => call.Presented);
                    window.MouseDown(at, button, modifiers);
                    window.MouseUp(at, button, modifiers);
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    var shown = presenter.Calls.Where(call => call.Presented).ToList();
                    Assert.True(shown.Count > before, $"{button} with {modifiers} opened no menu");
                    var popup = shown[^1].Popup;
                    // :15 a 1 x 1 rect at the pointer, in the row's own space; the menu holds the message actions.
                    Assert.Equal(new Size(1, 1), popup.PlacementRect!.Value.Size);
                    var target = popup.PlacementTarget ?? popup.Parent as Visual;
                    Assert.Equal(at, target!.TranslatePoint(popup.PlacementRect.Value.Position, window));
                    Assert.Equal(["Copy message", "Copy as markdown", "Edit from here"], ItemTitles(Assert.Single(LogicalQuery.SelfAndDescendants<RowActionItems>(popup.Child!))));
                }
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-011")] // :219, :31-34
    [Trait("Req", "UI-CHAT-012")] // :220
    [Trait("Req", "UI-CHAT-013")] // :222-226
    public void Real_right_click_menu_has_exact_titles_symbols_and_a_divider_only_before_edit_from_here_when_revertable() => headless.OnUiThread(() =>
    {
        using var h = RuntimeHarness();
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        h.Main.OnMain(() =>
        {
            var plain = new UserMessageRow(UserEntry("hello"), h.Runtime, canRevert: false);
            var revertable = new UserMessageRow(UserEntry("hello again"), h.Runtime, canRevert: true);
            var panel = new StackPanel { Spacing = 80 };
            panel.Children.Add(plain);
            panel.Children.Add(revertable);
            var window = Show(panel);

            var (_, first) = RightClick(presenter, window, Assert.Single(LogicalQuery.SelfAndDescendants<InlineText>(plain)));
            Assert.Equal(["Copy message", "Copy as markdown"], ItemTitles(first)); // :219-220, absent :222-226 (canRevert false)
            Assert.Empty(LogicalQuery.SelfAndDescendants<PopoverDivider>(first));
            Assert.Equal(["doc.on.doc", "number"], Symbols(first)); // :219-220

            var (_, second) = RightClick(presenter, window, Assert.Single(LogicalQuery.SelfAndDescendants<InlineText>(revertable)));
            Assert.Equal(["Copy message", "Copy as markdown", "Edit from here"], ItemTitles(second)); // :219-220,222-226
            var stack = Assert.IsType<StackPanel>(second.Child);
            Assert.IsType<PopoverDivider>(stack.Children[2]); // :222 startsGroup true precedes the row itself
            Assert.Equal(["doc.on.doc", "number", "arrow.uturn.backward"], Symbols(second)); // :219,220,222
            var editItem = LogicalQuery.SelfAndDescendants<PopoverItem>(second).Single(item => Title(item) == "Edit from here");
            Assert.Equal(ChromeTokens.PrimaryText.Resolve(editItem.ActualThemeVariant), TitleColor(editItem)); // :222-226 not destructive (Popovers.cs:377)
            Close(window);
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-011")] // :219, :31-34
    [Trait("Req", "UI-CHAT-012")] // :220
    public void Real_click_on_each_copy_item_confirms_Copied_in_place_and_writes_the_expected_clipboard_text() => headless.OnUiThread(() =>
    {
        using var h = RuntimeHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        AvaloniaWindow? window = null;
        AvaloniaWindow? menu = null;
        PopoverItem[] items = [];
        h.Main.OnMain(() =>
        {
            _ = AppCurrentEvent.CurrentWindow; // starts the app-wide event watch before the click
            var row = new UserMessageRow(UserEntry("- one\n- two"), h.Runtime, canRevert: false);
            window = Show(row);
            var (popup, actions) = RightClick(presenter, window, Assert.Single(LogicalQuery.SelfAndDescendants<InlineText>(row)));
            // The recorded popup never opens headless; host its content directly so its buttons take real clicks.
            var content = popup.Child!;
            popup.Child = null;
            menu = Show(content);
            items = LogicalQuery.SelfAndDescendants<PopoverItem>(actions).ToArray();
            RealClick(menu, Assert.Single(LogicalQuery.SelfAndDescendants<Button>(items[0])));
        });
        h.Main.OnMain(() =>
        {
            Settle(menu!);
            Assert.True(items[0].IsConfirmed);
            Assert.Equal("Copied", Title(items[0])); // :219 confirms: "Copied"
            // MessageText.plain: each item on its own line behind a bullet and two spaces (:31-33, :219).
            Assert.Equal("•  one\n•  two", window!.Clipboard!.TryGetTextAsync().GetAwaiter().GetResult());
            RealClick(menu!, Assert.Single(LogicalQuery.SelfAndDescendants<Button>(items[1])));
        });
        h.Main.OnMain(() =>
        {
            Settle(menu!);
            Assert.True(items[1].IsConfirmed);
            Assert.Equal("Copied", Title(items[1])); // :220 confirms: "Copied"
            // copyMessageText writes the raw text, reflowed (:31-34, :220); these two short lines are unchanged.
            Assert.Equal("- one\n- two", window!.Clipboard!.TryGetTextAsync().GetAwaiter().GetResult());
            Close(menu!);
            Close(window!);
        });
    });

    [Theory]
    [InlineData("Revert and keep file changes", false)]
    [InlineData("Revert files too", true)]
    [Trait("Req", "UI-CHAT-013")] // :222-226 click raises the row's own confirmation
    [Trait("Req", "UI-CHAT-014")] // :191-196, ED-216
    [Trait("Req", "UI-GIT-051")] // :191-196, ED-216
    public void Real_click_on_edit_from_here_posts_the_intent_then_the_row_raises_the_exact_destructive_dialog_and_routes_each_choice(string title, bool restoreFiles) => headless.OnUiThread(() =>
    {
        using var h = RuntimeHarness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var popovers = new RecordingPresenter();
        using var popoverSeam = Presentation.Presenter.Override(popovers);
        var dialogs = new RecordingConfirmationPresenter();
        using var dialogSeam = AlertPresentation.Presenter.Override(dialogs);
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        AvaloniaWindow? window = null;
        AvaloniaWindow? menu = null;
        UserMessageRow row = null!;
        h.Main.OnMain(() =>
        {
            h.Runtime.RehearseTurn("prompt");
            h.Runtime.Rehearse(new ProviderEvent.TurnCompleted(TurnStatus.Failed, null));
        });
        // FinishTurn awaits its checkpoint boundary before becoming idle (ThreadRuntime.swift:1254-1281).
        h.Main.Loop.RunUntil(() => { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); return h.Runtime.Phase == RuntimePhase.Idle; }, AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            var entry = h.Runtime.Entries.First(item => item.Item.TimelineItemContent is TimelineItem.Content.User);
            row = new UserMessageRow(entry, h.Runtime, canRevert: true);
            window = Show(row);
            var (popup, actions) = RightClick(popovers, window, Assert.Single(LogicalQuery.SelfAndDescendants<InlineText>(row)));
            var content = popup.Child!;
            popup.Child = null;
            menu = Show(content);
            var edit = LogicalQuery.SelfAndDescendants<PopoverItem>(actions).Single(item => Title(item) == "Edit from here");
            RealClick(menu, Assert.Single(LogicalQuery.SelfAndDescendants<Button>(edit))); // :133-ish equivalent: real hit test into the menu row
        });
        // The row's own onChange(of: menuRequests.confirmRevert) (:184-190) picks the intent up asynchronously.
        h.Main.Loop.RunUntil(() =>
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var requests = Assert.IsType<MessageMenuRequests>(typeof(UserMessageRow).GetField("menuRequests", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(row));
            return dialogs.Shown.Count == 1 || requests.ConfirmRevert; // ConfirmRevert resets to false once IsConfirmingRevert is set
        }, AppModelHarness.Bound);
        h.Main.Loop.RunUntil(() => { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); return dialogs.Shown.Count == 1; }, AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            var dialog = Assert.Single(dialogs.Shown);
            Assert.Equal(AlertKind.ConfirmationDialog, dialog.Kind); // :191 confirmationDialog
            Assert.Equal("Edit from this message?", dialog.Title); // :191
            Assert.Equal("This message and everything after it leave the thread, and your prompt returns to the composer.", dialog.Message); // :195
            Assert.Same(window, dialog.Owner); // anchored on the row's own window
            Assert.Equal(["Revert and keep file changes", "Revert files too", "Cancel"], dialog.Buttons.Select(button => button.Title)); // :192-193
            Assert.Equal([AlertButtonRole.None, AlertButtonRole.Destructive, AlertButtonRole.Cancel], dialog.Buttons.Select(button => button.Role)); // :193 role: .destructive
            Assert.Single(h.Runtime.Turns);
            dialogs.Press(title);
        });
        h.Main.OnMain(() =>
        {
            // No checkpoint: restoring must refuse, while keeping files trims this failed turn without a CLI (:202-206).
            Assert.Equal(restoreFiles ? 1 : 0, h.Runtime.Turns.Count);
            Close(menu!);
            Close(window!);
        });
    });

    // ---------------------------------------------------------------- shared helpers (this file only)

    private static ThreadRuntimePart12Harness RuntimeHarness()
    {
        System.Threading.Tasks.Task.Run(() => LoginEnvironment.Load()).GetAwaiter().GetResult();
        return new ThreadRuntimePart12Harness();
    }

    private static TimelineEntry UserEntry(string text) =>
        new(new TimelineItem(null, new TimelineItem.Content.User(new UserMessage { Text = text })));

    private static AvaloniaWindow Show(Control content)
    {
        var window = new AvaloniaWindow { Content = content, Width = 800, Height = 800 };
        window.Show();
        Settle(window);
        return window;
    }

    private static void Settle(AvaloniaWindow window)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
    }

    private static void Close(AvaloniaWindow window)
    {
        Settle(window);
        window.Content = null;
        window.Close();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    // Real secondary-button hit test into the given control, exactly as a right click reaches
    // RightClickCatcher.AttachTo's tunnel-phase PointerPressedEvent handler (MessagePopoverMenu.cs:90-112).
    private static (Popup Popup, RowActionItems Items) RightClick(RecordingPresenter presenter, AvaloniaWindow window, Control on)
    {
        var point = on.TranslatePoint(new Point(4, 4), window) ?? throw new Xunit.Sdk.XunitException("not in the window");
        window.MouseDown(point, MouseButton.Right, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Right, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var popup = presenter.Calls.Last(call => call.Presented).Popup;
        Assert.NotNull(popup.Child);
        return (popup, Assert.Single(LogicalQuery.SelfAndDescendants<RowActionItems>(popup.Child)));
    }

    // Real left-button hit test into a button already on screen, MouseMove then MouseDown/MouseUp (not a
    // synthetic RaiseEvent), per the MUTATION STANDARD's "real pointer input through real hit testing".
    private static void RealClick(AvaloniaWindow window, Button button)
    {
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window) ?? throw new Xunit.Sdk.XunitException("not in the window");
        window.MouseMove(point, RawInputModifiers.None);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    // A PopoverItem's title is a MiddleTruncatingText, whose Text is set only when measured; FullText is the title.
    private static string Title(PopoverItem item) => LogicalQuery.SelfAndDescendants<MiddleTruncatingText>(item).First().FullText;

    // Popovers.cs:375-377: the item's isDestructive foreground (Chrome.Danger vs Chrome.PrimaryText) is set on the
    // item's own row via the inherited TextElement.Foreground attached property, not on the title block directly.
    private static Color TitleColor(PopoverItem item) =>
        Assert.IsAssignableFrom<ISolidColorBrush>(TextElement.GetForeground(LogicalQuery.SelfAndDescendants<MiddleTruncatingText>(item).First())).Color;

    private static string[] ItemTitles(RowActionItems items) =>
        LogicalQuery.SelfAndDescendants<PopoverItem>(items).Select(Title).ToArray();

    private static string[] Symbols(RowActionItems items) =>
        LogicalQuery.SelfAndDescendants<PopoverItem>(items).Select(item => LogicalQuery.SelfAndDescendants<SwiftImage>(item).First().SymbolName ?? "").ToArray();
}
