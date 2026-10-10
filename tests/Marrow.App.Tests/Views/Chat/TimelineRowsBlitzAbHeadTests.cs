using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
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
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792), :395-476 the finished heads' pills, :480-521 HydraShimmer, :527-596 HydraBriefRow.
// Where a record's capture comparison asks for a frame the reference videos do not contain, the Swift source is the proof (OD-0098): the states the
// code names are mounted, rendered headless in both themes, and the values the code states are read back from the rendered frame. The pills are drawn
// in a .continuous rounded rectangle, which ED-181 still draws with circular arcs: their corner is the one thing these renders do not claim.
[Collection("Headless")]
public sealed class TimelineRowsBlitzAbHeadTests(HeadlessSession headless)
{
    private static readonly (ThemeVariant Theme, Color Backdrop)[] Themes =
    [
        (ThemeVariant.Dark, Color.FromRgb(24, 24, 27)),
        (ThemeVariant.Light, Color.FromRgb(244, 244, 246)),
    ];

    private static void Advance(TestRuntime main, ManualFrameSource frames, double milliseconds)
    {
        frames.Advance(TimeSpan.Zero);
        for (var elapsed = 0.0; elapsed < milliseconds; elapsed += 10)
        {
            main.AdvanceMilliseconds(10);
            frames.Advance(TimeSpan.FromMilliseconds(10));
        }
    }

    private static Color FillOver(ThemeVariant theme, Color backdrop) => BlitzAbFrame.Over(ChromeTokens.Accent.Opacity(0.1).Resolve(theme), backdrop);

    private static Border PillOf(Control root) => Mounted<Border>(root).First(border => border.Padding == new Thickness(12, 8, 14, 8));

    // The ink in the box, as its mass in each of the bins across its width (the coverage of the colour, summed down each strip of columns).
    private static double[] Mass(BlitzAbFrame frame, Rect box, Color backdrop, Color colour, int bins)
    {
        var width = box.Width / bins;
        return Enumerable.Range(0, bins).Select(bin => frame.InkMass(new Rect(box.Left + bin * width, box.Top, width, box.Height), backdrop, colour)).ToArray();
    }

    // How strongly the text in the rect is inked: the mean distance from the backdrop of its eight strongest pixels (the stems of its letters),
    // as a fraction of the distance a pixel wholly in the colour would have. Two inks that differ in opacity differ here by their ratio.
    private static double Peak(BlitzAbFrame frame, Rect area, Color backdrop, Color colour)
    {
        var distances = new List<int>();
        for (var x = (int)Math.Floor(area.Left); x < (int)Math.Ceiling(area.Right); x++)
            for (var y = (int)Math.Floor(area.Top); y < (int)Math.Ceiling(area.Bottom); y++)
                distances.Add(BlitzAbFrame.Distance(frame.At(x, y), backdrop));
        var full = BlitzAbFrame.Distance(BlitzAbFrame.Over(colour, backdrop), backdrop);
        return distances.OrderByDescending(distance => distance).Take(8).Average() / full;
    }

    // The mean distance from the backdrop of the eight strongest pixels in the area.
    private static double RawPeak(BlitzAbFrame frame, Rect area, Color backdrop)
    {
        var distances = new List<int>();
        for (var x = (int)Math.Floor(area.Left); x < (int)Math.Ceiling(area.Right); x++)
            for (var y = (int)Math.Floor(area.Top); y < (int)Math.Ceiling(area.Bottom); y++)
                distances.Add(BlitzAbFrame.Distance(frame.At(x, y), backdrop));
        return distances.OrderByDescending(distance => distance).Take(8).Average();
    }

    // The same words in the same font drawn alone in the ink the source gives them, in a window of their own: what the ink of a line should be
    // however thin its strokes are (a short word at 10 points has no stem that reaches the full colour).
    private static void IsInkedLike(BlitzAbFrame frame, Rect area, Color backdrop, string text, SwiftFont font, SwiftColor ink, ThemeVariant theme, string because)
    {
        var label = new TextBlock { Text = text };
        font.Apply(label);
        label.Foreground = ink.Brush(label);
        var reference = 0.0;
        BlitzAbRender.Render(new Border { Padding = new Thickness(8), Child = label }, theme, backdrop, 480, 60, (window, shot) => reference = RawPeak(shot, BlitzAbRender.BoxIn(label, window), backdrop));
        var drawn = RawPeak(frame, area, backdrop);
        Assert.True(reference > 60, $"{because}: the reference line is barely inked ({reference})");
        Assert.InRange(drawn / reference, 0.92, 1.08);
    }

    // ---- PR-CHAT-148: the thinking shimmer ----

    [Fact]
    [Trait("Req", "PR-CHAT-148")] // TimelineRows.swift:480-520; OD-0098: the band drawn over the text, masked to its glyphs, sweeping every 1.8 s, still while scrolling and under reduced motion
    public void The_shimmer_brightens_only_the_glyphs_under_a_band_a_third_as_wide_as_the_text_sweeping_left_to_right_every_1_8_seconds_in_both_themes() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        var genie = new FakeGeniePlatform();
        using var reduce = ChromeTokens.ReduceMotionPlatform.Override(genie);
        foreach (var (theme, backdrop) in Themes)
        {
            var ink = ChromeTokens.PrimaryText.Opacity(0.9).Resolve(theme);
            const string words = "Pushing the branch to the remote"; // :1056-1079 the words of a stage, here a long one: the band is a third of its width
            Control Words() => TimelineRows.ChatText(words, weight: SwiftFont.Weight.Medium, color: ChromeTokens.PrimaryText.Opacity(0.9));
            // The same words with no shimmer at all: the frame the shimmered ones are compared with.
            BlitzAbFrame? plainFrame = null; Rect plainBox = default;
            h.Main.OnMain(() =>
            {
                var plain = Words();
                BlitzAbRender.Render(new Border { Padding = new Thickness(24), Child = new Border { HorizontalAlignment = HorizontalAlignment.Left, Child = plain } }, theme, backdrop, 360, 70, (window, frame) =>
                { plainFrame = frame; plainBox = BlitzAbRender.BoxIn(plain, window); });
            });
            AvaloniaWindow window = null!;
            Control shimmer = null!, text = null!;
            h.Main.OnMain(() =>
            {
                text = Words();
                shimmer = new HydraShimmer().Body(text);
                window = BlitzAbRender.Open(new Border { Padding = new Thickness(24), Child = new Border { HorizontalAlignment = HorizontalAlignment.Left, Child = shimmer } }, theme, backdrop, 360, 70);
            });
            Border Band() => Mounted<Border>(shimmer).Single(border => border.Background is LinearGradientBrush);
            double Offset() => Assert.IsType<TranslateTransform>(Band().RenderTransform).X;
            // :497-498 the band is max(24, width / 3) wide; the sweep runs from -band to width.
            double side = 0, width = 0;
            h.Main.OnMain(() => { width = BlitzAbRender.BoxIn(text, window).Width; side = Band().Width; Assert.Equal(Math.Max(24, width / 3), side, 0.51); });
            // Step on until the band is over the middle of the words (the clock's own origin is not the test's to know).
            double x = 0;
            for (var guard = 0; guard < 80; guard++)
            {
                Advance(h.Main, frames, 30);
                h.Main.OnMain(() => x = Offset());
                if (x + side / 2 > width * 0.45 && x + side / 2 < width * 0.55) break;
            }
            h.Main.OnMain(() =>
            {
                Assert.InRange(x + side / 2, width * 0.4, width * 0.6);
                var frame = BlitzAbRender.Capture(window);
                var box = BlitzAbRender.BoxIn(text, window);
                Assert.Equal(plainBox.Size.Width, box.Width, 0.01);
                var plainMass = Mass(plainFrame!, plainBox, backdrop, ink, 16);
                var mass = Mass(frame, box, backdrop, ink, 16);
                var centre = x + side / 2;
                var step = box.Width / 16;
                for (var bin = 0; bin < 16; bin++)
                {
                    var middle = (bin + 0.5) * step;
                    var ratio = plainMass[bin] <= 0.5 ? 1 : mass[bin] / plainMass[bin];
                    // :507-512 the gradient is clear at both ends and 0.9 of the primary ink at the middle, so the glyphs under the middle are the brightest, and brighter than at the band's shoulders.
                    var distance = Math.Abs(middle - centre);
                    if (distance < side / 6) Assert.True(ratio >= 1.2, $"{theme}: under the band's middle (bin {bin}) the glyphs are {ratio:F3} of their plain ink");
                    else if (distance < side / 2 - step) Assert.True(ratio >= 1.05, $"{theme}: on the band (bin {bin}) the glyphs are {ratio:F3} of their plain ink");
                    if (Math.Abs(middle - centre) > side / 2 + step) Assert.True(Math.Abs(ratio - 1) <= 0.012, $"{theme}: outside the band (bin {bin}) the glyphs are {ratio:F3} of their plain ink");
                }
                // :517 .mask(content): the band paints only where the glyphs are; the inked pixels are the plain text's, not a lit rectangle.
                Assert.Equal(plainBox.TopLeft, box.TopLeft);
                int gaps = 0, lit = 0, inked = 0;
                for (var px = (int)Math.Floor(box.Left); px < (int)Math.Ceiling(box.Right); px++)
                {
                    for (var py = (int)Math.Floor(box.Top); py < (int)Math.Ceiling(box.Bottom); py++)
                    {
                        if (BlitzAbFrame.Distance(plainFrame!.At(px, py), backdrop) > 24) inked++;
                        if (BlitzAbFrame.Distance(plainFrame.At(px, py), backdrop) > 2) continue;
                        gaps++; // a pixel of the plain frame that is bare backdrop, between and around the glyphs
                        if (BlitzAbFrame.Distance(frame.At(px, py), backdrop) > 24) lit++;
                    }
                }
                Assert.True(inked > 150 && gaps > 600);
                Assert.True(lit <= 250, $"{theme}: {lit} of {gaps} backdrop pixels between the glyphs are lit by the band: it is not masked to the text"); // the glyphs' own fringe is about 130; with no mask the band lights about 1000
            });
            // :497 the band is never narrower than 24 points: a short word of about 60 points gets 24, not a third of it.
            h.Main.OnMain(() =>
            {
                var shortText = TimelineRows.ChatText("Merging", weight: SwiftFont.Weight.Medium, color: ChromeTokens.PrimaryText.Opacity(0.9));
                var shortShimmer = new HydraShimmer().Body(shortText);
                var shortWindow = BlitzAbRender.Open(new Border { Padding = new Thickness(24), Child = new Border { HorizontalAlignment = HorizontalAlignment.Left, Child = shortShimmer } }, theme, backdrop, 200, 70);
                try
                {
                    var narrow = BlitzAbRender.BoxIn(shortText, shortWindow).Width;
                    Assert.True(narrow / 3 < 24, $"the short word is {narrow} wide");
                    Assert.Equal(24, Mounted<Border>(shortShimmer).Single(border => border.Background is LinearGradientBrush).Width);
                }
                finally { BlitzAbRender.Shut(shortWindow); }
            });
            // :499-501 the phase is the reference time modulo 1.8 over 1.8: one full sweep later the band is where it was; half a sweep later it has moved by half the run.
            double after = 0, halfWay = 0;
            Advance(h.Main, frames, 900);
            h.Main.OnMain(() => halfWay = Offset());
            Advance(h.Main, frames, 900);
            h.Main.OnMain(() => after = Offset());
            var period = width + side;
            var tolerance = period / 54 * 2 + 1; // the band moves on a thirtieth-of-a-second beat: one step of it is period / 54
            Assert.True(Math.Abs(after - x) <= tolerance || Math.Abs(Math.Abs(after - x) - period) <= tolerance, $"{theme}: after 1.8 s the band is at {after}, it was at {x}");
            var moved = ((halfWay - x) % period + period) % period;
            Assert.True(Math.Abs(moved - period / 2) <= tolerance, $"{theme}: half a sweep moved the band by {moved} of {period}");
            // :495 paused while ScrollActivity reports scrolling: the picture does not change at all; once the reader stops it moves again.
            var scroller = Guid.NewGuid();
            double held = 0;
            h.Main.OnMain(() => { ScrollActivity.Shared.Current.SetScrolling(true, scroller); held = Offset(); });
            Advance(h.Main, frames, 400);
            h.Main.OnMain(() => { Assert.Equal(held, Offset()); ScrollActivity.Shared.Current.SetScrolling(false, scroller); });
            Advance(h.Main, frames, 200);
            h.Main.OnMain(() => Assert.NotEqual(held, Offset()));
            h.Main.OnMain(() => BlitzAbRender.Shut(window));
            // :486 under reduced motion the content is drawn plainly: the same frame as the words with no shimmer.
            h.Main.OnMain(() =>
            {
                genie.AccessibilityDisplayShouldReduceMotion = true;
                var still = Words();
                var stilled = new HydraShimmer().Body(still);
                Assert.Same(still, stilled);
                BlitzAbRender.Render(new Border { Padding = new Thickness(24), Child = new Border { HorizontalAlignment = HorizontalAlignment.Left, Child = stilled } }, theme, backdrop, 360, 70, (win, frame) =>
                    Assert.Equal(0, Mass(frame, BlitzAbRender.BoxIn(still, win), backdrop, ink, 8).Zip(Mass(plainFrame!, plainBox, backdrop, ink, 8), (a, b) => Math.Abs(a - b)).Sum(), 0.5));
                genie.AccessibilityDisplayShouldReduceMotion = false;
            });
        }
    });

    // ---- PR-CHAT-147: the lead's brief to a head ----

    private static (HydraBriefRow Row, TimelineEntry Brief) BriefRow(ThreadRuntimePart12Harness h, string text)
    {
        h.Runtime.RehearseTurn(text);
        var entry = h.Runtime.Entries.Last(candidate => candidate.TimelineEntryKind == TimelineEntry.Kind.User);
        var message = ((TimelineItem.Content.User)entry.Item.TimelineItemContent).Value;
        return (new HydraBriefRow(message, entry, h.Runtime), entry);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-147")] // TimelineRows.swift:537-595; OD-0098: the pill in both states drawn in both themes; it reads as cooking, shimmering, until the head answers
    public void The_brief_pill_is_drawn_cooking_with_a_moving_shimmer_and_then_as_the_brief_with_none_in_both_themes() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        foreach (var (theme, backdrop) in Themes)
        {
            var fill = FillOver(theme, backdrop);
            HydraBriefRow row = null!;
            AvaloniaWindow window = null!;
            h.Main.OnMain(() =>
            {
                row = BriefRow(h, "Build the thing").Row;
                window = BlitzAbRender.Open(new Border { Padding = new Thickness(24), Child = row }, theme, backdrop, 560, 70);
            });
            void CheckPill(string words, bool cooking, string because)
            {
                var frame = BlitzAbRender.Capture(window);
                var pill = PillOf(row);
                var box = BlitzAbRender.BoxIn(pill, window);
                // :589-591 the row is trailing-aligned with 96 points of leading padding, like a user message: the pill ends on the row's trailing edge.
                Assert.Equal(new Thickness(96, 0, 0, 0), row.Padding);
                Assert.Equal(560 - 24, box.Right, 0.5);
                Assert.True(BlitzAbFrame.Distance(frame.At((int)box.Left + 5, (int)(box.Top + box.Height / 2)), fill) <= 6, $"{because}: the pill's fill");
                Assert.True(BlitzAbFrame.Distance(frame.At((int)box.Left - 4, (int)(box.Top + box.Height / 2)), backdrop) <= 6, $"{because}: the pill ends where it measures");
                // :556-586 an 18-point glyph at the leading padding, 8 before the title, the title at callout medium in the primary ink at 0.9, then the chevron.
                var glyph = Assert.Single(Mounted<HydraGlyph>(row));
                Assert.Equal(box.Left + 12, Left(glyph, window), 0.5);
                Assert.True(cooking == glyph.IsRunning, $"{because}: the glyph runs: {glyph.IsRunning}, entries {h.Runtime.Entries.Count}, running {h.Runtime.IsRunning}");
                Assert.True(Texts(row).Contains(words), because + ": the texts are " + string.Join("|", Texts(row)));
                var title = TextNamed(row, words);
                Assert.Equal(Left(glyph, window) + 18 + 8, Left(title, window), 0.5);
                BlitzAbRender.TextIsColour(frame, BlitzAbRender.BoxIn(title, window), fill, ChromeTokens.PrimaryText.Opacity(0.9).Resolve(theme), because, slack: 40, closest: 130);
                Assert.InRange(Peak(frame, BlitzAbRender.BoxIn(title, window), fill, ChromeTokens.PrimaryText.Opacity(0.9).Resolve(theme)), 0.88, 1.12); // the primary ink at 0.9, not 0.75
                var chevron = Assert.Single(Mounted<SwiftImage>(row), image => image.SymbolName == "chevron.right");
                BlitzAbRender.InkIsColour(frame, BlitzAbRender.BoxIn(chevron, window), fill, ChromeTokens.SecondaryText.Resolve(theme), because, most: 1.6);
                Assert.Equal(box.Right, Left(chevron, window) + chevron.Bounds.Width + 14, 0.5);
                Assert.Equal(words, AutomationProperties.GetName(Mounted<Button>(row).Single()));
            }
            // The head has said nothing yet and the thread is running: it reads as cooking, with the thinking shimmer on its title.
            h.Main.OnMain(() => { Settle(window); CheckPill("Hank is starting to cook…", true, $"{theme} cooking"); });
            var cookingWidth = 0.0;
            double TextInk(string words) => BlitzAbRender.Capture(window).InkMass(BlitzAbRender.BoxIn(TextNamed(row, words), window), fill, ChromeTokens.PrimaryText.Opacity(0.9).Resolve(theme));
            double first = 0;
            h.Main.OnMain(() => { cookingWidth = BlitzAbRender.BoxIn(PillOf(row), window).Width; first = TextInk("Hank is starting to cook…"); });
            var moved = false;
            for (var i = 0; i < 12 && !moved; i++)
            {
                Advance(h.Main, frames, 100);
                h.Main.OnMain(() => moved = Math.Abs(TextInk("Hank is starting to cook…") - first) > first * 0.004);
            }
            Assert.True(moved, $"{theme}: the cooking title's ink never changed over 1.2 s: no shimmer");
            // The head answers: another entry of the brief's turn. Straight apostrophe, the glyph at rest, and a title that no longer moves.
            h.Main.OnMain(() => h.Runtime.Rehearse(new ProviderEvent.ToolStarted("tool-" + theme, Call(ToolCall.Kind.Read, "Read a", ToolCall.Status.Running))));
            h.Main.OnMain(() => Settle(window));
            Advance(h.Main, frames, 600);
            double still = 0;
            h.Main.OnMain(() =>
            {
                CheckPill("Hank's brief", false, $"{theme} answered");
                Assert.True(BlitzAbRender.BoxIn(PillOf(row), window).Width < cookingWidth, "the brief's pill is shorter than the cooking one");
                still = TextInk("Hank's brief");
            });
            for (var i = 0; i < 6; i++)
            {
                Advance(h.Main, frames, 100);
                h.Main.OnMain(() => Assert.Equal(still, TextInk("Hank's brief"), 1e-6));
            }
            h.Main.OnMain(() => BlitzAbRender.Shut(window));
        }
    });

    // ---- PR-CHAT-152: the finished heads' pills ----

    private static HydraHeadInfo Head(long index, HydraHeadInfo.Status status) =>
        new HydraHeadInfo(index, "task", HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated) with { HydraHeadInfoStatus = status };

    // A lead with head threads under it and the lead's runtime (the setup of TimelineRowsPrRecordsTests.TeamHarness).
    private static (AppModelHarness Store, AppModel App, ThreadRuntime Runtime) TeamHarness(params HydraHeadInfo[] heads)
    {
        System.Threading.Tasks.Task.Run(() => LoginEnvironment.Load()).GetAwaiter().GetResult();
        var store = new AppModelHarness();
        var project = AppModelSamples.Project("Team", store.NewFolder("team"));
        var lead = AppModelSamples.Thread(project);
        var team = heads.Select(head => AppModelSamples.Thread(project) with { ParentThreadID = lead.Id, Hydra = head }).ToArray();
        store.WriteLibrary([project], [lead, .. team]);
        AppModelHarness.WriteDocument(lead.Id);
        var app = store.MakeModel();
        store.PrepareBootstrap(app);
        var runtime = store.Main.OnMain(() => new ThreadRuntime(lead.Id, app));
        return (store, app, runtime);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-152")] // TimelineRows.swift:398-418, :438-476; OD-0098: heads of differing name lengths drawn from one leading edge, glyphs in one column, in both themes
    public void Finished_heads_of_differing_name_lengths_are_one_pill_each_from_one_leading_edge_with_their_glyphs_in_one_column_in_both_themes() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        var (store, app, runtime) = TeamHarness(Head(0, HydraHeadInfo.Status.Failed), Head(1, HydraHeadInfo.Status.Stopped), Head(2, HydraHeadInfo.Status.Completed),
            Head(11, HydraHeadInfo.Status.Completed), Head(5, HydraHeadInfo.Status.Running));
        using (store)
        {
            foreach (var (theme, backdrop) in Themes)
            {
                var fill = FillOver(theme, backdrop);
                HydraHeadsWorkingRow row = null!;
                AvaloniaWindow window = null!;
                store.Main.OnMain(() =>
                {
                    row = new HydraHeadsWorkingRow(new ValueList<int>([0, 1, 2, 11]), runtime);
                    var host = new Border { Padding = new Thickness(24), Child = row };
                    AppEnvironment.SetAppModel(host, app);
                    window = BlitzAbRender.Open(host, theme, backdrop, 640, 230);
                    row.ApplyTeam(app.Children(runtime.ThreadID));
                });
                Advance(store.Main, frames, 800); // the pills glide in (:402-415 .settleGlide, .smooth(duration: 0.3))
                store.Main.OnMain(() =>
                {
                    var frame = BlitzAbRender.Capture(window);
                    var pills = Mounted<FinishedHeadPill>(row).ToList();
                    // :412-415 one pill per head the caller names (finishedHeads(in:) names the finished ones), in its order; a head still at work is not named, and the floating panel shows it.
                    Assert.True(new[] { "Hank failed", "Walter was stopped", "Ada finished working", "Bo finished working" }.SequenceEqual(pills.Select(pill => AutomationProperties.GetName(pill) ?? "")), "the pills are " + string.Join(" | ", pills.Select(pill => AutomationProperties.GetName(pill))));
                    var boxes = pills.Select(pill => BlitzAbRender.BoxIn(PillOf(pill), window)).ToList();
                    Assert.Equal(new Thickness(0, 0, 96, 0), row.Padding); // :416-417 full width, lead-aligned, 96 points kept clear
                    Assert.True(boxes.Select(box => Math.Round(box.Width)).Distinct().Count() >= 3, $"{theme}: the pills are {string.Join(", ", boxes.Select(box => box.Width))} wide, not of differing widths");
                    for (var i = 0; i < pills.Count; i++)
                    {
                        var box = boxes[i];
                        var because = $"{theme}, {AutomationProperties.GetName(pills[i])}";
                        // :411 one leading edge whatever the width: every pill starts on the row's left, and the glyphs stand in one column (the padding's 12 in).
                        Assert.Equal(24, box.Left, 0.5);
                        Assert.True(box.Right <= 640 - 24 - 96 + 0.5, because);
                        if (i > 0) Assert.Equal(6, box.Top - boxes[i - 1].Bottom, 0.5); // :409 spacing 6
                        Assert.True(BlitzAbFrame.Distance(frame.At((int)box.Left + 5, (int)(box.Top + box.Height / 2)), fill) <= 6, $"{because}: the pill's fill");
                        Assert.True(BlitzAbFrame.Distance(frame.At((int)box.Right + 4, (int)(box.Top + box.Height / 2)), backdrop) <= 6, $"{because}: the pill ends where it measures");
                        var glyph = Assert.Single(Mounted<HydraGlyph>(pills[i]));
                        Assert.Equal(24 + 12, Left(glyph, window), 0.5);
                        // :455-460 the name in the primary ink at 0.75, then what became of the head, at callout medium; the chevron in the secondary ink 14 from the edge.
                        var text = Assert.Single(Mounted<TextBlock>(pills[i]));
                        var textBox = BlitzAbRender.BoxIn(text, window);
                        Assert.Equal(Left(glyph, window) + 18 + 8, textBox.Left, 0.5);
                        var name = ((Avalonia.Controls.Documents.Run)text.Inlines![0]).Text!;
                        var probe = TimelineRows.ChatText(name, weight: SwiftFont.Weight.Medium);
                        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        var nameWidth = probe.DesiredSize.Width;
                        var primary = ChromeTokens.PrimaryText.Resolve(theme);
                        var nameInk = Peak(frame, new Rect(textBox.Left, textBox.Top, nameWidth - 1, textBox.Height), fill, primary);
                        var wordsInk = Peak(frame, new Rect(textBox.Left + nameWidth + 4, textBox.Top, textBox.Width - nameWidth - 5, textBox.Height), fill, primary);
                        Assert.InRange(nameInk, 0.62, 0.85); // :455 the name is the primary ink at 0.75 ...
                        Assert.InRange(wordsInk, 0.88, 1.03); // ... and what became of the head is the label colour whole
                        Assert.InRange(nameInk / wordsInk, 0.64, 0.84);
                        var chevron = Assert.Single(Mounted<SwiftImage>(pills[i]), image => image.SymbolName == "chevron.right");
                        BlitzAbRender.InkIsColour(frame, BlitzAbRender.BoxIn(chevron, window), fill, ChromeTokens.SecondaryText.Resolve(theme), $"{because}: the chevron", most: 1.6);
                        Assert.Equal(box.Right, Left(chevron, window) + chevron.Bounds.Width + 14, 0.5);
                    }
                    BlitzAbRender.Shut(window);
                });
            }
        }
    });

    // ---- PR-CHAT-153: a tool call that sent out a head ----

    // How many pixels of the area are the colour (within the distance), the evidence of a disc of it.
    private static int Pixels(BlitzAbFrame frame, Rect area, Color colour, int within = 40)
    {
        var count = 0;
        for (var x = (int)Math.Floor(area.Left); x < (int)Math.Ceiling(area.Right); x++)
            for (var y = (int)Math.Floor(area.Top); y < (int)Math.Ceiling(area.Bottom); y++)
                if (BlitzAbFrame.Distance(frame.At(x, y), colour) <= within) count++;
        return count;
    }

    [Fact]
    [Trait("Req", "PR-CHAT-153")] // TimelineRows.swift:1766-1820, :1723-1755, HydraGlyph.swift:38-66; OD-0098: running, sent and failed drawn as the head's badge in both themes
    public void A_call_that_sent_out_a_head_is_drawn_as_the_heads_badge_running_sent_and_failed_never_as_a_spinner_in_both_themes() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        foreach (var (theme, backdrop) in Themes)
        {
            var fill = BlitzAbFrame.Over(SwiftColor.Quaternary.Opacity(0.32).Resolve(theme), backdrop); // :1819 .quaternary.opacity(0.32)
            Assert.True(BlitzAbFrame.Distance(fill, backdrop) > 15, "the badge's fill is visible against the backdrop");
            var cases = new (string Title, ToolCall.Status Status, string Words, bool Running, Color? Disc)[]
            {
                ("Pip: the merge popover", ToolCall.Status.Running, "Sending out Pip: the merge popover", true, null),
                ("Pip: the merge popover", ToolCall.Status.Completed, "Sent out Pip: the merge popover", false, ChromeTokens.Success.Resolve(theme)),
                ("Pip: the merge popover", ToolCall.Status.Failed, "Could not send out Pip: the merge popover", false, ChromeTokens.Danger.Resolve(theme)),
                ("Subagent", ToolCall.Status.Completed, "Sent out a head", false, ChromeTokens.Success.Resolve(theme)),
                ("Refactor: the store", ToolCall.Status.Running, "Sending out a head: Refactor: the store", true, null),
            };
            h.Main.OnMain(() =>
            {
                var column = new StackPanel { Spacing = 14 };
                var rows = cases.Select(item => new ToolRow(ToolEntry(Call(ToolCall.Kind.Agent, item.Title, item.Status)), h.Runtime)).ToList();
                foreach (var row in rows) column.Children.Add(row);
                BlitzAbRender.Render(new Border { Padding = new Thickness(24), Child = column }, theme, backdrop, 560, 280, (window, frame) =>
                {
                    for (var i = 0; i < cases.Length; i++)
                    {
                        var (title, status, words, running, disc) = cases[i];
                        var row = rows[i];
                        var because = $"{theme}: {words}";
                        // :1788-1793 the words, in one run after the glyph; :1792 callout medium in the primary ink at 0.9 for a badge.
                        Assert.Empty(Mounted<ToolStatusIcon>(row)); // never a spinner and a tool name (:1768-1770)
                        Assert.Empty(Mounted<ProgressBar>(row));
                        var label = Assert.Single(Mounted<MiddleTruncatingTextBlock>(row));
                        Assert.Equal(words, label.FullText);
                        // :1812-1819 the badge: the pill's insets and the quaternary at 0.32 in a pill-radius shape, starting where the row does.
                        var pill = PillOf(row);
                        var box = BlitzAbRender.BoxIn(pill, window);
                        Assert.Equal(24, box.Left, 0.5);
                        Assert.True(BlitzAbFrame.Distance(frame.At((int)box.Left + 5, (int)(box.Top + box.Height / 2)), fill) <= 6, $"{because}: the badge's fill");
                        Assert.True(BlitzAbFrame.Distance(frame.At((int)box.Right + 4, (int)(box.Top + box.Height / 2)), backdrop) <= 6, $"{because}: the badge ends where it measures");
                        var glyph = Assert.Single(Mounted<HydraGlyph>(row));
                        Assert.Equal(18, glyph.Size);
                        Assert.Equal(running, glyph.IsRunning);
                        var glyphBox = BlitzAbRender.BoxIn(glyph, window);
                        Assert.Equal(box.Left + 12, glyphBox.Left, 0.5);
                        var textBox = BlitzAbRender.BoxIn(label, window);
                        Assert.Equal(glyphBox.Right + 8, textBox.Left, 0.5); // :1783 spacing 8 in a badge
                        Assert.InRange(Peak(frame, textBox, fill, ChromeTokens.PrimaryText.Opacity(0.9).Resolve(theme)), 0.88, 1.05);
                        // HydraGlyph.swift:43-66 the outcome rides the glyph's bottom-right corner as a disc 9 across: green when sent, red when it never went; nothing while it goes.
                        var corner = new Rect(glyphBox.Right - 6, glyphBox.Bottom - 6, 12, 12);
                        foreach (var colour in new[] { ChromeTokens.Success.Resolve(theme), ChromeTokens.Danger.Resolve(theme) })
                        {
                            var found = Pixels(frame, corner, colour);
                            if (disc is { } wanted && wanted == colour) Assert.True(found >= 12, $"{because}: {found} pixels of the outcome's disc {colour} at the glyph's corner");
                            else Assert.True(found <= 2, $"{because}: {found} pixels of {colour} at the glyph's corner, none was due");
                        }
                    }
                });
            });
        }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-153")] // TimelineRows.swift:1766-1793: once the head's thread is linked the badge wears that head's glyph and its own status, the words name the head
    public void A_call_whose_head_is_linked_wears_that_heads_glyph_and_outcome_in_the_badge_in_both_themes() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        var (store, app, runtime) = TeamHarness(Head(3, HydraHeadInfo.Status.Running), Head(4, HydraHeadInfo.Status.Completed), Head(5, HydraHeadInfo.Status.Failed));
        using (store)
        {
            foreach (var (theme, backdrop) in Themes)
            {
                store.Main.OnMain(() =>
                {
                    var heads = app.Threads.Where(thread => thread.IsHydraHead).OrderBy(thread => thread.Hydra!.Index).Select(thread => thread.Id).ToList();
                    app.UpdateThread(heads[0], thread => thread with { Hydra = thread.Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Running } }); // the app's start-up left the unfinished head stopped; it is at work again
                    var cases = new (ToolCall.Status Status, string Words, string Persona, Color? Disc)[]
                    {
                        (ToolCall.Status.Running, "Sending out Otto: Tidy the popover", "Otto", null),
                        (ToolCall.Status.Completed, "Sent out Nova: Tidy the popover", "Nova", ChromeTokens.Success.Resolve(theme)),
                        (ToolCall.Status.Completed, "Sent out Remy: Tidy the popover", "Remy", ChromeTokens.Danger.Resolve(theme)),
                        (ToolCall.Status.Running, "Sending out Nova: Tidy the popover", "Nova", ChromeTokens.Success.Resolve(theme)), // the call still goes but the head is done: no ring, its outcome
                    };
                    var headFor = new[] { 0, 1, 2, 1 };
                    var column = new StackPanel { Spacing = 14 };
                    var rows = new List<ToolRow>();
                    for (var i = 0; i < cases.Length; i++)
                    {
                        var entry = ToolEntry(Call(ToolCall.Kind.Agent, "Tidy the popover", cases[i].Status));
                        runtime.RehearseHydraHead(entry.Id, heads[headFor[i]]);
                        var row = new ToolRow(entry, runtime);
                        rows.Add(row); column.Children.Add(row);
                    }
                    var host = new Border { Padding = new Thickness(24), Child = column };
                    AppEnvironment.SetAppModel(host, app);
                    BlitzAbRender.Render(host, theme, backdrop, 560, 200, (window, frame) =>
                    {
                        var fill = BlitzAbFrame.Over(SwiftColor.Quaternary.Opacity(0.32).Resolve(theme), backdrop);
                        for (var i = 0; i < cases.Length; i++)
                        {
                            var (_, words, persona, disc) = cases[i];
                            var because = $"{theme}: {words}";
                            var row = rows[i];
                            Assert.Equal(words, Assert.Single(Mounted<MiddleTruncatingTextBlock>(row)).FullText);
                            var glyph = Assert.Single(Mounted<HydraGlyph>(row));
                            Assert.Equal(persona, glyph.Persona.Name);
                            Assert.True((i == 0) == glyph.IsRunning, $"{because}: running {glyph.IsRunning}; the heads are " + string.Join(", ", app.Threads.Where(thread => thread.IsHydraHead).Select(thread => thread.Hydra!.Index + ":" + thread.Hydra.HydraHeadInfoStatus))); // :1779 a ring only while both the call and the head are at work
                            var box = BlitzAbRender.BoxIn(PillOf(row), window);
                            Assert.True(BlitzAbFrame.Distance(frame.At((int)box.Left + 5, (int)(box.Top + box.Height / 2)), fill) <= 6, $"{because}: the badge's fill");
                            var glyphBox = BlitzAbRender.BoxIn(glyph, window);
                            var corner = new Rect(glyphBox.Right - 6, glyphBox.Bottom - 6, 12, 12);
                            foreach (var colour in new[] { ChromeTokens.Success.Resolve(theme), ChromeTokens.Danger.Resolve(theme) })
                            {
                                var found = Pixels(frame, corner, colour);
                                if (disc is { } wanted && wanted == colour) Assert.True(found >= 12, $"{because}: {found} pixels of the head's outcome {colour} at the glyph's corner");
                                else Assert.True(found <= 2, $"{because}: {found} pixels of {colour} at the glyph's corner, none was due");
                            }
                        }
                    });
                });
            }
        }
    });

    // ---- PR-CHAT-154: the head-steps popover ----

    private static Control StepsBody(HydraHeadInfo? head, IReadOnlyList<TimelineEntry> steps) =>
        Assert.IsAssignableFrom<Control>(typeof(HydraHeadStepsPopover).GetMethod("Body", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, [head, steps]));

    [Fact]
    [Trait("Req", "PR-CHAT-154")] // TimelineRows.swift:1877-1931, :1934-1952, :1955-2006, :2008-2025; OD-0098: the popover of a finished head with every kind of step, drawn in both themes
    public void The_head_steps_popover_draws_the_header_the_task_every_kind_of_step_and_the_report_in_their_inks_at_440_points_in_both_themes() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var locale = SwiftFormatters.CurrentLocale.Override("en_US");
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        runtime.OnMain(() =>
        {
            foreach (var (theme, backdrop) in Themes)
            {
                var start = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
                var head = TimelineRowsFinTeam.Head(0, HydraHeadInfo.Status.Completed) with { Task = "Tidy the popover", Summary = "the report words", StartedAt = start, FinishedAt = start.AddSeconds(64), ToolCalls = 2 };
                var steps = new TimelineEntry[]
                {
                    ToolEntry(Call(ToolCall.Kind.Read, "Read the popover")),
                    Reply("what the head said"),
                    UserEntry("steer it on", null),
                    NoticeEntry(Notice.Level.Error, "it broke"),
                };
                var body = StepsBody(head, steps);
                BlitzAbRender.Render(new Border { Padding = new Thickness(24), Child = body }, theme, backdrop, 520, 480, (window, frame) =>
                {
                    var primary = ChromeTokens.PrimaryText.Resolve(theme);
                    var box = BlitzAbRender.BoxIn(Mounted<Border>(body).First(border => border.Padding == new Thickness(16)), window); // :1927-1930 .padding(16).frame(width: 440)
                    var because = $"{theme}";
                    // :1927-1930 a 440-point panel padded 16, its sections 16 apart.
                    Assert.Equal(440, box.Width, 0.5);
                    var glyph = Assert.Single(Mounted<HydraGlyph>(body));
                    var glyphBox = BlitzAbRender.BoxIn(glyph, window);
                    Assert.Equal(22, glyphBox.Width, 0.5);
                    Assert.Equal(box.Left + 16, glyphBox.Left, 0.5);
                    Assert.Equal(box.Top + 16, glyphBox.Top, 0.5);
                    // :1934-1952 the header: the name at headline in the primary ink, the status line at caption in the secondary ink on the trailing side.
                    var name = BlitzAbRender.BoxIn(TextNamed(body, "Hank"), window);
                    Assert.Equal(glyphBox.Right + 8, name.Left, 0.5);
                    IsInkedLike(frame, name, backdrop, "Hank", ChatFont.Chat(SwiftFont.TextStyle.Headline), ChromeTokens.PrimaryText, theme, $"{because}: the name");
                    var status = BlitzAbRender.BoxIn(TextNamed(body, "Done · 2 tools · 1m 4s"), window); // :2010-2025, the middle dot U+00B7
                    Assert.Equal(box.Right - 16, status.Right, 1.5);
                    BlitzAbRender.TextIsColour(frame, status, backdrop, ChromeTokens.SecondaryText.Resolve(theme), $"{because}: the status line", slack: 14, closest: 110); // thin caption strokes: no brighter than the secondary ink, and some pixel near it
                    // :1896-1902 the task, :1917-1924 the report: callout in the primary ink, under their sections.
                    var task = BlitzAbRender.BoxIn(Assert.Single(Mounted<SelectableTextBlock>(body), text => text.Text == "Tidy the popover"), window);
                    Assert.Equal(glyphBox.Bottom + 16, task.Top, 1.5);
                    IsInkedLike(frame, task, backdrop, "Tidy the popover", ChatFont.Chat(SwiftFont.TextStyle.Callout), ChromeTokens.PrimaryText, theme, $"{because}: the task");
                    // :2050 the section titles in the secondary ink, the steps 20 apart (:1904) under "Steps".
                    var stepsTitle = BlitzAbRender.BoxIn(TextNamed(body, "Steps"), window);
                    Assert.Equal(16, stepsTitle.Top - task.Bottom, 1.5);
                    BlitzAbRender.TextIsColour(frame, stepsTitle, backdrop, ChromeTokens.SecondaryText.Resolve(theme), $"{because}: the section title", slack: 14, closest: 110);
                    // :1960-1976 a tool step: the icon column 16 wide and 8 before its label at callout in the secondary ink.
                    var tool = BlitzAbRender.BoxIn(TextNamed(body, "Read the popover"), window);
                    Assert.Equal(box.Left + 16 + 16 + 8, tool.Left, 1.5);
                    IsInkedLike(frame, tool, backdrop, "Read the popover", ChatFont.Chat(SwiftFont.TextStyle.Callout), ChromeTokens.SecondaryText, theme, $"{because}: the tool step");
                    // :1978-1990 a reply in the primary ink, a later brief in the secondary, each selectable and 20 below the step before.
                    var reply = BlitzAbRender.BoxIn(Assert.Single(Mounted<SelectableTextBlock>(body), text => text.Text == "what the head said"), window);
                    Assert.Equal(20, reply.Top - tool.Bottom, 1.5);
                    IsInkedLike(frame, reply, backdrop, "what the head said", ChatFont.Chat(SwiftFont.TextStyle.Callout), ChromeTokens.PrimaryText, theme, $"{because}: the reply");
                    var brief = BlitzAbRender.BoxIn(Assert.Single(Mounted<SelectableTextBlock>(body), text => text.Text == "steer it on"), window);
                    Assert.Equal(20, brief.Top - reply.Bottom, 1.5);
                    IsInkedLike(frame, brief, backdrop, "steer it on", ChatFont.Chat(SwiftFont.TextStyle.Callout), ChromeTokens.SecondaryText, theme, $"{because}: the later brief");
                    // :1991-2002, :2050-2064 a notice: the level's glyph and its message in the level's colour (an error is the danger colour).
                    var notice = BlitzAbRender.BoxIn(Assert.Single(Mounted<SelectableTextBlock>(body), text => text.Text == "it broke"), window);
                    Assert.Equal(20, notice.Top - brief.Bottom, 1.5);
                    IsInkedLike(frame, notice, backdrop, "it broke", ChatFont.Chat(SwiftFont.TextStyle.Callout), ChromeTokens.Danger, theme, $"{because}: the notice");
                    var noticeGlyph = BlitzAbRender.BoxIn(Assert.Single(Mounted<SwiftImage>(body), image => image.SymbolName == "exclamationmark.octagon"), window);
                    Assert.Equal(box.Left + 16, noticeGlyph.Left - (16 - noticeGlyph.Width) / 2, 1.5);
                    BlitzAbRender.InkIsColour(frame, noticeGlyph, backdrop, ChromeTokens.Danger.Resolve(theme), $"{because}: the notice's glyph", most: 1.6);
                    // :1917-1924 the report: its own section 16 below the steps, 8 under its title, the text in the primary ink.
                    var reportTitle = BlitzAbRender.BoxIn(TextNamed(body, "Report"), window);
                    Assert.Equal(16, reportTitle.Top - notice.Bottom, 1.5);
                    var report = BlitzAbRender.BoxIn(Assert.Single(Mounted<SelectableTextBlock>(body), text => text.Text == "the report words"), window);
                    Assert.Equal(8, report.Top - reportTitle.Bottom, 1.5);
                    IsInkedLike(frame, report, backdrop, "the report words", ChatFont.Chat(SwiftFont.TextStyle.Callout), ChromeTokens.PrimaryText, theme, $"{because}: the report");
                    // The panel ends 16 under the report.
                    Assert.Equal(box.Bottom - 16, report.Bottom, 1.5);
                });
            }
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-154")] // TimelineRows.swift:1884-1931: the head's own runtime read straight from the model, so the list grows while the head works, to 520 points and then scrolls
    public void While_the_head_works_the_open_popover_grows_with_each_step_it_takes_up_to_520_points_and_then_scrolls() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        foreach (var (theme, backdrop) in Themes)
        {
            var (store, app, lead) = TimelineRowsFinTeam.Make(TimelineRowsFinTeam.Head(0, HydraHeadInfo.Status.Running) with { Task = "Tidy the popover" });
            using (store)
            {
                HydraHeadStepsPopover view = null!;
                ThreadRuntime head = null!;
                AvaloniaWindow window = null!;
                store.Main.OnMain(() =>
                {
                    var id = app.Threads.Single(thread => thread.IsHydraHead).Id;
                    app.UpdateThread(id, thread => thread with { Hydra = thread.Hydra! with { HydraHeadInfoStatus = HydraHeadInfo.Status.Running } });
                    head = app.Runtime(id);
                    view = new HydraHeadStepsPopover(id);
                    var host = new Border { Padding = new Thickness(24), Child = view };
                    AppEnvironment.SetAppModel(host, app);
                    window = BlitzAbRender.Open(host, theme, backdrop, 520, 640);
                });
                double Height() => Mounted<ScrollViewer>(view).Single().Bounds.Height; // the panel: the view itself fills the window it is mounted in
                var heights = new List<double>();
                store.Main.OnMain(() =>
                {
                    Settle(window);
                    Assert.Equal(440, BlitzAbRender.BoxIn(view, window).Width, 0.5);
                    Assert.Contains("No steps yet.", Texts(view));
                    Assert.Contains(Texts(view), text => text.StartsWith("Working", StringComparison.Ordinal)); // :2010-2012 a head at work
                    heights.Add(Height());
                });
                for (var step = 1; step <= 5; step++)
                {
                    var n = step;
                    store.Main.OnMain(() => head.Rehearse(new ProviderEvent.ToolStarted($"{theme}-step-{n}", Call(ToolCall.Kind.Read, $"Read file {n}", ToolCall.Status.Completed))));
                    store.Main.OnMain(() =>
                    {
                        Settle(window);
                        Assert.Contains(Texts(view), text => text.EndsWith($"file {n}", StringComparison.Ordinal));
                        Assert.DoesNotContain("No steps yet.", Texts(view));
                        heights.Add(Height());
                    });
                }
                store.Main.OnMain(() =>
                {
                    // The first step takes the place of the empty line; each next one adds the row spacing of 20 and its own line: the same each time.
                    Assert.True(heights[1] >= heights[0] - 4, $"{theme}: {string.Join(", ", heights)}");
                    var increments = heights.Skip(2).Zip(heights.Skip(1), (after, before) => after - before).ToList();
                    Assert.True(increments.All(increment => increment is >= 28 and <= 60), $"{theme}: the popover is {string.Join(", ", heights)} tall after each step; the scroller {Mounted<ScrollViewer>(view).Single().Height}, its extent {Mounted<ScrollViewer>(view).Single().Extent.Height}");
                    Assert.Equal(increments[0], increments[^1], 1.0);
                    Assert.True(heights[^1] < 520);
                    BlitzAbRender.Capture(window);
                    var box = BlitzAbRender.BoxIn(Mounted<ScrollViewer>(view).Single(), window);
                    var last = BlitzAbRender.BoxIn(Mounted<TextBlock>(view).Single(text => text.Text!.EndsWith("file 5", StringComparison.Ordinal)), window);
                    Assert.True(last.Bottom <= box.Bottom - 16 + 1, $"{theme}: the last step ends at {last.Bottom} in a popover to {box.Bottom}");
                });
                // Many more steps: the panel stops at 520 and scrolls.
                store.Main.OnMain(() => { for (var step = 6; step <= 60; step++) head.Rehearse(new ProviderEvent.ToolStarted($"{theme}-step-{step}", Call(ToolCall.Kind.Read, $"Read file {step}", ToolCall.Status.Completed))); });
                store.Main.OnMain(() =>
                {
                    Settle(window);
                    Assert.Equal(520, Height(), 0.5);
                    var scroller = Assert.Single(Mounted<ScrollViewer>(view));
                    Assert.True(scroller.Extent.Height > 520, $"{theme}: the steps are {scroller.Extent.Height} tall in a 520-point panel");
                    BlitzAbRender.Shut(window);
                });
            }
        }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-154")] // TimelineRows.swift:1856-1871: a row that did not send a head mounts no popover, no state and no anchor at all; the row that did mounts one
    public void A_row_that_did_not_send_a_head_mounts_no_popover_and_one_that_did_hangs_the_steps_from_its_badge() => headless.OnUiThread(() =>
    {
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        var (store, app, runtime) = TimelineRowsFinTeam.Make(TimelineRowsFinTeam.Head(3, HydraHeadInfo.Status.Completed));
        using (store)
        {
            store.Main.OnMain(() =>
            {
                var headID = app.Threads.Single(thread => thread.IsHydraHead).Id;
                var linkedEntry = ToolEntry(Call(ToolCall.Kind.Agent, "Tidy the popover"));
                runtime.RehearseHydraHead(linkedEntry.Id, headID);
                var linked = new ToolRow(linkedEntry, runtime);
                var departing = new ToolRow(ToolEntry(Call(ToolCall.Kind.Agent, "Pip: the merge popover", ToolCall.Status.Running)), runtime);
                var plain = new ToolRow(ToolEntry(Call(ToolCall.Kind.Read, "Read a file")), runtime);
                var column = new StackPanel { Spacing = 14 };
                foreach (var row in new Control[] { linked, departing, plain }) column.Children.Add(row);
                var host = new Border { Padding = new Thickness(24), Child = column };
                AppEnvironment.SetAppModel(host, app);
                var window = BlitzAbRender.Open(host, ThemeVariant.Dark, Color.FromRgb(24, 24, 27), 560, 200);
                try
                {
                    Settle(window);
                    Assert.Single(LogicalQuery.SelfAndDescendants<Avalonia.Controls.Primitives.Popup>(linked)); // the steps popover, hung from the badge
                    Assert.Empty(LogicalQuery.SelfAndDescendants<Avalonia.Controls.Primitives.Popup>(departing)); // a head still going out: nothing to open yet
                    Assert.Empty(LogicalQuery.SelfAndDescendants<Avalonia.Controls.Primitives.Popup>(plain));
                    Assert.Empty(LogicalQuery.SelfAndDescendants<AttachmentAnchorCapture>(departing));
                    Assert.Empty(LogicalQuery.SelfAndDescendants<Button>(departing)); // not tappable: no steps to open
                    Assert.Equal("Show the head's steps", ToolTip.GetTip(Assert.Single(Mounted<Button>(linked))));
                }
                finally { BlitzAbRender.Shut(window); }
            });
        }
    });

    // ---- PR-CHAT-146: the report popover ----

    [Fact]
    [Trait("Req", "PR-CHAT-146")] // TimelineRows.swift:607-639; OD-0098: a long report against the 460 by 460 bounds, a short one at its own size, 12-point markdown in the primary ink, both themes
    public void The_report_popover_of_a_long_report_fills_the_460_by_460_bounds_a_short_one_opens_at_its_final_size_and_the_markdown_reads_at_12_points_in_both_themes() => headless.OnUiThread(() =>
    {
        using var noNetwork = new NoLinkNetwork("docs.example"); // R-TEST-10: the report holds a link
        using var runtime = new TestRuntime();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        runtime.OnMain(() =>
        {
            foreach (var (theme, backdrop) in Themes)
            {
                var primary = ChromeTokens.PrimaryText.Resolve(theme);
                const string linked = "see [the docs](https://docs.example/x) for more words in this line";
                var paragraphs = Enumerable.Range(1, 80).Select(i => "paragraph " + i + " of the long report").ToList();
                paragraphs.Insert(1, linked); // the second paragraph: in view at the top of the scroller
                var longReport = string.Join("\n\n", paragraphs);
                var shortReport = "one short paragraph\n\nand a second";
                var tall = new HydraReportPopover(longReport);
                var shortOne = new HydraReportPopover(shortReport);
                var column = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 24 };
                column.Children.Add(tall); column.Children.Add(shortOne);
                BlitzAbRender.Render(new Border { Padding = new Thickness(24), Child = column }, theme, backdrop, 1060, 560, (window, frame) =>
                {
                    var because = $"{theme}";
                    // :623-626 the width defaults to 460 and a scroller up to 460 high at that width: a long report fills the bounds exactly and scrolls.
                    var box = BlitzAbRender.BoxIn(tall, window);
                    Assert.Equal(460, box.Width, 0.5);
                    var scroller = Assert.Single(Mounted<ScrollViewer>(tall));
                    Assert.Equal(460, BlitzAbRender.BoxIn(scroller, window).Height, 0.5);
                    Assert.True(scroller.Extent.Height > 460 * 4, $"{because}: the long report is {scroller.Extent.Height} tall");
                    // The whole body has been laid out already (every block at once), so the panel opened at its final size: a short report's panel is its body's height.
                    var shortScroller = Assert.Single(Mounted<ScrollViewer>(shortOne));
                    Assert.Equal(shortScroller.Extent.Height, shortScroller.Bounds.Height, 0.5);
                    Assert.InRange(shortScroller.Bounds.Height, 40, 400);
                    // :1927-1930 padded 16 horizontally and 14 vertically, full width, leading: the first line stands 16 in and 14 down.
                    var panel = BlitzAbRender.BoxIn(scroller, window);
                    static string Words(TextBlock text) => text.Text ?? string.Concat((text.Inlines ?? []).OfType<Avalonia.Controls.Documents.Run>().Select(run => run.Text));
                    var first = Mounted<TextBlock>(tall).First(text => Words(text).StartsWith("paragraph 1 ", StringComparison.Ordinal));
                    var firstBox = BlitzAbRender.BoxIn(first, window);
                    Assert.Equal(panel.Left + 16, firstBox.Left, 1.5);
                    Assert.Equal(panel.Top + 14, firstBox.Top, 3.0);
                    // :629-632 the callout text style with 12-point markdown, undimmed: the paragraph is drawn like a 12-point label in the primary ink.
                    var reference = new TextBlock { Text = Words(first), FontSize = 12, FontFamily = first.FontFamily, FontWeight = first.FontWeight };
                    reference.Foreground = ChromeTokens.PrimaryText.Brush(reference);
                    Rect referenceBox = default;
                    BlitzAbFrame? referenceFrame = null;
                    BlitzAbRender.Render(new Border { Padding = new Thickness(8), Child = reference }, theme, backdrop, 480, 60, (win, shot) => { referenceFrame = shot; referenceBox = BlitzAbRender.BoxIn(reference, win); });
                    var drawn = frame.Ink(firstBox, backdrop) ?? throw new Xunit.Sdk.XunitException($"{because}: the paragraph is not drawn");
                    var wanted = referenceFrame!.Ink(referenceBox, backdrop)!.Value;
                    Assert.Equal(wanted.Right - wanted.Left, drawn.Right - drawn.Left, 1.5); // as wide as the same words at 12 points: 13 would be 8 percent wider
                    Assert.Equal(wanted.Bottom - wanted.Top, drawn.Bottom - drawn.Top, 1.5);
                    Assert.InRange(RawPeak(frame, firstBox, backdrop) / RawPeak(referenceFrame, referenceBox, backdrop), 0.9, 1.1); // undimmed: the primary ink whole
                    // :631 environment.markdownPointSize 12 and :632 markdownDimmed false reach a paragraph with a link (a link-free one takes its size from the callout font):
                    // drawn like the same paragraph on its own at 12 points, undimmed, and not like one at 13.
                    var linkText = Mounted<LinkTextView>(tall).First();
                    var linkBox = BlitzAbRender.BoxIn(linkText, window);
                    var controls = new[] { 12.0, 13.0 }.Select(size =>
                    {
                        var control = new InlineText(linked, hasLinks: true);
                        MarkdownView.SetMarkdownPointSize(control, size);
                        return control;
                    }).ToArray();
                    var inks = new List<((int Left, int Top, int Right, int Bottom) Ink, double Peak)>();
                    foreach (var control in controls)
                        BlitzAbRender.Render(new Border { Padding = new Thickness(16), Child = control }, theme, backdrop, 480, 80, (win, shot) =>
                            inks.Add((shot.Ink(BlitzAbRender.BoxIn(Mounted<LinkTextView>(control).First(), win), backdrop)!.Value, RawPeak(shot, BlitzAbRender.BoxIn(Mounted<LinkTextView>(control).First(), win), backdrop))));
                    var linkInk = frame.Ink(linkBox, backdrop)!.Value;
                    var width12 = inks[0].Ink.Right - inks[0].Ink.Left;
                    var width13 = inks[1].Ink.Right - inks[1].Ink.Left;
                    Assert.True(width13 > width12 + 8, $"{because}: 12 and 13 points are {width12} and {width13} wide");
                    Assert.Equal(width12, linkInk.Right - linkInk.Left, 3.0);
                    Assert.InRange(RawPeak(frame, linkBox, backdrop) / inks[0].Peak, 0.9, 1.1);
                });
            }
        });
    });

    // ---- PR-COMPOSER-211: the image a tool call names ----

    private static void WritePng(string path, int width, int height, Func<int, int, Color> pixel) => TestPng.Write(path, width, height, pixel);

    [Fact]
    [Trait("Req", "PR-COMPOSER-211")] // AttachmentPreview.swift:436-438 `.task(id: path) { image = await ThumbnailCache.shared.thumbnail(for: path, pointSize: 900)?.image }`: the thumbnail is asked for at 900 points
    public void The_image_preview_asks_the_thumbnail_cache_for_the_picture_at_900_points() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        var cache = new ThumbnailCache();
        using var cacheSeam = ThumbnailCache.Shared.Override(cache);
        var path = Path.Combine(h.Project.Path, "ask.png");
        WritePng(path, 240, 120, (_, _) => Color.FromRgb(200, 40, 40));
        AvaloniaWindow window = null!;
        ToolImagePreview preview = null!;
        h.Main.OnMain(() =>
        {
            preview = new ToolImagePreview(path);
            window = Show(new Border { Width = 480, Child = preview });
        });
        h.Main.Loop.RunUntil(() => Mounted<Image>(preview).Single().Opacity == 1, AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            try
            {
                // The cache keys a thumbnail by path, pixels (twice the points) and whether it fills a square: 900 points is 1800 pixels, not the 56-point plate's 112.
                var held = (RecentCache<string, ThumbnailCache.Thumbnail>)typeof(ThumbnailCache).GetField("thumbnails", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cache)!;
                Assert.True(held.Contains(path + "#1800#false"), "the preview did not ask for the picture at 900 points");
                Assert.False(held.Contains(path + "#1800#true"));
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-211")] // AttachmentPreview.swift:436 `.task(id: path)`: the load is cancelled when the detail goes, and a picture that lands after that is not drawn
    public void An_image_preview_that_leaves_before_its_picture_has_loaded_draws_nothing_when_it_lands() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        var cache = new ThumbnailCache();
        using var cacheSeam = ThumbnailCache.Shared.Override(cache);
        var staying = Path.Combine(h.Project.Path, "stays.png");
        var leaving = Path.Combine(h.Project.Path, "leaves.png");
        WritePng(staying, 240, 120, (_, _) => Color.FromRgb(40, 190, 90));
        WritePng(leaving, 240, 120, (_, _) => Color.FromRgb(48, 80, 224));
        AvaloniaWindow window = null!;
        ToolImagePreview stays = null!, leaves = null!;
        StackPanel panel = null!;
        h.Main.OnMain(() =>
        {
            stays = new ToolImagePreview(staying);
            leaves = new ToolImagePreview(leaving);
            panel = new StackPanel { Width = 480, Children = { stays, leaves } };
            window = Show(panel);
            // The row collapses (its detail is taken out) before either picture has come back from the decoder.
            panel.Children.Remove(leaves);
            Settle(window);
        });
        // The one that stayed draws its picture; the one that left, cancelled with its detail, is never touched (its task returns before it asks, or drops the answer).
        h.Main.Loop.RunUntil(() => Mounted<Image>(stays).Single().Opacity == 1, AppModelHarness.Bound);
        h.Main.RunUntilIdle();
        h.Main.OnMain(() =>
        {
            try
            {
                Assert.Null(Mounted<Image>(leaves).Single().Source);
                Assert.Equal(0, Mounted<Image>(leaves).Single().Opacity);
                Assert.True(Mounted<Marrow.UI.Port.Nth.NthSpinner>(leaves).Single().IsVisible, "the spinner of a preview that left stays as it was");
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "PR-COMPOSER-211")] // TimelineRows.swift:2117-2119; AttachmentPreview.swift:391-440; OD-0098: a read and a command that wrote a screenshot, loading and after the image lands, in both themes
    public void An_expanded_row_that_names_an_image_shows_a_spinner_in_the_plate_until_the_image_lands_then_the_image_fitted_inside_it_up_to_420_tall_above_its_name_and_Open() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        var cache = new ThumbnailCache();
        using var cacheSeam = ThumbnailCache.Shared.Override(cache);
        var opened = new List<string>();
        var previous = ShellWorkspace.OpenWithDefaultApp;
        ShellWorkspace.OpenWithDefaultApp = opened.Add;
        try
        {
            var red = Color.FromRgb(224, 48, 48); var blue = Color.FromRgb(48, 80, 224); var green = Color.FromRgb(40, 190, 90); var yellow = Color.FromRgb(230, 200, 40);
            var wide = Path.Combine(h.Project.Path, "wide-screenshot.png");
            var tall = Path.Combine(h.Project.Path, "tall.png");
            WritePng(wide, 240, 120, (x, _) => x < 120 ? red : blue);
            WritePng(tall, 100, 400, (_, y) => y < 200 ? green : yellow);
            foreach (var (theme, backdrop) in Themes)
            {
                var plate = BlitzAbFrame.Over(SwiftColor.Quaternary.Opacity(0.45).Resolve(theme), backdrop);
                // A read names the file whole; a command that wrote it names it among its words (PR-CHAT-103): both get the preview.
                var cases = new (string Name, ToolCall Call, string Path, double Height, (double X, double Y, Color Colour)[] Samples)[]
                {
                    ("a read of a wide image", Call(ToolCall.Kind.Read, wide) with { Detail = "image detail" }, wide, 240,
                        [(0.25, 0.5, red), (0.75, 0.5, blue), (0.02, 0.5, red), (0.98, 0.5, blue)]),
                    ("a command that wrote a wide screenshot", Call(ToolCall.Kind.Command, "screencapture " + wide) with { Detail = "image detail" }, wide, 240,
                        [(0.25, 0.5, red), (0.75, 0.5, blue)]),
                    ("a read of a tall image", Call(ToolCall.Kind.Read, tall) with { Detail = "image detail" }, tall, 420,
                        [(0.5, 0.25, green), (0.5, 0.75, yellow), (0.1, 0.5, plate), (0.9, 0.5, plate)]),
                };
                var views = new List<ToolDetailView>();
                AvaloniaWindow window = null!;
                h.Main.OnMain(() =>
                {
                    var column = new StackPanel { Spacing = 24, Width = 480 };
                    foreach (var item in cases) { var view = new ToolDetailView(item.Call, h.Project.Path); views.Add(view); column.Children.Add(view); }
                    window = BlitzAbRender.Open(new Border { Padding = new Thickness(24), Child = column }, theme, backdrop, 560, 1100);
                    Settle(window);
                    // While loading: the small spinner in the plate (:416-417), the image not drawn yet.
                    foreach (var view in views)
                    {
                        var preview = Assert.Single(Mounted<ToolImagePreview>(view));
                        var box = BlitzAbRender.BoxIn(Assert.IsType<Border>(preview.Children[0]), window);
                        var spinner = Assert.Single(Mounted<Marrow.UI.Port.Nth.NthSpinner>(preview)); // OD-0136 amendment 2 (nth step 1): the plate's spinner is the nth spinner
                        Assert.True(spinner.IsVisible);
                        var spinnerBox = BlitzAbRender.BoxIn(spinner, window);
                        Assert.Equal(box.Center.X, spinnerBox.Center.X, 1.0);
                        Assert.Equal(box.Center.Y, spinnerBox.Center.Y, 1.0);
                        Assert.Equal(0, Assert.Single(Mounted<Image>(preview)).Opacity);
                    }
                });
                h.Main.Loop.RunUntil(() => views.All(view => !Mounted<Marrow.UI.Port.Nth.NthSpinner>(view).Single().IsVisible), AppModelHarness.Bound);
                h.Main.OnMain(() =>
                {
                    Settle(window);
                    var frame = BlitzAbRender.Capture(window);
                    for (var i = 0; i < cases.Length; i++)
                    {
                        var (name, call, path, height, samples) = cases[i];
                        var because = $"{theme}: {name}";
                        var preview = Assert.Single(Mounted<ToolImagePreview>(views[i]));
                        var plateBox = BlitzAbRender.BoxIn(Assert.IsType<Border>(preview.Children[0]), window);
                        // :408-421 as wide as the row, as tall as the image fitted to that width, at most 420; the loaded image replaces the spinner.
                        Assert.Equal(480, plateBox.Width, 0.5);
                        Assert.Equal(height, plateBox.Height, 1.0);
                        Assert.False(Assert.Single(Mounted<Marrow.UI.Port.Nth.NthSpinner>(preview)).IsVisible);
                        Assert.Equal(1, Assert.Single(Mounted<Image>(preview)).Opacity);
                        foreach (var (sx, sy, colour) in samples)
                        {
                            var at = frame.At((int)(plateBox.Left + sx * plateBox.Width), (int)(plateBox.Top + sy * plateBox.Height));
                            Assert.True(BlitzAbFrame.Distance(at, colour) <= 18, $"{because}: at ({sx}, {sy}) of the plate the pixel is {at}, expected {colour}");
                        }
                        // :422-433 6 points below, the file's name (caption, secondary, one line) at the leading edge and Open, in the accent, at the trailing edge.
                        var name2 = Assert.Single(Mounted<MiddleTruncatingTextBlock>(preview));
                        var nameBox = BlitzAbRender.BoxIn(name2, window);
                        Assert.Equal(Path.GetFileName(path), name2.FullText);
                        Assert.Equal(plateBox.Left, nameBox.Left, 1.0);
                        Assert.Equal(6, nameBox.Top - plateBox.Bottom, 3.0);
                        var openText = Assert.Single(Mounted<TextBlock>(Assert.Single(Mounted<Button>(preview))));
                        var openBox = BlitzAbRender.BoxIn(openText, window);
                        Assert.Equal(plateBox.Right, openBox.Right, 1.0);
                        IsInkedLike(frame, openBox, backdrop, "Open", SwiftFont.Caption, ChromeTokens.Accent, theme, $"{because}: Open");
                        IsInkedLike(frame, nameBox, backdrop, Path.GetFileName(path), SwiftFont.Caption, ChromeTokens.SecondaryText, theme, $"{because}: the file name");
                        // The detail text sits a row gap (20) below the preview, inside the same column.
                        var detail = Assert.Single(Mounted<LineLimitedSelectableText>(views[i]));
                        Assert.Equal(20, BlitzAbRender.BoxIn(detail, window).Top - BlitzAbRender.BoxIn(preview, window).Bottom, 1.0);
                    }
                    BlitzAbRender.Shut(window);
                });
            }
        }
        finally { ShellWorkspace.OpenWithDefaultApp = previous; }
    });

    // ---- PR-HYDRAMERGE-108: the report parsed as the pill appears ----

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-108")] // TimelineRows.swift:1028-1033 + MarkdownView.swift:135-148, :204-219; ENGINEERING DECISION: first-open latency is shown by the cache's state at the tap, not by a wall clock under a shared load
    public void A_tap_on_an_outcome_pill_whose_report_was_warmed_opens_on_the_blocks_already_parsed_and_a_cold_tap_parses_then() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var nonce = Guid.NewGuid().ToString("N");
        var warmBody = "```\nwarm report " + nonce + "\n```";
        var coldBody = "```\ncold report " + nonce + "\n```";
        var panel = new StackPanel { Spacing = 16 };
        AvaloniaWindow window = null!;
        HydraMergeRow warmRow = null!;
        Button Title(Control row) => Mounted<Button>(row).Single(button => Texts(button).Contains("Held back"));
        h.Main.OnMain(() =>
        {
            _ = AppCurrentEvent.CurrentWindow; // warms the lazy event subscription before Show presses its key
            Assert.Equal([warmBody, coldBody], MarkdownView.WarmTexts([warmBody, coldBody])); // nothing is cached yet
            warmRow = new HydraMergeRow(h.Runtime, new HydraMergePhase.Outcome(new UserMessage { Text = "Held back.\n" + warmBody }));
            panel.Children.Add(warmRow);
            window = Show(panel);
        });
        // The pill appeared and its task parsed the report off the main thread; the main actor has the blocks.
        h.Main.Loop.RunUntil(() => MarkdownView.WarmTexts([warmBody]).Count == 0, AppModelHarness.Bound);
        ValueList<MarkdownBlock> warm = default!;
        h.Main.OnMain(() =>
        {
            Assert.Empty(MarkdownView.WarmTexts([warmBody]));
            warm = MarkdownView.Blocks(warmBody); // a cache hit: the blocks the warm parse made
            Click(window, Title(warmRow));
        });
        h.Main.OnMain(() =>
        {
            Settle(window);
            var shown = presenter.Calls.Last(call => call.Presented);
            var views = LogicalQuery.SelfAndDescendants<MarkdownBlockView>(PopoverHost.Content(shown.Popup)!).ToList();
            Assert.Equal(warm.Count, views.Count);
            // The tap found them: the popover is drawn from those very blocks, not from a parse of its own.
            for (var i = 0; i < warm.Count; i++) Assert.Same(warm[i], views[i].Block);
            Assert.Empty(MarkdownView.WarmTexts([warmBody]));
            Click(window, Title(warmRow)); // closes it again
        });
        // A cold report (nothing warmed it): opening it parses now, on the main actor, and caches what it parsed.
        h.Main.OnMain(() =>
        {
            Assert.Equal([coldBody], MarkdownView.WarmTexts([coldBody]));
            var popover = new HydraReportPopover(coldBody);
            Assert.Empty(MarkdownView.WarmTexts([coldBody]));
            Assert.Same(MarkdownView.Blocks(coldBody)[0], LogicalQuery.SelfAndDescendants<MarkdownBlockView>(popover).First().Block);
        });
        h.Main.OnMain(() => Close(window));
    });
}
