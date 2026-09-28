using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
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

// Source: DroppyCode/UI/Markdown/LinkPopover.swift (B3 52bc792), :1-108.
// Cloud proof pass (overnight 2026-09-27): clauses of UI-THEME-042/043/044/045 not yet mutation-provable by
// LinkPopoverTests.cs. Mutations run and reverted by hand; see CLOUD REPORT.

[Collection("Headless")]
public sealed class LinkPopoverProofTests(HeadlessSession headless)
{
    private static Window Show(Control content)
    {
        content.HorizontalAlignment = HorizontalAlignment.Left;
        content.VerticalAlignment = VerticalAlignment.Top;
        var window = new Window { Width = 400, Height = 300, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [Fact]
    [Trait("Req", "UI-THEME-042")] // :76 VStack(alignment: .leading, spacing: 0)
    public void Rows_are_stacked_vertically_with_zero_spacing() => headless.OnUiThread(() =>
    {
        var content = new LinkPopoverContent(SwiftURL.Create("https://example.com/x")!, null, _ => { });
        var stack = Assert.IsType<StackPanel>(Assert.IsType<Border>(content.Child).Child);
        Assert.Equal(Orientation.Vertical, stack.Orientation);
        Assert.Equal(0, stack.Spacing);
    });

    [Fact]
    [Trait("Req", "UI-THEME-042")] // :39-40 popover.show(relativeTo: rect, of: view, preferredEdge: .minY): below the anchor
    public void Show_anchors_the_popover_below_the_links_own_bounds() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var anchor = new Border { Width = 10, Height = 10 };
        var window = Show(anchor);
        var rect = new Rect(1, 2, 10, 10);
        try
        {
            LinkPopover.Shared.Show(SwiftURL.Create("https://example.com/x")!, null, rect, anchor);
            var popup = LinkPopover.Shared.PopoverForTests;
            Assert.Same(anchor, popup.PlacementTarget);
            Assert.Equal(PlacementMode.AnchorAndGravity, popup.Placement);
            Assert.Equal(PopupAnchor.Bottom, popup.PlacementAnchor);
            Assert.Equal(PopupGravity.Bottom, popup.PlacementGravity);
            Assert.Equal(rect, popup.PlacementRect);
        }
        finally
        {
            LinkPopover.Shared.Close();
            window.Close();
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-042")] // :20 behavior = .transient: an outside click light-dismisses the popover
    public void The_popup_is_light_dismiss_enabled() => headless.OnUiThread(() =>
        Assert.True(LinkPopover.Shared.PopoverForTests.IsLightDismissEnabled));

    [Fact]
    [Trait("Req", "UI-THEME-042")] // :22 popover.delegate = self: a light dismiss (Popup.Closed) still resyncs isShown and stops the escape monitor
    public void The_popups_own_closed_event_resyncs_shown_state_and_stops_the_escape_monitor() => headless.OnUiThread(() =>
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

            // A real light dismiss cannot open this class's own themeless Popup in headless (see
            // LinkPopoverTests.cs's header comment), so raise the Popup's own Closed event directly: the exact
            // event the constructor wires to PopoverDidClose() (:22). A backing-field auto-event of type
            // EventHandler<EventArgs>, per Avalonia's own shape for Popup.Closed.
            var field = typeof(Popup).GetField(nameof(Popup.Closed), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var handler = (EventHandler<EventArgs>)field.GetValue(LinkPopover.Shared.PopoverForTests)!;
            handler(LinkPopover.Shared.PopoverForTests, EventArgs.Empty);

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();

            Assert.Single(recorder.Calls); // Closed already resynced isShown/escape monitor: Escape finds no active monitor, no second SetPresented
            Assert.True(sawKey); // :49 stopEscapeMonitor -> no handler left to swallow the key
        }
        finally
        {
            LinkPopover.Shared.Close();
            window.Close();
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-042")] // :35-37 popover.setFixedContent(content, size): the frame handed to the popup is the content's own measured size
    public void Show_sizes_the_popup_frame_to_the_contents_own_measured_size() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var anchor = new Border { Width = 10, Height = 10 };
        var window = Show(anchor);
        try
        {
            LinkPopover.Shared.Show(SwiftURL.Create("https://example.com/x")!, null, new Rect(0, 0, 10, 10), anchor);
            var frame = Assert.IsType<Border>(LinkPopover.Shared.PopoverForTests.Child);
            var content = (LinkPopoverContent)frame.Child!;
            content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); // the same constraint Show() itself used
            Assert.Equal(content.DesiredSize.Width, frame.Width);
            Assert.Equal(content.DesiredSize.Height, frame.Height);
        }
        finally
        {
            LinkPopover.Shared.Close();
            window.Close();
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-043")] // :29 mergeTarget is null ? null : MergeRequestLink.Create(url) - no merge target means no merge row, even for a merge-shaped URL, through the real Show() entry point
    public void Show_with_no_merge_target_produces_no_merge_row_even_for_a_merge_shaped_url() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var url = SwiftURL.Create("https://gitlab.com/g/p/-/merge_requests/7")!; // merge-shaped: MergeRequestLink.Create(url) would succeed if reached
        var anchor = new Border { Width = 10, Height = 10 };
        var window = Show(anchor);
        try
        {
            LinkPopover.Shared.Show(url, null, new Rect(0, 0, 10, 10), anchor);
            var frame = Assert.IsType<Border>(LinkPopover.Shared.PopoverForTests.Child);
            var content = (LinkPopoverContent)frame.Child!;
            var stack = Assert.IsType<StackPanel>(Assert.IsType<Border>(content.Child).Child);
            Assert.Equal(2, stack.Children.Count); // Open Link, Copy Link only - no Merge row, no divider
            Assert.All(stack.Children, child => Assert.IsType<PopoverItem>(child));
        }
        finally
        {
            LinkPopover.Shared.Close();
            window.Close();
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-043")] // :81 favicon == nil -> the placeholder symbol
    public void The_merge_row_shows_the_placeholder_symbol_until_the_favicon_loads() => headless.OnUiThread(() =>
    {
        var url = SwiftURL.Create("https://gitlab.com/g/p/-/merge_requests/7")!;
        var request = MergeRequestLink.Create(url)!;
        var content = new LinkPopoverContent(url, request, _ => { });
        // Show BEFORE reading the tree: content.Track(favicon, ...) rebuilds Child on AttachedToVisualTree
        // (ViewLifetime.cs Track), so a tree read before attach is the one about to be discarded.
        var window = Show(content); // Button templates apply only once attached to a rooted visual tree
        try
        {
            var stack = Assert.IsType<StackPanel>(Assert.IsType<Border>(content.Child).Child);
            var mergeItem = (PopoverItem)stack.Children[0];
            var glyph = mergeItem.GetVisualDescendants().OfType<SwiftImage>().Single();
            Assert.Equal("arrow.triangle.merge", glyph.SymbolName); // :81 favicon == nil -> the placeholder symbol
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-043")] // :80 "Merge" semibold + " " + request.Label regular; :332 AutomationProperties.SetName(button, title) with request.Title
    public void The_merge_row_shows_Merge_semibold_then_the_label_in_regular_weight() => headless.OnUiThread(() =>
    {
        var url = SwiftURL.Create("https://gitlab.com/g/p/-/merge_requests/7")!;
        var request = MergeRequestLink.Create(url)!;
        var content = new LinkPopoverContent(url, request, _ => { });
        var window = Show(content);
        try
        {
            var stack = Assert.IsType<StackPanel>(Assert.IsType<Border>(content.Child).Child);
            var mergeItem = (PopoverItem)stack.Children[0];
            var titleBlock = mergeItem.GetVisualDescendants().OfType<TextBlock>().First(t => t.Inlines is { Count: > 0 });
            var runs = titleBlock.Inlines!.Cast<Run>().ToArray();
            Assert.True(runs.Length >= 2);
            Assert.Equal("Merge", runs[0].Text);
            Assert.Equal(Avalonia.Media.FontWeight.SemiBold, runs[0].FontWeight);
            Assert.Contains(request.Label, runs[1].Text, StringComparison.Ordinal);
            Assert.NotEqual(Avalonia.Media.FontWeight.SemiBold, runs[1].FontWeight);

            var mergeButton = (Button)mergeItem.Child!;
            Assert.Equal(request.Title, Avalonia.Automation.AutomationProperties.GetName(mergeButton)); // :332 title, not a constant
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-043")] // :29-32,83-85 clicking Merge closes the real popover and hands the parsed request to the target
    public void Clicking_merge_closes_the_real_popover_and_hands_the_request_to_the_target() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var url = SwiftURL.Create("https://gitlab.com/g/p/-/merge_requests/7")!;
        MergeRequestLink? merged = null;
        var target = new MergeRequestTarget(Guid.NewGuid(), r => merged = r);
        var anchor = new Border { Width = 10, Height = 10 };
        var window = Show(anchor);
        try
        {
            LinkPopover.Shared.Show(url, target, new Rect(0, 0, 10, 10), anchor);
            // SetFixedContent wraps the content in a clipping frame Border (Popovers.cs SetFixedContent).
            var frame = Assert.IsType<Border>(LinkPopover.Shared.PopoverForTests.Child);
            var content = (LinkPopoverContent)frame.Child!;
            var stack = Assert.IsType<StackPanel>(Assert.IsType<Border>(content.Child).Child);
            var mergeButton = (Button)((PopoverItem)stack.Children[0]).Child!;

            mergeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs(); // Activate() closes then spawns the action on main

            Assert.NotNull(merged);
            Assert.Equal(url, merged!.Url);
            Assert.Equal(2, recorder.Calls.Count); // open, close
            Assert.False(recorder.Calls[1].Presented);
        }
        finally
        {
            LinkPopover.Shared.Close();
            window.Close();
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-044")] // :88-90 PopoverItem("Open Link", symbol: "safari")
    public void The_Open_Link_row_reads_exactly_that_with_the_compass_glyph() => headless.OnUiThread(() =>
    {
        var content = new LinkPopoverContent(SwiftURL.Create("https://example.com/x")!, null, _ => { });
        var window = Show(content); // Button templates apply only once attached to a rooted visual tree
        try
        {
            var stack = Assert.IsType<StackPanel>(Assert.IsType<Border>(content.Child).Child);
            var item = (PopoverItem)stack.Children[0];
            var titleBlock = item.GetVisualDescendants().OfType<TextBlock>().First();
            Assert.Equal("Open Link", titleBlock.Text);
            var glyph = item.GetVisualDescendants().OfType<SwiftImage>().Single();
            Assert.Equal("safari", glyph.SymbolName);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-045")] // :91-94 PopoverItem("Copy Link", symbol: "link"); clears then writes the absolute URL as plain text
    public void The_Copy_Link_row_reads_exactly_that_writes_the_absolute_url_with_the_link_glyph() => headless.OnUiThread(() =>
    {
        var url = SwiftURL.Create("https://example.com/x?y=1")!;
        var content = new LinkPopoverContent(url, null, _ => { });
        var window = Show(content); // Button templates apply only once attached to a rooted visual tree
        try
        {
            var stack = Assert.IsType<StackPanel>(Assert.IsType<Border>(content.Child).Child);
            var item = (PopoverItem)stack.Children[1];
            var titleBlock = item.GetVisualDescendants().OfType<TextBlock>().First();
            Assert.Equal("Copy Link", titleBlock.Text);
            var glyph = item.GetVisualDescendants().OfType<SwiftImage>().Single();
            Assert.Equal("link", glyph.SymbolName);

            var clipboard = TopLevel.GetTopLevel(content)!.Clipboard!;
            clipboard.SetTextAsync("stale").GetAwaiter().GetResult();
            var button = (Button)item.Child!;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            var copied = clipboard.TryGetTextAsync().GetAwaiter().GetResult();
            Assert.Equal(url.AbsoluteString, copied); // :93 setString(url.absoluteString, forType: .string), not the pretty title
        }
        finally
        {
            window.Close();
        }
    });
}
