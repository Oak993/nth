using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Testing;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// REVIEW (defects-priority, adversarial round 2). Sources: DroppyCode/UI/Markdown/StreamVeil.swift:4-8 ("a token that popped in whole read as a
// glitch"), :291-295; MarkdownView.swift:69-83 (with isStreaming false the reply is drawn by ProseRunView); ThreadRuntime.swift:3248-3265
// (completeMessage sets isStreaming false as the provider completes the message, right after its last delta). OD-0076 (owner, 2026-10-02):
// "nothing on screen changes by a cut ... content that swaps crossfades ... Where the Mac changes state without animation, the port still
// transitions ... an owner-ruled deviation wherever the source snaps."
//
// The unit makes the veil run in the app, and its ED says of the end of a stream: "the words in flight are not carried over (the port
// follows)". So when a reply ends while its last chunk is still dissolving, that chunk jumps from part of its alpha to the full colour in
// one frame: the cut OD-0076 forbids, on the last words of every reply whose completion arrives within the fade (0.12-0.40 s), which is
// the common case. Measured on the real row: the ink of the tail, as a share of its settled ink, right before and right after the stream
// ends.
[Collection("Headless")]
public sealed class StreamEndVeilReviewTests(HeadlessSession headless)
{
    private static TimelineEntry Streaming(string text) =>
        new(new TimelineItem(null, new TimelineItem.Content.Assistant(new AssistantMessage { Text = text, IsStreaming = true })));

    private static void Flush(TimelineEntry entry, string text, bool streaming = true) =>
        entry.Item = entry.Item with { TimelineItemContent = new TimelineItem.Content.Assistant(new AssistantMessage { Text = text, IsStreaming = streaming }) };

    private static void FreshVeilClock()
    {
        foreach (var name in new[] { "link", "deadline" })
            typeof(VeilClock).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(VeilClock.Shared, null);
    }

    private static void Tick(ThreadRuntimePart12Harness h, ManualFrameSource frames, Avalonia.Controls.Window window, double milliseconds)
    {
        h.Main.AdvanceMilliseconds(milliseconds);
        h.Main.OnMain(() => { frames.Advance(TimeSpan.FromMilliseconds(milliseconds)); Settle(window); });
    }

    // How much the row's text differs from the grey under it, summed over the row's first 40 px: the ink of what it draws.
    private static double Ink(Avalonia.Controls.Window window, Control row)
    {
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame() ?? throw new Xunit.Sdk.XunitException("no rendered frame");
        using var buffer = frame.Lock();
        var origin = row.TranslatePoint(new Point(0, 0), window) ?? throw new InvalidOperationException("the row is not mounted");
        var bytes = new byte[buffer.RowBytes];
        var total = 0.0;
        var left = (int)origin.X;
        var top = (int)origin.Y;
        var right = Math.Min(buffer.Size.Width, left + 400);
        var bottom = Math.Min(buffer.Size.Height, top + 40);
        for (var y = top; y < bottom; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(buffer.Address + y * buffer.RowBytes, bytes, 0, buffer.RowBytes);
            for (var x = left; x < right; x++)
                total += Math.Abs(Math.Max(bytes[x * 4], Math.Max(bytes[x * 4 + 1], bytes[x * 4 + 2])) - 128) / 255.0;
        }
        return total;
    }

    [Fact]
    [Trait("Req", "UI-THEME-047")] // StreamVeil.swift:4-8, :291-295; MarkdownView.swift:69-83; OD-0076 (the end of a stream is a change on screen: transitioned, never a cut)
    public void A_reply_that_ends_while_its_last_words_still_dissolve_does_not_snap_them_to_the_full_colour() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        FreshVeilClock();
        TimelineEntry entry = null!;
        AssistantMessageRow row = null!;
        Avalonia.Controls.Window window = null!;
        h.Main.OnMain(() =>
        {
            entry = Streaming("Hello");
            row = new AssistantMessageRow(entry, null, h.Runtime);
            window = Show(row);
            window.Background = new SolidColorBrush(Color.FromRgb(128, 128, 128));
            Settle(window);
        });
        try
        {
            var helloOnly = Ink(window, row);
            h.Main.OnMain(() => { Flush(entry, "Hello there"); Settle(window); });
            Tick(h, frames, window, 100); // the chunk " there" is a quarter through its 0.40 s fade: it shows at 1 - 0.75^1.6, about 0.37
            var inFlight = Ink(window, row);

            // The provider completes the message: the stream ends with " there" still dissolving.
            h.Main.OnMain(() => { Flush(entry, "Hello there", streaming: false); Settle(window); });
            var atEnd = Ink(window, row);

            Tick(h, frames, window, 1000);
            var settled = Ink(window, row);
            var tail = settled - helloOnly;
            Assert.True(tail > 0, $"the premise: the tail draws ink once settled (hello {helloOnly:0.#}, settled {settled:0.#})");
            var before = (inFlight - helloOnly) / tail;
            var after = (atEnd - helloOnly) / tail;
            Assert.InRange(before, 0.15, 0.6); // the premise: the tail was part way through its fade when the stream ended
            Assert.True(after < 0.75,
                $"the tail's ink jumped from {before:0.00} to {after:0.00} of its settled ink in the frame the stream ended: a cut (OD-0076), where it should carry on dissolving or crossfade");
        }
        finally
        {
            h.Main.OnMain(() => Close(window));
        }
    });
}
