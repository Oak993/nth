using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Markdown;

// Source: DroppyCode/UI/Markdown/LinkPopover.swift (B3 52bc792), :27-41 (show), :75-107 (content): the Merge row's favicon when it
// arrives over the network, the rows' width inside the popover's padding, and where Copy Link writes.
[Collection("Headless")]
public sealed class LinkPopoverRowsAndIconTests(HeadlessSession headless)
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

    private static byte[] Png16()
    {
        var bitmap = new WriteableBitmap(new PixelSize(16, 16), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (var locked = bitmap.Lock())
        {
            var pixels = new byte[locked.RowBytes * 16];
            for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 40; pixels[i + 1] = 90; pixels[i + 2] = 200; pixels[i + 3] = 255; } // an opaque blue square: no white plate to clean away
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, locked.Address, pixels.Length);
        }
        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        return stream.ToArray();
    }

    private static PopoverItem MergeRow(LinkPopoverContent content) =>
        (PopoverItem)Assert.IsType<StackPanel>(Assert.IsType<Border>(content.Child).Child).Children[0];

    private static bool Poll(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            if (done()) return true;
            System.Threading.Thread.Sleep(20);
        }
        return false;
    }

    private static System.Collections.Generic.Dictionary<string, System.Threading.Tasks.Task<Avalonia.Media.IImage?>> InFlight() =>
        (System.Collections.Generic.Dictionary<string, System.Threading.Tasks.Task<Avalonia.Media.IImage?>>)typeof(FaviconCache)
            .GetField("inFlight", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;

    [Fact]
    [Trait("Req", "UI-THEME-043")] // :98-106 the favicon task: a cache miss awaits the host's icon, and the arrival swaps the placeholder symbol (:81) for the image
    [Trait("Rule", "R-TEST-4")] // partial proof of UI-THEME-043: the cache-miss branch (held in flight, then answered) and the no-host guard; the order, text, placeholder, cached icon and click are asserted by LinkPopoverProofTests
    public void The_merge_row_swaps_the_placeholder_for_the_favicon_when_the_awaited_host_icon_arrives() => headless.OnUiThread(() =>
    {
        const string host = "linkpopover-merge-icon.example.org";
        var pending = new System.Threading.Tasks.TaskCompletionSource<Avalonia.Media.IImage?>();
        var arriving = new WriteableBitmap(new PixelSize(14, 14), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        FaviconCacheSeed.Remove(host);
        InFlight()[host] = pending.Task; // the host's icon is being fetched: the row's task awaits it (:103)
        var url = SwiftURL.Create("https://" + host + "/g/p/-/merge_requests/7")!;
        var content = new LinkPopoverContent(url, MergeRequestLink.Create(url)!, _ => { });
        var window = Show(content);
        try
        {
            // While the fetch is out: the placeholder merge glyph, no image.
            Dispatcher.UIThread.RunJobs();
            var before = MergeRow(content);
            Assert.Equal("arrow.triangle.merge", before.GetVisualDescendants().OfType<SwiftImage>().Single().SymbolName);
            Assert.Empty(before.GetVisualDescendants().OfType<Image>());

            // The icon arrives: the glyph gives way to that image.
            pending.SetResult(arriving);
            Assert.True(Poll(() => MergeRow(content).GetVisualDescendants().OfType<Image>().Any()), "the favicon never replaced the placeholder");
            var row = MergeRow(content);
            Assert.Same(arriving, row.GetVisualDescendants().OfType<Image>().Single().Source);
            Assert.Empty(row.GetVisualDescendants().OfType<SwiftImage>());
        }
        finally
        {
            window.Close();
            InFlight().Remove(host);
            FaviconCacheSeed.Remove(host);
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-043")] // :101-104 a cache miss goes to the host's own /favicon.ico and the decoded icon replaces the placeholder
    [Trait("Rule", "R-TEST-4")] // partial proof of UI-THEME-043: the cache-miss fetch end to end through an in-process transport
    public void A_cache_miss_fetches_the_hosts_favicon_ico_and_shows_the_decoded_icon() => headless.OnUiThread(() =>
    {
        const string host = "linkpopover-fetch-icon.example.org";
        var png = Png16();
        using var handler = new InProcessHttpHandler
        {
            Answer = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(png) { Headers = { { "Content-Type", "image/png" } } } },
        };
        using var transport = URLSession.Transport.Override(handler);
        FaviconCacheSeed.Remove(host);
        var url = SwiftURL.Create("https://" + host.ToUpperInvariant() + "/g/p/-/merge_requests/7")!; // the host is lowercased for the lookup
        var content = new LinkPopoverContent(url, MergeRequestLink.Create(url)!, _ => { });
        var window = Show(content);
        try
        {
            Assert.True(Poll(() => MergeRow(content).GetVisualDescendants().OfType<Image>().Any()), "the favicon never replaced the placeholder");
            var row = MergeRow(content);
            Assert.NotNull(row.GetVisualDescendants().OfType<Image>().Single().Source);
            Assert.Empty(row.GetVisualDescendants().OfType<SwiftImage>());
            Assert.Equal(["GET https://" + host + "/favicon.ico"], handler.Received.Select(received => received.Method + " " + received.Url).ToArray());
        }
        finally
        {
            window.Close();
            FaviconCacheSeed.Remove(host);
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-043")] // :100 guard let host = url.host: a link with no host never asks for an icon, and the placeholder stays
    [Trait("Rule", "R-TEST-4")] // partial proof of UI-THEME-043: the no-host guard
    public void A_merge_row_for_a_link_with_no_host_never_fetches_a_favicon() => headless.OnUiThread(() =>
    {
        using var handler = new InProcessHttpHandler { Answer = _ => new HttpResponseMessage(HttpStatusCode.NotFound) };
        using var transport = URLSession.Transport.Override(handler);
        var merge = SwiftURL.Create("https://linkpopover-nohost.example.org/g/p/-/merge_requests/9")!;
        var hostless = SwiftURL.Create("mailto:someone@example.org")!;
        Assert.Null(hostless.Host);
        var content = new LinkPopoverContent(hostless, MergeRequestLink.Create(merge)!, _ => { }); // a merge request alongside a host-less address: the guard decides
        var window = Show(content);
        try
        {
            System.Threading.Thread.Sleep(150);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("arrow.triangle.merge", MergeRow(content).GetVisualDescendants().OfType<SwiftImage>().Single().SymbolName);
            Assert.Empty(handler.Received);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "UI-THEME-044")] // :96-97 padding 6 and frame width 220 make every row (and the divider) 208 wide, as the rows' own Spacer fills the stack (Popovers.swift:312)
    public void All_rows_fill_the_208_points_inside_the_popover_padding() => headless.OnUiThread(() =>
    {
        var url = SwiftURL.Create("https://gitlab.com/g/p/-/merge_requests/7")!;
        var content = new LinkPopoverContent(url, MergeRequestLink.Create(url)!, _ => { });
        var window = Show(content);
        try
        {
            var border = Assert.IsType<Border>(content.Child);
            Assert.Equal(new Thickness(6), border.Padding);
            Assert.Equal(220, border.Bounds.Width);
            var stack = Assert.IsType<StackPanel>(border.Child);
            Assert.Equal(4, stack.Children.Count); // Merge, divider, Open Link, Copy Link
            Assert.All(stack.Children, row => Assert.Equal(208, row.Bounds.Width));
            Assert.Equal(220, content.Bounds.Width);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(0, 0, 220, 110)] // :36 an empty measure falls back to 220 by 110
    [InlineData(0, 64, 220, 110)]
    [InlineData(220, 64, 220, 64)] // a real measure is the size the popover takes
    [Trait("Req", "UI-THEME-042")] // :35-37 the popover's size is the hosted content's intrinsic size, 220 by 110 when that comes back empty
    [Trait("Rule", "R-TEST-4")] // partial proof of UI-THEME-042: the size fallback; the outside-click, escape and second-show clauses need a real popup window (NATIVE-ONLY)
    public void The_popover_takes_the_measured_size_or_220_by_110_when_the_measure_is_empty(double measuredWidth, double measuredHeight, double width, double height) => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var anchor = new Border { Width = 10, Height = 10 };
        var window = Show(anchor);
        var previous = LinkPopover.MeasureContent;
        try
        {
            LinkPopover.MeasureContent = _ => new Size(measuredWidth, measuredHeight);
            LinkPopover.Shared.Show(SwiftURL.Create("https://example.com/x")!, null, new Rect(0, 0, 10, 10), anchor);
            var frame = Assert.IsType<Border>(PopoverHost.Content(LinkPopover.Shared.PopoverForTests));
            Assert.Equal(width, frame.Width);
            Assert.Equal(height, frame.Height);
        }
        finally
        {
            LinkPopover.MeasureContent = previous;
            LinkPopover.Shared.Close();
            window.Close();
        }
    });

    [Fact]
    [Trait("Req", "UI-THEME-045")] // :91-94 the pasteboard takes the url's absolute string; here the content is not in the window's own tree (a popup's root), so the write goes through the link's window
    public void Copy_Link_writes_through_the_window_the_link_is_in_when_the_popup_content_has_a_root_of_its_own() => headless.OnUiThread(() =>
    {
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var anchor = new Border { Width = 10, Height = 10 };
        var window = Show(anchor);
        try
        {
            var url = SwiftURL.Create("https://example.com/Mixed/Case?Q=1")!;
            LinkPopover.Shared.Show(url, null, new Rect(0, 0, 10, 10), anchor);
            var frame = Assert.IsType<Border>(PopoverHost.Content(LinkPopover.Shared.PopoverForTests));
            var content = (LinkPopoverContent)frame.Child!;
            Assert.Null(TopLevel.GetTopLevel(content)); // not attached to any window: like a popup's own root, it has no clipboard of its own here
            var clipboard = window.Clipboard!;
            clipboard.SetTextAsync("stale").GetAwaiter().GetResult();

            var copy = (PopoverItem)Assert.IsType<StackPanel>(Assert.IsType<Border>(content.Child).Child).Children[1];
            ((Button)copy.Child!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(url.AbsoluteString, clipboard.TryGetTextAsync().GetAwaiter().GetResult());
        }
        finally
        {
            LinkPopover.Shared.Close();
            window.Close();
        }
    });
}
