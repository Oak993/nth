using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Testing;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Fixtures;

// ED-NEW-lean-11: the real AssistantMessageRow fed a streamed reply flush after flush on the manual frame and time sources, as the timeline does
// (TimelineRowsStreamVeilTests' recipe). Used by the settled-block budget and pixel tests.
internal sealed class SettledBlocksRig : IDisposable
{
    private readonly ThreadRuntimePart12Harness harness;
    private readonly IDisposable frameSeam;

    internal ManualFrameSource Frames { get; } = new();
    internal TimelineEntry Entry { get; private set; } = null!;
    internal AssistantMessageRow Row { get; private set; } = null!;
    internal AvaloniaWindow Window { get; private set; } = null!;

    internal SettledBlocksRig(string first, double height = 700)
    {
        harness = Views.Chat.TimelineRowsPrSupport.Harness();
        frameSeam = FrameClock.Source.Override(Frames);
        FreshVeilClock();
        harness.Main.OnMain(() =>
        {
            Entry = new TimelineEntry(new TimelineItem(null, new TimelineItem.Content.Assistant(new AssistantMessage { Text = first, IsStreaming = true })));
            Row = new AssistantMessageRow(Entry, null, harness.Runtime);
            Window = Views.Chat.TimelineRowsPrSupport.Show(Row);
            Window.Height = height;
            Views.Chat.TimelineRowsPrSupport.Settle(Window);
        });
    }

    internal void OnMain(Action body) => harness.Main.OnMain(body);

    // One flush of the reply, committed to layout as the timeline commits it: the row takes the new item on main (its tracking posts the rebuild there),
    // then the window lays out.
    internal void Flush(string text, bool streaming = true)
    {
        harness.Main.OnMain(() => Entry.Item = Entry.Item with
        {
            TimelineItemContent = new TimelineItem.Content.Assistant(new AssistantMessage { Text = text, IsStreaming = streaming }),
        });
        harness.Main.OnMain(() => Views.Chat.TimelineRowsPrSupport.Settle(Window));
    }

    internal void Tick(double milliseconds)
    {
        harness.Main.AdvanceMilliseconds(milliseconds);
        harness.Main.OnMain(() => { Frames.Advance(TimeSpan.FromMilliseconds(milliseconds)); Views.Chat.TimelineRowsPrSupport.Settle(Window); });
    }

    // The window's rendered frame now, as BlitzAbRender.Capture takes it, reduced to a SHA-256 of its pixels.
    internal string Frame()
    {
        var hash = "";
        harness.Main.OnMain(() =>
        {
            for (var pass = 0; pass < 4; pass++) { Window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            using var bitmap = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(Window) ?? throw new Xunit.Sdk.XunitException("no rendered frame");
            using var buffer = bitmap.Lock();
            var pixels = new byte[buffer.RowBytes * buffer.Size.Height];
            System.Runtime.InteropServices.Marshal.Copy(buffer.Address, pixels, 0, pixels.Length);
            hash = $"{buffer.Size.Width}x{buffer.Size.Height}:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels))}";
        });
        return hash;
    }

    // When each block place of the row's veils arrived (MarkdownVeils' arrivals: null for the baseline), sorted by place.
    internal string Arrivals()
    {
        var arrivals = (System.Collections.IDictionary)typeof(MarkdownVeils).GetField("arrivals", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Row.Veils)!;
        return string.Join(";", arrivals.Keys.Cast<string>().Order(StringComparer.Ordinal)
            .Select(key => key + "=" + (arrivals[key] is DateTimeOffset at ? at.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-")));
    }

    public void Dispose()
    {
        harness.Main.OnMain(() => Views.Chat.TimelineRowsPrSupport.Close(Window));
        frameSeam.Dispose();
        harness.Dispose();
    }

    // TimelineRowsStreamVeilTests.FreshVeilClock: the shared veil clock starts afresh on this test's frame source and time.
    internal static void FreshVeilClock()
    {
        foreach (var name in new[] { "link", "deadline" })
            typeof(VeilClock).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(VeilClock.Shared, null);
        typeof(VeilClock).GetField("now", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(VeilClock.Shared, DateTimeOffset.MinValue);
    }

    // A deterministic reply of at least `length` characters: paragraphs, headings, lists, a quote and a fenced code block, each closed by a blank
    // line, then an open tail paragraph the next flushes extend. No links and no mentions (a link paragraph fetches favicons).
    internal static string Reply(int length)
    {
        var text = new StringBuilder();
        for (var i = 0; text.Length < length; i++)
        {
            if (i % 11 == 5) text.Append("## Section ").Append(i).Append("\n\n");
            else if (i % 7 == 3) text.Append("- first point of list ").Append(i).Append("\n- second point with **bold** words\n- third point with `code` in it\n\n");
            else if (i % 13 == 8) text.Append("> A quoted line ").Append(i).Append(" that stands apart from the rest.\n\n");
            else if (i % 17 == 12) text.Append("```swift\nlet value").Append(i).Append(" = compute(")
                .Append(i).Append(")\nprint(value").Append(i).Append(")\n```\n\n");
            else text.Append("Paragraph ").Append(i).Append(" says something plain about the work at hand, with a few more words to give it the length of a real")
                .Append(" reply's paragraph, and *one emphasis* too. The next sentence keeps it going for a while longer.\n\n");
        }
        return text.Append("Tail paragraph begins").ToString();
    }

    // A scripted stream of a reply: deterministic flush sizes (from a few characters to over a thousand) and gaps between flushes (from one frame to
    // well over a second), so blocks arrive while others are still arriving, settle, and rest.
    internal static IEnumerable<(string Text, double Gap)> Script(string reply)
    {
        int[] sizes = [37, 211, 503, 89, 1200, 14, 640, 302, 55, 980];
        double[] gaps = [16, 50, 120, 400, 1100, 30, 260, 1500, 75, 16, 700];
        var at = 0;
        for (var step = 0; at < reply.Length; step++)
        {
            at = Math.Min(reply.Length, at + sizes[step % sizes.Length]);
            yield return (reply[..at], gaps[step % gaps.Length]);
        }
    }

    // The visual descendants of the row now, to count what a flush creates.
    internal HashSet<Visual> Shown() => [.. Row.GetVisualDescendants()];
}
