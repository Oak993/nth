using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792), :56-62 MessageMenuRequests, :165-178 the user row's right-click popover, :1383 the reply's menu.
// PR-CHAT-161: a row's storage and its entry are released when it scrolls away, whatever menu it opened still lives. The menus are opened by the Windows
// context-menu key (PR-CHAT-087 windows_path: Key.Apps on the content), which puts no pointer in the row: Avalonia keeps the element a pointer pressed on reachable
// after it leaves the tree (measured 2026-10-04: a bare Border does the same), which would make any release test of a clicked row a test of the framework. Real
// overlay popups are used, so the row's own close-on-disappear runs; the test lets go of its own references to the popups (the presenter's list) before it collects.
[Collection("Headless")]
public sealed class TimelineRowsBlitzAbReleaseTests(HeadlessSession headless)
{
    // The row and its entry are made and dropped inside this method, so no local of the test pins them. Returns weak references to the row and its entry, and the
    // menu's own content, which the test keeps alive: the menu outlives the row. (The message text is not asserted: the menu's snapshot holds it by design, and the
    // markdown caches key on it, in the Swift as here.)
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Row, WeakReference Entry, Control Menu) MountOpenMenuAndScrollAway(ThreadRuntimePart12Harness h, AvaloniaWindow window, Panel panel,
        TimelineRowsPrPopupPresenter presenter, bool sent)
    {
        var text = "a row's own words " + Guid.NewGuid();
        var entry = sent ? UserEntry(text, Guid.NewGuid()) : Reply(text);
        Control row = sent ? new UserMessageRow(entry, h.Runtime, canRevert: true) : new AssistantMessageRow(entry, null, h.Runtime);
        panel.Children.Insert(0, row);
        Settle(window);
        Hover(window, row);
        Settle(window);
        // The context-menu key on every element of the row: the one that wraps its content opens the menu (no pointer in it).
        foreach (var element in row.GetSelfAndVisualDescendants().OfType<Control>().ToList())
            element.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Apps, Source = element });
        Settle(window);
        Assert.Contains(presenter.Calls, call => call.Presented && call.Popup.IsOpen);
        // The menu's own content, kept: it is what an AppKit menu is, alive after the row is gone until it is dismissed.
        var menu = presenter.Calls.Last(call => call.Presented).Popup.Child!;
        var weak = (new WeakReference(row), new WeakReference(entry), menu);
        panel.Children.Remove(row); // the row scrolls out of the virtualized list with its menu open
        window.MouseMove(new Point(2, 690), RawInputModifiers.None);
        Settle(window);
        return weak;
    }


    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Req", "PR-CHAT-161")] // TimelineRows.swift:56-62, :165-178, :1383; PR-CHAT-087 windows_path: a row that scrolls away with its menu open is released, the menu alive
    public void A_row_that_scrolls_away_with_its_menu_open_is_released_by_the_collector_while_the_menu_lives_on(bool sent) => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var presenter = new TimelineRowsPrPopupPresenter(); // no manual frame clock here: a pending animation frame of the hover fade would pin the row until the test advanced it
        using var seam = Presentation.Presenter.Override(presenter);
        var panel = new StackPanel { Spacing = 20 };
        var window = Show(panel);
        (WeakReference Row, WeakReference Entry, Control Menu) kept = (null!, null!, null!);
        h.Main.OnMain(() =>
        {
            _ = AppCurrentEvent.CurrentWindow;
            kept = MountOpenMenuAndScrollAway(h, window, panel, presenter, sent);
            window.KeyPress(Key.F13, RawInputModifiers.None, PhysicalKey.F13, null); // the app keeps the last input event (AppCurrentEvent, NSApp.currentEvent): a newer one lets go of the row's element
        });
        // The popups close themselves when their anchor is gone; the presenter's list is the test's own record of them, and lets go too.
        for (var pass = 0; pass < 4; pass++)
        {
            h.Main.OnMain(() => Settle(window));
            if (pass == 1) h.Main.OnMain(() => { Assert.DoesNotContain(presenter.Calls, call => call.Popup.IsOpen); presenter.Calls.Clear(); });
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        h.Main.OnMain(() =>
        {
            var which = sent ? "the sent message" : "the reply";
            // The menu is alive and the row is gone: nothing the menu's callbacks hold reaches the row, its entry or its state.
            Assert.False(kept.Row.IsAlive, $"{which}'s row is pinned by its menu");
            Assert.False(kept.Entry.IsAlive, $"{which}'s entry is pinned by its menu");
            GC.KeepAlive(kept.Menu);
            Close(window);
        });
    });
}
