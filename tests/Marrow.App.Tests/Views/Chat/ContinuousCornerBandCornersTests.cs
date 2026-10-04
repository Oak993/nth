using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// The corner clause of UI-CHAT-034, -036, -037 and -054 (OD-0102: Apple's corner at the largest radius that fits, h / 3.057), read off the rendered pixels at ALL FOUR
// corners of each mounted element. ContinuousCornerReproofTests.REVIEW_the_four_band_elements_render_... reads the top-left 8 x 8 at a tolerance of 60; each of these
// elements is a plain Border WITH CONTENT, which ED-297 (9) left on the slack clip (Skia's coarse anti-aliasing of a non-convex path: 64 of 255 at the one-step steps card,
// 700 x 45). The element's content is made transparent (Opacity 0, still mounted: the Border keeps its child) so only the fill and its corner are compared, with a
// SwiftShapeBorder of the same size (which fills the path itself) as the reference, over each corner's whole run-in. The delegation card is the unsent one: the sent card's
// DotFieldSweep is redrawn over the capture by the headless compositor (ED-NEW-rec-tlr (11): 60 of 255 along its bottom-left arc in headless, 0 in a real window).
[Collection("Headless")]
public sealed class ContinuousCornerBandCornersTests(HeadlessSession headless)
{
    private const double E = 1.528665;

    private static Border PillOf(Control root, Thickness padding) =>
        Mounted<Border>(root).Concat(root is Border own ? [own] : []).First(border => border.Padding == padding && SwiftShapes.GetCornerStyle(border) is not null);

    private static int[,] RedChannel(TopLevel window)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        using var frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window) ?? throw new Xunit.Sdk.XunitException("no rendered frame");
        using var buffer = frame.Lock();
        var row = new byte[buffer.RowBytes];
        var red = new int[buffer.Size.Height, buffer.Size.Width];
        for (var y = 0; y < buffer.Size.Height; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(buffer.Address + y * buffer.RowBytes, row, 0, buffer.RowBytes);
            for (var x = 0; x < buffer.Size.Width; x++) red[y, x] = 255 - row[x * 4 + 1]; // the fill is opaque red over a light window: 255 less green is its coverage
        }
        return red;
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // the corner clause only of UI-CHAT-034, UI-CHAT-036, UI-CHAT-037, UI-CHAT-054, read off the rendered pixels at every corner
    public void Every_corner_of_the_four_band_elements_renders_Apples_corner_at_the_largest_radius_within_a_few_levels() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        using var noNetwork = new NoLinkNetwork("gitlab.com");
        h.Main.OnMain(() =>
        {
            const string twoLines = "A warning long enough that it wraps onto a second line when the row is only 380 points wide.";
            var notice = new NoticeRow(NoticeEntry(Notice.Level.Warning, twoLines));
            var turn = Guid.NewGuid();
            var steps = new TurnFinishedBlock(h.Runtime, turn, Summary(turn) with { Duration = 64 }, ValueList<TimelineEntry>.Empty,
                new ValueList<TimelineEntry>([ToolEntry(Call(ToolCall.Kind.Read, "Read a"), turn)]), null, canUndo: false, chronological: true);
            var files = new TurnFileCard(new FileChangeSummary(new ValueList<FileChangeSummary.File>([new FileChangeSummary.File("src/file1.swift", 1, 1, false)]), 40, 7),
                canUndo: false, onRevert: () => { }, onReview: _ => { });
            var delegation = new HydraDelegationBlock("[{\"task\": \"Tidy 0\", \"prompt\": \"p\", \"name\": \"Hank\"}]", sent: false);
            var panel = new StackPanel { Width = 700, Spacing = 20 };
            foreach (var control in new Control[] { new Border { Width = 380, Child = notice }, steps, files, delegation }) panel.Children.Add(control);
            var window = Show(panel);
            var results = new List<string>();
            var failures = new List<string>();
            try
            {
                var shapes = new (string Name, Border Shape)[]
                {
                    ("notice, two lines", PillOf(notice, new Thickness(12, 8, 14, 8))), ("steps card, one step", PillOf(steps, new Thickness(14))),
                    ("Edited-files card", PillOf(files, new Thickness(12, 6, 6, 6))), ("delegation card, one head", PillOf(delegation, new Thickness(12, 8, 14, 8))),
                };
                foreach (var (name, shape) in shapes)
                {
                    Assert.True(shape.Child is not null, $"{name}: the band element is a Border with content");
                    shape.Background = Brushes.Red;
                    shape.Child!.Opacity = 0;
                }
                Settle(window);
                Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var mounted = RedChannel(window);
                foreach (var (name, shape) in shapes)
                {
                    var origin = shape.TranslatePoint(default, window) ?? throw new InvalidOperationException(name + " is not mounted");
                    var size = shape.Bounds.Size;
                    var drawn = ContinuousCorner.LargestRadius(size);
                    Assert.True(size.Height > 36 && drawn < 18, $"{name}: {size.Height} pt is not in the band");
                    var reference = new SwiftShapeBorder { Width = size.Width, Height = size.Height, Background = Brushes.Red, Margin = new Thickness(origin.X, origin.Y, 0, 0),
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top }.RoundedRectangle(18, RoundedCornerStyle.Continuous);
                    var refWindow = Show(new Panel { Children = { reference } });
                    Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    var expected = RedChannel(refWindow);
                    Close(refWindow);
                    // Each corner's square: the run-in of the drawn corner (E * L = h / 2) and two pixels more, from the vertex inwards.
                    var side = (int)Math.Ceiling(E * drawn) + 2;
                    int x0 = (int)Math.Floor(origin.X), y0 = (int)Math.Floor(origin.Y), x1 = (int)Math.Ceiling(origin.X + size.Width), y1 = (int)Math.Ceiling(origin.Y + size.Height);
                    var squares = new[] { (x0, y0), (x1 - side, y0), (x1 - side, y1 - side), (x0, y1 - side) };
                    var worst = new int[4];
                    for (var c = 0; c < 4; c++)
                    {
                        var (sx, sy) = squares[c];
                        for (var y = sy; y < sy + side; y++)
                            for (var x = sx; x < sx + side; x++) worst[c] = Math.Max(worst[c], Math.Abs(mounted[y, x] - expected[y, x]));
                    }
                    var seen = $"{name} ({size.Width:F1} x {size.Height:F1} pt at {origin}, L {drawn:F2}): top-left {worst[0]}, top-right {worst[1]}, bottom-right {worst[2]}, bottom-left {worst[3]}";
                    results.Add(seen);
                    if (worst.Any(level => level > 8)) failures.Add(seen);
                }
            }
            finally { Close(window); }
            Assert.True(failures.Count == 0, "over 8 of 255: " + string.Join("; ", failures) + " | " + string.Join("; ", results));
        });
    });
}
