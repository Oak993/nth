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
}
