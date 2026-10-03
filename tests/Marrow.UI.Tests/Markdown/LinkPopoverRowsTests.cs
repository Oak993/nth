using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Markdown;

// Source under test: DroppyCode/UI/Markdown/LinkPopover.swift (B3 52bc792), :1-108, for UI-THEME-042 to 045. The
// popover is shown through the Presentation seam (PopoverRecorder), so nothing real opens; link opening goes through
// the ShellWorkspace seam, never a browser. LinkPopover.Shared is a process-wide singleton (:10), so every test that
// calls Show ends with Close. A click on a row closes the popover first and runs its action after
// (Popovers.swift:266-271, the row every PopoverItem here is).
[Collection("Headless")]
public sealed class LinkPopoverRowsTests(HeadlessSession headless)
{
    private const string MergeUrl = "https://github.com/Org/Repo/pull/5";

    private static Window Host(Control content)
    {
        content.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        content.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        var window = new Window { Width = 400, Height = 300, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    // The rows of the content the shared popover is showing, as the popup holds them.
    private static List<Control> ShownRows()
    {
        var frame = Assert.IsType<Border>(PopoverHost.Content(LinkPopover.Shared.PopoverForTests)); // the site's frame, not the card the presenter hangs it on (OD-0095)
        var content = Assert.IsType<LinkPopoverContent>(frame.Child);
        return Assert.IsType<StackPanel>(Assert.IsType<Border>(content.Child).Child).Children.ToList();
    }

    private static void Click(Control row)
    {
        ((Button)((PopoverItem)row).Child!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs(); // the action runs after the popover closes, in a task of its own
    }

    private static string RowName(Control row) => AutomationProperties.GetName(((PopoverItem)row).Child!) ?? "";

    private static void WithOpenSeams(Action<List<string>> body)
    {
        var previousOpen = ShellWorkspace.OpenWithDefaultApp;
        var previousReveal = ShellWorkspace.RevealInFileExplorer;
        var opened = new List<string>();
        ShellWorkspace.OpenWithDefaultApp = opened.Add;
        ShellWorkspace.RevealInFileExplorer = path => opened.Add("reveal " + path);
        try { body(opened); }
        finally
        {
            ShellWorkspace.OpenWithDefaultApp = previousOpen;
            ShellWorkspace.RevealInFileExplorer = previousReveal;
        }
    }

    private static MergeRequestTarget Target(Action<MergeRequestLink> merge) => new(Guid.NewGuid(), merge);

    // ---- UI-THEME-043, UI-THEME-044, UI-THEME-045: which rows, in which order (:76-95) ----

    [Fact]
    [Trait("Req", "UI-THEME-043")] // DroppyCode/UI/Markdown/LinkPopover.swift:76-94: Merge first, a divider, Open Link, Copy Link last
    [Trait("Req", "UI-THEME-044")]
    [Trait("Req", "UI-THEME-045")]
    public void A_merge_request_link_with_a_chat_target_shows_Merge_a_divider_Open_Link_and_Copy_Link_in_that_order() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var anchor = new Border { Width = 10, Height = 10 };
        var window = Host(anchor);
        try
        {
            LinkPopover.Shared.Show(SwiftURL.Create(MergeUrl)!, Target(_ => { }), new Rect(0, 0, 10, 10), anchor);

            var rows = ShownRows();
            Assert.Equal(4, rows.Count);
            Assert.Equal("Merge #5", RowName(rows[0])); // the request's own title
            Assert.IsType<PopoverDivider>(rows[1]);
            Assert.Equal("Open Link", RowName(rows[2]));
            Assert.Equal("Copy Link", RowName(rows[3]));
        }
        finally
        {
            LinkPopover.Shared.Close();
            window.Close();
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-043")] // DroppyCode/UI/Markdown/LinkPopover.swift:29: `MergeRequestLink(url:)` is failable, so a plain link has no Merge row even when the chat can merge
    [Trait("Req", "UI-THEME-044")]
    [Trait("Req", "UI-THEME-045")]
    public void A_plain_link_with_a_chat_target_shows_only_Open_Link_then_Copy_Link() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var anchor = new Border { Width = 10, Height = 10 };
        var window = Host(anchor);
        try
        {
            LinkPopover.Shared.Show(SwiftURL.Create("https://example.com/x")!, Target(_ => { }), new Rect(0, 0, 10, 10), anchor);

            var rows = ShownRows();
            Assert.Equal(["Open Link", "Copy Link"], rows.Select(RowName));
            Assert.All(rows, row => Assert.IsType<PopoverItem>(row));
        }
        finally
        {
            LinkPopover.Shared.Close();
            window.Close();
        }
    });

    // ---- UI-THEME-043: the Merge row's words and its click (:77-87, :29-33) ----

    [Fact]
    [Trait("Req", "UI-THEME-043")] // DroppyCode/UI/Markdown/LinkPopover.swift:80: "Merge" semibold, then one space, then the request label regular
    public void The_merge_row_reads_Merge_in_semibold_then_one_space_then_the_label_in_the_regular_weight() => headless.OnUiThread(() =>
    {
        var url = SwiftURL.Create("https://gitlab.com/g/p/-/merge_requests/7")!;
        var content = new LinkPopoverContent(url, MergeRequestLink.Create(url), _ => { });
        var window = Host(content);
        try
        {
            var stack = Assert.IsType<StackPanel>(Assert.IsType<Border>(content.Child).Child);
            var title = ((PopoverItem)stack.Children[0]).GetVisualDescendants().OfType<TextBlock>().First(block => block.Inlines is { Count: > 0 });
            var runs = title.Inlines!.Cast<Run>().ToArray();

            Assert.Equal("Merge", runs[0].Text);
            Assert.Equal(Avalonia.Media.FontWeight.SemiBold, runs[0].FontWeight);
            Assert.Equal("Merge !7", string.Concat(runs.Select(run => run.Text))); // "!7" is a GitLab label, the one space between
            Assert.All(runs.Skip(1), run => Assert.Equal(Avalonia.Media.FontWeight.Normal, run.FontWeight));
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "UI-THEME-043")] // DroppyCode/UI/Markdown/LinkPopover.swift:30-33: the popover closes, then the chat gets the request
    public void Clicking_Merge_closes_the_popover_before_the_chat_receives_the_request() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var url = SwiftURL.Create(MergeUrl)!;
        var closedWhenMerged = -1;
        MergeRequestLink? merged = null;
        var anchor = new Border { Width = 10, Height = 10 };
        var window = Host(anchor);
        try
        {
            LinkPopover.Shared.Show(url, Target(request =>
            {
                closedWhenMerged = recorder.Calls.Count(call => !call.Presented);
                merged = request;
            }), new Rect(0, 0, 10, 10), anchor);

            Click(ShownRows()[0]);

            Assert.Equal(url, merged?.Url);
            Assert.Equal("#5", merged!.Label);
            Assert.Equal(1, closedWhenMerged); // already dismissed when the callback ran
        }
        finally
        {
            LinkPopover.Shared.Close();
            window.Close();
        }
    });

    // ---- UI-THEME-044: Open Link (:88-90) ----

    [Fact]
    [Trait("Req", "UI-THEME-044")] // DroppyCode/UI/Markdown/LinkPopover.swift:88-90 with Popovers.swift:266-271: close, then open the link's own URL
    public void Clicking_Open_Link_closes_the_popover_then_opens_that_links_url_through_the_seam() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var anchor = new Border { Width = 10, Height = 10 };
        var window = Host(anchor);
        try
        {
            WithOpenSeams(opened =>
            {
                LinkPopover.Shared.Show(SwiftURL.Create("https://example.com/Some/Path?q=1")!, null, new Rect(0, 0, 10, 10), anchor);

                Click(ShownRows()[0]);

                Assert.Equal(["https://example.com/Some/Path?q=1"], opened);
                Assert.Equal(2, recorder.Calls.Count); // shown, then closed by the click
                Assert.False(recorder.Calls[1].Presented);
            });
        }
        finally
        {
            LinkPopover.Shared.Close();
            window.Close();
        }
    });

    // ---- UI-THEME-045: Copy Link (:91-94) ----

    [Fact]
    [Trait("Req", "UI-THEME-045")] // DroppyCode/UI/Markdown/LinkPopover.swift:91-94 with Popovers.swift:271: close, and the clipboard holds the absolute URL as plain text
    public void Clicking_Copy_Link_closes_the_popover_and_replaces_the_clipboard_with_the_absolute_url_not_the_pretty_title() => headless.OnUiThread(() =>
    {
        var url = SwiftURL.Create("https://GitHub.com/Org/Repo/pull/5?Tab=files")!; // a merge request: the row above shows "Merge #5", the clipboard must not
        var content = new LinkPopoverContent(url, MergeRequestLink.Create(url), _ => { });
        var closes = 0;
        Popovers.SetClosePopoverBox(content, new PopoverCloseBox(() => closes++));
        var window = Host(content);
        try
        {
            var copy = Assert.IsType<StackPanel>(Assert.IsType<Border>(content.Child).Child).Children[3];
            Assert.Equal("Copy Link", RowName(copy));
            var clipboard = TopLevel.GetTopLevel(content)!.Clipboard!;
            clipboard.SetTextAsync("stale").GetAwaiter().GetResult();

            Click(copy);

            Assert.Equal(1, closes);
            Assert.Equal(url.AbsoluteString, clipboard.TryGetTextAsync().GetAwaiter().GetResult());
            Assert.Equal("https://GitHub.com/Org/Repo/pull/5?Tab=files", url.AbsoluteString); // set up: the string has its own case and query
        }
        finally { window.Close(); }
    });

    // ---- UI-THEME-042: the popover itself (:9-66, :96-97) ----

    [Fact]
    [Trait("Req", "UI-THEME-042")] // DroppyCode/UI/Markdown/LinkPopover.swift:97 `.frame(width: 220, ...)`: exactly 220 whatever the words in it
    public void The_content_measures_exactly_220_wide_with_a_short_or_a_very_long_label() => headless.OnUiThread(() =>
    {
        var plain = new LinkPopoverContent(SwiftURL.Create("https://example.com/x")!, null, _ => { });
        var longUrl = SwiftURL.Create("https://gitlab.com/some-group/some-long-project-name/-/merge_requests/123456789")!;
        var merge = new LinkPopoverContent(longUrl, MergeRequestLink.Create(longUrl), _ => { });

        plain.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        merge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Assert.Equal(220, plain.DesiredSize.Width);
        Assert.Equal(220, merge.DesiredSize.Width);
        Assert.True(merge.DesiredSize.Height > plain.DesiredSize.Height); // the Merge row and its divider are real height
    });

    [Fact]
    [Trait("Req", "UI-THEME-042")] // DroppyCode/UI/Markdown/LinkPopover.swift:55-59: only key code 53 (Escape) closes; any other key goes on to the window
    public void Keys_other_than_Escape_leave_the_popover_open_and_still_reach_the_window() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var anchor = new Border { Width = 10, Height = 10 };
        var window = Host(anchor);
        var seen = new List<Key>();
        window.AddHandler(InputElement.KeyDownEvent, (_, e) => seen.Add(e.Key));
        try
        {
            LinkPopover.Shared.Show(SwiftURL.Create("https://example.com/x")!, null, new Rect(0, 0, 10, 10), anchor);

            window.KeyPress(Key.A, RawInputModifiers.None, PhysicalKey.A, "a");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();

            Assert.Single(recorder.Calls); // still the one present, no dismiss
            Assert.Equal([Key.A, Key.Enter], seen);
        }
        finally
        {
            LinkPopover.Shared.Close();
            window.Close();
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-042")] // DroppyCode/UI/Markdown/LinkPopover.swift:43-46, :62-65: closing takes the key monitor away, so Escape is no longer swallowed
    public void After_the_popover_is_closed_Escape_reaches_the_window_again() => headless.OnUiThread(() =>
    {
        LinkPopover.Shared.PopoverDidClose(); // the shared popover starts with no key monitor, whatever ran before
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var anchor = new Border { Width = 10, Height = 10 };
        var window = Host(anchor);
        var sawEscape = 0;
        window.AddHandler(InputElement.KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) sawEscape++; });
        try
        {
            LinkPopover.Shared.Show(SwiftURL.Create("https://example.com/x")!, null, new Rect(0, 0, 10, 10), anchor);
            LinkPopover.Shared.Close();

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, sawEscape);
            Assert.Equal(2, recorder.Calls.Count); // shown, closed: Escape did not close anything a second time
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "UI-THEME-042")] // DroppyCode/UI/Markdown/LinkPopover.swift:22, :28, :48-50: once the system has dismissed the popover (outside click), it is not shown, so the next link opens it without a dismiss first
    public void After_the_system_dismisses_the_popover_the_next_Show_closes_nothing_first() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var anchor = new Border { Width = 10, Height = 10 };
        var window = Host(anchor);
        try
        {
            LinkPopover.Shared.Show(SwiftURL.Create("https://example.com/a")!, null, new Rect(0, 0, 10, 10), anchor);
            LinkPopover.Shared.PopoverDidClose(); // what a transient popover's own dismissal reports

            LinkPopover.Shared.Show(SwiftURL.Create("https://example.com/b")!, null, new Rect(0, 0, 10, 10), anchor);

            Assert.Equal([true, true], recorder.Calls.Select(call => call.Presented)); // present, present: no dismiss in between
        }
        finally
        {
            LinkPopover.Shared.Close();
            window.Close();
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-042")] // DroppyCode/UI/Markdown/LinkPopover.swift:10, :27-41: one popover serves every link; opening on another link moves it there
    public void Opening_on_another_link_moves_the_same_popover_to_that_links_bounds_and_row_urls() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var first = new Border { Width = 40, Height = 10 };
        var second = new Border { Width = 40, Height = 10 };
        var window = Host(new StackPanel { Children = { first, second } });
        var firstRect = new Rect(1, 2, 30, 8);
        var secondRect = new Rect(5, 1, 20, 9);
        try
        {
            WithOpenSeams(opened =>
            {
                LinkPopover.Shared.Show(SwiftURL.Create("https://example.com/a")!, null, firstRect, first);
                var popup = LinkPopover.Shared.PopoverForTests;
                Assert.Same(first, popup.PlacementTarget);
                Assert.Equal(firstRect, popup.PlacementRect);

                LinkPopover.Shared.Show(SwiftURL.Create("https://example.com/b")!, null, secondRect, second);

                Assert.Same(popup, LinkPopover.Shared.PopoverForTests); // the same popup object
                Assert.Same(second, popup.PlacementTarget);
                Assert.Equal(secondRect, popup.PlacementRect);
                Click(ShownRows()[0]);
                Assert.Equal(["https://example.com/b"], opened); // the rows are for the new link
            });
        }
        finally
        {
            LinkPopover.Shared.Close();
            window.Close();
        }
    });
}
