using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Markdown;

// Source under test: DroppyCode/UI/Markdown/LinkPopover.swift (B3 52bc792), :1-108. The popover a link in the
// chat opens on a right-click. Port shape read (ED-095, R-VIEW-9): NSPopover is an Avalonia Popup, shown and
// dismissed through the Presentation seam and recorded here by PopoverRecorder (Fixtures/PopoverRecorder.cs),
// because headless cannot open a themeless Popup (precedent: AttachmentPreviewPanelTests.cs). LinkPopover.Shared
// is a process-wide singleton (:10), so every test that calls Show ends by calling Close, to leave no escape
// monitor attached to a window the test is about to dispose.
//
// UNTESTABLE for now, and why: the :36 non-positive-measurement fallback (220x110) never fires with real rows,
// which always measure a positive size; Copy Link's clipboard write is not asserted (no existing test in this
// suite reads back a headless TopLevel's clipboard content either, e.g. MarkdownView.cs's CopyButton); the
// constructor's `popover.Closed` resync (review fix: :22 popover.delegate = self must keep firing
// popoverDidClose on a real .transient light dismiss, which the PopoverRecorder seam never drives) has no test
// here, because opening this class's own real Popup even once throws in headless ("Unable to create IPopupImpl
// and no overlay layer is found for the target control"), confirmed empirically and matching this file's own
// PopoverRecorder rationale above; AttachmentPreviewPanel.cs's matching resync line (:60-65) is equally untested.
[Collection("Headless")]
public sealed class LinkPopoverTests(HeadlessSession headless)
{
    private static Window Show(Control content)
    {
        content.HorizontalAlignment = HorizontalAlignment.Left;
        content.VerticalAlignment = VerticalAlignment.Top;
        var window = new Window { Width = 400, Height = 300, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Button ButtonOf(PopoverItem item) => (Button)item.Child!;

    // ---- LinkPopoverContent's shape (PR-THEME-147, UI-THEME-042 to 045) ----

    [Fact]
    [Trait("Req", "UI-THEME-044")] // DroppyCode/UI/Markdown/LinkPopover.swift:88-94: no merge target, no Merge row
    public void With_no_request_the_content_has_only_Open_Link_and_Copy_Link() => headless.OnUiThread(() =>
    {
        var content = new LinkPopoverContent(SwiftURL.Create("https://example.com/x")!, null, _ => { });
        var stack = Assert.IsType<StackPanel>(Assert.IsType<Border>(content.Child).Child);

        Assert.Equal(2, stack.Children.Count);
        Assert.All(stack.Children, child => Assert.IsType<PopoverItem>(child));
    });

    [Fact]
    [Trait("Req", "PR-THEME-147")] // DroppyCode/UI/Markdown/LinkPopover.swift:77-90: Merge, a divider, then the two universal rows
    public void With_a_request_the_content_shows_Merge_then_a_divider_then_the_two_universal_rows() => headless.OnUiThread(() =>
    {
        var url = SwiftURL.Create("https://gitlab.com/g/p/-/merge_requests/7")!;
        var request = MergeRequestLink.Create(url)!;
        var content = new LinkPopoverContent(url, request, _ => { });
        var stack = Assert.IsType<StackPanel>(Assert.IsType<Border>(content.Child).Child);

        Assert.Equal(4, stack.Children.Count);
        Assert.IsType<PopoverItem>(stack.Children[0]);
        Assert.IsType<PopoverDivider>(stack.Children[1]);
        Assert.IsType<PopoverItem>(stack.Children[2]);
        Assert.IsType<PopoverItem>(stack.Children[3]);
    });

    [Fact]
    [Trait("Req", "UI-THEME-042")] // DroppyCode/UI/Markdown/LinkPopover.swift:96-97: 220 wide, 6 point padding, leading-aligned
    public void The_content_is_220_points_wide_with_6_point_padding() => headless.OnUiThread(() =>
    {
        var content = new LinkPopoverContent(SwiftURL.Create("https://example.com/x")!, null, _ => { });
        var border = Assert.IsType<Border>(content.Child);

        Assert.Equal(220, border.Width);
        Assert.Equal(new Thickness(6), border.Padding);
        Assert.Equal(HorizontalAlignment.Left, border.HorizontalAlignment); // :97 alignment: .leading
    });

    // ---- PR-THEME-145/PR-THEME-147: Open Link runs MarkdownLinkOpener.open ----

    [Fact]
    [Trait("Req", "UI-THEME-044")] // DroppyCode/UI/Markdown/LinkPopover.swift:88-90
    public void Open_Link_runs_the_url_through_MarkdownLinkOpener() => headless.OnUiThread(() =>
    {
        var url = SwiftURL.Create("https://example.com/x")!;
        var content = new LinkPopoverContent(url, null, _ => { });
        var stack = Assert.IsType<StackPanel>(Assert.IsType<Border>(content.Child).Child);
        var openLink = (PopoverItem)stack.Children[0];
        var window = Show(content);

        var previous = ShellWorkspace.OpenWithDefaultApp;
        string? opened = null;
        ShellWorkspace.OpenWithDefaultApp = path => opened = path;
        try
        {
            ButtonOf(openLink).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs(); // Activate() runs the action via MainActor.Spawn
            Assert.Equal("https://example.com/x", opened);
        }
        finally
        {
            ShellWorkspace.OpenWithDefaultApp = previous;
            window.Close();
        }
    });

    // ---- UI-THEME-043: Merge hands the parsed request to the chat ----

    [Fact]
    [Trait("Req", "UI-THEME-043")] // DroppyCode/UI/Markdown/LinkPopover.swift:83-85
    public void Merge_hands_the_parsed_request_to_the_callback() => headless.OnUiThread(() =>
    {
        var url = SwiftURL.Create("https://gitlab.com/g/p/-/merge_requests/7")!;
        MergeRequestLink? received = null;
        var content = new LinkPopoverContent(url, MergeRequestLink.Create(url), r => received = r);
        var stack = Assert.IsType<StackPanel>(Assert.IsType<Border>(content.Child).Child);
        var mergeRow = (PopoverItem)stack.Children[0];
        var window = Show(content);

        try
        {
            ButtonOf(mergeRow).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.NotNull(received);
            Assert.Equal(url, received!.Url);
        }
        finally
        {
            window.Close();
        }
    });

    // ---- LinkPopover.Show/Close (:27-46): the Presentation seam and the escape monitor ----

    [Fact]
    [Trait("Req", "UI-THEME-042")] // DroppyCode/UI/Markdown/LinkPopover.swift:39-40
    public void Show_presents_the_popover_through_the_presentation_seam() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var anchor = new Border { Width = 10, Height = 10 };
        var window = Show(anchor);
        try
        {
            LinkPopover.Shared.Show(SwiftURL.Create("https://example.com/x")!, null, new Rect(0, 0, 10, 10), anchor);

            Assert.Single(recorder.Calls);
            Assert.True(recorder.Calls[0].Presented);
        }
        finally
        {
            LinkPopover.Shared.Close();
            window.Close();
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-042")] // DroppyCode/UI/Markdown/LinkPopover.swift:43-46
    public void Close_dismisses_the_popover_through_the_presentation_seam() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var anchor = new Border { Width = 10, Height = 10 };
        var window = Show(anchor);
        try
        {
            LinkPopover.Shared.Show(SwiftURL.Create("https://example.com/x")!, null, new Rect(0, 0, 10, 10), anchor);

            LinkPopover.Shared.Close();

            Assert.Equal(2, recorder.Calls.Count);
            Assert.False(recorder.Calls[1].Presented);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-042")] // DroppyCode/UI/Markdown/LinkPopover.swift:53-58: Escape closes and swallows the key
    public void Escape_closes_the_popover_and_the_key_never_bubbles() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var anchor = new Border { Width = 10, Height = 10 };
        var window = Show(anchor);
        var sawKey = false;
        window.AddHandler(InputElement.KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) sawKey = true; });
        try
        {
            LinkPopover.Shared.Show(SwiftURL.Create("https://example.com/x")!, null, new Rect(0, 0, 10, 10), anchor);

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(2, recorder.Calls.Count);
            Assert.False(recorder.Calls[1].Presented);
            Assert.False(sawKey); // :58 return nil: swallowed before the bubble phase
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-042")] // DroppyCode/UI/Markdown/LinkPopover.swift:48-50: popoverDidClose stops the monitor
    public void PopoverDidClose_stops_the_escape_monitor_so_a_later_escape_does_nothing_more() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var anchor = new Border { Width = 10, Height = 10 };
        var window = Show(anchor);
        try
        {
            LinkPopover.Shared.Show(SwiftURL.Create("https://example.com/x")!, null, new Rect(0, 0, 10, 10), anchor);
            LinkPopover.Shared.PopoverDidClose(); // e.g. the light-dismiss path, not Close() itself

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();

            Assert.Single(recorder.Calls); // Escape found no active monitor: no second SetPresented call
        }
        finally
        {
            LinkPopover.Shared.Close();
            window.Close();
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-042")] // DroppyCode/UI/Markdown/LinkPopover.swift:28: an already-open popover closes first
    public void Showing_again_while_open_closes_the_previous_one_first() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var anchor = new Border { Width = 10, Height = 10 };
        var window = Show(anchor);
        try
        {
            LinkPopover.Shared.Show(SwiftURL.Create("https://example.com/a")!, null, new Rect(0, 0, 10, 10), anchor);

            LinkPopover.Shared.Show(SwiftURL.Create("https://example.com/b")!, null, new Rect(0, 0, 10, 10), anchor);

            Assert.Equal(3, recorder.Calls.Count); // open a, close a, open b
            Assert.True(recorder.Calls[0].Presented);
            Assert.False(recorder.Calls[1].Presented);
            Assert.True(recorder.Calls[2].Presented);
        }
        finally
        {
            LinkPopover.Shared.Close();
            window.Close();
        }
    });

    // :35 NSHostingView(rootView: content).intrinsicContentSize is the size of a hosted view, with its styles and templates
    // in effect; the rows are buttons, which a detached control measures empty (12 pt tall, 23 with Merge, for 64 and 101).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "UI-THEME-042")] // DroppyCode/UI/Markdown/LinkPopover.swift:35-37: the popover is its content's measured size
    public void The_popover_is_exactly_as_big_as_its_content_measured_in_a_window(bool withMerge) => headless.OnUiThread(() =>
    {
        var url = SwiftURL.Create(withMerge ? "https://gitlab.com/g/p/-/merge_requests/7" : "https://example.com/x")!;
        LinkPopoverContent Make() => new(url, withMerge ? MergeRequestLink.Create(url) : null, _ => { });
        var hosted = Make();
        var hostWindow = new Window { Width = 400, Height = 300, Content = hosted };
        hostWindow.Show();
        Dispatcher.UIThread.RunJobs();
        hosted.Measure(Size.Infinity);
        var expected = hosted.DesiredSize;
        hostWindow.Close();
        Assert.True(expected.Height > 40, $"the fixture's rows should be taller than a detached measure, was {expected}");

        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var anchor = new Border { Width = 10, Height = 10 };
        var window = Show(anchor);
        try
        {
            LinkPopover.Shared.Show(url, withMerge ? new MergeRequestTarget(Guid.NewGuid(), _ => { }) : null, new Rect(0, 0, 10, 10), anchor);

            var frame = Assert.IsType<Border>(PopoverHost.Content(Assert.Single(recorder.Calls).Popup)); // the site's frame, not the card the presenter hangs it on (OD-0095)
            Assert.Equal(expected.Width, frame.Width);
            Assert.Equal(expected.Height, frame.Height);
        }
        finally
        {
            LinkPopover.Shared.Close();
            window.Close();
        }
    });
}
