using Avalonia.Automation;
using Avalonia.Controls.Templates;
using Avalonia.LogicalTree;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Composer;
using Marrow.App.Views.Diff;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Git;
using Marrow.Services.Providers;
using Marrow.Testing;
using Marrow.UI.Markdown;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Xunit;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using AvaloniaWindow = Avalonia.Controls.Window;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// New records this cloud session proved for App.Views.Chat.TimelineRows's PR- scope, to the OVERNIGHT.md mutation
// standard. Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792). Kept in a file of its own so it never
// collides with parallel work on TimelineRowsTests.cs and the other TimelineRowsPr*.cs files.
[Collection("Headless")]
public sealed class TimelineRowsPrRecordsTests(HeadlessSession headless)
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;

    // ---- PR-CHAT-094: ToolPresentation.symbol - a fixed glyph per tool kind (:2222-2233) ----

    [Theory]
    [Trait("Req", "PR-CHAT-094")]
    [InlineData(ToolCall.Kind.Command, "terminal")]
    [InlineData(ToolCall.Kind.Read, "doc.text")]
    [InlineData(ToolCall.Kind.Edit, "pencil")]
    [InlineData(ToolCall.Kind.Search, "magnifyingglass")]
    [InlineData(ToolCall.Kind.Web, "globe")]
    [InlineData(ToolCall.Kind.Mcp, "puzzlepiece.extension")]
    [InlineData(ToolCall.Kind.Agent, "person.2")]
    [InlineData(ToolCall.Kind.Other, "wrench.and.screwdriver")]
    public void Each_tool_kind_has_its_fixed_glyph(ToolCall.Kind kind, string symbol) =>
        Assert.Equal(symbol, ToolPresentation.Symbol(kind));

    // ---- PR-CHAT-099: ToolStatusIcon - a mini spinner while running, a red cross when failed, a raised hand when
    // declined, the kind's (or an override) glyph when completed, all in a caption-size 16-point-wide column (:2067-2092).
    // OD-0136 amendment 2, step 8 (tool call): running is the 14 px spinner of the nth status slot and completed is its tick (a read of an image keeps its photo glyph) ----

    [Fact]
    [Trait("Req", "PR-CHAT-099")]
    public void Running_shows_the_working_status_mark() => headless.OnUiThread(() =>
    {
        var call = new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = "run", ToolCallStatus = ToolCall.Status.Running };
        var icon = new ToolStatusIcon(call);
        var column = Assert.IsType<Border>(icon.Child);
        Assert.Equal(TimelineMetrics.IconWidth, column.Width);
        var mark = Assert.IsType<Marrow.UI.Port.Nth.NthStatusMark>(column.Child);
        Assert.Equal(Marrow.UI.Port.Nth.NthStatus.Working, mark.Status);
        Assert.Equal(Marrow.UI.Port.Nth.NthDoneMark.Tick, mark.DoneMark);
        Assert.Equal(14, mark.MarkSize);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-099")]
    public void Failed_shows_a_red_cross() => headless.OnUiThread(() =>
    {
        var call = new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = "run", ToolCallStatus = ToolCall.Status.Failed };
        var icon = new ToolStatusIcon(call);
        var column = Assert.IsType<Border>(icon.Child);
        var glyph = Assert.IsType<SwiftImage>(column.Child);
        Assert.Equal("xmark.circle.fill", glyph.SymbolName);
        Assert.Equal(ChromeTokens.Danger.Resolve(glyph.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(glyph.Foreground).Color);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-099")]
    public void Declined_shows_a_raised_hand() => headless.OnUiThread(() =>
    {
        var call = new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = "run", ToolCallStatus = ToolCall.Status.Declined };
        var icon = new ToolStatusIcon(call);
        var column = Assert.IsType<Border>(icon.Child);
        var glyph = Assert.IsType<SwiftImage>(column.Child);
        Assert.Equal("hand.raised.slash", glyph.SymbolName);
        Assert.Equal(ChromeTokens.Warning.Resolve(glyph.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(glyph.Foreground).Color);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-099")]
    public void Completed_shows_the_done_tick_unless_an_override_glyph_is_given() => headless.OnUiThread(() =>
    {
        var call = new ToolCall { ToolCallKind = ToolCall.Kind.Read, Title = "read", ToolCallStatus = ToolCall.Status.Completed };
        // The kind's own glyph (:2222-2233, "doc.text" for a read) was the done look; step 8 draws the tick, in place (a row made already done does not replay it).
        var plain = Assert.IsType<Marrow.UI.Port.Nth.NthStatusMark>(((Border)new ToolStatusIcon(call).Child!).Child);
        Assert.Equal(Marrow.UI.Port.Nth.NthStatus.Done, plain.Status);
        Assert.Equal(Marrow.UI.Port.Nth.NthDoneMark.Tick, plain.DoneMark);
        Assert.True(double.IsNegativeInfinity(Assert.IsType<Marrow.UI.Port.Nth.NthStatusState>(plain.Snapshot().State).ChangedAt));

        var overridden = (SwiftImage)((Border)new ToolStatusIcon(call, symbol: "photo").Child!).Child!;
        Assert.Equal("photo", overridden.SymbolName); // :1784 a read that looked at an image uses the photo glyph
    });

    [Theory]
    [Trait("Req", "PR-CHAT-099")]
    [InlineData(ToolCall.Status.Running)]
    [InlineData(ToolCall.Status.Failed)]
    [InlineData(ToolCall.Status.Declined)]
    [InlineData(ToolCall.Status.Completed)]
    public void Every_status_icon_is_at_caption_size_in_a_sixteen_point_column(ToolCall.Status status) => headless.OnUiThread(() =>
    {
        var call = new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = "run", ToolCallStatus = status };
        var column = (Border)new ToolStatusIcon(call).Child!;
        Assert.Equal(16.0, column.Width); // TimelineMetrics.IconWidth, :2090
        Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Caption), column.GetValue(SwiftImage.FontProperty)); // :2089, set on the column
    });

    // ---- PR-CHAT-102: DiffDetailCache - parsed once per distinct diff, cached sixty deep (:2169-2182) ----

    [Fact]
    [Trait("Req", "PR-CHAT-102")]
    public void A_repeated_diff_and_path_returns_the_same_parsed_instance_without_reparsing()
    {
        var diff = "diff --git a/x.txt b/x.txt\n--- a/x.txt\n+++ b/x.txt\n@@ -1 +1 @@\n-old\n+new\n";
        var path = "x-" + Guid.NewGuid().ToString("N") + ".txt"; // a fresh key: no collision with another test's cache entry
        var first = DiffDetailCache.File(diff, path);
        var second = DiffDetailCache.File(diff, path);
        Assert.Same(first, second); // a cache hit; a fresh parse would be an equal but distinct DiffFile record
        var differentPath = "y-" + Guid.NewGuid().ToString("N") + ".txt";
        var third = DiffDetailCache.File(diff, differentPath);
        Assert.NotSame(first, third); // the key is path + "\n" + diff (:2176): a different path is a different entry
        Assert.Equal(first.Hunks, third.Hunks); // same diff text, so the parsed hunks still agree
    }

    [Fact]
    [Trait("Req", "PR-CHAT-102")]
    public void The_cache_limit_is_sixty()
    {
        var cache = typeof(DiffDetailCache).GetField("cache", HiddenStatic)!.GetValue(null)!;
        var limit = (long)cache.GetType().GetField("limit", Hidden)!.GetValue(cache)!;
        Assert.Equal(60L, limit);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-102")] // TimelineRows.swift:2171-2182 RecentCache(limit: 60); Core/Support/RecentCache.swift:43-55 two generations, :22-28 a hit in the previous one is moved back into the current
    public void The_diff_cache_holds_two_generations_of_sixty_so_up_to_120_files_the_121st_rotates_the_oldest_out_and_a_hit_in_the_older_one_is_kept_by_the_next_rotation()
    {
        var cache = (RecentCache<string, DiffFile>)typeof(DiffDetailCache).GetField("cache", HiddenStatic)!.GetValue(null)!;
        int Current() => ((System.Collections.ICollection)cache.GetType().GetField("current", Hidden)!.GetValue(cache)!).Count;
        var salt = Guid.NewGuid().ToString("N");
        string Diff(int index) => "diff --git a/f.txt b/f.txt" + "\n" + "--- a/f.txt" + "\n" + "+++ b/f.txt" + "\n" + "@@ -1 +1 @@" + "\n" + "-old " + salt + " " + index + "\n" + "+new" + "\n";
        string Path(int index) => "f-" + salt + "-" + index + ".txt";
        string Key(int index) => Path(index) + "\n" + Diff(index); // :2176 path + newline + diff
        // The cache is shared by every test, so first walk it to a rotation: the next entry is then the only one in the current generation.
        var filler = 1000;
        var before = Current();
        do { before = Current(); DiffDetailCache.File(Diff(filler), Path(filler)); filler++; } while (Current() >= before);
        Assert.Equal(1, Current());
        var first = filler - 1;
        // 119 more fill the current generation to sixty and roll it over once: the filler and the 119 are the previous generation (60) and the current (60): 120 held.
        for (var index = 1; index <= 119; index++) DiffDetailCache.File(Diff(index), Path(index));
        Assert.True(cache.Contains(Key(first)));
        Assert.All(Enumerable.Range(1, 119), index => Assert.True(cache.Contains(Key(index)), "diff " + index + " is held among the 120"));
        // A hit costs no parse: the same instance comes back (the ones in the current generation, so that asking does not itself rotate it).
        var sixty = DiffDetailCache.File(Diff(60), Path(60));
        Assert.Same(sixty, DiffDetailCache.File(Diff(60), Path(60)));
        // The 121st distinct diff rotates the older generation out: the filler and diffs 1 to 59 go, 60 to 120 stay.
        DiffDetailCache.File(Diff(120), Path(120));
        Assert.False(cache.Contains(Key(first)));
        Assert.All(Enumerable.Range(1, 59), index => Assert.False(cache.Contains(Key(index)), "diff " + index + " is out after the 121st"));
        Assert.All(Enumerable.Range(60, 61), index => Assert.True(cache.Contains(Key(index)), "diff " + index + " is still held"));
        // A hit in the previous generation moves the entry back into the current one (:24-26): asked for, 61 survives the next rotation while 62 does not.
        var sixtyOne = DiffDetailCache.File(Diff(61), Path(61));
        Assert.Same(sixtyOne, DiffDetailCache.File(Diff(61), Path(61)));
        var count = Current();
        var fresh = 200;
        do { count = Current(); DiffDetailCache.File(Diff(fresh), Path(fresh)); fresh++; } while (Current() > count);
        Assert.True(cache.Contains(Key(61)), "a diff asked for again is kept by the rotation that drops the older generation");
        Assert.False(cache.Contains(Key(62)), "a diff nobody asked for again goes with the older generation");
    }

    // ---- PR-CHAT-116: TurnEndRow.label - the three phrasings, over RelativeTime.duration fixtures (:2448-2455).
    // STOP cloud-TimelineRows-pr #15 (PR-CHAT-116): the record itself says "The exact rendered duration strings are those
    // Foundation produces on the reference runtime and are UNRESOLVED until captured there (B3_PLAN.md 6.1 E1)". The
    // expected strings below ("1m 4s", "1h 2m", ...) are the port's own expectations, so they cannot prove the clause; the
    // rest of the record (the three phrasings, the rounding, the clamp at zero, the caption/secondary styling, the row
    // suppressed when a reply exists) does run here (under the local /tmp/icushim; ordinary tests on Windows) and passes,
    // but the record is not claimed: these tests carry Rule R-TEST-4 until the reference-runtime capture exists.
    // 2026-10-03: the reference video now gives "5s", "10s", "28s", "38s", "1m 23s" and "Failed after 1s"
    // (TimelineRowsCaptureTests.The_turn_end_label_reads_as_the_reference_video_shows); 0 s, a negative interval and the
    // hour cases are in no frame, so the record stays open (STOP #11 in TimelineRowsTests). ----
    // SUPERSEDED 2026-10-04 (OD-0098, ED-197): TimelineRowsBlitzAbCardTests proves PR-CHAT-116 from the source and swift-foundation's
    // documented narrow units formatting (ENGINEERING DECISION: the strings of ED-197 / O-65, read from its source), for the frames' strings
    // and for 0, a negative interval, a half second, 60, 3599, 3600 and 3725 seconds.

    [Theory]
    [Trait("Rule", "R-TEST-4")] // WINDOWS-ONLY here: TimelineRows.Duration needs icu.dll for the current locale.
    [InlineData(TurnStatus.Completed, 0.0, "Worked for 0s")]
    [InlineData(TurnStatus.Running, 45.0, "Worked for 45s")]
    [InlineData(TurnStatus.Completed, 64.0, "Worked for 1m 4s")]
    [InlineData(TurnStatus.Completed, 90.0, "Worked for 1m 30s")]
    [InlineData(TurnStatus.Completed, 3599.0, "Worked for 59m 59s")]
    [InlineData(TurnStatus.Completed, 3725.0, "Worked for 1h 2m")]
    [InlineData(TurnStatus.Completed, -5.0, "Worked for 0s")] // max(0, ...) clamps a negative interval before formatting
    [InlineData(TurnStatus.Interrupted, 90.0, "Stopped after 1m 30s")]
    [InlineData(TurnStatus.Failed, 90.0, "Failed after 1m 30s")]
    public void The_turn_end_line_reads_the_right_phrase_and_duration(TurnStatus status, double seconds, string expected)
    {
        var summary = new TurnSummary { TurnID = Guid.NewGuid(), Status = status, Duration = seconds, FilesChanged = 0, Additions = 0, Deletions = 0 };
        Assert.Equal(expected, TurnEndRow.Label(summary));
    }

    [Theory]
    [Trait("Rule", "R-TEST-4")] // WINDOWS-ONLY here: same icu.dll dependency via TimelineRows.Duration.
    [InlineData(TurnStatus.Completed, "Worked for 5s")]
    [InlineData(TurnStatus.Running, "Worked for 5s")]
    [InlineData(TurnStatus.Interrupted, "Stopped after 5s")]
    [InlineData(TurnStatus.Failed, "Failed after 5s")]
    public void The_turn_end_row_renders_its_label_at_caption_size_in_secondary_text(TurnStatus status, string expected)
    {
        var summary = new TurnSummary { TurnID = Guid.NewGuid(), Status = status, Duration = 5, FilesChanged = 0, Additions = 0, Deletions = 0 };
        var row = new TurnEndRow(summary, hasReply: false);
        var text = Assert.IsType<TextBlock>(row.Child);
        Assert.Equal(expected, text.Text);
        Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Caption).PointSize, text.FontSize);
        Assert.Equal(ChromeTokens.SecondaryText.Resolve(text.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // one clause of PR-CHAT-116; the phrase/duration clauses are WINDOWS-ONLY above.
    public void The_turn_end_row_renders_nothing_when_the_turn_has_a_reply()
    {
        var summary = new TurnSummary { TurnID = Guid.NewGuid(), Status = TurnStatus.Completed, Duration = 5, FilesChanged = 0, Additions = 0, Deletions = 0 };
        var row = new TurnEndRow(summary, hasReply: true);
        Assert.Null(row.Child);
    }

    // ---- PR-CHAT-144: leadingURL / withoutFirstLine (:375-387) and the row that uses them (:252-257, :304-311).
    // leadingURL calls SwiftCharacter.IsWhitespace, which needs icu.dll: this container has none, so these tests ran under
    // the local, uncommitted /tmp/icushim (see the commit message); they are ordinary tests on Windows. ----

    [Theory]
    [Trait("Req", "PR-CHAT-144")]
    [InlineData("https://example.com/x\nrest", "https://example.com/x")]
    [InlineData("HTTP://EXAMPLE.COM\nrest", "HTTP://EXAMPLE.COM")] // case-insensitive scheme match
    [InlineData("  https://example.com  \nrest", "https://example.com")] // the first line is trimmed first
    public void A_leading_http_or_https_line_with_no_whitespace_is_the_leading_url(string body, string expected)
    {
        var url = HydraReportRow.LeadingURL(body);
        Assert.NotNull(url);
        Assert.Equal(expected, url!.AbsoluteString);
    }

    [Theory]
    [Trait("Req", "PR-CHAT-144")]
    [InlineData("ftp://example.com\nrest")] // wrong scheme
    [InlineData("not a url\nrest")] // no scheme at all
    [InlineData("https://example.com has a space\nrest")] // whitespace inside the first line
    [InlineData("")] // no first line at all
    [InlineData("\n\nhttps://example.com")] // the URL is not on the first non-empty... it IS the first non-empty line
    public void Anything_else_on_the_first_line_is_no_leading_url(string body)
    {
        // The last case is a control: ComponentsSeparatedByNewlines skips the two leading blank lines, so the
        // first NON-EMPTY line is "https://example.com" and IS a leading URL; assert that positively instead.
        if (body == "\n\nhttps://example.com")
        {
            Assert.NotNull(HydraReportRow.LeadingURL(body));
            return;
        }
        Assert.Null(HydraReportRow.LeadingURL(body));
    }

    [Theory]
    [Trait("Req", "PR-CHAT-144")]
    [InlineData("https://example.com/x\nsecond\nthird", "second\nthird")]
    [InlineData("https://example.com/x\n  second  ", "second")] // Trimmed() strips the remainder's outer whitespace
    [InlineData("https://example.com/x", "")] // no newline at all: the empty string
    [InlineData("https://example.com/x\n", "")]
    public void Without_first_line_is_everything_after_the_first_newline_trimmed(string body, string expected) =>
        Assert.Equal(expected, HydraReportRow.WithoutFirstLine(body));

    // PR-CHAT-144, row level (:252-257, :304-311): the address becomes an Open link BESIDE the pill's button, and the chevron
    // and popover cover only what the note says after that line.

    private static HydraReportRow NoteRow(string text) => new(new UserMessage { Text = text });

    [Fact]
    [Trait("Req", "PR-CHAT-144")]
    public void A_note_led_by_an_address_gets_an_Open_link_beside_the_button_and_keeps_its_chevron_for_the_rest() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        h.Main.OnMain(() =>
        {
            var row = NoteRow("Merge request opened.\nhttps://example.com/pr/1\nThe details after the address.");
            var window = Show(row);
            try
            {
                var buttons = row.GetVisualDescendants().OfType<Button>().ToList();
                var open = Assert.Single(buttons, button => Texts(button).Contains("Open"));
                var report = Assert.Single(buttons, button => !ReferenceEquals(button, open));
                Assert.DoesNotContain(open, report.GetVisualDescendants()); // a sibling control, never nested in the button
                Assert.DoesNotContain(report, open.GetVisualDescendants());
                var pill = Assert.IsType<HStack>(open.GetVisualParent());
                Assert.Equal(8, pill.Spacing);
                Assert.Equal(2, pill.Children.Count);
                Assert.Contains(report, pill.Children[0].GetVisualDescendants().Append(pill.Children[0])); // the button first, the link after it
                Assert.Same(open, pill.Children[1]);
                Assert.Contains("chevron.right", Symbols(report));
                Assert.True(report.IsEnabled);
                Assert.Equal("Show the message", ToolTip.GetTip(report));
                var label = Assert.Single(open.GetVisualDescendants().OfType<TextBlock>());
                Assert.Equal("Open", label.Text);
                Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Caption, SwiftFont.Weight.Semibold).Resolve().FontSize, label.FontSize);
                Assert.Equal(Avalonia.Media.FontWeight.SemiBold, label.FontWeight);
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, label), BrushColor(label.Foreground));
                Assert.Equal("Open in the browser", ToolTip.GetTip(open));
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-144")]
    public void Clicking_Open_opens_the_address_in_the_default_browser() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        string? opened = null;
        using var browser = Marrow.Services.Port.Workspace.Open.Override(url => { opened = url.AbsoluteString; return true; });
        h.Main.OnMain(() =>
        {
            var row = NoteRow("Merge request opened.\nhttps://example.com/pr/1\nThe details after the address.");
            var window = Show(row);
            try
            {
                var open = Assert.Single(row.GetVisualDescendants().OfType<Button>(), button => Texts(button).Contains("Open"));
                Click(window, open);
                Assert.Equal("https://example.com/pr/1", opened);
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-144")]
    public void A_note_that_is_nothing_but_an_address_has_no_popover_and_an_address_with_trailing_text_is_no_link() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        h.Main.OnMain(() =>
        {
            var bare = NoteRow("Merge request opened.\nhttps://example.com/pr/1");
            var trailing = NoteRow("Merge request opened.\nhttps://example.com/pr/1 is ready for review");
            var panel = new StackPanel();
            panel.Children.Add(bare);
            panel.Children.Add(trailing);
            var window = Show(panel);
            try
            {
                var bareButton = Assert.Single(bare.GetVisualDescendants().OfType<Button>(), button => Texts(button).Contains("Open") == false);
                Assert.Single(bare.GetVisualDescendants().OfType<Button>(), button => Texts(button).Contains("Open")); // the link is still there
                Assert.False(bareButton.IsEnabled); // nothing after the address: nothing to open
                Assert.DoesNotContain("chevron.right", Symbols(bareButton));
                // a first line that is an address WITH trailing text is not treated as a link
                var trailingButton = Assert.Single(trailing.GetVisualDescendants().OfType<Button>());
                Assert.DoesNotContain("Open", Texts(trailingButton));
                Assert.True(trailingButton.IsEnabled);
                Assert.Contains("chevron.right", Symbols(trailingButton));
            }
            finally { Close(window); }
        });
    });

    // ---- PR-CHAT-089: assistant messageActions - Copy last code block only when a fenced block exists; Add to
    // reply de-duplicates by exact text and captures the runtime weakly (:1392-1412) ----

    [Fact]
    [Trait("Req", "PR-CHAT-089")]
    public void Copy_last_code_block_is_offered_only_when_the_text_holds_a_fenced_block()
    {
        using var h = TimelineRowsPrSupport.Harness();
        var withCode = AssistantMessageRow.MessageActions("prose\n```\ncode\n```\nmore", "code", h.Runtime);
        Assert.Equal(["Copy message", "Copy as markdown", "Copy last code block", "Add to reply"], withCode.Select(item => item.Title));

        var withoutCode = AssistantMessageRow.MessageActions("prose only", null, h.Runtime);
        Assert.Equal(["Copy message", "Copy as markdown", "Add to reply"], withoutCode.Select(item => item.Title));
    }

    [Fact]
    [Trait("Req", "PR-CHAT-089")]
    public void Add_to_reply_appends_a_quote_unless_the_exact_text_is_already_quoted()
    {
        using var h = TimelineRowsPrSupport.Harness();
        var items = AssistantMessageRow.MessageActions("some reply text", null, h.Runtime);
        var addToReply = items.Single(item => item.Title == "Add to reply");
        Assert.Equal("arrow.turn.down.left", addToReply.Symbol);
        Assert.True(addToReply.StartsGroup);
        Assert.Equal("Added", addToReply.Confirms);

        h.Main.OnMain(addToReply.Action);
        Assert.Equal(["some reply text"], h.Runtime.Draft.Quotes.Select(quote => quote.Text));

        h.Main.OnMain(addToReply.Action); // the exact same text again: no duplicate (:1408)
        Assert.Equal(["some reply text"], h.Runtime.Draft.Quotes.Select(quote => quote.Text));

        var different = AssistantMessageRow.MessageActions("a different reply", null, h.Runtime);
        h.Main.OnMain(different.Single(item => item.Title == "Add to reply").Action);
        Assert.Equal(["some reply text", "a different reply"], h.Runtime.Draft.Quotes.Select(quote => quote.Text));
    }

    [Fact]
    [Trait("Req", "PR-CHAT-089")]
    public void Add_to_reply_captures_the_runtime_through_a_weak_reference_field_not_a_strong_one()
    {
        using var h = TimelineRowsPrSupport.Harness();
        var action = AssistantMessageRow.MessageActions("text", null, h.Runtime).Single(item => item.Title == "Add to reply").Action;
        // :1406 `[weak runtime]`: the compiler-generated closure must hold a WeakReference<ThreadRuntime> field, never
        // a ThreadRuntime field directly, or the row's whole runtime graph would outlive the popup that built the menu.
        var closureFields = action.Target!.GetType().GetFields(Hidden | BindingFlags.Public);
        Assert.Contains(closureFields, field => field.FieldType == typeof(WeakReference<ThreadRuntime>));
        Assert.DoesNotContain(closureFields, field => field.FieldType == typeof(ThreadRuntime));

        // The weak capture still resolves and works while the runtime is alive.
        h.Main.OnMain(action);
        Assert.Equal(["text"], h.Runtime.Draft.Quotes.Select(quote => quote.Text));
    }

    // ---- PR-CHAT-078: ReplyQuote.peel/excerpt (ThreadRuntime.swift:58-99, cited windows_path: transcribed line for
    // line) plus a capture check of the rendered quote chip (TimelineRows.swift:92-117) ----

    [Fact]
    [Trait("Req", "PR-CHAT-078")]
    public void A_message_with_no_leading_quote_comes_back_whole_with_no_quotes()
    {
        var (quotes, body) = ReplyQuote.Peel("just a plain message\nsecond line");
        Assert.Empty(quotes);
        Assert.Equal("just a plain message\nsecond line", body);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-078")]
    public void A_single_quote_block_is_lifted_out_with_its_marker_and_one_space_stripped()
    {
        var (quotes, body) = ReplyQuote.Peel("> quoted line one\n> quoted line two\n\nthe actual words");
        var quote = Assert.Single(quotes);
        Assert.Equal("quoted line one\nquoted line two", quote.Text);
        Assert.Equal("the actual words", body);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-078")]
    public void Two_quote_blocks_separated_by_one_blank_line_both_lift_out()
    {
        var (quotes, body) = ReplyQuote.Peel(">first\n\n>second\n\nwords");
        Assert.Equal(["first", "second"], quotes.Select(quote => quote.Text));
        Assert.Equal("words", body);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-078")]
    public void A_leading_slash_command_stays_with_the_words_after_the_quotes()
    {
        var (quotes, body) = ReplyQuote.Peel("/compact\n\n> quoted\n\nwords after");
        var quote = Assert.Single(quotes);
        Assert.Equal("quoted", quote.Text);
        Assert.Equal("/compact words after", body);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-078")]
    public void A_leading_slash_command_with_no_words_after_the_quotes_comes_back_alone()
    {
        var (quotes, body) = ReplyQuote.Peel("/compact\n\n> quoted\n\n");
        Assert.Single(quotes);
        Assert.Equal("/compact", body);
    }

    // Excerpt's word split calls SwiftCharacter.IsWhitespace, which needs icu.dll; these ran under the local, uncommitted
    // /tmp/icushim (see the commit message) and are ordinary tests on Windows.

    [Theory]
    [Trait("Req", "PR-CHAT-078")]
    [InlineData("one line", "one line")]
    [InlineData("  leading and trailing  \nsecond", "leading and trailing")] // the first non-empty line, trimmed
    [InlineData("\n\nfirst real line", "first real line")] // blank lines before the first real one are skipped
    [InlineData("word1    word2\tword3\nsecond", "word1 word2 word3")] // internal whitespace runs collapse to one space
    public void The_excerpt_is_the_first_nonempty_line_whitespace_collapsed(string text, string expected) =>
        Assert.Equal(expected, new ReplyQuote { Text = text }.Excerpt);

    [Fact]
    [Trait("Req", "PR-CHAT-078")]
    public void The_excerpt_cuts_at_72_characters_with_a_trailing_ellipsis()
    {
        var exact72 = new string('a', 72);
        Assert.Equal(exact72, new ReplyQuote { Text = exact72 }.Excerpt);
        var over = new string('a', 73);
        Assert.Equal(new string('a', 72) + "…", new ReplyQuote { Text = over }.Excerpt);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-078")]
    public void The_quote_chip_shows_the_excerpt_glyph_tooltip_and_automation_name() => headless.OnUiThread(() =>
    {
        var rowType = typeof(UserMessageRow);
        var method = rowType.GetMethod("QuoteChip", HiddenStatic)!;
        var quote = new ReplyQuote { Text = "  quoted   words  here  " };
        var chip = (Control)method.Invoke(null, [quote])!;
        var window = Show(chip);
        try
        {

        // :110 .modifier(ChipChrome()): a 9/7/5 padded capsule filled with Chrome.overlay(0.12) (ComposerView.cs:1619-1628).
        var capsule = Assert.IsType<Border>(chip);
        Assert.Equal(new Avalonia.Thickness(9, 5, 7, 5), capsule.Padding);
        Assert.Equal(ChromeTokens.Overlay(0.12).Resolve(capsule.ActualThemeVariant),
            Assert.IsAssignableFrom<ISolidColorBrush>(capsule.Background).Color);
        Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(capsule)); // :110 Capsule(style: .continuous)
        Assert.True(capsule.Bounds.Height > 0);
        Assert.Equal(new Avalonia.CornerRadius(Math.Min(capsule.Bounds.Width, capsule.Bounds.Height) / 2), capsule.CornerRadius); // a capsule rounds by half its shorter side

        var stack = Assert.IsType<HStack>(capsule.Child);
        Assert.Equal(Orientation.Horizontal, stack.Orientation);
        Assert.Equal(5, stack.Spacing); // :98 HStack(spacing: 5)
        Assert.Equal(2, stack.Children.Count);

        var glyph = Assert.IsType<SwiftImage>(stack.Children[0]);
        Assert.Equal("text.quote", glyph.SymbolName);
        Assert.Equal(SwiftFont.System(SwiftFont.TextStyle.Footnote, weight: SwiftFont.Weight.Semibold), glyph.GetValue(SwiftImage.FontProperty));
        Assert.Equal(ChromeTokens.SecondaryText.Resolve(glyph.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(glyph.Foreground).Color);

        var excerpt = Assert.IsType<TextBlock>(stack.Children[1]);
        Assert.Equal("quoted words here", excerpt.Text); // whitespace-collapsed excerpt, not the raw quote text
        Assert.Equal(1, excerpt.MaxLines);
        Assert.Equal(TextTrimming.CharacterEllipsis, excerpt.TextTrimming);
        Assert.Equal(260, excerpt.MaxWidth);
        Assert.Equal(SwiftFont.Subheadline.PointSize, excerpt.FontSize);
        Assert.Equal(ChromeTokens.PrimaryText.Opacity(0.9).Resolve(excerpt.ActualThemeVariant),
            Assert.IsAssignableFrom<ISolidColorBrush>(excerpt.Foreground).Color);

        Assert.Equal(quote.Text, ToolTip.GetTip(capsule)); // :111 .help(quote.text): the full, uncollapsed quote
        Assert.Equal("Quote: " + quote.Excerpt, Avalonia.Automation.AutomationProperties.GetName(capsule));
        }
        finally { Close(window); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-078")]
    public void A_quote_chip_keeps_its_ideal_size_even_when_offered_less_room_than_its_text_needs() => headless.OnUiThread(() =>
    {
        var chip = (Control)typeof(UserMessageRow).GetMethod("QuoteChip", HiddenStatic)!.Invoke(null, [new ReplyQuote { Text = "quoted words that need well over forty points" }])!;
        var stack = Assert.IsType<HStack>(Assert.IsType<Border>(chip).Child);
        chip.Measure(new Size(40, 200)); // :109 .fixedSize(): the chip is measured at its ideal width, not squeezed into the offer
        Assert.True(stack.Children[1].DesiredSize.Width > 100, "the excerpt kept its ideal width: " + stack.Children[1].DesiredSize.Width);
    });

    [Fact]
    [Trait("Req", "PR-CHAT-078")]
    public void A_message_led_by_quotes_shows_them_as_chips_above_a_bubble_that_keeps_only_the_typed_words() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        h.Main.OnMain(() =>
        {
            var row = new UserMessageRow(UserEntry("> first quote\n\n> second quote\n\nthe typed words", Guid.NewGuid()), h.Runtime, canRevert: false);
            var window = Show(row);
            try
            {
                var bubble = Assert.Single(row.GetVisualDescendants().OfType<InlineText>());
                Assert.Equal("the typed words", bubble.Source);
                var chips = row.GetVisualDescendants().OfType<Border>().Where(border => (Avalonia.Automation.AutomationProperties.GetName(border) ?? "").StartsWith("Quote: ", StringComparison.Ordinal)).ToList();
                Assert.Equal(["Quote: first quote", "Quote: second quote"], chips.Select(chip => Avalonia.Automation.AutomationProperties.GetName(chip)));
                Assert.Equal(["first quote", "second quote"], chips.Select(chip => ToolTip.GetTip(chip)));
                var strip = Assert.IsType<StackPanel>(chips[0].GetVisualParent());
                Assert.Equal(4, strip.Spacing); // :96 the quote strip's spacing
                Assert.Equal(HorizontalAlignment.Right, strip.HorizontalAlignment);
                Assert.Equal(new Thickness(0, 0, UserBubble.Tail, 0), strip.Margin); // :116 .padding(.trailing, UserBubble.tail)
                var column = Assert.IsType<StackPanel>(strip.GetVisualParent());
                Assert.True(column.Children.IndexOf(strip) < column.Children.IndexOf(column.Children.First(child => child.GetVisualDescendants().Append(child).Contains(bubble)))); // above the bubble
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-078")]
    public void Quote_lines_that_are_not_at_the_start_show_no_chips_and_stay_in_the_bubble() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        h.Main.OnMain(() =>
        {
            const string message = "look at this\n> not a leading quote";
            var row = new UserMessageRow(UserEntry(message, Guid.NewGuid()), h.Runtime, canRevert: false);
            var window = Show(row);
            try
            {
                Assert.DoesNotContain("text.quote", Symbols(row));
                Assert.Equal(message, Assert.Single(row.GetVisualDescendants().OfType<InlineText>()).Source);
            }
            finally { Close(window); }
        });
    });

    // ---- PR-CHAT-020: TimelineMetrics - eight named constants, never a hard-coded literal at a call site (:8-28).
    // verification_method asks for "capture comparison plus a static check that no row hard-codes these numbers".
    // The constant values themselves are a real, mutation-provable clause of expected_result (below). The "static
    // check" half is not: every one of the eight values (8, 12, 14, 16, 18, 20, 22) is an extremely common integer
    // in this file for reasons that have nothing to do with these constants (font point sizes, unrelated local
    // spacing, unrelated named constants like HeadSize/RestingSize/CornerRadius on other types), so a text-level
    // literal scan produced dozens of false positives when tried (HeadSize = 12, a 16x16 unrelated glyph frame, a
    // font point size of 12, and so on) with no reliable way from source text alone to tell a genuine hard-coded
    // TimelineMetrics duplicate from an unrelated coincidence. Filed as STOP cloud-TimelineRows-pr #13: the "capture
    // comparison" half (rendering the actual eight 96-point/four 820-point sites and asserting their geometry
    // reads from these constants, per the record's own windows_path) is undone in this session too. Not claiming
    // this record.
    // SUPERSEDED 2026-10-04 (OD-0098, unit blitz-timelinerows-ab): TimelineRowsBlitzAbBlockTests claims PR-CHAT-020: the pills and cards
    // of the conversation mounted and measured, the seven 96-point sites of the Swift (six in TimelineRows.swift, one in ChatBadge.swift:77;
    // the record's "eight" is one too many, and its four 820-point sites are ThreadTimeline's and SubagentPanel's, not this file's) laid out in
    // a column, and a targeted static check (the literals that would duplicate a constant), without the false positives a scan of every 8, 12, 14, 16,
    // 18, 20 and 22 gave.

    [Fact]
    [Trait("Rule", "R-TEST-4")] // one clause of PR-CHAT-020 (expected_result's exact values); see STOP #13 above.
    public void The_eight_shared_geometry_constants_have_the_source_values()
    {
        Assert.Equal(20.0, TimelineMetrics.RowSpacing);
        Assert.Equal(22.0, TimelineMetrics.HoverLineHeight);
        Assert.Equal(16.0, TimelineMetrics.IconWidth);
        Assert.Equal(8.0, TimelineMetrics.IconSpacing);
        Assert.Equal(12.0, TimelineMetrics.PillLeading);
        Assert.Equal(14.0, TimelineMetrics.PillTrailing);
        Assert.Equal(8.0, TimelineMetrics.PillVertical);
        Assert.Equal(18.0, TimelineMetrics.PillRadius);
    }

    // ---- PR-GIT-151: the turn file card is shown only when the turn's summary has files, and its title is
    // singular at one file (:2688-2700, :2739-2740) ----

    [Theory]
    [Trait("Req", "PR-GIT-151")]
    [InlineData(1, "Edited 1 file")]
    [InlineData(2, "Edited 2 files")]
    [InlineData(5, "Edited 5 files")]
    public void The_file_card_title_is_singular_at_exactly_one_file(int fileCount, string expected) => headless.OnUiThread(() =>
    {
        var changes = new FileChangeSummary(new ValueList<FileChangeSummary.File>(
            Enumerable.Range(0, fileCount).Select(i => new FileChangeSummary.File($"f{i}.txt", 1, 0, false))), fileCount, 0);
        var card = new TurnFileCard(changes, canUndo: false, onRevert: () => { }, onReview: _ => { });
        var window = Show(card);
        var title = card.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text!.StartsWith("Edited ", StringComparison.Ordinal));
        Assert.Equal(expected, title.Text);
        Close(window);
    });

    // TurnFinishedBlock's constructor always builds Header(), which reads TurnEndRow.Label -> TimelineRows.Duration -> icu.dll:
    // on this container these ran under the local, uncommitted /tmp/icushim; on Windows they are ordinary tests.
    [Fact]
    [Trait("Req", "PR-GIT-151")]
    public void The_file_card_is_shown_only_when_the_turns_summary_has_files() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var turn = Guid.NewGuid();
        var withFile = Summary(turn) with { Changes = new FileChangeSummary(new ValueList<FileChangeSummary.File>([new FileChangeSummary.File("f.txt", 1, 0, false)]), 1, 0) };
        var noFiles = Summary(turn) with { Changes = new FileChangeSummary(ValueList<FileChangeSummary.File>.Empty, 0, 0) };
        var noChanges = Summary(turn);

        TurnFinishedBlock BlockFor(TurnSummary summary) => new(h.Runtime, turn, summary, ValueList<TimelineEntry>.Empty,
            new ValueList<TimelineEntry>([Reply("an answer so the turn shows a body")]), null, false, false);

        foreach (var (summary, cards) in new[] { (withFile, 1), (noFiles, 0), (noChanges, 0) })
        {
            var block = BlockFor(summary);
            var window = Show(block);
            try { Assert.Equal(cards, block.GetVisualDescendants().OfType<TurnFileCard>().Count()); }
            finally { Close(window); }
        }
    });

    // :2707-2714 the one-off parse for a turn recorded before per-file summaries existed. The runtime's diff cache is seeded
    // through its private CacheDiff, so ParsedDiff returns known files without a repository; a case that must NOT parse is
    // told apart from one that does by whether the seeded files show up as a card.

    private static ValueList<DiffFile> Files151(params string[] paths) =>
        new(paths.Select(path => new DiffFile(path, DiffFile.Change.Modified, false, ValueList<DiffHunk>.Empty, reportedAdditions: 3, reportedDeletions: 1)));

    private static (Guid Turn, TurnSummary Summary) FinishedTurn151(ThreadRuntimePart12Harness h)
    {
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            h.Runtime.RehearseTurn("prompt");
            h.Runtime.Rehearse(new ProviderEvent.TurnCompleted(TurnStatus.Failed, null));
        });
        h.Main.Loop.RunUntil(() => { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); return h.Runtime.Phase == RuntimePhase.Idle; }, AppModelHarness.Bound);
        return h.Main.OnMain(() =>
        {
            var end = h.Runtime.Entries.Single(entry => entry.TimelineEntryKind == TimelineEntry.Kind.TurnEnd);
            var summary = Assert.IsType<TimelineItem.Content.TurnEnd>(end.Item.TimelineItemContent).Value with { Changes = null };
            // a turn recorded before per-file summaries existed: its stored record carries no changes
            end.Item = end.Item with { TimelineItemContent = new TimelineItem.Content.TurnEnd(summary) };
            return (summary.TurnID, summary);
        });
    }

    private static FileChangeSummary? StoredChanges151(ThreadRuntimePart12Harness h) =>
        h.Main.OnMain(() => Assert.IsType<TimelineItem.Content.TurnEnd>(h.Runtime.Entries.Single(entry => entry.TimelineEntryKind == TimelineEntry.Kind.TurnEnd).Item.TimelineItemContent).Value.Changes);

    [Theory]
    [Trait("Req", "PR-GIT-151")]
    [InlineData(2, false, true)]   // the turn reports two changed files, no per-file summary: parse
    [InlineData(0, true, true)]    // no file count but the turn has tool edits: parse
    [InlineData(0, false, false)]  // nothing changed, no edits: never parse
    public void An_old_turn_without_a_change_summary_is_parsed_once_and_the_result_is_stored_back_on_the_runtime(int filesChanged, bool hasEdits, bool parses) => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var (turn, summary) = FinishedTurn151(h);
        var parsed = Files151("src/a.swift", "src/b.swift");
        h.Main.OnMain(() => Invoke(h.Runtime, "CacheDiff", Task.FromResult(parsed), (Guid?)turn));
        TurnFinishedBlock? block = null;
        AvaloniaWindow? window = null;
        h.Main.OnMain(() =>
        {
            var content = new List<TimelineEntry> { Reply("an answer") };
            if (hasEdits) content.Insert(0, Tool(edits: new ValueList<FileEdit>([new FileEdit("src/a.swift", "@@ -1 +1 @@\n-a\n+b\n", 1, 1)])));
            block = Block104(h.Runtime, turn, content, summary: summary with { FilesChanged = filesChanged, Changes = null });
            window = Show(block);
        });
        try
        {
            if (parses)
            {
                h.Main.Loop.RunUntil(() => { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); return block!.GetVisualDescendants().OfType<TurnFileCard>().Any(); }, AppModelHarness.Bound);
                h.Main.OnMain(() =>
                {
                    Assert.Contains("Edited 2 files", Texts(Assert.Single(block!.GetVisualDescendants().OfType<TurnFileCard>())));
                });
                Assert.Equal(FileChangeSummaryDiff.Create(parsed), StoredChanges151(h)); // written back so the work is not repeated
            }
            else
            {
                h.Main.Loop.RunUntilIdle();
                h.Main.OnMain(() => Settle(window!));
                h.Main.OnMain(() => Assert.Empty(block!.GetVisualDescendants().OfType<TurnFileCard>()));
                Assert.Null(StoredChanges151(h));
            }
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    [Fact]
    [Trait("Req", "PR-GIT-151")]
    public void A_turn_that_already_has_a_change_summary_is_not_parsed_and_a_parse_that_yields_nothing_leaves_the_card_absent() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var (turn, summary) = FinishedTurn151(h);
        h.Main.OnMain(() => Invoke(h.Runtime, "CacheDiff", Task.FromResult(Files151("src/x.swift", "src/y.swift", "src/z.swift")), (Guid?)turn));
        TurnFinishedBlock? own = null;
        AvaloniaWindow? window = null;
        h.Main.OnMain(() =>
        {
            var ownChanges = new FileChangeSummary(new ValueList<FileChangeSummary.File>([new FileChangeSummary.File("only.txt", 1, 0, false)]), 1, 0);
            own = Block104(h.Runtime, turn, [Reply("an answer")], summary: summary with { FilesChanged = 5, Changes = ownChanges });
            window = Show(own);
        });
        try
        {
            h.Main.Loop.RunUntilIdle();
            h.Main.OnMain(() =>
            {
                Settle(window!);
                Assert.Contains("Edited 1 file", Texts(Assert.Single(own!.GetVisualDescendants().OfType<TurnFileCard>()))); // its own record, not the seeded three
            });
            Assert.Null(StoredChanges151(h)); // nothing was parsed, so nothing was stored
        }
        finally { h.Main.OnMain(() => Close(window!)); }

        // a parse that finds no files leaves the card absent
        var (turn2, summary2) = (turn, summary);
        using var h2 = Harness();
        using var frames2 = FrameClock.Source.Override(new ManualFrameSource());
        var (emptyTurn, emptySummary) = FinishedTurn151(h2);
        h2.Main.OnMain(() => Invoke(h2.Runtime, "CacheDiff", Task.FromResult(Files151()), (Guid?)emptyTurn));
        TurnFinishedBlock? empty = null;
        AvaloniaWindow? emptyWindow = null;
        h2.Main.OnMain(() =>
        {
            empty = Block104(h2.Runtime, emptyTurn, [Reply("an answer")], summary: emptySummary with { FilesChanged = 2, Changes = null });
            emptyWindow = Show(empty);
        });
        try
        {
            h2.Main.Loop.RunUntilIdle();
            h2.Main.OnMain(() => { Settle(emptyWindow!); Assert.Empty(empty!.GetVisualDescendants().OfType<TurnFileCard>()); });
        }
        finally { h2.Main.OnMain(() => Close(emptyWindow!)); }
    });

    [Fact]
    [Trait("Req", "PR-GIT-151")]
    public void The_parse_is_keyed_on_the_turn_and_a_cancelled_parse_writes_nothing() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var (turn, summary) = FinishedTurn151(h);
        var gate = new TaskCompletionSource<ValueList<DiffFile>>();
        h.Main.OnMain(() => Invoke(h.Runtime, "CacheDiff", gate.Task, (Guid?)turn));
        TurnFinishedBlock? block = null;
        AvaloniaWindow? window = null;
        h.Main.OnMain(() =>
        {
            block = Block104(h.Runtime, turn, [Reply("an answer")], summary: summary with { FilesChanged = 2, Changes = null });
            window = Show(block);
        });
        h.Main.Loop.RunUntilIdle();
        h.Main.OnMain(() => Close(window!)); // the row goes away while its parse is still running
        gate.SetResult(Files151("src/a.swift", "src/b.swift"));
        h.Main.Loop.RunUntilIdle();
        h.Main.OnMain(() => Avalonia.Threading.Dispatcher.UIThread.RunJobs());
        Assert.Null(StoredChanges151(h));
    });

    // ---- PR-CHAT-108, -109, -110: the turn's file card, its files popover and its revert confirmation (:2735-2858) ----

    private static FileChangeSummary Changes108(int count) =>
        new(new ValueList<FileChangeSummary.File>(Enumerable.Range(1, count).Select(i => new FileChangeSummary.File("src/file" + i + ".swift", i, 1, false))), 40, 7);

    private static string[] LogicalTexts(Control control) => LogicalQuery.SelfAndDescendants<TextBlock>(control).Select(block => block.Text ?? "").ToArray();

    private static void Press(Button button) => button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    [Fact]
    [Trait("Req", "PR-CHAT-108")] // TimelineRows.swift:2735-2827; the card's corner at 40 pt is Apple's at the largest radius that fits (OD-0102, ContinuousCornerReproofTests)
    public void The_file_card_reads_Edited_1_file_or_Edited_N_files_beside_the_badge_and_the_totals_with_a_chevron() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            var one = new TurnFileCard(Changes108(1), canUndo: false, onRevert: () => { }, onReview: _ => { });
            var three = new TurnFileCard(Changes108(3), canUndo: false, onRevert: () => { }, onReview: _ => { });
            var panel = new StackPanel();
            panel.Children.Add(one);
            panel.Children.Add(three);
            var window = Show(panel);
            try
            {
                Assert.Contains("Edited 1 file", Texts(one));
                var title = TextNamed(three, "Edited 3 files");
                Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Callout, SwiftFont.Weight.Medium).Resolve().FontSize, title.FontSize);
                Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Callout, SwiftFont.Weight.Medium).Resolve().FontWeight, title.FontWeight);
                Assert.Equal(Resolved(ChromeTokens.PrimaryText.Opacity(0.9), title), BrushColor(title.Foreground));
                var badge = three.GetVisualDescendants().OfType<SwiftImage>().Single(image => image.Width == 18 && image.Height == 18);
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, badge), BrushColor(badge.Foreground));
                var stat = Assert.Single(three.GetVisualDescendants().OfType<DiffStatLabel>());
                Assert.Equal(40, stat.Additions);
                Assert.Equal(7, stat.Deletions);
                var titleButton = three.GetVisualDescendants().OfType<Button>().First();
                Assert.True(titleButton.IsEnabled);
                Assert.Equal("Show the files this turn changed", ToolTip.GetTip(titleButton));
                var chevron = titleButton.GetVisualDescendants().OfType<SwiftImage>().Single(image => image.SymbolName == "chevron.right");
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, chevron), BrushColor(chevron.Foreground));
                Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Caption2, SwiftFont.Weight.Semibold), chevron.GetValue(SwiftImage.FontProperty));
                Assert.Equal("Edited 3 files", Avalonia.Automation.AutomationProperties.GetName(three));
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-108")] // TimelineRows.swift:2735-2827; the card's corner at 40 pt is Apple's at the largest radius that fits (OD-0102, ContinuousCornerReproofTests)
    public void A_card_with_no_files_has_a_disabled_title_and_no_chevron() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            var card = new TurnFileCard(new FileChangeSummary(ValueList<FileChangeSummary.File>.Empty, 0, 0), canUndo: false, onRevert: () => { }, onReview: _ => { });
            var window = Show(card);
            try
            {
                Assert.Contains("Edited 0 files", Texts(card));
                Assert.False(card.GetVisualDescendants().OfType<Button>().First().IsEnabled);
                Assert.DoesNotContain("chevron.right", Symbols(card));
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-108")] // TimelineRows.swift:2735-2827; the card's corner at 40 pt is Apple's at the largest radius that fits (OD-0102, ContinuousCornerReproofTests)
    public void The_round_controls_follow_a_20_point_gap_revert_only_when_undoable_on_a_hugging_quaternary_pill() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            var undoable = new TurnFileCard(Changes108(2), canUndo: true, onRevert: () => { }, onReview: _ => { });
            var fixedTurn = new TurnFileCard(Changes108(2), canUndo: false, onRevert: () => { }, onReview: _ => { });
            var panel = new StackPanel();
            panel.Children.Add(undoable);
            panel.Children.Add(fixedTurn);
            var window = Show(panel);
            try
            {
                Assert.Equal(["arrow.uturn.backward", "plusminus"], undoable.GetVisualDescendants().OfType<MutedRoundGlyph>().Select(glyph => Symbols(glyph).Single()));
                Assert.Equal(["plusminus"], fixedTurn.GetVisualDescendants().OfType<MutedRoundGlyph>().Select(glyph => Symbols(glyph).Single()));
                var buttons = undoable.GetVisualDescendants().OfType<Button>().ToList();
                var revert = buttons.Single(button => button.GetVisualDescendants().OfType<MutedRoundGlyph>().Any(glyph => Symbols(glyph).Contains("arrow.uturn.backward")));
                var review = buttons.Single(button => button.GetVisualDescendants().OfType<MutedRoundGlyph>().Any(glyph => Symbols(glyph).Contains("plusminus")));
                Assert.Equal("Revert this turn's files and conversation", ToolTip.GetTip(revert));
                Assert.Equal("Show this turn's changes", ToolTip.GetTip(review));
                Assert.False(revert.Focusable);
                Assert.False(review.Focusable);
                // :2768 a fixed 20 by 1 gap, not a spacer, right after the title.
                var row = Assert.IsType<StackPanel>(buttons[0].GetVisualParent() is StackPanel ? buttons[0].GetVisualParent() : buttons[0].GetVisualParent()!.GetVisualParent());
                Assert.Equal(8, row.Spacing);
                Assert.Equal(20, row.Children[1].Width);
                Assert.Equal(1, row.Children[1].Height);
                // :2818-2822 padded 12 leading, 6 trailing and 6 vertical on .quaternary at 0.32, hugging its content.
                var pill = undoable.GetVisualDescendants().OfType<Border>().First(border => border.Padding == new Thickness(TimelineMetrics.PillLeading, 6, 6, 6));
                Assert.Equal(new CornerRadius(TimelineMetrics.PillRadius), SwiftShapes.StatedCornerRadius(pill));
                // :2822 `.continuous`: the card is 40 pt tall, in the band (13.1 < r < 20) of ED-275 (2), which OD-0102 (owner, 2026-10-04) settles as Apple's corner at the
                // largest radius that fits, 40 / (2 * 1.528665) = 13.08 (control points and clip: ContinuousCornerReproofTests, the Edited-files card).
                Assert.True(ContinuousCornerChecks.Drawn(pill, expectApple: true));
                Assert.Equal(Resolved(SwiftColor.Quaternary.Opacity(0.32), pill), BrushColor(pill.Background));
                Assert.Equal(HorizontalAlignment.Left, pill.HorizontalAlignment);
                // both popovers open below their buttons (arrowEdge: .bottom)
                Assert.All(undoable.GetVisualDescendants().OfType<Popup>().Where(popup => popup.PlacementTarget is not null), popup => Assert.Equal(PlacementMode.Bottom, popup.Placement));
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-108")] // TimelineRows.swift:2735-2827; the card's corner at 40 pt is Apple's at the largest radius that fits (OD-0102, ContinuousCornerReproofTests)
    public void A_muted_round_glyph_is_a_28_point_overlay_disc_whose_glyph_and_fill_come_up_under_the_pointer() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        MutedRoundGlyph? glyph = null;
        AvaloniaWindow? window = null;
        h.Main.OnMain(() =>
        {
            glyph = new MutedRoundGlyph("plusminus");
            var host = new StackPanel { Margin = new Thickness(20) };
            host.Children.Add(glyph);
            window = Show(host);
            var disc = glyph.GetVisualDescendants().OfType<Border>().Single(border => border.Width == 28 && border.Height == 28);
            Assert.Equal(new CornerRadius(14), disc.CornerRadius);
            var image = glyph!.GetVisualDescendants().OfType<SwiftImage>().Single();
            Assert.Equal("plusminus", image.SymbolName);
            Assert.Equal(ChromeTokens.IconFont, image.GetValue(SwiftImage.FontProperty));
            Assert.Equal(Resolved(ChromeTokens.SecondaryText, image), BrushColor(image.Foreground));
            Assert.Equal(Resolved(ChromeTokens.Overlay(0.1), disc), BrushColor(disc.Background));
            Hover(window, disc);
        });
        try
        {
            // :2475 withAnimation(Chrome.hover), `.easeOut(duration: 0.1)`: the glyph and the fill are sampled on the frame clock at 20, 50 and 100 ms
            // and sit where the ease-out Bezier (0, 0, 0.58, 1) puts them (35, 68 and 100 per cent), not where a linear or slower fade would
            // (a 0.2 s linear animation is at 10, 25 and 50 per cent at the same instants).
            void Sample(int ms, double fraction)
            {
                h.Main.OnMain(() =>
                {
                    var disc = glyph!.GetVisualDescendants().OfType<Border>().Single(border => border.Width == 28 && border.Height == 28);
                    var image = glyph!.GetVisualDescendants().OfType<SwiftImage>().Single();
                    var expected = HoverEaseOut(fraction);
                    var fill = Along(Resolved(ChromeTokens.Overlay(0.1), disc), Resolved(ChromeTokens.Overlay(0.18), disc), BrushColor(disc.Background));
                    var glyphShare = Along(Resolved(ChromeTokens.SecondaryText, image), Resolved(ChromeTokens.PrimaryText, image), BrushColor(image.Foreground));
                    Assert.True(Math.Abs(fill - expected) <= 0.06, $"at {ms} ms the fill is {fill:F3} of the way to the hovered fill; the 0.1 s ease-out has it at {expected:F3}");
                    Assert.True(Math.Abs(glyphShare - expected) <= 0.06, $"at {ms} ms the glyph is {glyphShare:F3} of the way to primary; the 0.1 s ease-out has it at {expected:F3}");
                });
            }
            h.Main.OnMain(() =>
            {
                // Hovering cuts nothing: in the frame the pointer arrives both are still at rest.
                var disc = glyph!.GetVisualDescendants().OfType<Border>().Single(border => border.Width == 28 && border.Height == 28);
                var image = glyph!.GetVisualDescendants().OfType<SwiftImage>().Single();
                Assert.Equal(Resolved(ChromeTokens.Overlay(0.1), disc), BrushColor(disc.Background));
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, image), BrushColor(image.Foreground));
            });
            frames.Advance(TimeSpan.Zero);
            h.Main.AdvanceMilliseconds(20); frames.Advance(TimeSpan.FromMilliseconds(20)); Sample(20, 0.2);
            h.Main.AdvanceMilliseconds(30); frames.Advance(TimeSpan.FromMilliseconds(30)); Sample(50, 0.5);
            h.Main.AdvanceMilliseconds(50); frames.Advance(TimeSpan.FromMilliseconds(50)); Sample(100, 1.0);
            h.Main.AdvanceMilliseconds(900); frames.Advance(TimeSpan.FromMilliseconds(900));
            h.Main.OnMain(() =>
            {
                var disc = glyph!.GetVisualDescendants().OfType<Border>().Single(border => border.Width == 28 && border.Height == 28);
                var image = glyph!.GetVisualDescendants().OfType<SwiftImage>().Single();
                Assert.Equal(Resolved(ChromeTokens.PrimaryText, image), BrushColor(image.Foreground));
                Assert.Equal(Resolved(ChromeTokens.Overlay(0.18), disc), BrushColor(disc.Background));
                // The pointer leaves: the same curve back (:2475 withAnimation on the hover change, either way).
                window!.MouseMove(new Point(2, 2), RawInputModifiers.None);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            });
            frames.Advance(TimeSpan.Zero);
            h.Main.AdvanceMilliseconds(50); frames.Advance(TimeSpan.FromMilliseconds(50));
            h.Main.OnMain(() =>
            {
                var disc = glyph!.GetVisualDescendants().OfType<Border>().Single(border => border.Width == 28 && border.Height == 28);
                var back = 1 - Along(Resolved(ChromeTokens.Overlay(0.1), disc), Resolved(ChromeTokens.Overlay(0.18), disc), BrushColor(disc.Background));
                Assert.True(Math.Abs(back - HoverEaseOut(0.5)) <= 0.06, $"50 ms after the pointer left the fill has come {back:F3} of the way back; the ease-out has it at {HoverEaseOut(0.5):F3}");
            });
            h.Main.AdvanceMilliseconds(200); frames.Advance(TimeSpan.FromMilliseconds(200));
            h.Main.OnMain(() =>
            {
                var disc = glyph!.GetVisualDescendants().OfType<Border>().Single(border => border.Width == 28 && border.Height == 28);
                var image = glyph!.GetVisualDescendants().OfType<SwiftImage>().Single();
                Assert.Equal(Resolved(ChromeTokens.Overlay(0.1), disc), BrushColor(disc.Background));
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, image), BrushColor(image.Foreground));
            });
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-108")] // TimelineRows.swift:2735-2827; the card's corner at 40 pt is Apple's at the largest radius that fits (OD-0102, ContinuousCornerReproofTests)
    public void The_review_control_hands_its_own_anchor_to_the_review_once_the_pointer_has_been_there_and_does_nothing_before() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var reviewed = new List<Control>();
        TurnFileCard? card = null;
        AvaloniaWindow? window = null;
        h.Main.OnMain(() =>
        {
            card = new TurnFileCard(Changes108(2), canUndo: false, onRevert: () => { }, onReview: reviewed.Add);
            var host = new StackPanel { Margin = new Thickness(20) };
            host.Children.Add(card);
            window = Show(host);
            var review = card.GetVisualDescendants().OfType<Button>().Last();
            Click(window, review);
            Assert.Empty(reviewed); // a click with no hover first does nothing
            Assert.Empty(card.GetVisualDescendants().OfType<AttachmentAnchorCapture>());
            Hover(window, review);
        });
        try
        {
            h.Main.OnMain(() =>
            {
                Settle(window!);
                var anchor = Assert.Single(card!.GetVisualDescendants().OfType<AttachmentAnchorCapture>());
                Click(window!, card!.GetVisualDescendants().OfType<Button>().Last());
                Assert.Same(anchor, Assert.Single(reviewed)); // the changes open on the button itself
            });
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-109")]
    public void The_files_popover_lists_twelve_files_then_and_1_more_file_or_and_N_more_files() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        h.Main.OnMain(() =>
        {
            string[] Lines(int count) =>
                Texts(Assert.IsAssignableFrom<Control>(Invoke(new TurnFileCard(Changes108(count), canUndo: false, onRevert: () => { }, onReview: _ => { }), "FilesPopover")));
            var twelve = Lines(12);
            Assert.Equal(12, twelve.Count(text => text.StartsWith("src/file", StringComparison.Ordinal)));
            Assert.DoesNotContain(twelve, text => text.StartsWith("and ", StringComparison.Ordinal));
            var thirteen = Lines(13);
            Assert.Equal(12, thirteen.Count(text => text.StartsWith("src/file", StringComparison.Ordinal)));
            Assert.DoesNotContain("src/file13.swift", thirteen);
            Assert.Contains("and 1 more file", thirteen);
            Assert.Contains("and 8 more files", Lines(20)); // the total minus twelve

            var binary = new TurnFileCard(
                new FileChangeSummary(new ValueList<FileChangeSummary.File>([new("logo.png", 0, 0, true), new("a.swift", 3, 2, false)]), 3, 2),
                canUndo: false, onRevert: () => { }, onReview: _ => { });
            var popover = Assert.IsAssignableFrom<Control>(Invoke(binary, "FilesPopover"));
            var word = TextNamed(popover, "Binary");
            Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Caption).Resolve().FontSize, word.FontSize);
            Assert.Equal(Resolved(ChromeTokens.SecondaryText, word), BrushColor(word.Foreground));
            var path = LogicalQuery.SelfAndDescendants<TextBlock>(popover).Single(block => block.Text == "logo.png");
            Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Callout).Resolve().FontSize, path.FontSize);
            Assert.Equal(1, path.MaxLines);
            var stat = Assert.Single(LogicalQuery.SelfAndDescendants<DiffStatLabel>(popover));
            Assert.Equal(3, stat.Additions);
            Assert.Equal(2, stat.Deletions);
            var rows = Assert.IsType<StackPanel>(popover);
            Assert.Equal(0, rows.Spacing);
            Assert.All(rows.Children, control => Assert.Equal(new Thickness(12, 7), control.Margin)); // :2847-2848 padded 12 horizontally and 7 vertically
            var more = Assert.IsAssignableFrom<Control>(Invoke(new TurnFileCard(Changes108(14), canUndo: false, onRevert: () => { }, onReview: _ => { }), "FilesPopover"));
            var overflow = TextNamed(more, "and 2 more files");
            Assert.Equal(Resolved(ChromeTokens.SecondaryText, overflow), BrushColor(overflow.Foreground));
            Assert.Equal(new Thickness(12, 7), overflow.Margin); // the overflow line is padded like the file rows
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-110")]
    public void The_revert_disc_asks_in_a_300_point_popover_where_only_Revert_reverts() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var reverts = 0;
        TurnFileCard? card = null;
        AvaloniaWindow? window = null;
        Button? cancelButton = null, goButton = null;
        h.Main.OnMain(() =>
        {
            card = new TurnFileCard(Changes108(2), canUndo: true, onRevert: () => reverts++, onReview: _ => { });
            window = Show(card);
            Click(window, card.GetVisualDescendants().OfType<Button>().Single(button => ToolTip.GetTip(button) as string == "Revert this turn's files and conversation"));
        });
        try
        {
            h.Main.OnMain(() =>
            {
                Settle(window!);
                Assert.Single(presenter.Calls, call => call.Presented);
                var confirm = Assert.IsAssignableFrom<Control>(Invoke(card!, "RevertConfirmation"));
                var sheet = LogicalQuery.SelfAndDescendants<Control>(confirm).First(control => control.Width == 300);
                var heading = TextNamed(confirm, "Revert up to this point?");
                Assert.Equal(SwiftFont.System(14, SwiftFont.Weight.Semibold).Resolve().FontSize, heading.FontSize);
                Assert.Equal(SwiftFont.System(14, SwiftFont.Weight.Semibold).Resolve().FontWeight, heading.FontWeight);
                var message = TextNamed(confirm, "Files go back to how they were before this turn, and the turn leaves the conversation.");
                Assert.Equal(SwiftFont.System(SwiftFont.TextStyle.Callout).Resolve().FontSize, message.FontSize);
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, message), BrushColor(message.Foreground));
                Assert.Equal(TextWrapping.Wrap, message.TextWrapping);
                Assert.Contains(LogicalQuery.SelfAndDescendants<Decorator>(sheet), decorator => decorator.Padding == new Thickness(16));
                var cancel = LogicalQuery.SelfAndDescendants<Button>(confirm).Single(button => LogicalTexts(button).Contains("Cancel"));
                var go = LogicalQuery.SelfAndDescendants<Button>(confirm).Single(button => LogicalTexts(button).Contains("Revert"));
                Assert.True(go.IsDefault);
                Assert.False(cancel.IsDefault);
                cancelButton = cancel;
                goButton = go;
                Press(cancel);
            });
            h.Main.Loop.RunUntilIdle();
            h.Main.OnMain(() =>
            {
                Assert.False(presenter.Calls[^1].Presented); // cancelling closes the popover ...
                Assert.Equal(0, reverts); // ... and does nothing
                Press(goButton!);
                Assert.Equal(1, reverts);
            });
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-110")]
    public void Confirming_from_a_turns_card_reverts_with_files_restored() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            h.Runtime.RehearseTurn("prompt");
            h.Runtime.Rehearse(new ProviderEvent.TurnCompleted(TurnStatus.Failed, null));
        });
        h.Main.Loop.RunUntil(() => { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); return h.Runtime.Phase == RuntimePhase.Idle; }, AppModelHarness.Bound);
        AvaloniaWindow? window = null;
        TurnFinishedBlock? block = null;
        h.Main.OnMain(() =>
        {
            var summary = Assert.IsType<TimelineItem.Content.TurnEnd>(h.Runtime.Entries.Single(entry => entry.TimelineEntryKind == TimelineEntry.Kind.TurnEnd).Item.TimelineItemContent).Value;
            block = Block104(h.Runtime, summary.TurnID, [Reply("an answer")], canUndo: true, summary: summary with { Changes = Changes108(2) });
            window = Show(block);
            Assert.Single(h.Runtime.Turns);
        });
        try
        {
            h.Main.OnMain(() =>
            {
                var card = Assert.Single(block!.GetVisualDescendants().OfType<TurnFileCard>());
                var go = LogicalQuery.SelfAndDescendants<Button>(Assert.IsAssignableFrom<Control>(Invoke(card, "RevertConfirmation"))).Single(button => LogicalTexts(button).Contains("Revert"));
                Press(go);
            });
            h.Main.Loop.RunUntilIdle();
            // No checkpoint: restoring files must refuse, so the turn stays; a keep-the-files revert would have trimmed it.
            h.Main.OnMain(() => Assert.Single(h.Runtime.Turns));
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    // ---- PR-CHAT-112, -113: the plan's popover (:2279-2313, :2287) ----

    private static Control PlanDetail(PlanCard card, TimelineEntry entry) =>
        Assert.IsAssignableFrom<Control>(Invoke(card, "Detail", ((TimelineItem.Content.Plan)entry.Item.TimelineItemContent).Value));

    [Fact]
    [Trait("Req", "PR-CHAT-112")]
    public void The_plan_popover_is_520_wide_with_the_markdown_in_a_420_point_scroller_and_the_two_buttons_only_while_proposed() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            var proposed = PlanEntry(ProposedPlan.State.Proposed);
            var detail = PlanDetail(new PlanCard(proposed, h.Runtime), proposed);
            var window = Show(detail);
            try
            {
                var sheet = LogicalQuery.SelfAndDescendants<Control>(detail).First(control => control.Width == 520);
                Assert.Contains(LogicalQuery.SelfAndDescendants<Decorator>(sheet), decorator => decorator.Padding == new Thickness(16));
                var heading = TextNamed(detail, "Plan");
                Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Headline, SwiftFont.Weight.Semibold).Resolve().FontSize, heading.FontSize);
                Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Headline, SwiftFont.Weight.Semibold).Resolve().FontWeight, heading.FontWeight);
                Assert.Single(LogicalQuery.SelfAndDescendants<CopyButton>((Control)heading.Parent!)); // the header's one copy button
                var column = Assert.IsType<StackPanel>(((Control)heading.Parent!).Parent); // the header row's stack: heading + scroller + buttons
                Assert.Equal(12, column.Spacing);
                var scroll = Assert.Single(LogicalQuery.SelfAndDescendants<PopoverScroll>(detail));
                var markdown = Assert.Single(LogicalQuery.SelfAndDescendants<MarkdownView>(scroll));
                Assert.Equal("```\nstep one\n```", markdown.Text);
                Assert.Equal(new Thickness(16, 0), markdown.Margin);
                Assert.True(Assert.IsType<ScrollViewer>(scroll.Child).Height <= 420);
                Assert.Equal(["Implement plan", "Dismiss"], LogicalQuery.SelfAndDescendants<Button>(detail).Select(button => LogicalTexts(button).FirstOrDefault(text => text.Length != 0)).Where(text => text is "Implement plan" or "Dismiss"));
            }
            finally { Close(window); }

            var accepted = PlanEntry(ProposedPlan.State.Accepted);
            var done = PlanDetail(new PlanCard(accepted, h.Runtime), accepted);
            Assert.DoesNotContain("Implement plan", LogicalTexts(done));
            Assert.DoesNotContain("Dismiss", LogicalTexts(done));
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-112")]
    public void The_scroller_holds_a_tall_plan_to_420_points() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        Control? detail = null;
        AvaloniaWindow? window = null;
        h.Main.OnMain(() =>
        {
            var tall = PlanEntry(ProposedPlan.State.Proposed, markdown: string.Join("\n\n", Enumerable.Range(1, 80).Select(i => "paragraph " + i)));
            detail = PlanDetail(new PlanCard(tall, h.Runtime), tall);
            window = new AvaloniaWindow { Width = 900, Height = 900, Content = detail };
            // the headless test app has no theme, so a ScrollViewer has no template; give it the minimal one a themed app has
            window.Styles.Add(new Avalonia.Styling.Style(x => Avalonia.Styling.Selectors.OfType(x, typeof(ScrollViewer)))
            {
                Setters = { new Avalonia.Styling.Setter(TemplatedControl.TemplateProperty, new Avalonia.Controls.Templates.FuncControlTemplate<ScrollViewer>((viewer, scope) =>
                    new Avalonia.Controls.Presenters.ScrollContentPresenter { Name = "PART_ContentPresenter", CanVerticallyScroll = true, [!Avalonia.Controls.Presenters.ScrollContentPresenter.ContentProperty] = viewer[!ContentControl.ContentProperty] }.RegisterInNameScope(scope))) },
            });
            window.Show();
            Settle(window);
        });
        try
        {
            h.Main.OnMain(() =>
            {
                Settle(window!);
                var scroll = Assert.Single(LogicalQuery.SelfAndDescendants<PopoverScroll>(detail!));
                Assert.Equal(420, Assert.IsType<ScrollViewer>(scroll.Child).Height);
            });
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-112")]
    public void The_plan_buttons_stay_greyed_while_the_thread_is_busy_with_the_reason_and_carry_no_tip_when_free() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        Control? detail = null;
        AvaloniaWindow? window = null;
        Panel Row() => (Panel)LogicalQuery.SelfAndDescendants<Button>(detail!).First(button => LogicalTexts(button).Contains("Implement plan")).GetLogicalAncestors().OfType<Panel>()
            .First(panel => LogicalQuery.SelfAndDescendants<Button>(panel).Count() == 2);
        h.Main.OnMain(() =>
        {
            var entry = PlanEntry(ProposedPlan.State.Proposed);
            detail = PlanDetail(new PlanCard(entry, h.Runtime), entry);
            window = Show(detail);
            Assert.True(Row().IsEnabled);
            Assert.Null(ToolTip.GetTip(Row()));
            h.Runtime.RehearseTurn("busy");
        });
        try
        {
            h.Main.OnMain(() =>
            {
                Settle(window!);
                var row = Row();
                Assert.False(row.IsEnabled); // greyed, not gone
                Assert.Equal("Wait for the current turn to finish", ToolTip.GetTip(row));
                Assert.True(ToolTip.GetShowOnDisabled(row));
                Assert.Equal(2, LogicalQuery.SelfAndDescendants<Button>(row).Count());
                // a popover opened while the thread is already busy starts greyed, before any change is observed
                var later = PlanEntry(ProposedPlan.State.Proposed);
                var fresh = PlanDetail(new PlanCard(later, h.Runtime), later);
                Assert.False(Assert.IsAssignableFrom<Panel>(LogicalQuery.SelfAndDescendants<Button>(fresh).First(button => LogicalTexts(button).Contains("Dismiss")).GetLogicalParent()).IsEnabled);
            });
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-112")]
    public void A_pending_plan_approval_also_greys_the_plan_buttons_while_the_thread_is_idle() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        Control? open = null;
        AvaloniaWindow? window = null;
        var entry = PlanEntry(ProposedPlan.State.Proposed);
        Panel RowOf(Control detail) => Assert.IsAssignableFrom<Panel>(LogicalQuery.SelfAndDescendants<Button>(detail).First(button => LogicalTexts(button).Contains("Dismiss")).GetLogicalParent());
        h.Main.OnMain(() =>
        {
            open = PlanDetail(new PlanCard(entry, h.Runtime), entry);
            window = Show(open);
            Assert.True(RowOf(open).IsEnabled);
            h.Runtime.Rehearse(new ProviderEvent.Approval(new ApprovalRequest("a1", ApprovalRequest.Kind.Plan, "Approve this plan?", ValueList<ApprovalRequest.Option>.Empty)));
            Assert.NotNull(h.Runtime.PendingPlanApproval);
            Assert.False(h.Runtime.IsRunning); // only the pending approval can be what greys them
            var held = PlanDetail(new PlanCard(entry, h.Runtime), entry); // opened after the approval arrived: greyed at once
            Assert.False(RowOf(held).IsEnabled);
            Assert.Equal("Wait for the current turn to finish", ToolTip.GetTip(RowOf(held)));
        });
        try
        {
            // and the one already open when the approval arrived follows it
            h.Main.OnMain(() =>
            {
                Settle(window!);
                Assert.False(RowOf(open!).IsEnabled);
            });
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-113")] // TimelineRows.swift:2287 `MarkdownView(text: plan.markdown, isStreaming: plan.state == .drafting)`: the flag is the drafting state alone, read when the detail is built
    public void A_drafting_plan_renders_with_the_streaming_markdown_and_it_switches_the_moment_the_state_leaves_drafting() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            bool Streams(ProposedPlan.State state)
            {
                var entry = PlanEntry(state);
                return LogicalQuery.SelfAndDescendants<MarkdownView>(PlanDetail(new PlanCard(entry, h.Runtime), entry)).Single().IsStreaming;
            }
            Assert.True(Streams(ProposedPlan.State.Drafting));
            Assert.False(Streams(ProposedPlan.State.Proposed));
            Assert.False(Streams(ProposedPlan.State.Accepted));
            Assert.False(Streams(ProposedPlan.State.Dismissed));
        });
    });

    // ---- PR-CHAT-098: a tool row's chevron points down for a popover/head and right (rotating ninety degrees when
    // expanded) for an in-place expansion, beside the subject, and is absent otherwise (:1799-1810). The rotation is a
    // snappy animation, so the test ticks the manual frame clock before it reads the angle. (An earlier version read the
    // angle before any tick, failed, left its window open, and the session teardown then re-threw a glyph-typeface
    // error that hid the real failure: the "Cascadia Mono / Inter glyphTypeface" STOPs #14 and #15 were that, not a font
    // gap. Every window below is now closed in a finally so a real assertion failure shows itself.)

    private static string[] Symbols(Avalonia.Controls.Control control) =>
        control.GetVisualDescendants().OfType<SwiftImage>().Select(image => image.SymbolName ?? "").ToArray();

    [Fact]
    [Trait("Req", "PR-CHAT-098")]
    public void A_call_that_sent_out_a_head_draws_chevron_down() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the head's glyph animates while running
        ToolRow? row = null;
        AvaloniaWindow? window = null;
        h.Main.OnMain(() =>
        {
            var entry = new TimelineEntry(new TimelineItem(null, new TimelineItem.Content.Tool(
                new ToolCall { ToolCallKind = ToolCall.Kind.Agent, Title = "Pip: task", ToolCallStatus = ToolCall.Status.Running })));
            h.Runtime.RehearseHydraHead(entry.Id, Guid.NewGuid());
            row = new ToolRow(entry, h.Runtime);
            var host = new Border { Child = row };
            Marrow.App.Port.AppEnvironment.SetAppModel(host, h.App);
            window = Show(host);
        });
        h.Main.OnMain(() =>
        {
            Assert.Contains("chevron.down", Symbols(row!));
            Assert.DoesNotContain("chevron.right", Symbols(row!));
            Close(window!);
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-098")]
    public void A_call_that_shows_output_draws_chevron_right_rotating_ninety_degrees_when_expanded() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        ToolRow? row = null;
        AvaloniaWindow? window = null;
        h.Main.OnMain(() =>
        {
            var entry = new TimelineEntry(new TimelineItem(null, new TimelineItem.Content.Tool(
                new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = "run a thing", ToolCallStatus = ToolCall.Status.Completed, Output = "some output" })));
            row = new ToolRow(entry, h.Runtime);
            var host = new Border { Child = row };
            Marrow.App.Port.AppEnvironment.SetAppModel(host, h.App);
            window = Show(host);
        });
        try
        {
            h.Main.OnMain(() =>
            {
                Assert.Contains("chevron.right", Symbols(row!));
                Assert.DoesNotContain("chevron.down", Symbols(row!));
                var chevron = row!.GetVisualDescendants().OfType<SwiftImage>().Single(image => image.SymbolName == "chevron.right");
                Assert.Equal(0, ((RotateTransform?)chevron.RenderTransform)?.Angle ?? 0);
                Click(window!, row!.GetVisualDescendants().OfType<Button>().First());
            });
            // The turn is a snappy animation: it reaches 90 only as the manual clock ticks (480 ms is past its settling time).
            for (var i = 0; i < 30; i++) { h.Main.Advance(TimeSpan.FromMilliseconds(16)); manual.Advance(TimeSpan.FromMilliseconds(16)); }
            h.Main.OnMain(() =>
            {
                var chevron = row!.GetVisualDescendants().OfType<SwiftImage>().Single(image => image.SymbolName == "chevron.right");
                Assert.Equal(90, ((RotateTransform?)chevron.RenderTransform)?.Angle ?? 0);
            });
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-098")]
    public void A_call_that_opens_the_changes_popover_draws_a_static_chevron_down() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        ToolRow? row = null;
        AvaloniaWindow? window = null;
        h.Main.OnMain(() =>
        {
            var edits = new ValueList<FileEdit>([new FileEdit("src/a.swift", "@@ -1 +1 @@\n-a\n+b\n", 1, 1)]);
            row = new ToolRow(ToolEntry(Call(ToolCall.Kind.Edit, "Edit a.swift") with { Edits = edits }), h.Runtime);
            var host = new Border { Child = row };
            Marrow.App.Port.AppEnvironment.SetAppModel(host, h.App);
            window = Show(host);
        });
        try
        {
            h.Main.OnMain(() =>
            {
                Assert.Contains("chevron.down", Symbols(row!));
                Assert.DoesNotContain("chevron.right", Symbols(row!));
                var chevron = row!.GetVisualDescendants().OfType<SwiftImage>().Single(image => image.SymbolName == "chevron.down");
                Assert.Null(chevron.RenderTransform); // a static affordance: it never rotates
            });
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    [Theory]
    [Trait("Req", "PR-CHAT-098")]
    [InlineData(true)]
    [InlineData(false)]
    public void Either_chevron_is_caption2_semibold_in_secondary_text_immediately_after_the_rows_own_text(bool popover) => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        ToolRow? row = null;
        AvaloniaWindow? window = null;
        h.Main.OnMain(() =>
        {
            var call = popover
                ? Call(ToolCall.Kind.Edit, "Edit a.swift") with { Edits = new ValueList<FileEdit>([new FileEdit("src/a.swift", "@@ -1 +1 @@\n-a\n+b\n", 1, 1)]) }
                : Call(ToolCall.Kind.Command, "run a thing") with { Output = "some output" };
            row = new ToolRow(ToolEntry(call), h.Runtime);
            var host = new Border { Child = row };
            Marrow.App.Port.AppEnvironment.SetAppModel(host, h.App);
            window = Show(host);
        });
        try
        {
            h.Main.OnMain(() =>
            {
                var chevron = row!.GetVisualDescendants().OfType<SwiftImage>().Single(image => image.SymbolName == (popover ? "chevron.down" : "chevron.right"));
                Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Caption2, SwiftFont.Weight.Semibold), chevron.GetValue(SwiftImage.FontProperty));
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, chevron), BrushColor(chevron.Foreground));
                var line = Assert.IsAssignableFrom<Panel>(chevron.Parent);
                var label = line.Children.OfType<MiddleTruncatingTextBlock>().Single();
                var labelAt = line.Children.IndexOf(label);
                var chevronAt = line.Children.IndexOf(chevron);
                Assert.True(chevronAt > labelAt, "the chevron follows the row's text");
                // the diff stat (an edit's counts) may sit between the text and the chevron; nothing else may
                Assert.All(line.Children.Skip(labelAt + 1).Take(chevronAt - labelAt - 1), child => Assert.IsType<DiffStatLabel>(child));
            });
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-098")]
    public void A_call_with_nothing_to_show_draws_no_chevron() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        ToolRow? row = null;
        AvaloniaWindow? window = null;
        h.Main.OnMain(() =>
        {
            var entry = new TimelineEntry(new TimelineItem(null, new TimelineItem.Content.Tool(
                new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = "run a thing", ToolCallStatus = ToolCall.Status.Completed })));
            row = new ToolRow(entry, h.Runtime);
            var host = new Border { Child = row };
            Marrow.App.Port.AppEnvironment.SetAppModel(host, h.App);
            window = Show(host);
        });
        h.Main.OnMain(() =>
        {
            Assert.DoesNotContain("chevron.right", Symbols(row!));
            Assert.DoesNotContain("chevron.down", Symbols(row!));
            Close(window!);
        });
    });

    // ---- PR-CHAT-092: an expanded work group shows only the two latest steps until the earlier-steps line is
    // tapped (:1522-1557). The tap runs inside a snappy animation transaction, so the line leaves and the earlier rows
    // arrive only as the manual frame clock ticks. ----

    [Theory]
    [Trait("Req", "PR-CHAT-092")]
    [InlineData(2, 0, "")]
    [InlineData(3, 1, "1 earlier step")]
    [InlineData(5, 3, "3 earlier steps")]
    public void Only_the_two_latest_steps_show_until_the_earlier_line_is_revealed(int total, int hidden, string label) => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            // Command-kind entries with no output/detail (no chevron, no output to lay out) stand in for "a step of the work": PR-CHAT-092 is about which/how-many mount, not their
            // own content, which every other WorkSteps-adjacent record already covers.
            var entries = new ValueList<TimelineEntry>(Enumerable.Range(0, total).Select(_ => Tool()));
            var showsAll = false;
            var steps = new WorkSteps(entries, h.Runtime, new Binding<bool>(() => showsAll, value => showsAll = value));
            var window = Show(steps);
            var texts = steps.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();
            if (hidden > 0) Assert.Contains(label, texts);
            else Assert.DoesNotContain(texts, t => t.Contains("earlier step", StringComparison.Ordinal));
            // Only the latest (total - hidden) steps are mounted.
            var rows = steps.GetVisualDescendants().OfType<ToolRow>().ToArray();
            Assert.Equal(total - hidden, rows.Length);
            Close(window);
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-092")]
    public void Tapping_the_earlier_steps_line_reveals_every_step() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        AvaloniaWindow? window = null;
        WorkSteps? steps = null;
        var showsAll = new ObservableBoolBox();
        h.Main.OnMain(() =>
        {
            var entries = new ValueList<TimelineEntry>(Enumerable.Range(0, 5).Select(_ => Tool()));
            steps = new WorkSteps(entries, h.Runtime, showsAll.AsBinding());
            window = Show(steps);
        });
        try
        {
            h.Main.OnMain(() =>
            {
                Assert.Equal(2, steps!.GetVisualDescendants().OfType<ToolRow>().Count());
                Click(window!, steps!.GetVisualDescendants().OfType<Button>().Single());
            });
            for (var i = 0; i < 30; i++) { h.Main.Advance(TimeSpan.FromMilliseconds(16)); manual.Advance(TimeSpan.FromMilliseconds(16)); }
            h.Main.OnMain(() =>
            {
                Assert.True(showsAll.Value);
                Assert.Equal(5, steps!.GetVisualDescendants().OfType<ToolRow>().Count());
                Assert.DoesNotContain(steps!.GetVisualDescendants().OfType<TextBlock>(), block => (block.Text ?? "").Contains("earlier step", StringComparison.Ordinal)); // the earlier-steps line itself is removed once shown
            });
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-092")]
    public void The_earlier_steps_line_is_an_ellipsis_in_a_16_point_column_and_the_count_both_in_secondary_text_with_12_points_trailing() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        AvaloniaWindow? window = null;
        WorkSteps? steps = null;
        h.Main.OnMain(() =>
        {
            steps = new WorkSteps(new ValueList<TimelineEntry>(Enumerable.Range(0, 3).Select(_ => Tool())), h.Runtime, new ObservableBoolBox().AsBinding());
            window = Show(steps);
        });
        try
        {
            h.Main.OnMain(() =>
            {
                var ellipsis = Assert.Single(steps!.GetVisualDescendants().OfType<SwiftImage>(), image => image.SymbolName == "ellipsis");
                Assert.Equal(TimelineMetrics.IconWidth, ellipsis.Width);
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, ellipsis), BrushColor(ellipsis.Foreground));
                var line = Assert.IsType<StackPanel>(ellipsis.Parent);
                Assert.Equal(12, line.Margin.Right);
                var count = Assert.Single(line.Children.OfType<TextBlock>());
                Assert.Equal("1 earlier step", count.Text);
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, count), BrushColor(count.Foreground));
            });
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-092")]
    public void Tapping_the_earlier_steps_line_asks_the_timeline_to_reveal_its_end_inside_a_snappy_240_millisecond_animation() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        AvaloniaWindow? window = null;
        WorkSteps? steps = null;
        var reveals = new List<Animation?>();
        h.Main.OnMain(() =>
        {
            steps = new WorkSteps(new ValueList<TimelineEntry>(Enumerable.Range(0, 3).Select(_ => Tool())), h.Runtime, new ObservableBoolBox().AsBinding());
            var host = new Border { Child = steps };
            ThreadTimeline.SetRevealTimelineEnd(host, new TimelineRevealBox { Action = () => reveals.Add(Transaction.Current) });
            window = Show(host);
        });
        try
        {
            h.Main.OnMain(() => Click(window!, steps!.GetVisualDescendants().OfType<Button>().Single()));
            var reveal = Assert.Single(reveals);
            Assert.Equal(Animation.Snappy(duration: 0.24), reveal);
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-092")]
    public void What_the_agent_said_between_two_steps_renders_as_dimmed_12_point_secondary_markdown_indented_past_the_icon_column() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        AvaloniaWindow? window = null;
        WorkSteps? steps = null;
        h.Main.OnMain(() =>
        {
            steps = new WorkSteps(new ValueList<TimelineEntry>([Reply("looking at the config"), Tool()]), h.Runtime, new ObservableBoolBox().AsBinding());
            window = Show(steps);
        });
        try
        {
            h.Main.OnMain(() =>
            {
                Assert.Single(steps!.GetVisualDescendants().OfType<ToolRow>());
                var markdown = Assert.Single(steps!.GetVisualDescendants().OfType<MarkdownView>());
                Assert.Equal("looking at the config", markdown.Text);
                Assert.Equal(12, MarkdownView.GetMarkdownPointSize(markdown));
                Assert.True(MarkdownView.GetMarkdownDimmed(markdown));
                Assert.Equal(TimelineMetrics.IconWidth + TimelineMetrics.IconSpacing, markdown.Margin.Left);
                Assert.Equal(24, markdown.Margin.Left);
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, markdown), BrushColor(Avalonia.Controls.Documents.TextElement.GetForeground(markdown)));
            });
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    // ---- PR-CHAT-077: a sent message is a trailing tinted bubble with an iMessage tail (:118-129, :163-164, :1167-1207) ----

    [Fact]
    [Trait("Req", "PR-CHAT-077")]
    public void The_bubble_path_rounds_three_corners_and_draws_the_tail_through_the_exact_control_points()
    {
        var geometry = Assert.IsType<PathGeometry>(new UserBubble().Path(new Rect(0, 0, 200, 50)));
        var figure = Assert.Single(geometry.Figures!);
        Assert.True(figure.IsClosed);
        Assert.True(figure.IsFilled);
        Assert.Equal(new Point(18, 0), figure.StartPoint);
        var segments = figure.Segments!.ToList();
        Assert.Equal(10, segments.Count);
        Assert.Equal(new Point(178, 0), Assert.IsType<LineSegment>(segments[0]).Point);
        var topRight = Assert.IsType<ArcSegment>(segments[1]);
        Assert.Equal(new Point(196, 18), topRight.Point);
        Assert.Equal(new Size(18, 18), topRight.Size);
        Assert.Equal(SweepDirection.Clockwise, topRight.SweepDirection);
        Assert.False(topRight.IsLargeArc);
        Assert.Equal(new Point(196, 39), Assert.IsType<LineSegment>(segments[2]).Point);
        var tip = Assert.IsType<BezierSegment>(segments[3]);
        Assert.Equal((new Point(196, 49), new Point(200, 50), new Point(200, 50)), (tip.Point1, tip.Point2, tip.Point3));
        var curl = Assert.IsType<BezierSegment>(segments[4]);
        Assert.Equal((new Point(196, 50.5), new Point(192, 49), new Point(189, 46)), (curl.Point1, curl.Point2, curl.Point3));
        var ease = Assert.IsType<BezierSegment>(segments[5]);
        Assert.Equal((new Point(184, 50), new Point(180, 50), new Point(175, 50)), (ease.Point1, ease.Point2, ease.Point3));
        Assert.Equal(new Point(18, 50), Assert.IsType<LineSegment>(segments[6]).Point);
        var bottomLeft = Assert.IsType<ArcSegment>(segments[7]);
        Assert.Equal(new Point(0, 32), bottomLeft.Point);
        Assert.Equal(SweepDirection.Clockwise, bottomLeft.SweepDirection);
        Assert.Equal(new Point(0, 18), Assert.IsType<LineSegment>(segments[8]).Point);
        var topLeft = Assert.IsType<ArcSegment>(segments[9]);
        Assert.Equal(new Point(18, 0), topLeft.Point);
        Assert.Equal(SweepDirection.Clockwise, topLeft.SweepDirection);
        Assert.Equal(18, UserBubble.CornerRadius);
        Assert.Equal(4, UserBubble.Tail);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-077")]
    public void A_sent_message_is_a_trailing_bubble_with_14_18_and_9_point_insets_96_points_clear_on_the_leading_side_tinted_at_14_percent() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        h.Main.OnMain(() =>
        {
            var row = new UserMessageRow(UserEntry("hello there", Guid.NewGuid()), h.Runtime, canRevert: false);
            var window = Show(row);
            var text = Assert.Single(row.GetVisualDescendants().OfType<InlineText>());
            Assert.Equal("hello there", text.Source);
            Assert.True(text.HugsContent);
            var inset = Assert.IsType<Border>(text.Parent);
            Assert.Equal(new Thickness(14, 9, 14 + UserBubble.Tail, 9), inset.Padding);
            var shape = Assert.Single(row.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>());
            Assert.Equal(Resolved(ChromeTokens.Accent.Opacity(0.14), shape), BrushColor(shape.Fill));
            var outer = row.GetVisualDescendants().OfType<Border>().First(border => border.Padding.Left == 96);
            Assert.Equal(-(TimelineMetrics.HoverLineHeight + 6), outer.Margin.Bottom);
            var column = row.GetVisualDescendants().OfType<StackPanel>().First(stack => stack.Spacing == 6);
            Assert.Equal(HorizontalAlignment.Right, column.HorizontalAlignment);
            Close(window);
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-077")]
    public void The_bubble_shape_is_only_a_background_and_never_grows_the_bubble_past_its_text() => headless.OnUiThread(() =>
    {
        // TimelineRows.swift:128 .background(.tint.opacity(0.14), in: UserBubble()): a SwiftUI background takes the
        // bubble's size and never changes it. The tail's curve reaches a fraction of a point past the rect it is drawn
        // in (UserBubble :1190-1194), so a shape that took part in layout would grow the bubble a little on every pass.
        using var h = Harness();
        h.Main.OnMain(() =>
        {
            var row = new UserMessageRow(UserEntry("hello there", Guid.NewGuid()), h.Runtime, canRevert: false);
            var window = Show(row);
            try
            {
                for (var pass = 0; pass < 20; pass++)
                {
                    Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    window.UpdateLayout();
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                }
                var shape = Assert.Single(row.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>());
                var inset = Assert.IsType<Border>(Assert.Single(row.GetVisualDescendants().OfType<InlineText>()).Parent);
                Assert.Equal(inset.DesiredSize.Height, inset.Bounds.Height, 3);
                Assert.True(inset.Bounds.Height < 60, "bubble height " + inset.Bounds.Height);
                Assert.Equal(inset.Bounds.Size, shape.Bounds.Size);
            }
            finally { Close(window); }
        });
    });

    // ---- PR-CHAT-079: a sent message's attachments are a 56-point thumbnail strip above the bubble (:1209-1350) ----

    [Fact]
    [Trait("Req", "PR-CHAT-079")]
    public void The_strip_hugs_its_thumbnails_56_points_square_6_points_apart_each_carrying_its_name_as_a_tooltip() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        h.Main.OnMain(() =>
        {
            var files = new ValueList<Attachment>([
                new Attachment { Name = "a.txt", Path = @"C:\no-such-folder\a.txt", MimeType = "text/plain" },
                new Attachment { Name = "b.txt", Path = @"C:\no-such-folder\b.txt", MimeType = "text/plain" }]);
            var strip = new AttachmentStrip(files);
            var window = Show(strip);
            var row = strip.GetVisualDescendants().OfType<StackPanel>().Single(stack => stack.Orientation == Orientation.Horizontal);
            Assert.Equal(6, row.Spacing);
            var plates = strip.GetVisualDescendants().OfType<Border>().Where(border => border.Width == 56 && border.Height == 56).ToArray();
            Assert.Equal(2, plates.Length);
            Assert.All(plates, plate => Assert.True(plate.ClipToBounds));
            var thumbnails = strip.GetVisualDescendants().OfType<AttachmentThumbnail>().ToArray();
            Assert.Equal(2, thumbnails.Length);
            Assert.Equal("a.txt", ToolTip.GetTip(thumbnails[0]));
            Assert.Equal("b.txt", ToolTip.GetTip(thumbnails[1]));
            Close(window);
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-079")]
    public void A_non_image_non_video_attachment_shows_the_shell_icon_on_the_plate_at_size_times_zero_point_zero_eight_padding_labelled_with_its_name() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        h.Main.OnMain(() =>
        {
            var file = new Attachment { Name = "notes.txt", Path = @"C:\no-such-folder\notes.txt", MimeType = "text/plain" };
            var preview = new AttachmentPreviewSlot();
            var thumbnail = new AttachmentThumbnail(file, preview);
            var window = Show(thumbnail);
            var icon = Assert.Single(thumbnail.GetVisualDescendants().OfType<Image>());
            Assert.Equal(new Thickness(56 * 0.08), icon.Margin);
            Assert.Equal(Stretch.Uniform, icon.Stretch);
            var plate = Assert.Single(thumbnail.GetVisualDescendants().OfType<Border>(), border => border.Width == 56);
            Assert.Equal("notes.txt", Avalonia.Automation.AutomationProperties.GetName(plate));
            Assert.Equal(Resolved(SwiftColor.Quaternary.Opacity(0.6), plate), BrushColor(plate.Background));
            Close(window);
        });
    });

    // ---- PR-CHAT-080: a revertable sent message shows a pencil on hover that toggles the message editor (:130-161) ----

    private static Control HoverLine080(Control row) =>
        row.GetVisualDescendants().OfType<Control>().Single(control => control.Height == TimelineMetrics.HoverLineHeight && double.IsNaN(control.Width));

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-080 (STOP #11): TimelineRows.swift:130-161
    public void A_revertable_message_shows_a_pencil_only_on_hover_and_a_locked_one_shows_none() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var pencilClock = new ManualFrameSource(); using var pencilFrames = FrameClock.Source.Override(pencilClock); // OD-0076: the pencil line fades on the frame clock
        UserMessageRow? row = null;
        UserMessageRow? locked = null;
        AvaloniaWindow? window = null;
        h.Main.OnMain(() =>
        {
            row = new UserMessageRow(UserEntry("edit me", Guid.NewGuid()), h.Runtime, canRevert: true);
            locked = new UserMessageRow(UserEntry("keep me", Guid.NewGuid()), h.Runtime, canRevert: false);
            var panel = new StackPanel { Spacing = 60 };
            panel.Children.Add(row);
            panel.Children.Add(locked);
            window = Show(panel);
            Assert.Empty(HoverLine080(locked).GetVisualDescendants().OfType<Button>());
            var pencil = Assert.Single(HoverLine080(row).GetVisualDescendants().OfType<Button>());
            Assert.Equal("Edit message", ToolTip.GetTip(pencil));
            Assert.Contains("pencil", Symbols(pencil));
            Assert.Equal(0, HoverLine080(row).Opacity);
            Hover(window, row.GetVisualDescendants().OfType<InlineText>().Single());
        });
        pencilClock.Advance(TimeSpan.Zero); h.Main.AdvanceMilliseconds(200); pencilClock.Advance(TimeSpan.FromMilliseconds(200)); // OD-0076: let the pencil line's fade finish
        h.Main.OnMain(() =>
        {
            Assert.Equal(1, HoverLine080(row!).Opacity);
            Assert.Equal(0, HoverLine080(locked!).Opacity);
            Close(window!);
        });
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-080 (STOP #11): TimelineRows.swift:132-135, :148-155
    public void The_pencil_opens_the_edit_on_the_runtime_through_the_editor_popover_and_a_second_tap_closes_it() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var pencilClock = new ManualFrameSource(); using var pencilFrames = FrameClock.Source.Override(pencilClock); // OD-0076: the pencil line fades on the frame clock
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var turn = Guid.NewGuid();
        UserMessageRow? row = null;
        AvaloniaWindow? window = null;
        // The thread's history lands first: installing it drops an edit whose turn it does not hold
        // (ThreadRuntime.swift:172-182), which is the runtime's rule, not the row's.
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            row = new UserMessageRow(UserEntry("edit me", turn), h.Runtime, canRevert: true);
            window = Show(row);
            Click(window, Assert.Single(HoverLine080(row).GetVisualDescendants().OfType<Button>()));
            var edit = Assert.IsType<PromptEdit>(h.Runtime.MessageEdit);
            Assert.Equal(turn, edit.Id);
            Assert.Equal("edit me", edit.Text);
        });
        pencilClock.Advance(TimeSpan.Zero); h.Main.AdvanceMilliseconds(200); pencilClock.Advance(TimeSpan.FromMilliseconds(200)); // OD-0076: let the pencil line's fade finish
        h.Main.OnMain(() =>
        {
            var shown = Assert.Single(presenter.Calls, call => call.Presented);
            Assert.IsType<MessageEditor>(PopoverHost.Content(shown.Popup));
            Assert.Equal(1, HoverLine080(row!).Opacity); // editing keeps the line up without a hover
            Click(window!, Assert.Single(HoverLine080(row!).GetVisualDescendants().OfType<Button>()));
            Assert.Null(h.Runtime.MessageEdit);
            Close(window!);
        });
    });

    // ---- PR-CHAT-082: Edit from here raises a two-button confirmation that decides whether files go back too (:184-206) ----

    [Theory]
    [Trait("Req", "PR-CHAT-082")]
    [InlineData("Revert and keep file changes", false)]
    [InlineData("Revert files too", true)]
    public void Edit_from_here_raises_the_dialog_verbatim_and_routes_the_pressed_button_to_restoreFiles(string title, bool restoreFiles) => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var presenter = new RecordingConfirmationPresenter();
        using var seam = AlertPresentation.Presenter.Override(presenter);
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        AvaloniaWindow? window = null;
        h.Main.OnMain(() =>
        {
            h.Runtime.RehearseTurn("prompt");
            h.Runtime.Rehearse(new ProviderEvent.TurnCompleted(TurnStatus.Failed, null));
        });
        h.Main.Loop.RunUntil(() => { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); return h.Runtime.Phase == RuntimePhase.Idle; }, AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            var entry = h.Runtime.Entries.First(item => item.Item.TimelineItemContent is TimelineItem.Content.User);
            var row = new UserMessageRow(entry, h.Runtime, true);
            window = Show(row);
            var requests = (MessageMenuRequests)Field(row, "menuRequests")!;
            requests.ConfirmRevert = true;
            Settle(window);
        });
        h.Main.Loop.RunUntil(() => { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); return presenter.Shown.Count == 1; }, AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            var dialog = Assert.Single(presenter.Shown);
            Assert.Equal(AlertKind.ConfirmationDialog, dialog.Kind);
            Assert.Equal("Edit from this message?", dialog.Title);
            Assert.Equal("This message and everything after it leave the thread, and your prompt returns to the composer.", dialog.Message);
            Assert.Same(window, dialog.Owner);
            Assert.Equal(["Revert and keep file changes", "Revert files too", "Cancel"], dialog.Buttons.Select(button => button.Title));
            Assert.Equal([AlertButtonRole.None, AlertButtonRole.Destructive, AlertButtonRole.Cancel], dialog.Buttons.Select(button => button.Role));
            Assert.Single(h.Runtime.Turns);
            presenter.Press(title);
        });
        h.Main.OnMain(() =>
        {
            // No checkpoint: restoring must refuse, while keeping files trims this failed turn without one.
            Assert.Equal(restoreFiles ? 1 : 0, h.Runtime.Turns.Count);
            Close(window!);
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-082")]
    public void The_confirming_intent_resets_immediately_so_the_same_row_can_ask_again() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var presenter = new RecordingConfirmationPresenter();
        using var seam = AlertPresentation.Presenter.Override(presenter);
        MessageMenuRequests? requests = null;
        AvaloniaWindow? window = null;
        h.Main.OnMain(() =>
        {
            var row = new UserMessageRow(UserEntry("edit me", Guid.NewGuid()), h.Runtime, canRevert: true);
            window = Show(row);
            requests = (MessageMenuRequests)Field(row, "menuRequests")!;
            requests.ConfirmRevert = true;
            Settle(window);
        });
        h.Main.Loop.RunUntil(() => { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); return presenter.Shown.Count == 1; }, AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            // :184-190 the intent is a one-shot signal: it is consumed and reset to false as soon as the row takes it up.
            Assert.False(requests!.ConfirmRevert);
            presenter.Cancel(); // dismissing resets isConfirmingRevert=false so the row may ask again
        });
        h.Main.Loop.RunUntilIdle();
        h.Main.OnMain(() =>
        {
            requests!.ConfirmRevert = true;
            Settle(window!);
        });
        h.Main.Loop.RunUntil(() => { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); return presenter.Shown.Count == 2; }, AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            Assert.Equal(2, presenter.Shown.Count); // asking again after the reset raises the dialog a second time
            Close(window!);
        });
    });

    // ---- PR-CHAT-088: an assistant reply renders its markdown and shows the turn duration on its hover line (:1353-1390) ----

    // The label is built eagerly at construction (TimelineRows.cs, `new TextBlock { Text = TurnEndRow.Label(summary) }`), and
    // TurnEndRow.Label calls TimelineRows.Duration -> icu.dll: on this container these tests ran under the local, uncommitted
    // /tmp/icushim (see the commit message); on Windows they are ordinary tests.

    private static Control ReplyHoverLine(Control row) =>
        row.GetVisualDescendants().OfType<Control>().Single(control => control.Height == TimelineMetrics.HoverLineHeight && double.IsNaN(control.Width));

    [Fact]
    [Trait("Req", "PR-CHAT-088")]
    public void A_reply_renders_its_markdown_and_keeps_a_hidden_22_point_line_with_the_turn_label_in_caption_secondary_text() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            var row = new AssistantMessageRow(Reply("some `code` here"), Summary(Guid.NewGuid()), h.Runtime);
            var window = Show(row);
            try
            {
                var markdown = Assert.Single(row.GetVisualDescendants().OfType<MarkdownView>());
                Assert.Equal("some `code` here", markdown.Text);
                Assert.False(markdown.IsStreaming);
                var line = ReplyHoverLine(row);
                Assert.Equal(0, line.Opacity);
                var label = Assert.Single(line.GetVisualDescendants().OfType<TextBlock>());
                Assert.Equal("Worked for 5s", label.Text);
                Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Caption).Resolve().FontSize, label.FontSize);
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, label), BrushColor(label.Foreground));
                var column = Assert.IsType<StackPanel>(line.GetVisualParent());
                Assert.Equal(2, column.Spacing); // :1363 the leading stack's spacing
                Assert.Equal(new Thickness(0, 0, 0, -(TimelineMetrics.HoverLineHeight + 2)), column.Margin); // :1384-1388 the room is inside the row but not its height
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-088")]
    public void A_reply_without_a_summary_has_no_label_on_its_hover_line() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            var row = new AssistantMessageRow(Reply("no summary"), null, h.Runtime);
            var window = Show(row);
            try { Assert.Empty(ReplyHoverLine(row).GetVisualDescendants().OfType<TextBlock>()); }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-088")]
    public void Hovering_a_settled_reply_fades_the_line_in_over_120_milliseconds_and_a_streaming_reply_never_shows_it() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        AssistantMessageRow? settled = null;
        AssistantMessageRow? streaming = null;
        AvaloniaWindow? window = null;
        h.Main.OnMain(() =>
        {
            settled = new AssistantMessageRow(Reply("settled reply"), Summary(Guid.NewGuid()), h.Runtime);
            streaming = new AssistantMessageRow(new TimelineEntry(new TimelineItem(null, new TimelineItem.Content.Assistant(new AssistantMessage { Text = "still typing", IsStreaming = true }))), Summary(Guid.NewGuid()), h.Runtime);
            var panel = new StackPanel { Spacing = 60 };
            panel.Children.Add(settled);
            panel.Children.Add(streaming);
            window = Show(panel);
            Hover(window, settled.GetVisualDescendants().OfType<MarkdownView>().Single());
        });
        try
        {
            // easeOut over 0.12 s on the frame clock: part way at 60 ms, fully in by 120 ms.
            frames.Advance(TimeSpan.Zero);
            h.Main.AdvanceMilliseconds(60);
            frames.Advance(TimeSpan.FromMilliseconds(60));
            Assert.InRange(ReplyHoverLine(settled!).Opacity, 0.01, 0.99);
            h.Main.AdvanceMilliseconds(60);
            frames.Advance(TimeSpan.FromMilliseconds(60));
            Assert.Equal(1, ReplyHoverLine(settled!).Opacity);
            h.Main.OnMain(() => Hover(window!, streaming!.GetVisualDescendants().OfType<MarkdownView>().Single()));
            h.Main.AdvanceMilliseconds(200);
            frames.Advance(TimeSpan.FromMilliseconds(200));
            Assert.Equal(0, ReplyHoverLine(streaming!).Opacity);
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    // ---- PR-CHAT-100: a failed tool row shows its exit code and a declined one shows Declined (:1832-1841) ----

    [Fact]
    [Trait("Req", "PR-CHAT-100")]
    public void A_failed_call_shows_its_exit_code_in_danger_and_a_declined_one_shows_Declined_in_warning() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        h.Main.OnMain(() =>
        {
            var failed = new ToolRow(ToolEntry(Call(ToolCall.Kind.Command, "make", ToolCall.Status.Failed) with { ExitCode = 2 }), h.Runtime);
            var noCode = new ToolRow(ToolEntry(Call(ToolCall.Kind.Command, "make", ToolCall.Status.Failed)), h.Runtime);
            var declined = new ToolRow(ToolEntry(Call(ToolCall.Kind.Command, "rm -rf /", ToolCall.Status.Declined)), h.Runtime);
            var panel = new StackPanel();
            panel.Children.Add(failed);
            panel.Children.Add(noCode);
            panel.Children.Add(declined);
            var window = Show(panel);
            var exit = failed.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == "exit 2");
            Assert.Equal(Resolved(ChromeTokens.Danger, exit), BrushColor(exit.Foreground));
            Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Caption).Resolve().FontSize, exit.FontSize);
            var noCodeTexts = noCode.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();
            Assert.DoesNotContain(noCodeTexts, text => text.StartsWith("exit", StringComparison.Ordinal));
            Assert.DoesNotContain("Declined", noCodeTexts);
            var word = declined.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == "Declined");
            Assert.Equal(Resolved(ChromeTokens.Warning, word), BrushColor(word.Foreground));
            Close(window);
        });
    });

    // ---- PR-CHAT-101: an expanded tool row's output is capped at thirty lines with a toggle (:2094-2167, esp. :2103-2113, :2141-2162) ----

    private static string Lines101(int count) => string.Join("\n", Enumerable.Range(1, count).Select(i => "line " + i));

    [Fact]
    [Trait("Req", "PR-CHAT-101")]
    public void Output_over_thirty_lines_shows_its_first_thirty_and_a_link_that_reveals_the_rest_then_Show_less() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        ToolDetailView? view = null;
        AvaloniaWindow? window = null;
        h.Main.OnMain(() =>
        {
            view = new ToolDetailView(Call(ToolCall.Kind.Command, "cat") with { Output = "\n\n" + Lines101(31) + "\n\n" });
            window = Show(view);
            Assert.Contains(Lines101(30), Texts(view));
            Assert.DoesNotContain(Lines101(31), Texts(view));
            var link = Assert.Single(view.GetVisualDescendants().OfType<Button>());
            Assert.Contains("Show full output (31 lines)", Texts(link));
            Click(window, link);
        });
        h.Main.OnMain(() =>
        {
            Assert.Contains(Lines101(31), Texts(view!));
            Assert.Contains("Show less", Texts(Assert.Single(view!.GetVisualDescendants().OfType<Button>())));
            Close(window!);
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-101")]
    public void Thirty_lines_show_whole_with_no_link_and_the_detail_is_capped_at_twelve_lines_in_secondary_monospace() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        h.Main.OnMain(() =>
        {
            var view = new ToolDetailView(Call(ToolCall.Kind.Command, "cat") with { Output = Lines101(30), Detail = "the detail" });
            var window = Show(view);
            Assert.Contains(Lines101(30), Texts(view));
            Assert.Empty(view.GetVisualDescendants().OfType<Button>());
            var detail = view.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == "the detail");
            Assert.Equal(12, detail.MaxLines);
            Assert.Equal(Resolved(ChromeTokens.SecondaryText, detail), BrushColor(detail.Foreground));
            Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Caption, design: SwiftFont.Design.Monospaced).Resolve().FontFamily, detail.FontFamily);
            Close(window);
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-101")]
    public void Each_edit_with_a_diff_shows_its_path_above_its_lines_on_a_rounded_plate_and_a_diffless_edit_shows_nothing() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        h.Main.OnMain(() =>
        {
            var call = Call(ToolCall.Kind.Edit, "e") with
            {
                Edits = new ValueList<FileEdit>([new FileEdit("src/a.swift", "@@ -1 +1 @@\n-a\n+b\n", 1, 1), new FileEdit("src/b.swift", "", 0, 0)]),
            };
            // Inspected without a layout pass: the edits are built with the view, and DiffLinesView's own font
            // (App.Views.Diff.DiffInspector's) does not resolve headless (not investigated further, since nothing in this clause
            // needs a layout pass to verify).
            var view = new ToolDetailView(call);
            Assert.Single(view.GetVisualDescendants().OfType<DiffLinesView>());
            var path = view.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == "src/a.swift");
            Assert.Equal(new Thickness(10, 6), path.Margin);
            Assert.Equal(Resolved(ChromeTokens.SecondaryText, path), BrushColor(path.Foreground));
            var plate = Assert.IsType<Border>(Assert.IsType<StackPanel>(path.Parent).Parent);
            Assert.Equal(new CornerRadius(10), plate.CornerRadius);
            Assert.True(plate.ClipToBounds);
            Assert.Equal(Resolved(SwiftColor.Quaternary.Opacity(0.45), plate), BrushColor(plate.Background));
            Assert.DoesNotContain("src/b.swift", Texts(view));
        });
    });

    // ---- PR-CHAT-104: a finished turn collapses to a header, its final answer and its file summary (:2589-2715).
    // TurnSummary is a required parameter of TurnFinishedBlock and Header() builds the label through TimelineRows.Duration ->
    // icu.dll, so on this container these tests ran under the local, uncommitted /tmp/icushim (see the commit message); on
    // Windows they are ordinary tests. ----

    private const string CodeOnlyReply101 = "```\nplan\n```";

    private static TurnFinishedBlock Block104(ThreadRuntime runtime, Guid turn, IEnumerable<TimelineEntry> content, bool chronological = false, bool canUndo = false,
        IEnumerable<TimelineEntry>? users = null, TurnSummary? summary = null) =>
        new(runtime, turn, summary ?? Summary(turn), new ValueList<TimelineEntry>(users ?? []), new ValueList<TimelineEntry>(content), null, canUndo, chronological);

    private static Border Rule104(Control block) =>
        block.GetVisualDescendants().OfType<Border>().Where(border => border.Height == 1 && border.Opacity == 0.6).SingleOrDefault()!;

    [Fact]
    [Trait("Req", "PR-CHAT-104")]
    public void A_finished_turn_opens_folded_to_its_prompt_a_summary_line_a_rule_and_its_answer() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            var turn = Guid.NewGuid();
            var content = new[] { Reply("narration before the tool"), ToolEntry(Call(ToolCall.Kind.Command, "make"), turn), Reply(CodeOnlyReply101) };
            var block = Block104(h.Runtime, turn, content, users: [UserEntry("do it", turn)]);
            var window = Show(block);
            try
            {
                var stack = Assert.IsType<StackPanel>(block.Child);
                Assert.IsType<UserMessageRow>(stack.Children[0]);
                Assert.Equal(TimelineMetrics.RowSpacing, stack.Children[0].Margin.Bottom); // :2599-2602 the user rows first, a row gap below each
                var header = Assert.IsType<Button>(stack.Children[1]);
                Assert.Equal("Show this turn's steps", ToolTip.GetTip(header));
                Assert.Equal("Show this turn's steps", Avalonia.Automation.AutomationProperties.GetName(header));
                var label = header.GetVisualDescendants().OfType<TextBlock>().Single();
                Assert.Equal("Worked for 5s", label.Text);
                Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Callout).Resolve().FontSize, label.FontSize);
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, label), BrushColor(label.Foreground));
                var chevron = header.GetVisualDescendants().OfType<SwiftImage>().Single();
                Assert.Equal("chevron.right", chevron.SymbolName);
                Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Caption2, SwiftFont.Weight.Semibold), chevron.GetValue(SwiftImage.FontProperty));
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, chevron), BrushColor(chevron.Foreground));
                Assert.Equal(0, ((RotateTransform?)chevron.RenderTransform)?.Angle ?? 0);
                // :2624-2629 a hairline at 0.6 opacity with half a row gap on each side, after the header.
                var rule = Rule104(block);
                Assert.NotNull(rule);
                Assert.Equal(new Thickness(0, TimelineMetrics.RowSpacing / 2), rule.Margin);
                Assert.True(stack.Children.IndexOf(stack.Children.First(child => child.GetVisualDescendants().Append(child).Contains(rule))) > 1);
                // only the replies after the last tool call are the answer; the narration before it stays folded away
                var answer = Assert.Single(block.GetVisualDescendants().OfType<FoldedAnswerView>());
                Assert.Equal(CodeOnlyReply101, Assert.Single(answer.GetVisualDescendants().OfType<MarkdownView>()).Text);
                Assert.Empty(block.GetVisualDescendants().OfType<ToolRow>());
                var column = Assert.IsType<StackPanel>(answer.GetVisualParent());
                Assert.Equal(TimelineMetrics.RowSpacing, column.Spacing); // :2665
                Assert.Equal(TimelineMetrics.RowSpacing, column.Margin.Bottom); // :2679
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-104")]
    public void A_finished_turn_with_nothing_to_show_has_no_rule_and_no_answer() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            var turn = Guid.NewGuid();
            var block = Block104(h.Runtime, turn, [ToolEntry(Call(ToolCall.Kind.Command, "make"), turn)]);
            var window = Show(block);
            try
            {
                Assert.Single(block.GetVisualDescendants().OfType<Button>());
                Assert.Null(Rule104(block));
                Assert.Empty(block.GetVisualDescendants().OfType<FoldedAnswerView>());
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-104")]
    public void The_chevron_opens_the_full_steps_in_a_filled_card_asks_the_timeline_to_reveal_the_end_and_a_second_tap_folds_again() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        TurnFinishedBlock? block = null;
        AvaloniaWindow? window = null;
        var reveals = new List<Animation?>();
        h.Main.OnMain(() =>
        {
            var turn = Guid.NewGuid();
            var content = new[] { ToolEntry(Call(ToolCall.Kind.Command, "make"), turn), Reply(CodeOnlyReply101) };
            block = Block104(h.Runtime, turn, content);
            var host = new Border { Child = block };
            ThreadTimeline.SetRevealTimelineEnd(host, new TimelineRevealBox { Action = () => reveals.Add(Transaction.Current) });
            window = Show(host);
        });
        Button Header() => block!.GetVisualDescendants().OfType<Button>().First();
        void Tick() { for (var i = 0; i < 30; i++) { h.Main.Advance(TimeSpan.FromMilliseconds(16)); manual.Advance(TimeSpan.FromMilliseconds(16)); } }
        try
        {
            h.Main.OnMain(() => Click(window!, Header()));
            Tick();
            h.Main.OnMain(() =>
            {
                Assert.Equal("Hide this turn's steps", ToolTip.GetTip(Header()));
                Assert.Equal("Hide this turn's steps", Avalonia.Automation.AutomationProperties.GetName(Header()));
                Assert.Equal(90, ((RotateTransform?)block!.GetVisualDescendants().OfType<SwiftImage>().First(image => image.SymbolName == "chevron.right").RenderTransform)?.Angle ?? 0);
                Assert.Equal(Animation.Snappy(duration: 0.24), Assert.Single(reveals)); // opening asks the timeline to reveal the end, inside the animation
                var card = Assert.Single(block!.GetVisualDescendants().OfType<Border>(), border => border.Padding == new Thickness(14));
                Assert.Equal(new CornerRadius(TimelineMetrics.PillRadius), card.CornerRadius);
                Assert.Equal(Resolved(SwiftColor.Quaternary.Opacity(0.32), card), BrushColor(card.Background));
                Assert.Equal(TimelineMetrics.RowSpacing, card.Margin.Bottom); // :2663 a response follows
                Assert.NotEmpty(card.GetVisualDescendants().OfType<ToolRow>()); // the full steps
                Assert.Empty(block!.GetVisualDescendants().OfType<FoldedAnswerView>()); // the folded answer gives way to the steps
            });
            h.Main.OnMain(() => Click(window!, Header()));
            Tick();
            h.Main.OnMain(() =>
            {
                Assert.Equal("Show this turn's steps", ToolTip.GetTip(Header()));
                Assert.Equal(0, ((RotateTransform?)block!.GetVisualDescendants().OfType<SwiftImage>().First(image => image.SymbolName == "chevron.right").RenderTransform)?.Angle ?? 0);
                Assert.Single(reveals); // closing does not ask again
                Assert.Empty(block!.GetVisualDescendants().OfType<ToolRow>());
                Assert.Single(block!.GetVisualDescendants().OfType<FoldedAnswerView>());
            });
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-104")]
    // Rewritten for the lead's ruling of 2026-10-04 (OD-0076): the branches themselves still carry no transition and no opacity (no whole-layer fade over a body that can run to
    // thousands of points), but the fold is no longer a cut: the body's height glides on snappy(0.24) and the incoming rows fade in (TimelineRowsTurnFoldMotionTests).
    public void Neither_body_branch_carries_an_opacity_of_its_own_the_height_glide_and_the_row_fades_are_the_blocks() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var clock = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            var turn = Guid.NewGuid();
            var block = Block104(h.Runtime, turn, [Reply(CodeOnlyReply101)]);
            var derived = Invoke(block, "DerivedFor", false);
            var answer = Assert.IsAssignableFrom<Control>(Invoke(block, "Answer", derived));
            var steps = Assert.IsAssignableFrom<Control>(Invoke(block, "Steps", derived, false));
            foreach (var branch in new[] { answer, steps })
            {
                var host = new Panel();
                var window = Show(host);
                try
                {
                    // a fade over a block that can run to thousands of points is a whole-layer composite the renderer may draw as nothing
                    Transaction.WithAnimation(Animation.Snappy(duration: 0.24), () => SwiftTransition.Insert(host, 0, branch));
                    Assert.Equal(1, branch.Opacity);
                    Transaction.WithAnimation(Animation.Snappy(duration: 0.24), () => SwiftTransition.Remove(host, branch));
                    Assert.Empty(host.Children);
                }
                finally { Close(window); }
            }
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-104")]
    public void The_fold_starts_from_the_chronological_flag() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            var turn = Guid.NewGuid();
            var block = Block104(h.Runtime, turn, [ToolEntry(Call(ToolCall.Kind.Command, "make"), turn), Reply(CodeOnlyReply101)], chronological: true);
            var window = Show(block);
            try
            {
                Assert.Equal("Hide this turn's steps", ToolTip.GetTip(block.GetVisualDescendants().OfType<Button>().First()));
                Assert.NotEmpty(block.GetVisualDescendants().OfType<ToolRow>());
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-104")]
    public void The_folded_turn_shows_its_plans_and_its_warning_and_error_notices_but_not_info_notices_after_the_answer() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            var turn = Guid.NewGuid();
            var content = new[]
            {
                ToolEntry(Call(ToolCall.Kind.Command, "make"), turn), Reply("the answer"), PlanEntry(ProposedPlan.State.Proposed),
                NoticeEntry(Notice.Level.Warning, "careful"), NoticeEntry(Notice.Level.Error, "it broke"), NoticeEntry(Notice.Level.Info, "just so you know"),
            };
            var block = Block104(h.Runtime, turn, content);
            var window = Show(block);
            try
            {
                var answer = Assert.Single(block.GetVisualDescendants().OfType<FoldedAnswerView>());
                var column = Assert.IsType<StackPanel>(answer.GetVisualParent());
                Assert.Equal([typeof(FoldedAnswerView), typeof(PlanCard), typeof(NoticeRow), typeof(NoticeRow)], column.Children.Select(child => child.GetType())); // answer, then plans, then notices
                Assert.Equal(["careful", "it broke"], column.Children.OfType<NoticeRow>().Select(row => Assert.Single(row.GetVisualDescendants().OfType<SelectableTextBlock>()).Text));
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-104")]
    public void The_heads_the_turn_sent_out_and_its_file_card_sit_under_the_answer_even_when_folded() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            var turn = Guid.NewGuid();
            var summary = Summary(turn) with { Changes = new FileChangeSummary(new ValueList<FileChangeSummary.File>([new FileChangeSummary.File("src/a.swift", 3, 1, false), new FileChangeSummary.File("src/b.swift", 1, 0, false)]), 4, 1) };
            var agent = ToolEntry(Call(ToolCall.Kind.Agent, "Pip: task"), turn);
            var block = Block104(h.Runtime, turn, [agent, Reply("the answer")], summary: summary);
            var window = Show(block);
            try
            {
                var stack = Assert.IsType<StackPanel>(block.Child);
                var heads = Assert.Single(block.GetVisualDescendants().OfType<HydraHeadsList>());
                var card = Assert.Single(block.GetVisualDescendants().OfType<TurnFileCard>());
                var answer = Assert.Single(block.GetVisualDescendants().OfType<FoldedAnswerView>());
                int At(Control control) => stack.Children.IndexOf(stack.Children.First(child => child == control || child.GetVisualDescendants().Contains(control)));
                Assert.True(At(answer) < At(heads) && At(heads) < At(card), "answer, then heads, then the file card");
                Assert.Equal(TimelineMetrics.RowSpacing, heads.Margin.Bottom); // :2685 a row gap between the heads and the file card
                Assert.Contains("Edited 2 files", Texts(card));
            }
            finally { Close(window); }
        });
    });

    // ---- PR-CHAT-115: a notice renders as plain text at info level and as a tinted pill at warning/error (:2388-2430) ----

    [Fact]
    [Trait("Req", "PR-CHAT-115")]
    public void An_info_notice_is_a_bare_line_of_secondary_selectable_text() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        h.Main.OnMain(() =>
        {
            var row = new NoticeRow(NoticeEntry(Notice.Level.Info, "Context compacted"));
            var window = Show(row);
            var glyph = row.GetVisualDescendants().OfType<SwiftImage>().Single();
            Assert.Equal("info.circle", glyph.SymbolName);
            Assert.Equal(Resolved(SwiftColor.Secondary, glyph), BrushColor(glyph.Foreground));
            var message = Assert.Single(row.GetVisualDescendants().OfType<SelectableTextBlock>());
            Assert.Equal("Context compacted", message.Text);
            Assert.Equal(Resolved(SwiftColor.Secondary, message), BrushColor(message.Foreground));
            Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Callout).Resolve().FontSize, message.FontSize);
            var pill = row.GetVisualDescendants().OfType<Border>().First();
            Assert.Equal(default, pill.Padding);
            Assert.Equal(Resolved(SwiftColor.Clear, pill), BrushColor(pill.Background));
            Assert.Equal(HorizontalAlignment.Stretch, pill.HorizontalAlignment);
            Close(window);
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-115")]
    public void A_warning_or_error_notice_sits_in_a_full_width_pill_tinted_at_ten_percent_of_its_theme_status_colour() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        h.Main.OnMain(() =>
        {
            var warning = new NoticeRow(NoticeEntry(Notice.Level.Warning, "Low on context"));
            var error = new NoticeRow(NoticeEntry(Notice.Level.Error, "The provider failed"));
            var panel = new StackPanel();
            panel.Children.Add(warning);
            panel.Children.Add(error);
            var window = Show(panel);
            foreach (var (row, symbol, color, text) in new[] { (warning, "exclamationmark.triangle", ChromeTokens.Warning, "Low on context"), (error, "exclamationmark.octagon", ChromeTokens.Danger, "The provider failed") })
            {
                var glyph = row.GetVisualDescendants().OfType<SwiftImage>().Single();
                Assert.Equal(symbol, glyph.SymbolName);
                Assert.Equal(Resolved(color, glyph), BrushColor(glyph.Foreground));
                var message = row.GetVisualDescendants().OfType<SelectableTextBlock>().Single();
                Assert.Equal(text, message.Text);
                Assert.Equal(Resolved(SwiftColor.Primary, message), BrushColor(message.Foreground));
                var pill = row.GetVisualDescendants().OfType<Border>().First();
                Assert.Equal(new Thickness(TimelineMetrics.PillLeading, TimelineMetrics.PillVertical, TimelineMetrics.PillTrailing, TimelineMetrics.PillVertical), pill.Padding);
                Assert.Equal(Resolved(color.Opacity(0.1), pill), BrushColor(pill.Background));
                Assert.Equal(new CornerRadius(TimelineMetrics.PillRadius), pill.CornerRadius);
                Assert.Equal(HorizontalAlignment.Stretch, pill.HorizontalAlignment);
            }
            Close(window);
        });
    });

    // ---- PR-CHAT-114: a checklist renders as a card headed by a done-of-total count (:2343-2382) ----

    [Fact]
    [Trait("Req", "PR-CHAT-114")]
    public void A_checklist_is_a_card_headed_N_of_M_done_with_a_glyph_and_line_per_step() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            var entry = new TimelineEntry(new TimelineItem(null, Steps(("Read the code", TodoStep.Status.Done), ("Write the fix", TodoStep.Status.Active), ("Run tests", TodoStep.Status.Pending))));
            var row = new TodoListRow(entry);
            var window = Show(row);
            var header = TextNamed(row, "1 of 3 done");
            Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Caption).Resolve().FontSize, header.FontSize);
            Assert.Equal(Resolved(ChromeTokens.SecondaryText, header), BrushColor(header.Foreground));
            Assert.Equal(["checklist", "checkmark.circle.fill", "circle.dotted.circle", "circle"], Symbols(row));
            var glyphs = row.GetVisualDescendants().OfType<SwiftImage>().ToList();
            Assert.Equal(Resolved(ChromeTokens.SecondaryText, glyphs[0]), BrushColor(glyphs[0].Foreground));
            Assert.Equal(Resolved(ChromeTokens.Accent, glyphs[1]), BrushColor(glyphs[1].Foreground));
            Assert.Equal(Resolved(ChromeTokens.Accent, glyphs[2]), BrushColor(glyphs[2].Foreground));
            Assert.Equal(Resolved(SwiftColor.Tertiary, glyphs[3]), BrushColor(glyphs[3].Foreground));
            var done = TextNamed(row, "Read the code");
            Assert.Equal(Resolved(SwiftColor.Secondary, done), BrushColor(done.Foreground));
            var strike = Assert.Single(done.TextDecorations!);
            Assert.Equal(TextDecorationLocation.Strikethrough, strike.Location);
            var active = TextNamed(row, "Write the fix");
            Assert.Equal(Resolved(SwiftColor.Primary, active), BrushColor(active.Foreground));
            Assert.True(active.TextDecorations is null || active.TextDecorations.Count == 0);
            var card = row.GetVisualDescendants().OfType<Border>().First(border => border.Padding == new Thickness(14));
            Assert.Equal(new CornerRadius(TimelineMetrics.PillRadius), card.CornerRadius);
            Assert.Equal(Resolved(SwiftColor.Quaternary.Opacity(0.32), card), BrushColor(card.Background));
            Assert.Equal(96, row.Padding.Right);
            Close(window);
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-114")]
    public void A_checklist_with_no_steps_draws_nothing_and_an_update_rewrites_the_same_rows_in_place() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var clock = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(clock);
        TodoListRow? row = null;
        TextBlock? before = null;
        AvaloniaWindow? window = null;
        TimelineEntry? entry = null;
        h.Main.OnMain(() =>
        {
            var empty = new TodoListRow(new TimelineEntry(new TimelineItem(null, Steps())));
            entry = new TimelineEntry(new TimelineItem(null, Steps(("One", TodoStep.Status.Active), ("Two", TodoStep.Status.Pending))));
            row = new TodoListRow(entry);
            var panel = new StackPanel();
            panel.Children.Add(empty);
            panel.Children.Add(row);
            window = Show(panel);
            Assert.Empty(empty.GetVisualDescendants().OfType<TextBlock>());
            Assert.Empty(empty.GetVisualDescendants().OfType<Border>());
            before = TextNamed(row, "One");
            entry.Item = entry.Item with { TimelineItemContent = Steps(("One", TodoStep.Status.Done), ("Two", TodoStep.Status.Active)) };
        });
        Animate(h.Main, clock, 2000); // the change is animated (:2372): the glyphs and the count are read once they have settled
        h.Main.OnMain(() =>
        {
            Settle(window!);
            Assert.Contains("1 of 2 done", Texts(row!));
            Assert.Same(before, TextNamed(row!, "One"));
            Assert.Equal(["checklist", "checkmark.circle.fill", "circle.dotted.circle"], Symbols(row!));
            Close(window!);
        });
    });

    // ---- PR-CHAT-111: a plan renders as a chat badge whose title and caption follow its state (:2263-2278, :2318-2337) ----

    [Fact]
    [Trait("Req", "PR-CHAT-111")]
    public void A_plans_badge_title_caption_and_attention_follow_its_state() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            var states = new[] { ProposedPlan.State.Drafting, ProposedPlan.State.Proposed, ProposedPlan.State.Accepted, ProposedPlan.State.Dismissed };
            var cards = states.Select(state => new PlanCard(PlanEntry(state), h.Runtime)).ToArray();
            var panel = new StackPanel();
            foreach (var card in cards) panel.Children.Add(card);
            var window = Show(panel);
            string[] Words(PlanCard card) => Texts(card).Where(text => text.Length != 0).ToArray();
            Assert.Equal(["Writing a plan"], Words(cards[0]));
            Assert.Equal(["Plan ready", "Implement or dismiss"], Words(cards[1]));
            Assert.Equal(["Plan", "Approved"], Words(cards[2]));
            Assert.Equal(["Plan", "Dismissed"], Words(cards[3]));
            // :2335-2337 only a proposed plan draws the attention dots.
            Assert.Equal([false, true, false, false], cards.Select(card => card.GetVisualDescendants().OfType<DotFieldControl>().Any()));
            // :2272-2276 the spinner while drafting, the clipboard in the tint otherwise.
            var spinner = Assert.Single(cards[0].GetVisualDescendants().OfType<Marrow.UI.Port.Nth.NthStatusMark>()); // OD-0136 amendment 2, step 8: the 14 px spinner of the nth status slot (was the 3-cell working spinner)
            Assert.Equal(18, Assert.IsType<Border>(spinner.Parent).Width); // ChatBadge gives its glyph an 18 x 18 frame (:26); the spinner inside is 14 Assert.Equal(Marrow.UI.Port.Nth.NthStatus.Working, spinner.Status); Assert.Equal(14, spinner.MarkSize);
            Assert.DoesNotContain("list.bullet.clipboard", Symbols(cards[0]));
            var clipboard = cards[1].GetVisualDescendants().OfType<SwiftImage>().Single(image => image.SymbolName == "list.bullet.clipboard");
            Assert.Equal(Resolved(ChromeTokens.Accent, clipboard), BrushColor(clipboard.Foreground));
            Assert.All(cards, card => Assert.Contains("chevron.right", Symbols(card)));
            Assert.All(cards, card => Assert.True(card.GetVisualDescendants().OfType<Button>().First().IsEnabled));
            Close(window);
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-111")]
    public void A_plan_with_empty_markdown_has_no_chevron_and_cannot_be_opened() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            var card = new PlanCard(PlanEntry(ProposedPlan.State.Drafting, markdown: ""), h.Runtime);
            var window = Show(card);
            Assert.DoesNotContain("chevron.right", Symbols(card));
            Assert.False(card.GetVisualDescendants().OfType<Button>().First().IsEnabled);
            Close(window);
        });
    });

    // ---- PR-CHAT-143: HydraReportRow - a pill on the user's side naming who is done, the report one tap away
    // (:241-373). The title, the mark or overlapped glyphs, the chevron, the pill chrome, the disabled no-tooltip
    // pill, and the tap that opens the report. ----

    private static UserMessage Report143(string text, params long[] heads) => new() { Text = text, HydraHeads = new ValueList<long>(heads) };
    private static UserMessage Note143(string text) => new() { Text = text, HydraHeads = null };

    private static string Title143(UserMessage message)
    {
        var row = new HydraReportRow(message);
        var window = Show(row);
        try { return LogicalQuery.SelfAndDescendants<TextBlock>(row).Single(block => block.MaxLines == 1).Text!; }
        finally { Close(window); }
    }

    // Two mutants of TimelineRows.cs ReportOutcomes are equivalent, not survivors: `colon < 3` -> `colon < 4` (the line
    // begins with "## ", so a colon at index 3 means an empty head name, and no roster persona has one), and lowercasing
    // the whole line instead of from the colon on (a marker before the colon lands in the head's name, which then matches
    // no roster persona). Both leave every output identical.
    [Fact]
    [Trait("Req", "PR-CHAT-143")]
    public void A_heads_report_names_who_is_done_and_a_failed_or_stopped_head_with_its_outcome() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            Assert.Equal("Hank is done", Title143(Report143("## Hank: tidy\nall good", 0)));
            Assert.Equal("Hank and Walter are done", Title143(Report143("## Hank: tidy\n## Walter: build", 0, 1)));
            Assert.Equal("Hank, Walter and Ada are done", Title143(Report143("## Hank: a\n## Walter: b\n## Ada: c", 0, 1, 2)));
            Assert.Equal("Hank and Ada are done, Walter failed", Title143(Report143("## Hank: a\n## Walter: b (Failed: tests)\n## Ada: c", 0, 1, 2)));
            Assert.Equal("Ada stopped", Title143(Report143("  ## Ada: c (stopped by you)", 2)));
            Assert.Equal("Hank is done, Walter failed, Ada stopped", Title143(Report143("## Hank: a\n## Walter: b (failed)\n## Ada: c (STOPPED)", 0, 1, 2)));
            // A failed head is listed before a stopped one in the caller's order, each after the done heads.
            Assert.Equal("Ada is done, Hank stopped, Walter failed", Title143(Report143("## Hank: a (stopped)\n## Walter: b (failed)\n## Ada: c", 0, 1, 2)));
            // The marker needs its opening parenthesis, and is only read from the colon on.
            Assert.Equal("Hank is done", Title143(Report143("## Hank: failed once, fixed", 0)));
            Assert.Equal("Hank is done", Title143(Report143("## Hank: a stopped) or failed) one", 0)));
            Assert.Equal("Hank is done", Title143(Report143("## Hank (failed): a", 0)));
            // A line names a head only when it begins with ## and holds a colon.
            Assert.Equal("Hank is done", Title143(Report143("Hank: b (failed)\n### Hank: c (failed)\n#Hank: d (failed)", 0)));
            Assert.Equal("Hank is done", Title143(Report143("## Hank b (failed)", 0)));
            Assert.Equal("Walter failed", Title143(Report143("body\n   ## Walter :  x (failed)\nmore", 1)));
            // :350-351 one line, capped at 140 characters.
            var many = Enumerable.Range(0, 25).Select(i => (long)i).ToArray();
            var names = many.Select(i => HydraRoster.Persona(i).Name).ToList();
            var full = string.Join(", ", names.Take(24)) + " and " + names[24] + " are done";
            Assert.True(full.Length > 140);
            Assert.Equal(full[..139] + "\U00002026", Title143(Report143("report", many))); // U+2026 HORIZONTAL ELLIPSIS
            Assert.Equal("Hank failed", Title143(Report143("## Hank: a (failed)\n\n\n", 0)));
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-143")]
    public void A_heads_report_pill_overlaps_the_glyphs_and_sits_in_the_tinted_pill_with_96_points_clear_trailing() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            var row = new HydraReportRow(Report143("## Hank: a\nbody\n## Walter: b\nbody", 0, 1));
            var window = Show(row);
            try
            {
                var glyphs = LogicalQuery.SelfAndDescendants<HydraGlyph>(row).ToList();
                Assert.Equal(["Hank", "Walter"], glyphs.Select(glyph => glyph.Persona.Name));
                Assert.All(glyphs, glyph => Assert.Equal(18, glyph.Size));
                Assert.Equal(-4, Assert.IsType<HStack>(glyphs[0].Parent).Spacing);
                Assert.Equal(8, Assert.IsType<HStack>(glyphs[0].Parent!.Parent).Spacing);
                Assert.Empty(LogicalQuery.SelfAndDescendants<HydraMarkImage>(row));
                var title = TextNamed(row, "Hank and Walter are done");
                Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Callout, SwiftFont.Weight.Medium).Resolve().FontSize, title.FontSize);
                Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Callout, SwiftFont.Weight.Medium).Resolve().FontWeight, title.FontWeight);
                Assert.Equal(Resolved(ChromeTokens.PrimaryText.Opacity(0.9), title), BrushColor(title.Foreground));
                Assert.Equal(1, title.MaxLines);
                Assert.Equal(TextTrimming.CharacterEllipsis, title.TextTrimming);
                var chevron = LogicalQuery.SelfAndDescendants<SwiftImage>(row).Single(image => image.SymbolName == "chevron.right");
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, chevron), BrushColor(chevron.Foreground));
                Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Caption2, SwiftFont.Weight.Semibold), chevron.GetValue(SwiftImage.FontProperty));
                var button = LogicalQuery.SelfAndDescendants<Button>(row).Single();
                Assert.True(button.IsEnabled);
                Assert.Equal("Show the message", ToolTip.GetTip(button));
                var pill = LogicalQuery.SelfAndDescendants<Border>(row).First(border => border.Padding == new Thickness(TimelineMetrics.PillLeading, TimelineMetrics.PillVertical, TimelineMetrics.PillTrailing, TimelineMetrics.PillVertical));
                Assert.Equal(Resolved(ChromeTokens.Accent.Opacity(0.1), pill), BrushColor(pill.Background));
                Assert.Equal(new CornerRadius(TimelineMetrics.PillRadius), pill.CornerRadius);
                Assert.Equal(HorizontalAlignment.Left, pill.HorizontalAlignment);
                Assert.Equal(new Thickness(0, 0, 96, 0), row.Padding);
                Assert.Equal("Hank and Walter are done", Avalonia.Automation.AutomationProperties.GetName(row));
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-143")]
    public void A_note_from_Hydra_shows_its_first_line_less_one_stop_beside_the_mark_and_a_pill_with_nothing_behind_it_is_disabled_with_no_tip() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            Assert.Equal("Held the heads back", HydraReportRow.WithoutTrailingStop("Held the heads back."));
            Assert.Equal("Wait..", HydraReportRow.WithoutTrailingStop("Wait..."));
            Assert.Equal("No stop", HydraReportRow.WithoutTrailingStop("No stop"));
            // The title is the first line only, cut at the first newline.
            Assert.Equal("Held the heads back", Title143(Note143("Held the heads back.\n\n  The lead is busy.  ")));
            Assert.Equal("First. line", Title143(Note143("First. line.\nsecond.")));
            Assert.Equal("Hydra", Title143(Note143("")));
            var note = new HydraReportRow(Note143("Held the heads back.\n\n  The lead is busy.  "));
            var bare = new HydraReportRow(Report143("Just a title."));
            var panel = new StackPanel();
            panel.Children.Add(note);
            panel.Children.Add(bare);
            var window = Show(panel);
            try
            {
                var mark = Assert.Single(LogicalQuery.SelfAndDescendants<HydraMarkImage>(note));
                Assert.Equal(8, Assert.IsType<HStack>(mark.Parent).Spacing);
                Assert.Equal(18, mark.Width);
                Assert.Equal(18, mark.Height);
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, mark), BrushColor(LogicalQuery.SelfAndDescendants<SwiftImage>(mark).Single(image => image.SymbolName != "chevron.right").Foreground));
                Assert.Empty(LogicalQuery.SelfAndDescendants<HydraGlyph>(note));
                var noteButton = LogicalQuery.SelfAndDescendants<Button>(note).Single();
                Assert.True(noteButton.IsEnabled);
                Assert.Equal("Show the message", ToolTip.GetTip(noteButton));
                Assert.Contains("chevron.right", Symbols(note));
                Assert.Equal(new Thickness(0, 0, 96, 0), note.Padding);
                // Nothing behind the title: a disabled control, no chevron and no tooltip at all.
                var bareButton = LogicalQuery.SelfAndDescendants<Button>(bare).Single();
                Assert.False(bareButton.IsEnabled);
                Assert.Null(ToolTip.GetTip(bareButton));
                Assert.DoesNotContain("chevron.right", Symbols(bare));
                // With a leading address the pill's button and the Open link sit 8 points apart in one row.
                var linked = new HydraReportRow(Note143("Merged.\nhttps://x.example/a\nmore"));
                var linkedWindow = Show(linked);
                try
                {
                    var open = LogicalQuery.SelfAndDescendants<Button>(linked).Single(button => LogicalTexts(button).Contains("Open"));
                    var line = Assert.IsType<HStack>(open.Parent);
                    Assert.Equal(8, line.Spacing);
                    Assert.Equal(2, line.Children.Count);
                }
                finally { Close(linkedWindow); }
                // A bare note (no roster at all) is the same.
                var bareNote = new HydraReportRow(Note143("Just a title.\n   \n"));
                var bareWindow = Show(bareNote);
                try
                {
                    var button = LogicalQuery.SelfAndDescendants<Button>(bareNote).Single();
                    Assert.False(button.IsEnabled);
                    Assert.Null(ToolTip.GetTip(button));
                    Assert.DoesNotContain("chevron.right", Symbols(bareNote));
                }
                finally { Close(bareWindow); }
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-143")]
    public void Tapping_a_pill_opens_the_report_a_heads_report_in_the_heads_popover_and_a_note_in_the_note_popover() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        HydraReportRow? note = null, heads = null;
        AvaloniaWindow? window = null;
        h.Main.OnMain(() =>
        {
            note = new HydraReportRow(Note143("Held back.\n\n  the lead is busy\n\nand then some  "));
            heads = new HydraReportRow(Report143("## Hank: a\n```\nhank body\n```", 0));
            _ = AppCurrentEvent.CurrentWindow; // warms the lazy event subscription before Show presses its key
            var panel = new StackPanel();
            panel.Children.Add(note);
            panel.Children.Add(heads);
            window = Show(panel);
            Press(LogicalQuery.SelfAndDescendants<Button>(note).Single());
        });
        try
        {
            h.Main.OnMain(() =>
            {
                Settle(window!);
                var shown = Assert.Single(presenter.Calls, call => call.Presented);
                var popover = Assert.Single(LogicalQuery.SelfAndDescendants<HydraReportPopover>(shown.Popup.Child!));
                Assert.Empty(LogicalQuery.SelfAndDescendants<HydraReportsPopover>(shown.Popup.Child!));
                // The popover shows the note's body (what follows the first line, trimmed), not its title.
                Assert.Equal<MarkdownBlock>([new MarkdownBlock.Paragraph("the lead is busy"), new MarkdownBlock.Paragraph("and then some")], LogicalQuery.SelfAndDescendants<MarkdownBlockView>(popover).Select(view => view.Block).ToList());
                // Tapping again closes it.
                Press(LogicalQuery.SelfAndDescendants<Button>(note!).Single());
            });
            h.Main.OnMain(() =>
            {
                Settle(window!);
                Press(LogicalQuery.SelfAndDescendants<Button>(heads!).Single());
            });
            h.Main.OnMain(() =>
            {
                Settle(window!);
                var shown = presenter.Calls.Last(call => call.Presented);
                var popover = Assert.Single(LogicalQuery.SelfAndDescendants<HydraReportsPopover>(shown.Popup.Child!));
                Assert.Empty(LogicalQuery.SelfAndDescendants<HydraReportPopover>(shown.Popup.Child!));
                Assert.Equal(460, popover.Width);
                // It carries the whole message the row was given, each head's section in it.
                Assert.Contains(LogicalQuery.SelfAndDescendants<MarkdownBlockView>(popover), view => view.Block is MarkdownBlock.Code { CodeText: "hank body" });
            });
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    // ---- PR-CHAT-147: HydraBriefRow - the lead's brief to a head as a pill that reads as cooking until the head
    // answers (:537-595). ----

    private static void SetPrivate(object target, string property, object? value) =>
        target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetSetMethod(true)!.Invoke(target, [value]);

    private static (HydraBriefRow Row, TimelineEntry Brief, UserMessage Message) BriefRow147(ThreadRuntimePart12Harness h, string text = "Build the thing")
    {
        h.Runtime.RehearseTurn(text);
        var entry = h.Runtime.Entries.Last(candidate => candidate.TimelineEntryKind == TimelineEntry.Kind.User);
        var message = ((TimelineItem.Content.User)entry.Item.TimelineItemContent).Value;
        return (new HydraBriefRow(message, entry, h.Runtime), entry, message);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-147")]
    public void A_brief_reads_as_cooking_with_the_shimmer_until_its_turn_has_output_and_then_as_the_brief() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        HydraBriefRow? row = null;
        AvaloniaWindow? window = null;
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            row = BriefRow147(h).Row;
            window = Show(row);
        });
        try
        {
            h.Main.OnMain(() =>
            {
                var title = TextNamed(row!, "Hank is starting to cook\U00002026"); // U+2026 HORIZONTAL ELLIPSIS
                Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Callout, SwiftFont.Weight.Medium).Resolve().FontSize, title.FontSize);
                Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Callout, SwiftFont.Weight.Medium).Resolve().FontWeight, title.FontWeight);
                Assert.Equal(Resolved(ChromeTokens.PrimaryText.Opacity(0.9), title), BrushColor(title.Foreground));
                Assert.Equal(1, title.MaxLines);
                Assert.Equal(TextTrimming.CharacterEllipsis, title.TextTrimming);
                // Shimmering while cooking: the text sits in the shimmer's own panel, beside its masked band layer.
                var shimmer = Assert.IsType<Panel>(title.Parent);
                Assert.Contains(shimmer.Children, child => child is Panel { OpacityMask: not null });
                var glyph = Assert.Single(LogicalQuery.SelfAndDescendants<HydraGlyph>(row!));
                Assert.Equal(18, glyph.Size);
                Assert.True(glyph.IsRunning);
                Assert.Equal(8, Assert.IsType<HStack>(glyph.Parent).Spacing);
                var chevron = LogicalQuery.SelfAndDescendants<SwiftImage>(row!).Single(image => image.SymbolName == "chevron.right");
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, chevron), BrushColor(chevron.Foreground));
                Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Caption2, SwiftFont.Weight.Semibold), chevron.GetValue(SwiftImage.FontProperty));
                var button = LogicalQuery.SelfAndDescendants<Button>(row!).Single();
                Assert.Equal("Show the brief", ToolTip.GetTip(button));
                Assert.False(button.Focusable);
                Assert.Equal("Hank is starting to cook\U00002026", Avalonia.Automation.AutomationProperties.GetName(button));
                var pill = LogicalQuery.SelfAndDescendants<Border>(row!).First(border => border.Padding == new Thickness(TimelineMetrics.PillLeading, TimelineMetrics.PillVertical, TimelineMetrics.PillTrailing, TimelineMetrics.PillVertical));
                Assert.Equal(Resolved(ChromeTokens.Accent.Opacity(0.1), pill), BrushColor(pill.Background));
                Assert.Equal(new CornerRadius(TimelineMetrics.PillRadius), pill.CornerRadius);
                Assert.Equal(HorizontalAlignment.Right, pill.HorizontalAlignment);
                Assert.Equal(HorizontalAlignment.Right, LogicalQuery.SelfAndDescendants<StackPanel>(row!).First().HorizontalAlignment);
                Assert.Equal(new Thickness(96, 0, 0, 0), row!.Padding);
                h.Runtime.Rehearse(new ProviderEvent.ToolStarted("tool-1", Call(ToolCall.Kind.Read, "Read a", ToolCall.Status.Running)));
            });
            h.Main.OnMain(() =>
            {
                Settle(window!);
                // The head has answered: straight apostrophe, no shimmer, the glyph at rest.
                var title = TextNamed(row!, "Hank's brief");
                Assert.IsType<HStack>(title.Parent);
                Assert.False(Assert.Single(LogicalQuery.SelfAndDescendants<HydraGlyph>(row!)).IsRunning);
                Assert.Equal("Hank's brief", Avalonia.Automation.AutomationProperties.GetName(LogicalQuery.SelfAndDescendants<Button>(row!).Single()));
            });
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-147")]
    public void A_brief_in_a_thread_that_is_not_running_never_reads_as_cooking() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        HydraBriefRow? row = null;
        AvaloniaWindow? window = null;
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            row = BriefRow147(h).Row;
            SetPrivate(h.Runtime, "Phase", RuntimePhase.Idle);
            window = Show(row);
        });
        try
        {
            h.Main.OnMain(() =>
            {
                Settle(window!);
                TextNamed(row!, "Hank's brief");
                Assert.False(Assert.Single(LogicalQuery.SelfAndDescendants<HydraGlyph>(row!)).IsRunning);
                // And when the thread starts running with nothing produced, it cooks.
                SetPrivate(h.Runtime, "Phase", RuntimePhase.Running);
            });
            h.Main.OnMain(() =>
            {
                Settle(window!);
                TextNamed(row!, "Hank is starting to cook\U00002026");
                Assert.True(Assert.Single(LogicalQuery.SelfAndDescendants<HydraGlyph>(row!)).IsRunning);
            });
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-147")]
    public void The_brief_belongs_to_the_threads_own_head_and_falls_back_to_the_first_place_in_the_roster() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        var second = HydraRoster.Persona(1).Name;
        Assert.NotEqual(HydraRoster.Persona(0).Name, second);
        HydraBriefRow? row = null;
        AvaloniaWindow? window = null;
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            h.App.UpdateThread(h.Thread.Id, thread => thread with { Hydra = new HydraHeadInfo(1, "the task", HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated) });
            row = BriefRow147(h).Row;
            window = Show(row);
        });
        try
        {
            h.Main.OnMain(() =>
            {
                Settle(window!);
                TextNamed(row!, second + " is starting to cook\U00002026");
                Assert.Equal(second, Assert.Single(LogicalQuery.SelfAndDescendants<HydraGlyph>(row!)).Persona.Name);
                // The thread stops being a head: the row follows it back to the first place.
                h.App.UpdateThread(h.Thread.Id, thread => thread with { Hydra = null });
            });
            h.Main.OnMain(() =>
            {
                Settle(window!);
                TextNamed(row!, HydraRoster.Persona(0).Name + " is starting to cook\U00002026");
            });
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-147")]
    public void The_output_scan_is_replayed_until_the_entry_count_moves() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        HydraBriefRow? row = null;
        AvaloniaWindow? window = null;
        TimelineEntry? brief = null;
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            brief = BriefRow147(h).Brief;
            h.Runtime.Rehearse(new ProviderEvent.ToolStarted("tool-1", Call(ToolCall.Kind.Read, "Read a", ToolCall.Status.Running)));
            Assert.Equal(2, h.Runtime.Entries.Count);
            SetPrivate(h.Runtime, "Phase", RuntimePhase.Idle);
            row = new HydraBriefRow(((TimelineItem.Content.User)brief.Item.TimelineItemContent).Value, brief, h.Runtime);
            window = Show(row);
        });
        try
        {
            h.Main.OnMain(() =>
            {
                Settle(window!);
                TextNamed(row!, "Hank's brief"); // scanned at two entries: the tool shares the brief's turn
                // The same two entries in count, but the second one no longer belongs to the brief's turn ...
                SetPrivate(h.Runtime, "Entries", new ValueList<TimelineEntry>([brief!, Reply("elsewhere")]));
                // ... and the thread starts running, which rebuilds the row without the count having moved.
                SetPrivate(h.Runtime, "Phase", RuntimePhase.Running);
            });
            h.Main.OnMain(() =>
            {
                Settle(window!);
                TextNamed(row!, "Hank's brief"); // replayed, not scanned again
                // The count moves: scanned afresh, and nothing shares the brief's turn now.
                SetPrivate(h.Runtime, "Entries", new ValueList<TimelineEntry>([brief!]));
            });
            h.Main.OnMain(() =>
            {
                Settle(window!);
                TextNamed(row!, "Hank is starting to cook\U00002026");
            });
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-147")]
    public void A_brief_with_no_turn_of_its_own_has_no_output_even_when_other_entries_have_none_either() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        HydraBriefRow? row = null;
        AvaloniaWindow? window = null;
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            var brief = new TimelineEntry(new TimelineItem(null, new TimelineItem.Content.User(new UserMessage { Text = "brief" })));
            var other = new TimelineEntry(new TimelineItem(null, new TimelineItem.Content.User(new UserMessage { Text = "other" })));
            h.Runtime.RehearseTurn(null); // running, so a brief with no output reads as cooking
            SetPrivate(h.Runtime, "Entries", new ValueList<TimelineEntry>([brief, other]));
            row = new HydraBriefRow(((TimelineItem.Content.User)brief.Item.TimelineItemContent).Value, brief, h.Runtime);
            window = Show(row);
        });
        try
        {
            h.Main.OnMain(() =>
            {
                Settle(window!);
                TextNamed(row!, "Hank is starting to cook\U00002026");
            });
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-147")]
    public void Tapping_the_brief_pill_opens_the_full_brief() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        HydraBriefRow? row = null;
        AvaloniaWindow? window = null;
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            _ = AppCurrentEvent.CurrentWindow; // warms the lazy event subscription before Show presses its key
            h.App.UpdateThread(h.Thread.Id, thread => thread with { Hydra = new HydraHeadInfo(1, "the task", HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated) });
            row = BriefRow147(h, "Build the thing, in full").Row;
            window = Show(row);
            Press(LogicalQuery.SelfAndDescendants<Button>(row).Single());
        });
        try
        {
            h.Main.OnMain(() =>
            {
                Settle(window!);
                var shown = Assert.Single(presenter.Calls, call => call.Presented);
                var popover = Assert.Single(LogicalQuery.SelfAndDescendants<HydraBriefPopover>(shown.Popup.Child!));
                Assert.Equal("Build the thing, in full", popover.Text);
                Assert.Equal("the task", popover.Task);
                Assert.Equal(HydraRoster.Persona(1).Name, popover.Persona.Name);
            });
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });


    private static double FontSize(SwiftFont font) => font.Resolve().FontSize;

    private static ToolRow ToolRowFor(ThreadRuntime runtime, ToolCall call, System.Guid? turn = null) =>
        new(ToolEntry(call, turn), runtime);

    // A lead with head threads under it and the lead's runtime: ThreadRuntimePart12Harness's setup with the heads added.
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

    private static HydraHeadInfo Head(long index, HydraHeadInfo.Status status) =>
        new HydraHeadInfo(index, "task", HydraHeadInfo.Kind.Droppy, HydraHeadInfo.Origin.Delegated) with { HydraHeadInfoStatus = status };



    // ---- PR-CHAT-145: a report's markdown is parsed off the main thread as the pill appears, per head for a batch
    // (:317-324). ----

    [Fact]
    [Trait("Req", "PR-CHAT-145")]
    public void A_reports_markdown_is_warmed_as_the_pill_appears_the_whole_body_for_a_note_and_each_head_for_a_batch() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var nonce = System.Guid.NewGuid().ToString("N");
        var noteBody = "```\nnote " + nonce + "\n```";
        var linkedBody = "https://x.example/" + nonce + "\n```\nlinked " + nonce + "\n```";
        var linkedDetails = "```\nlinked " + nonce + "\n```";
        var linkOnlyBody = "https://x.example/only-" + nonce;
        var batch = "## Hank: a\n```\nhank " + nonce + "\n```\n## Walter: b\n```\nwalter " + nonce + "\n```";
        var heads = HydraReportDigest.Parse(batch).Heads.Select(head => head.Body).ToList();
        AvaloniaWindow? window = null;
        h.Main.OnMain(() =>
        {
            Assert.Equal(2, heads.Count);
            // Nothing is warm yet, so the assertions below can only be met by the rows.
            Assert.Equal(5, MarkdownView.WarmTexts([noteBody, linkedBody, .. heads, linkOnlyBody]).Count);
            var panel = new StackPanel();
            panel.Children.Add(new HydraReportRow(Note143("Held back.\n" + noteBody)));
            panel.Children.Add(new HydraReportRow(Note143("Merged.\n" + linkedBody)));
            panel.Children.Add(new HydraReportRow(Note143("Merged.\n" + linkOnlyBody)));
            panel.Children.Add(new HydraReportRow(Report143(batch, 0, 1)));
            window = Show(panel);
        });
        try
        {
            h.Main.Loop.RunUntil(() => MarkdownView.WarmTexts([noteBody, linkedBody, .. heads]).Count == 0, AppModelHarness.Bound);
            h.Main.Loop.RunUntilIdle();
            h.Main.OnMain(() =>
            {
                // A batch warms head by head, never as one text.
                Assert.Equal([batch], MarkdownView.WarmTexts([batch]));
                // A note warms its whole body, the address line and all, not just what is behind the address.
                Assert.Equal([linkedDetails], MarkdownView.WarmTexts([linkedDetails]));
                // A note with nothing behind its address has nothing to show, so nothing is warmed.
                Assert.Equal([linkOnlyBody], MarkdownView.WarmTexts([linkOnlyBody]));
            });
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-145")]
    public void A_row_that_goes_away_before_its_parse_lands_warms_nothing() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var nonce = System.Guid.NewGuid().ToString("N");
        var goneBody = "```\ngone " + nonce + "\n```";
        var keptBody = "```\nkept " + nonce + "\n```";
        AvaloniaWindow? window = null;
        AvaloniaWindow? gone = null;
        h.Main.OnMain(() =>
        {
            gone = Show(new HydraReportRow(Note143("Held back.\n" + goneBody)));
            Close(gone); // the row leaves the tree before the main actor has run the parse's continuation
            window = Show(new HydraReportRow(Note143("Held back.\n" + keptBody)));
        });
        try
        {
            h.Main.Loop.RunUntil(() => MarkdownView.WarmTexts([keptBody]).Count == 0, AppModelHarness.Bound);
            h.Main.Loop.RunUntilIdle();
            h.Main.OnMain(() => Assert.Equal([goneBody], MarkdownView.WarmTexts([goneBody])));
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });



    // ---- PR-CHAT-146: HydraReportPopover - 460 wide, a scroller up to 460 high, callout text with 12-point markdown,
    // every block laid out at once, selectable, padded 16 by 14 (:607-639). ----

    private static readonly string TallReport146 = string.Join("\n\n", Enumerable.Range(1, 80).Select(i => "paragraph " + i));
    private static readonly string MediumReport146 = string.Join("\n\n", Enumerable.Range(1, 8).Select(i => "paragraph " + i));

    [Fact]
    [Trait("Req", "PR-CHAT-146")]
    public void The_report_popover_is_460_wide_with_a_scroller_that_opens_at_its_final_height_up_to_460() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        h.Main.OnMain(() =>
        {
            // No window and no layout pass: the whole body has already sized the scroller, so it opens at its final size.
            var tall = new HydraReportPopover(TallReport146);
            Assert.Equal(460, tall.Width);
            Assert.Equal(460, Assert.IsType<ScrollViewer>(Assert.Single(LogicalQuery.SelfAndDescendants<PopoverScroll>(tall)).Child).Height);
            var medium = new HydraReportPopover(MediumReport146);
            var height = Assert.IsType<ScrollViewer>(Assert.Single(LogicalQuery.SelfAndDescendants<PopoverScroll>(medium)).Child).Height;
            Assert.InRange(height, 81, 459);
            // The width is the caller's to choose, and the scroller is measured at it.
            var narrow = new HydraReportPopover(MediumReport146, width: 200);
            Assert.Equal(200, narrow.Width);
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-146")]
    public void The_report_popover_lays_every_block_out_as_selectable_12_point_markdown_in_the_callout_style_padded_16_by_14() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            const string text = "one\n\n```\ntwo\n```\n\nthree";
            var popover = new HydraReportPopover(text);
            var window = Show(popover);
            try
            {
                var blocks = LogicalQuery.SelfAndDescendants<MarkdownBlockView>(popover).ToList();
                Assert.Equal(MarkdownView.Blocks(text).Count, blocks.Count);
                Assert.Equal(3, blocks.Count);
                var stack = Assert.IsType<StackPanel>(blocks[0].Parent);
                Assert.Equal(10, stack.Spacing);
                Assert.All(blocks, block =>
                {
                    Assert.Equal(12, MarkdownView.GetMarkdownPointSize(block));
                    Assert.False(MarkdownView.GetMarkdownDimmed(block));
                    Assert.True(MarkdownView.GetMarkdownBlockSelection(block));
                });
                Assert.Equal(SwiftFont.System(SwiftFont.TextStyle.Callout).Resolve().FontSize, TextElement.GetFontSize(stack));
                Assert.Equal(SwiftFont.System(SwiftFont.TextStyle.Callout).Resolve().FontWeight, TextElement.GetFontWeight(stack));
                Assert.Equal(new Thickness(16, 14), Assert.IsAssignableFrom<Decorator>(stack.Parent).Padding);
            }
            finally { Close(window); }
        });
    });


    [Fact]
    [Trait("Req", "PR-CHAT-149")]
    public void The_delegation_card_says_Summoning_heads_while_streaming_then_names_the_count_or_the_one_head()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
            runtime.OnMain(() =>
            {
                const string hank = "{\"task\": \"Tidy the popover\", \"prompt\": \"Tidy it.\", \"name\": \"Hank\"}";
                const string anon = "{\"task\": \"Write tests\", \"prompt\": \"Write them.\"}";
                string[] Words(string json, bool sent = false)
                {
                    var block = new HydraDelegationBlock(json, sent);
                    var window = Show(block);
                    var words = Texts(block);
                    Close(window);
                    return words;
                }
                // Streaming: the first brief has closed, the block has not.
                var streaming = Words("[\n" + hank + ",\n{\"task\": \"Wri");
                Assert.Equal("Summoning heads", streaming[0]);
                Assert.Contains("Tidy the popover", streaming);
                Assert.Contains("Writing the next brief\U00002026", streaming); // U+2026 HORIZONTAL ELLIPSIS
                Assert.Equal(["Summoning heads"], Words("[\n{\"task\": \"a"));
                Assert.Equal(["Summoning heads"], Words("not json at all"));
                Assert.Equal("Summoning Hank", Words("[\n" + hank + "\n]")[0]);
                Assert.Equal("Summoning a head", Words("[\n" + anon + "\n]")[0]);
                Assert.Equal("Summoning 2 heads", Words("[\n" + hank + ",\n" + anon + "\n]")[0]);
                Assert.Equal("Summoned 2 heads", Words("[\n" + hank + ",\n" + anon + "\n]", sent: true)[0]);
                Assert.DoesNotContain("Writing the next brief\U00002026", Words("[\n" + hank + ",\n" + anon + "\n]")); // U+2026 HORIZONTAL ELLIPSIS
                // Sent while still streaming: the block is done being written.
                Assert.Equal("Summoned Hank", Words("[\n" + hank + ",\n{\"ta", sent: true)[0]);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-149")]
    public void The_delegation_card_lists_each_head_with_its_glyph_task_and_other_project_and_once_sent_checks_and_sweeps()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
            runtime.OnMain(() =>
            {
                const string json = "[\n{\"task\": \"Tidy the popover\", \"prompt\": \"p\", \"name\": \"Walter\", \"project\": \"/Users/me/code/website\"},\n{\"task\": \"Write tests\", \"prompt\": \"p\", \"project\": \"docs\"}\n]";
                var block = new HydraDelegationBlock(json, sent: true);
                var window = Show(block);
                var marks = LogicalQuery.SelfAndDescendants<HydraMarkImage>(block).ToList();
                Assert.Equal(2, marks.Count);
                Assert.All(marks, mark => Assert.Equal(16, mark.Width));
                Assert.All(marks, mark => Assert.Equal(Resolved(ChromeTokens.SecondaryText, mark), BrushColor(LogicalQuery.SelfAndDescendants<SwiftImage>(mark).Single().Foreground)));
                var glyph = Assert.Single(LogicalQuery.SelfAndDescendants<HydraGlyph>(block));
                Assert.Equal("Walter", glyph.Persona.Name);
                Assert.Equal(16, glyph.Size);
                var task = TextNamed(block, "Tidy the popover");
                Assert.Equal(FontSize(ChatFont.Chat(SwiftFont.TextStyle.Caption)), task.FontSize);
                Assert.Equal(Resolved(ChromeTokens.PrimaryText.Opacity(0.75), task), BrushColor(task.Foreground));
                var project = TextNamed(block, "website");
                Assert.Contains("docs", Texts(block));
                var tag = (Control)project.Parent!;
                Assert.Equal("in website", AutomationProperties.GetName(tag));
                Assert.Equal(2, tag.Margin.Left);
                Assert.Contains("arrow.right", Symbols(tag));
                Assert.Equal(FontSize(ChatFont.Chat(SwiftFont.TextStyle.Caption2, SwiftFont.Weight.Medium)), project.FontSize);
                var title = TextNamed(block, "Summoned 2 heads");
                Assert.Equal(FontSize(ChatFont.Chat(SwiftFont.TextStyle.Callout, SwiftFont.Weight.Medium)), title.FontSize);
                var check = LogicalQuery.SelfAndDescendants<SwiftImage>(block).Single(image => image.SymbolName == "checkmark.circle.fill");
                Assert.Equal("sent", AutomationProperties.GetName(check));
                Assert.Single(LogicalQuery.SelfAndDescendants<DotFieldSweep>(block));
                var pill = LogicalQuery.SelfAndDescendants<Border>(block).First(border => border.Padding == new Thickness(TimelineMetrics.PillLeading, TimelineMetrics.PillVertical, TimelineMetrics.PillTrailing, TimelineMetrics.PillVertical));
                Assert.Equal(Resolved(ChromeTokens.Accent.Opacity(0.1), pill), BrushColor(pill.Background));
                Close(window);

                var unsent = new HydraDelegationBlock(json);
                var unsentWindow = Show(unsent);
                Assert.DoesNotContain("checkmark.circle.fill", Symbols(unsent));
                Assert.Empty(LogicalQuery.SelfAndDescendants<DotFieldSweep>(unsent));
                Close(unsentWindow);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-152")]
    public void A_finished_heads_pill_names_its_outcome_beside_its_glyph_with_the_report_one_tap_away()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
            runtime.OnMain(() =>
            {
                var failed = new FinishedHeadPill(Head(0, HydraHeadInfo.Status.Failed));
                var stopped = new FinishedHeadPill(Head(1, HydraHeadInfo.Status.Stopped));
                var done = new FinishedHeadPill(Head(2, HydraHeadInfo.Status.Completed));
                var panel = new StackPanel();
                panel.Children.Add(failed);
                panel.Children.Add(stopped);
                panel.Children.Add(done);
                var window = Show(panel);
                string Words(Control pill) => string.Concat(LogicalQuery.SelfAndDescendants<TextBlock>(pill).Single().Inlines!.OfType<Avalonia.Controls.Documents.Run>().Select(run => run.Text));
                Assert.Equal("Hank failed", Words(failed));
                Assert.Equal("Walter was stopped", Words(stopped));
                Assert.Equal("Ada finished working", Words(done));
                Assert.Equal("Hank failed", AutomationProperties.GetName(failed));
                var runs = LogicalQuery.SelfAndDescendants<TextBlock>(failed).Single().Inlines!.OfType<Avalonia.Controls.Documents.Run>().ToList();
                Assert.Equal("Hank", runs[0].Text);
                Assert.Equal(Resolved(ChromeTokens.PrimaryText.Opacity(0.75), failed), BrushColor(runs[0].Foreground));
                Assert.Equal(FontSize(ChatFont.Chat(SwiftFont.TextStyle.Callout, SwiftFont.Weight.Medium)), runs[1].FontSize);
                var glyph = Assert.Single(LogicalQuery.SelfAndDescendants<HydraGlyph>(failed));
                Assert.Equal(18, glyph.Size);
                Assert.Equal(HydraHeadInfo.Status.Failed, glyph.Status);
                var button = LogicalQuery.SelfAndDescendants<Button>(failed).Single();
                Assert.Equal("Show the report", ToolTip.GetTip(button));
                Assert.Contains("chevron.right", Symbols(failed));
                var pill = LogicalQuery.SelfAndDescendants<Border>(failed).First(border => border.Padding == new Thickness(TimelineMetrics.PillLeading, TimelineMetrics.PillVertical, TimelineMetrics.PillTrailing, TimelineMetrics.PillVertical));
                Assert.Equal(Resolved(ChromeTokens.Accent.Opacity(0.1), pill), BrushColor(pill.Background));
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-152")]
    public void The_finished_heads_row_draws_one_leading_aligned_pill_per_named_head_found_among_this_threads_helpers()
    {
        headless.OnUiThread(() =>
        {
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
            using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
            var (store, app, runtime) = TeamHarness(Head(0, HydraHeadInfo.Status.Failed), Head(1, HydraHeadInfo.Status.Completed), Head(2, HydraHeadInfo.Status.Running));
            using (store)
            {
                store.Main.OnMain(() =>
                {
                    var row = new HydraHeadsWorkingRow(new ValueList<int>([1, 0, 5]), runtime);
                    var host = new Border { Child = row };
                    Marrow.App.Port.AppEnvironment.SetAppModel(host, app);
                    var window = Show(host);
                    // Independently test the :404-418 dictionary and order against real fixture children;
                    // the mounted lookup regression above exercises the corrected AppModel.Helpers.
                    row.ApplyTeam(app.Children(runtime.ThreadID));
                    var pills = LogicalQuery.SelfAndDescendants<FinishedHeadPill>(row).ToList();
                    Assert.Equal(["Walter finished working", "Hank failed"], pills.Select(pill => AutomationProperties.GetName(pill)));
                    var column = LogicalQuery.SelfAndDescendants<StackPanel>(row).First();
                    Assert.Equal(6, column.Spacing);
                    Assert.Equal(HorizontalAlignment.Left, column.HorizontalAlignment);
                    Assert.Equal(new Thickness(0, 0, 96, 0), row.Padding);
                    Close(window);
                });
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-153")]
    public void A_call_sending_out_a_head_draws_as_a_head_badge_that_reads_Sending_out_Sent_out_or_Could_not_send_out()
    {
        headless.OnUiThread(() =>
        {
            using var h = Harness();
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
            h.Main.OnMain(() =>
            {
                var sending = ToolRowFor(h.Runtime, Call(ToolCall.Kind.Agent, "Pip: the merge popover", ToolCall.Status.Running));
                var couldNot = ToolRowFor(h.Runtime, Call(ToolCall.Kind.Agent, "Pip: the merge popover", ToolCall.Status.Failed));
                var generic = ToolRowFor(h.Runtime, Call(ToolCall.Kind.Agent, "Subagent"));
                var unnamed = ToolRowFor(h.Runtime, Call(ToolCall.Kind.Agent, "Refactor: the store"));
                var panel = new StackPanel();
                foreach (var row in new[] { sending, couldNot, generic, unnamed }) panel.Children.Add(row);
                var window = Show(panel);
                Assert.Contains("Sending out Pip: the merge popover", Texts(sending));
                Assert.Contains("Could not send out Pip: the merge popover", Texts(couldNot));
                Assert.Contains("Sent out a head", Texts(generic));
                Assert.Contains("Sent out a head: Refactor: the store", Texts(unnamed));
                Assert.Empty(LogicalQuery.SelfAndDescendants<ProgressBar>(sending));
                Assert.Empty(LogicalQuery.SelfAndDescendants<ToolStatusIcon>(sending));
                var glyph = Assert.Single(LogicalQuery.SelfAndDescendants<HydraGlyph>(sending));
                Assert.Equal("Pip", glyph.Persona.Name);
                Assert.True(glyph.IsRunning);
                Assert.Null(glyph.Status);
                Assert.Equal(HydraHeadInfo.Status.Failed, LogicalQuery.SelfAndDescendants<HydraGlyph>(couldNot).Single().Status);
                Assert.Equal(HydraHeadInfo.Status.Completed, LogicalQuery.SelfAndDescendants<HydraGlyph>(generic).Single().Status);
                Assert.Equal("Hank", LogicalQuery.SelfAndDescendants<HydraGlyph>(generic).Single().Persona.Name);
                var label = LogicalQuery.SelfAndDescendants<TextBlock>(sending).Single(block => block.Text == "Sending out Pip: the merge popover");
                Assert.Equal(FontSize(ChatFont.Chat(SwiftFont.TextStyle.Callout, SwiftFont.Weight.Medium)), label.FontSize);
                Assert.Equal(Resolved(ChromeTokens.PrimaryText.Opacity(0.9), label), BrushColor(label.Foreground));
                var pill = LogicalQuery.SelfAndDescendants<Border>(sending).Single(border => border.Padding == new Thickness(TimelineMetrics.PillLeading, TimelineMetrics.PillVertical, TimelineMetrics.PillTrailing, TimelineMetrics.PillVertical));
                Assert.Equal(Resolved(SwiftColor.Quaternary.Opacity(0.32), pill), BrushColor(pill.Background));
                Assert.Equal(new CornerRadius(TimelineMetrics.PillRadius), pill.CornerRadius);
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-153")]
    public void A_call_whose_head_is_linked_reads_Sending_out_or_Sent_out_the_heads_name_and_the_title()
    {
        headless.OnUiThread(() =>
        {
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
            var (store, app, runtime) = TeamHarness(Head(3, HydraHeadInfo.Status.Running));
            using (store)
            {
                store.Main.OnMain(() =>
                {
                    var headID = app.Threads.Single(thread => thread.IsHydraHead).Id;
                    var running = ToolEntry(Call(ToolCall.Kind.Agent, "Tidy the popover", ToolCall.Status.Running));
                    var finished = ToolEntry(Call(ToolCall.Kind.Agent, "Tidy the popover"));
                    runtime.RehearseHydraHead(running.Id, headID);
                    runtime.RehearseHydraHead(finished.Id, headID);
                    var panel = new StackPanel();
                    panel.Children.Add(new ToolRow(running, runtime));
                    panel.Children.Add(new ToolRow(finished, runtime));
                    var host = new Border { Child = panel };
                    Marrow.App.Port.AppEnvironment.SetAppModel(host, app);
                    var window = Show(host);
                    Assert.Contains("Sending out Otto: Tidy the popover", Texts(panel.Children[0]));
                    Assert.Contains("Sent out Otto: Tidy the popover", Texts(panel.Children[1]));
                    Assert.All(LogicalQuery.SelfAndDescendants<HydraGlyph>(panel), glyph => Assert.Equal("Otto", glyph.Persona.Name));
                    Close(window);
                });
            }
        });
    }



    // ---- PR-CHAT-149 (geometry, persona and project rules the ported card tests above do not reach) ----

    private const string Hank149 = "{\"task\": \"Tidy the popover\", \"prompt\": \"Tidy it.\", \"name\": \"Hank\"}";

    private static Border CardPill149(HydraDelegationBlock block) =>
        LogicalQuery.SelfAndDescendants<Border>(block).First(border => border.Padding == new Thickness(TimelineMetrics.PillLeading, TimelineMetrics.PillVertical, TimelineMetrics.PillTrailing, TimelineMetrics.PillVertical));

    [Fact]
    [Trait("Req", "PR-CHAT-149")]
    public void The_card_stacks_its_header_and_rows_6_and_5_apart_and_the_next_brief_line_sits_past_a_16_point_clear_column() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            var block = new HydraDelegationBlock("[\n" + Hank149 + ",\n{\"task\": \"Wri");
            var window = Show(block);
            try
            {
                var pill = CardPill149(block);
                Assert.Equal(new CornerRadius(TimelineMetrics.PillRadius), pill.CornerRadius);
                Assert.Equal(Resolved(ChromeTokens.Accent.Opacity(0.1), pill), BrushColor(pill.Background));
                var column = Assert.IsType<StackPanel>(pill.Child);
                Assert.Equal(6, column.Spacing);
                var header = Assert.IsType<HStack>(column.Children[0]);
                Assert.Equal(Orientation.Horizontal, header.Orientation);
                Assert.Equal(8, header.Spacing);
                var mark = Assert.IsType<HydraMarkImage>(header.Children[0]);
                Assert.Equal(16, mark.Width);
                Assert.Equal(16, mark.Height);
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, mark), BrushColor(LogicalQuery.SelfAndDescendants<SwiftImage>(mark).Single().Foreground));
                var title = Assert.IsType<TextBlock>(Assert.IsType<OpacityCrossfade>(header.Children[1]).Current); // the title's words crossfade (:664), so the Text sits in a crossfade
                Assert.Equal("Summoning heads", title.Text);
                Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Callout, SwiftFont.Weight.Medium).Resolve().FontWeight, title.FontWeight);
                var rows = Assert.IsType<StackPanel>(column.Children[1]);
                Assert.Equal(5, rows.Spacing);
                Assert.Equal(2, rows.Children.Count);
                var next = Assert.IsType<HStack>(rows.Children[1]);
                Assert.Equal(Orientation.Horizontal, next.Orientation);
                Assert.Equal(8, next.Spacing);
                var clear = Assert.IsAssignableFrom<Control>(next.Children[0]);
                Assert.Equal(16, clear.Width);
                Assert.Equal(16, clear.Height);
                var words = Assert.IsType<TextBlock>(next.Children[1]);
                Assert.Equal("Writing the next brief\U00002026", words.Text); // U+2026 HORIZONTAL ELLIPSIS
                Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Caption).Resolve().FontSize, words.FontSize);
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, words), BrushColor(words.Foreground));
                // A head row: the glyph, then the task, at 8 points.
                var head = Assert.IsType<HStack>(rows.Children[0]);
                Assert.Equal(8, head.Spacing);
                Assert.Equal(Orientation.Horizontal, head.Orientation);
                // A closed block streams no next-brief line.
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-149")]
    public void A_head_is_named_by_its_trimmed_roster_name_and_any_other_name_stands_as_the_mark_and_a_head() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            string One(string name)
            {
                var block = new HydraDelegationBlock("[\n{\"task\": \"T\", \"prompt\": \"p\", \"name\": " + name + "}\n]");
                var window = Show(block);
                try { return string.Join("|", Texts(block)) + "#" + string.Join(",", LogicalQuery.SelfAndDescendants<HydraGlyph>(block).Select(g => g.Persona.Name)) + "#" + LogicalQuery.SelfAndDescendants<HydraMarkImage>(block).Count(); }
                finally { Close(window); }
            }
            Assert.Equal("Summoning Walter|T#Walter#1", One("\"  Walter \""));
            Assert.Equal("Summoning a head|T##2", One("\"Nobody\""));
            Assert.Equal("Summoning a head|T##2", One("\"   \""));
            Assert.Equal("Summoning a head|T##2", One("null"));
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-149")]
    public void A_project_reads_as_its_last_path_component_and_a_blank_one_shows_no_tag() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            string[] Tags(string project)
            {
                var block = new HydraDelegationBlock("[\n{\"task\": \"T\", \"prompt\": \"p\", \"project\": " + project + "}\n]");
                var window = Show(block);
                try { return LogicalQuery.SelfAndDescendants<StackPanel>(block).Where(p => Avalonia.Automation.AutomationProperties.GetName(p) is { } name && name.StartsWith("in ", StringComparison.Ordinal)).Select(p => Avalonia.Automation.AutomationProperties.GetName(p)!).ToArray(); }
                finally { Close(window); }
            }
            Assert.Equal(["in website"], Tags("\"  /Users/me/code/website \""));
            Assert.Equal(["in docs"], Tags("\"docs\""));
            Assert.Equal(["in b"], Tags("\"a/b/\""));
            Assert.Empty(Tags("\"   \""));
            Assert.Empty(Tags("\"\""));
            Assert.Empty(Tags("null"));
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-152")]
    public void Tapping_a_finished_heads_pill_opens_that_heads_report_and_every_pill_starts_at_the_same_leading_edge() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        FinishedHeadPill? failed = null, done = null;
        AvaloniaWindow? window = null;
        h.Main.OnMain(() =>
        {
            _ = AppCurrentEvent.CurrentWindow; // warms the lazy event subscription before Show presses its key
            var stopped = Head(1, HydraHeadInfo.Status.Stopped) with { Summary = "the stopped report" };
            failed = new FinishedHeadPill(Head(0, HydraHeadInfo.Status.Failed));
            done = new FinishedHeadPill(stopped);
            var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
            column.Children.Add(failed);
            column.Children.Add(done);
            window = Show(column);
            var left = failed.TranslatePoint(default, window)!.Value.X;
            Assert.Equal(left, done.TranslatePoint(default, window)!.Value.X);
            Border Capsule(Control pill) => LogicalQuery.SelfAndDescendants<Border>(pill).First(border => border.Padding == new Thickness(TimelineMetrics.PillLeading, TimelineMetrics.PillVertical, TimelineMetrics.PillTrailing, TimelineMetrics.PillVertical));
            Assert.NotEqual(Capsule(failed).Bounds.Width, Capsule(done).Bounds.Width); // however narrow, each hangs off the same leading edge
            Assert.Equal(left, Capsule(failed).TranslatePoint(default, window)!.Value.X);
            Assert.Equal(left, Capsule(done).TranslatePoint(default, window)!.Value.X);
            Press(LogicalQuery.SelfAndDescendants<Button>(done).Single());
        });
        try
        {
            h.Main.OnMain(() =>
            {
                Settle(window!);
                var shown = Assert.Single(presenter.Calls, call => call.Presented);
                var popover = Assert.Single(LogicalQuery.SelfAndDescendants<HydraHeadReportPopover>(shown.Popup.Child!));
                Assert.Equal(1, popover.Head.Index);
                Assert.Equal("the stopped report", popover.Head.Summary);
            });
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-148")]
    public void The_shimmer_is_a_band_a_third_as_wide_as_the_text_masked_to_it_and_nothing_under_reduced_motion()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
            var genie = new FakeGeniePlatform();
            using var reduce = ChromeTokens.ReduceMotionPlatform.Override(genie);
            runtime.OnMain(() =>
            {
                var plain = new TextBlock { Text = "Merging" };
                genie.AccessibilityDisplayShouldReduceMotion = true;
                Assert.Same(plain, new HydraShimmer().Body(plain));
                genie.AccessibilityDisplayShouldReduceMotion = false;
                var text = new TextBlock { Text = "Pushing the branch", Width = 150 };
                var shimmer = new HydraShimmer().Body(text);
                var window = Show(shimmer);
                var band = LogicalQuery.SelfAndDescendants<Border>(shimmer).Single(border => border.Background is LinearGradientBrush);
                Assert.Equal(50, band.Width);
                var gradient = (LinearGradientBrush)band.Background!;
                Assert.Equal([0.0, 0.5, 1.0], gradient.GradientStops.Select(stop => stop.Offset));
                Assert.Equal(
                    [Resolved(ChromeTokens.PrimaryText.Opacity(0), band), Resolved(ChromeTokens.PrimaryText.Opacity(0.9), band), Resolved(ChromeTokens.PrimaryText.Opacity(0), band)],
                    gradient.GradientStops.Select(stop => stop.Color));
                var layer = (Control)band.Parent!;
                Assert.False(layer.IsHitTestVisible);
                Assert.Same(text, Assert.IsType<VisualBrush>(layer.OpacityMask).Visual);
                // A narrow text still gets a 24-point band.
                var narrow = new TextBlock { Text = "Go", Width = 30 };
                var small = new HydraShimmer().Body(narrow);
                var smallWindow = Show(small);
                Assert.Equal(24, LogicalQuery.SelfAndDescendants<Border>(small).Single(border => border.Background is LinearGradientBrush).Width);
                Close(smallWindow);
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-151")]
    [Trait("Req", "PR-HYDRAMERGE-102")]
    [Trait("Req", "PR-HYDRAMERGE-103")]
    public void The_track_is_drawn_from_its_three_values_dots_lit_as_the_head_reaches_them_and_the_head_arcing_between_dots()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            runtime.OnMain(() =>
            {
                var body = new HydraMergeTrackBody(position: 2, done: 0, rest: 0, count: 7, hops: true);
                var window = Show(new StackPanel { Children = { body } });
                // :868-870 seven 4-point dots 3 apart: 46 wide, 20 high.
                Assert.Equal(46, body.Width);
                Assert.Equal(20, body.Height);
                var dots = LogicalQuery.SelfAndDescendants<Panel>(body).Where(panel => panel.Width == 4 && panel.Height == 4).ToList();
                Assert.Equal(7, dots.Count);
                Assert.Equal([0.0, 7, 14, 21, 28, 35, 42], dots.Select(Canvas.GetLeft));
                Assert.All(dots, dot => Assert.Equal(16, Canvas.GetTop(dot)));
                double Lit(Panel dot) => ((Control)dot.Children[1]).Opacity;
                Assert.Equal([1.0, 1, 1, 0, 0, 0, 0], dots.Select(Lit));
                Assert.Equal(Resolved(ChromeTokens.Overlay(0.28), dots[0]), BrushColor(((Avalonia.Controls.Shapes.Ellipse)dots[0].Children[0]).Fill));
                Assert.Equal(Resolved(ChromeTokens.Accent, dots[0]), BrushColor(((Avalonia.Controls.Shapes.Ellipse)dots[0].Children[1]).Fill));
                var head = Assert.Single(LogicalQuery.SelfAndDescendants<HydraMarkImage>(body));
                Assert.Equal(12, head.Width);
                Assert.Equal(10, Canvas.GetLeft(head), 9);
                Assert.Equal(0, Canvas.GetTop(head), 9);
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, head), BrushColor(LogicalQuery.SelfAndDescendants<SwiftImage>(head).Single().Foreground));
                // Half way between two dots the head is at the top of its 4-point arc; 2.7 lights the fourth dot early.
                body.AnimatableData = (2.5, (0, 0));
                Assert.Equal(-4, Canvas.GetTop(head), 9);
                Assert.Equal(2.5 * 7 + 2 - 6, Canvas.GetLeft(head), 9);
                body.AnimatableData = (2.7, (0, 0));
                Assert.Equal([1.0, 1, 1, 1, 0, 0, 0], dots.Select(Lit));
                // The outcome: the wave lights the dots left to right and the head pops once.
                body.AnimatableData = (6, (0.5, 0));
                Assert.Equal(1.18, Assert.IsType<ScaleTransform>(head.RenderTransform).ScaleX, 9);
                body.AnimatableData = (0, (0.5625, 0));
                Assert.Equal([1.0, 1, 1, 1, 0.5, 0, 0], dots.Select(Lit));
                // Settled: the dots faded, the head at 18 points pinned to the leading edge, the frame shrunk to it.
                body.AnimatableData = (6, (1, 1));
                Assert.Equal(18, head.Width);
                Assert.Equal(0, Canvas.GetLeft(head), 9);
                Assert.Equal(1, Canvas.GetTop(head), 9);
                Assert.Equal(18, body.Width);
                Assert.All(dots, dot => Assert.Equal(0, ((Control)dot.Parent!).Opacity));
                Assert.Equal(1, Assert.IsType<ScaleTransform>(head.RenderTransform).ScaleX, 9);
                // Without hops (reduced motion) there is no arc and no pop.
                var still = new HydraMergeTrackBody(position: 2.5, done: 0.5, rest: 0, count: 7, hops: false);
                var stillWindow = Show(new StackPanel { Children = { still } });
                var stillHead = LogicalQuery.SelfAndDescendants<HydraMarkImage>(still).Single();
                Assert.Equal(0, Canvas.GetTop(stillHead), 9);
                Assert.Equal(1, Assert.IsType<ScaleTransform>(stillHead.RenderTransform).ScaleX, 9);
                Close(stillWindow);
                Close(window);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-151")]
    [Trait("Req", "PR-HYDRAMERGE-103")]
    public void The_track_glides_to_a_new_step_from_where_the_head_is_and_eases_into_the_outcome()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var frames = new ManualFrameSource();
            using var frameSeam = FrameClock.Source.Override(frames);
            HydraMergeTrack? track = null;
            AvaloniaWindow? window = null;
            HydraMergeTrackBody Body() => LogicalQuery.SelfAndDescendants<HydraMergeTrackBody>(track!).Single();
            void Frames(double milliseconds)
            {
                frames.Advance(System.TimeSpan.Zero);
                runtime.AdvanceMilliseconds(milliseconds);
                frames.Advance(System.TimeSpan.FromMilliseconds(milliseconds));
            }
            runtime.OnMain(() =>
            {
                track = new HydraMergeTrack(step: 0, count: 7, mode: HydraMergeTrack.Mode.Running);
                window = Show(new StackPanel { Children = { track } });
                Assert.Equal((0.0, (0.0, 0.0)), Body().AnimatableData);
                track.Update(step: 3, count: 7, mode: HydraMergeTrack.Mode.Running);
            });
            Frames(100);
            var midway = runtime.OnMain(() => Body().AnimatableData.Position);
            Assert.InRange(midway, 0.01, 2.99);
            // Retargeted mid-flight: the head goes on from where it is, not from the ground.
            runtime.OnMain(() => track!.Update(step: 5, count: 7, mode: HydraMergeTrack.Mode.Running));
            frames.Advance(System.TimeSpan.Zero);
            Assert.InRange(runtime.OnMain(() => Body().AnimatableData.Position), midway - 0.01, 5);
            Frames(2000);
            Assert.Equal(5, runtime.OnMain(() => Body().AnimatableData.Position));
            runtime.OnMain(() => track!.Update(step: 6, count: 7, mode: HydraMergeTrack.Mode.Resting));
            Frames(2000);
            runtime.OnMain(() =>
            {
                Assert.Equal((6.0, (1.0, 1.0)), Body().AnimatableData);
                Close(window!);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-150")]
    [Trait("Req", "PR-HYDRAMERGE-101")]
    [Trait("Req", "PR-HYDRAMERGE-106")]
    public void The_merge_pill_shows_the_stage_under_way_with_an_ellipsis_and_goes_once_the_merge_is_over_without_a_note()
    {
        headless.OnUiThread(() =>
        {
            using var h = Harness();
            var frames = new ManualFrameSource();
            using var frameSeam = FrameClock.Source.Override(frames);
            using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
            HydraMergeRow? row = null;
            AvaloniaWindow? window = null;
            h.Main.OnMain(() =>
            {
                h.Runtime.IsHydraMerging = true;
                h.Runtime.HydraMergeStep = HydraMergeStage.Pushing;
                h.Runtime.HydraMergeStage = HydraMergeStage.Pushing.Words;
                row = new HydraMergeRow(h.Runtime, new HydraMergePhase.Merging());
                window = Show(row);
                Assert.Contains("Pushing the branch\U00002026", Texts(row)); // U+2026 HORIZONTAL ELLIPSIS
                var pill = LogicalQuery.SelfAndDescendants<Border>(row).Single(border => AutomationProperties.GetName(border) is not null);
                Assert.Equal("Merging the team's work: Pushing the branch\U00002026", AutomationProperties.GetName(pill)); // U+2026 HORIZONTAL ELLIPSIS
                Assert.Equal(new Thickness(TimelineMetrics.PillLeading, TimelineMetrics.PillVertical, TimelineMetrics.PillTrailing, TimelineMetrics.PillVertical), pill.Padding);
                Assert.Equal(Resolved(ChromeTokens.Accent.Opacity(0.1), pill), BrushColor(pill.Background));
                var body = Assert.Single(LogicalQuery.SelfAndDescendants<HydraMergeTrackBody>(row));
                Assert.Equal(3, body.AnimatableData.Position);
                Assert.DoesNotContain(Texts(row), text => text.Contains("Open", System.StringComparison.Ordinal));
                h.Runtime.HydraMergeStage = null;
            });
            h.Main.OnMain(() =>
            {
                Settle(window!);
                Assert.Contains("Merging\U00002026", Texts(row!)); // U+2026 HORIZONTAL ELLIPSIS
                h.Runtime.IsHydraMerging = false;
            });
            // :949-952 removal uses the glide spring. Complete its source-defined transition before asserting absence.
            frames.Advance(System.TimeSpan.Zero);
            h.Main.AdvanceMilliseconds(2000);
            frames.Advance(System.TimeSpan.FromMilliseconds(2000));
            h.Main.OnMain(() =>
            {
                Settle(window!);
                Assert.Empty(LogicalQuery.SelfAndDescendants<HydraMergeTrack>(row!));
                Assert.Empty(Texts(row!));
                Close(window!);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-150")]
    [Trait("Req", "PR-HYDRAMERGE-102")] // TimelineRows.swift:963-965, :1036-1039: with the outcome landed and the runtime's step cleared the head is pinned to the last dot, done under the flourish and resting after
    [Trait("Req", "PR-HYDRAMERGE-106")] // TimelineRows.swift:1019: the accessibility label is the outcome's title once there is one
    [Trait("Req", "PR-HYDRAMERGE-104")]
    [Trait("Req", "PR-HYDRAMERGE-107")]
    public void When_the_note_lands_the_pill_morphs_to_its_title_and_link_and_a_successful_merge_flourishes_for_1100_milliseconds()
    {
        headless.OnUiThread(() =>
        {
            using var h = Harness();
            var frames = new ManualFrameSource();
            using var frameSeam = FrameClock.Source.Override(frames);
            using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
            using var browser = Marrow.Services.Port.Workspace.Open.Override(_ => true);
            HydraMergeRow? row = null;
            AvaloniaWindow? window = null;
            (double Position, (double Done, double Resting) Outcome) Track() => LogicalQuery.SelfAndDescendants<HydraMergeTrackBody>(row!).Single().AnimatableData;
            h.Main.OnMain(() =>
            {
                h.Runtime.IsHydraMerging = true;
                h.Runtime.HydraMergeStep = HydraMergeStage.Opening;
                h.Runtime.HydraMergeStage = HydraMergeStage.Opening.Words;
                row = new HydraMergeRow(h.Runtime, new HydraMergePhase.Merging());
                window = Show(row);
                Assert.Equal((4.0, (0.0, 0.0)), Track());
                h.Runtime.IsHydraMerging = false;
                h.Runtime.HydraMergeStep = null;
                row.Update(new HydraMergePhase.Outcome(new UserMessage { Text = "Hydra merged the team's work.\nhttps://gitlab.com/g/p/-/merge_requests/7\nThree heads landed." }));
            });
            void Frames(double milliseconds)
            {
                frames.Advance(System.TimeSpan.Zero);
                h.Main.AdvanceMilliseconds(milliseconds);
                frames.Advance(System.TimeSpan.FromMilliseconds(milliseconds));
            }
            // Under 1.1 s: the outcome has landed and the flourish is still on.
            Frames(1000);
            h.Main.OnMain(() =>
            {
                Settle(window!);
                Assert.Contains("Hydra merged the team's work", Texts(row!));
                Assert.DoesNotContain(Texts(row!), text => text.EndsWith("…", System.StringComparison.Ordinal)); // U+2026 HORIZONTAL ELLIPSIS
                var pill = LogicalQuery.SelfAndDescendants<Border>(row!).Single(border => AutomationProperties.GetName(border) is not null);
                Assert.Equal("Hydra merged the team's work", AutomationProperties.GetName(pill));
                var open = LogicalQuery.SelfAndDescendants<Button>(row!).Single(button => Texts(button).Contains("Open"));
                Assert.Equal("Open in the browser", ToolTip.GetTip(open));
                var title = LogicalQuery.SelfAndDescendants<Button>(row!).Single(button => Texts(button).Contains("Hydra merged the team's work"));
                Assert.Equal("Show the message", ToolTip.GetTip(title));
                Assert.Contains("chevron.right", Symbols(title));
                // :1036-1042 done: the head on the last dot, every dot lit, not yet settled.
                Assert.Equal((6.0, (1.0, 0.0)), Track());
            });
            // Past 1.1 s the flourish ends and the head settles to its resting size.
            h.Main.AdvanceMilliseconds(200);
            Frames(2000);
            h.Main.OnMain(() =>
            {
                Assert.Equal((6.0, (1.0, 1.0)), Track());
                Close(window!);
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-CHAT-150")]
    [Trait("Req", "PR-HYDRAMERGE-105")]
    public void An_outcome_that_is_not_a_merge_settles_at_once_with_no_flourish_and_an_outcome_with_nothing_behind_it_is_disabled()
    {
        headless.OnUiThread(() =>
        {
            using var h = Harness();
            var frames = new ManualFrameSource();
            using var frameSeam = FrameClock.Source.Override(frames);
            using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
            HydraMergeRow? row = null;
            AvaloniaWindow? window = null;
            h.Main.OnMain(() =>
            {
                h.Runtime.IsHydraMerging = true;
                row = new HydraMergeRow(h.Runtime, new HydraMergePhase.Merging());
                window = Show(row);
                h.Runtime.IsHydraMerging = false;
                row.Update(new HydraMergePhase.Outcome(new UserMessage { Text = "The merge stopped." }));
            });
            frames.Advance(System.TimeSpan.Zero);
            h.Main.AdvanceMilliseconds(2000);
            frames.Advance(System.TimeSpan.FromMilliseconds(2000));
            h.Main.OnMain(() =>
            {
                Settle(window!);
                Assert.Equal((6.0, (1.0, 1.0)), LogicalQuery.SelfAndDescendants<HydraMergeTrackBody>(row!).Single().AnimatableData);
                var title = LogicalQuery.SelfAndDescendants<Button>(row!).Single();
                Assert.False(title.IsEnabled);
                Assert.Equal("", ToolTip.GetTip(title)); // Swift :1103 .help(outcome.details.isEmpty ? "" : "Show the message")
                Assert.DoesNotContain("chevron.right", Symbols(row!));
                Close(window!);
            });
        });
    }

    // ---- Tool rows: what the survivors of the mechanical gate left open (PR-CHAT-093, -096, -097, -098, -153) ----

    private static string ImageFile(ThreadRuntimePart12Harness h, string name)
    {
        var full = System.IO.Path.Combine(h.Project.Path, name);
        System.IO.File.WriteAllBytes(full, []);
        return new Uri(full).AbsoluteUri; // a file:// address resolves the same on every platform (AttachmentPreview.cs:66-67)
    }

    private static (ToolRow Row, AvaloniaWindow Window) MountRow(ThreadRuntime runtime, ToolCall call, string? workingDirectory = null)
    {
        var row = new ToolRow(ToolEntry(call), runtime, workingDirectory);
        var window = Show(row);
        Hover(window, LogicalQuery.SelfAndDescendants<MiddleTruncatingTextBlock>(row).Single());
        return (row, window);
    }

    [Fact]
    [Trait("Req", "PR-CHAT-097")]
    [Trait("Req", "PR-CHAT-098")]
    public void A_read_of_an_image_opens_the_image_however_much_output_it_has_and_any_other_row_with_output_unfolds() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var path = ImageFile(h, "shot.png");
        (ToolRow Row, AvaloniaWindow Window) read = (null!, null!), command = (null!, null!), bare = (null!, null!);
        h.Main.OnMain(() =>
        {
            _ = AppCurrentEvent.CurrentWindow;
            read = MountRow(h.Runtime, Call(ToolCall.Kind.Read, path) with { Output = "some text" });
            command = MountRow(h.Runtime, Call(ToolCall.Kind.Command, path) with { Output = "some text" });
            bare = MountRow(h.Runtime, Call(ToolCall.Kind.Command, path));
        });
        try
        {
            h.Main.OnMain(() =>
            {
                foreach (var (row, window) in new[] { read, command, bare }) Settle(window);
                // The tooltip, the chevron and the icon say what a tap will do.
                var readButton = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(read.Row));
                Assert.Equal("Show the image", ToolTip.GetTip(readButton));
                Assert.Contains("chevron.down", Symbols(read.Row));
                Assert.Contains("photo", Symbols(read.Row));
                Assert.Equal("Show the output", ToolTip.GetTip(Assert.Single(LogicalQuery.SelfAndDescendants<Button>(command.Row))));
                Assert.Contains("chevron.right", Symbols(command.Row));
                Assert.DoesNotContain("photo", Symbols(command.Row));
                var bareButton = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(bare.Row));
                Assert.Equal("Show the image", ToolTip.GetTip(bareButton));
                Assert.Contains("chevron.down", Symbols(bare.Row));
                // The line is a hit target across its width whatever it draws.
                Assert.Equal(0, BrushColor(Assert.IsAssignableFrom<Panel>(readButton.Content).Background).A);
                Press(readButton);
            });
            h.Main.OnMain(() =>
            {
                Settle(read.Window);
                Assert.Single(presenter.Calls, call => call.Presented);
                Assert.Empty(LogicalQuery.SelfAndDescendants<ToolDetailView>(read.Row));
                Press(Assert.Single(LogicalQuery.SelfAndDescendants<Button>(command.Row)));
            });
            h.Main.OnMain(() =>
            {
                Settle(command.Window);
                // Unfolded in place: the output, and no image panel.
                Assert.Single(presenter.Calls, call => call.Presented);
                Assert.Single(LogicalQuery.SelfAndDescendants<ToolDetailView>(command.Row));
                Press(Assert.Single(LogicalQuery.SelfAndDescendants<Button>(bare.Row)));
            });
            h.Main.OnMain(() =>
            {
                Settle(bare.Window);
                Assert.Equal(2, presenter.Calls.Count(call => call.Presented));
                Assert.Empty(LogicalQuery.SelfAndDescendants<ToolDetailView>(bare.Row));
            });
        }
        finally { h.Main.OnMain(() => { Close(read.Window); Close(command.Window); Close(bare.Window); }); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-097")]
    public void An_unfolded_output_folds_away_again_when_the_row_is_tapped_a_second_time() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        (ToolRow Row, AvaloniaWindow Window) mounted = (null!, null!);
        h.Main.OnMain(() => mounted = MountRow(h.Runtime, Call(ToolCall.Kind.Command, "ls") with { Output = "a\nb" }));
        try
        {
            void Tick() { for (var i = 0; i < 30; i++) { h.Main.Advance(TimeSpan.FromMilliseconds(16)); manual.Advance(TimeSpan.FromMilliseconds(16)); } }
            h.Main.OnMain(() => Press(Assert.Single(LogicalQuery.SelfAndDescendants<Button>(mounted.Row))));
            Tick();
            h.Main.OnMain(() =>
            {
                Assert.Single(LogicalQuery.SelfAndDescendants<ToolDetailView>(mounted.Row));
                Press(Assert.Single(LogicalQuery.SelfAndDescendants<Button>(mounted.Row)));
            });
            Tick();
            h.Main.OnMain(() =>
            {
                Assert.Empty(LogicalQuery.SelfAndDescendants<ToolDetailView>(mounted.Row));
                Assert.Empty(mounted.Row.GetVisualDescendants().OfType<ToolDetailView>());
            });
        }
        finally { h.Main.OnMain(() => Close(mounted.Window)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-097")]
    public void A_row_becomes_a_control_when_its_call_is_linked_to_a_head_even_before_the_heads_record_exists() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        ToolRow? row = null;
        AvaloniaWindow? window = null;
        var entry = ToolEntry(Call(ToolCall.Kind.Agent, "Pip: the merge popover", ToolCall.Status.Running));
        h.Main.OnMain(() =>
        {
            row = new ToolRow(entry, h.Runtime);
            var host = new Border { Child = row };
            Marrow.App.Port.AppEnvironment.SetAppModel(host, h.App);
            window = Show(host);
            Assert.Empty(LogicalQuery.SelfAndDescendants<Button>(row));
            h.Runtime.RehearseHydraHead(entry.Id, System.Guid.NewGuid()); // the model holds no thread for it
        });
        try
        {
            h.Main.OnMain(() =>
            {
                Settle(window!);
                Assert.Equal("Show the head's steps", ToolTip.GetTip(Assert.Single(LogicalQuery.SelfAndDescendants<Button>(row!))));
            });
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-153")]
    public void A_departing_heads_generic_titles_and_missing_names_read_as_a_head_and_a_named_face_leads_the_title() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            string One(string title, ToolCall.Status status = ToolCall.Status.Completed)
            {
                var row = new ToolRow(ToolEntry(Call(ToolCall.Kind.Agent, title, status)), h.Runtime);
                var window = Show(row);
                try { return LogicalQuery.SelfAndDescendants<MiddleTruncatingTextBlock>(row).Single().FullText + "#" + LogicalQuery.SelfAndDescendants<HydraGlyph>(row).Single().Persona.Name; }
                finally { Close(window); }
            }
            var first = HydraRoster.Persona(0).Name;
            Assert.Equal("Sent out a head#" + first, One("Subagent"));
            Assert.Equal("Sent out a head#" + first, One("AGENT"));
            Assert.Equal("Sent out a head#" + first, One("task"));
            Assert.Equal("Sent out a head#" + first, One(""));
            Assert.Equal("Sent out a head#" + first, One("  Task  "));
            Assert.Equal("Sent out a head: Task force#" + first, One("Task force"));
            Assert.Equal("Sent out a head: tidy things#" + first, One("tidy things"));
            Assert.Equal("Sending out a head: tidy things#" + first, One("tidy things", ToolCall.Status.Running));
            Assert.Equal("Could not send out a head: tidy things#" + first, One("tidy things", ToolCall.Status.Failed));
            var second = HydraRoster.Persona(1).Name;
            Assert.Equal("Sent out " + second + ": the merge popover#" + second, One("  " + second + " :  the merge popover  "));
            Assert.Equal("Sent out " + second + "#" + second, One(second + ":"));
            Assert.Equal("Sent out a head: Nobody: the merge popover#" + first, One("Nobody: the merge popover"));
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-093")]
    public void Every_root_word_of_a_kind_is_dropped_from_the_front_of_its_subject_whatever_its_case_and_no_other_word_is() => headless.OnUiThread(() =>
    {
        var roots = new Dictionary<ToolCall.Kind, string[]>
        {
            [ToolCall.Kind.Command] = ["run", "bash", "shell", "exec", "execute", "command"],
            [ToolCall.Kind.Read] = ["read", "view", "open", "cat"],
            [ToolCall.Kind.Edit] = ["edit", "write", "multiedit", "create", "notebookedit", "apply_patch", "patch"],
            [ToolCall.Kind.Search] = ["search", "grep", "glob", "find"],
            [ToolCall.Kind.Web] = ["fetch", "webfetch", "websearch", "browse"],
            [ToolCall.Kind.Mcp] = ["call"],
            [ToolCall.Kind.Agent] = ["delegate", "agent", "task"],
            [ToolCall.Kind.Other] = ["use"],
        };
        foreach (var (kind, words) in roots)
        {
            var verb = ToolPresentation.Verb(Call(kind, "x"));
            foreach (var word in words)
            {
                Assert.Equal(verb + " the subject", ToolPresentation.Label(Call(kind, word + " the subject")));
                Assert.Equal(verb + " the subject", ToolPresentation.Label(Call(kind, word.ToUpperInvariant() + " the subject")));
                Assert.Equal(verb + " " + word + "s the subject", ToolPresentation.Label(Call(kind, word + "s the subject")));
                Assert.Equal(verb, ToolPresentation.Label(Call(kind, word)));
            }
            Assert.Equal(verb + " zzz the subject", ToolPresentation.Label(Call(kind, "zzz the subject")));
            // Only a leading word counts.
            Assert.Equal(verb + " the " + words[0] + " subject", ToolPresentation.Label(Call(kind, "the " + words[0] + " subject")));
        }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-098")]
    [Trait("Req", "PR-CHAT-097")]
    public void A_failed_rows_exit_code_sits_at_the_trailing_edge_on_the_labels_line_and_the_run_is_laid_out_left_to_right_from_zero() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            var call = Call(ToolCall.Kind.Edit, "src/a.cs", ToolCall.Status.Failed) with { Output = "out", ExitCode = 2, Edits = new ValueList<FileEdit>([new FileEdit("src/a.cs", "diff", 3, 2)]) };
            var row = new ToolRow(ToolEntry(call), h.Runtime);
            var window = Show(new Border { Width = 400, Child = row });
            try
            {
                Settle(window);
                var label = Assert.Single(LogicalQuery.SelfAndDescendants<MiddleTruncatingTextBlock>(row));
                var run = Assert.IsAssignableFrom<Panel>(label.Parent);
                Assert.Equal(0, run.Children[0].Bounds.Left, precision: 3);
                var pill = LogicalQuery.SelfAndDescendants<Border>(row).Single(border => border.Child == run);
                Assert.NotNull(pill.Background); // a plain row's line is a solid hit area, drawn clear
                Assert.Equal(0, BrushColor(pill.Background).A);
                for (var index = 1; index < run.Children.Count; index++)
                    Assert.InRange(run.Children[index].Bounds.Left - run.Children[index - 1].Bounds.Right, TimelineMetrics.IconSpacing - 1, TimelineMetrics.IconSpacing + 1);
                Assert.True(run.Children.Count >= 4); // glyph, label, stat, chevron
                Assert.All(run.Children, child => Assert.True(child.Bounds.Width > 0 && child.Bounds.Height > 0)); // each one measured to its own size
                Assert.InRange(run.Children[0].Bounds.Width, TimelineMetrics.IconWidth - 1, TimelineMetrics.IconWidth + 1);
                var midline = run.Children.Select(child => child.Bounds.Top + child.Bounds.Height / 2).ToList();
                Assert.All(midline, y => Assert.InRange(y, midline[0] - 1, midline[0] + 1)); // centred on one line
                var exit = TextNamed(row, "exit 2");
                Assert.True(exit.TranslatePoint(default, row)!.Value.X > label.TranslatePoint(default, row)!.Value.X + label.Bounds.Width);
                var exitMid = exit.TranslatePoint(new Point(0, exit.Bounds.Height / 2), window)!.Value.Y;
                var labelMid = label.TranslatePoint(new Point(0, label.Bounds.Height / 2), window)!.Value.Y;
                Assert.Equal(labelMid, exitMid, precision: 1);
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-148")]
    [Trait("Req", "PR-HYDRAMERGE-104")]
    public void The_shimmer_band_runs_left_to_right_moves_only_every_thirtieth_of_a_second_and_rests_while_the_reader_scrolls() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var reduce = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        Control? shimmer = null;
        AvaloniaWindow? window = null;
        var scroller = System.Guid.NewGuid();
        double Offset() => Assert.IsType<TranslateTransform>(LogicalQuery.SelfAndDescendants<Border>(shimmer!).Single(border => border.Background is LinearGradientBrush).RenderTransform).X;
        void Advance(double milliseconds)
        {
            h.Main.AdvanceMilliseconds(milliseconds);
            frames.Advance(TimeSpan.FromMilliseconds(milliseconds));
        }
        h.Main.OnMain(() =>
        {
            shimmer = new HydraShimmer().Body(new TextBlock { Text = "Pushing the branch", Width = 150 });
            window = Show(shimmer);
            var gradient = (LinearGradientBrush)LogicalQuery.SelfAndDescendants<Border>(shimmer).Single(border => border.Background is LinearGradientBrush).Background!;
            Assert.Equal(new RelativePoint(0, 0.5, RelativeUnit.Relative), gradient.StartPoint);
            Assert.Equal(new RelativePoint(1, 0.5, RelativeUnit.Relative), gradient.EndPoint);
        });
        try
        {
            double start = 0;
            h.Main.OnMain(() => start = Offset());
            Advance(10); // less than a thirtieth of a second: no new frame is drawn
            h.Main.OnMain(() => Assert.Equal(start, Offset()));
            Advance(60);
            double moved = 0;
            h.Main.OnMain(() => { moved = Offset(); Assert.NotEqual(start, moved); });
            h.Main.OnMain(() => ScrollActivity.Shared.Current.SetScrolling(true, scroller));
            Advance(200);
            h.Main.OnMain(() => Assert.Equal(moved, Offset())); // held still while the reader scrolls
            h.Main.OnMain(() => ScrollActivity.Shared.Current.SetScrolling(false, scroller));
            Advance(200);
            h.Main.OnMain(() => Assert.NotEqual(moved, Offset()));
        }
        finally { h.Main.OnMain(() => { ScrollActivity.Shared.Current.SetScrolling(false, scroller); Close(window!); }); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-089")]
    public void The_copy_actions_put_the_message_the_markdown_and_the_last_code_block_on_the_clipboard() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        AvaloniaWindow? window = null;
        const string text = "answer\n\n```\nfirst\n```\n\n```\nlast\n```";
        var actions = AssistantMessageRow.MessageActions(text, TimelineRows.LastCodeBlock(text), h.Runtime);
        h.Main.OnMain(() =>
        {
            _ = AppCurrentEvent.CurrentWindow; // warms the lazy event subscription before Show presses its key
            window = Show(new Border());
        });
        try
        {
            string Copied(string title)
            {
                h.Main.OnMain(actions.Single(item => item.Title == title).Action);
                return h.Main.OnMain(() =>
                {
                    Settle(window!);
                    return window!.Clipboard!.TryGetTextAsync().GetAwaiter().GetResult() ?? "";
                });
            }
            Assert.Equal("last", Copied("Copy last code block"));
            Assert.Equal(text, Copied("Copy as markdown"));
            Assert.Equal(ProseReflow.Reflowed(MessageText.Plain(text)), Copied("Copy message"));
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-088")]
    public void A_reply_is_rebuilt_only_when_its_text_or_streaming_flag_moves_and_its_hover_line_shows_only_on_hover_of_a_finished_reply() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        AvaloniaWindow? window = null;
        var entry = Reply("first words");
        AssistantMessageRow? row = null;
        h.Main.OnMain(() =>
        {
            row = new AssistantMessageRow(entry, null, h.Runtime);
            window = Show(row);
            var column = Assert.IsType<StackPanel>(row.Child);
            Assert.Equal(0, BrushColor(column.Background).A); // the whole reply is a hover target
            var line = column.Children.OfType<StackPanel>().Single(stack => stack.Height == TimelineMetrics.HoverLineHeight);
            Assert.Equal(0, line.Opacity); // the line is hidden until the pointer is over the reply
        });
        try
        {
            Control? before = null;
            h.Main.OnMain(() =>
            {
                before = row!.Child;
                entry.Item = new TimelineItem(entry.Item.TurnID, new TimelineItem.Content.Assistant(new AssistantMessage { Text = "first words" }));
            });
            h.Main.OnMain(() =>
            {
                Settle(window!);
                Assert.Same(before, row!.Child); // same text and streaming flag: not rebuilt
                entry.Item = new TimelineItem(entry.Item.TurnID, new TimelineItem.Content.Assistant(new AssistantMessage { Text = "first words", IsStreaming = true }));
            });
            h.Main.OnMain(() =>
            {
                Settle(window!);
                Assert.NotSame(before, row!.Child);
                before = row!.Child;
                entry.Item = new TimelineItem(entry.Item.TurnID, new TimelineItem.Content.Assistant(new AssistantMessage { Text = "more words", IsStreaming = true }));
            });
            h.Main.OnMain(() =>
            {
                Settle(window!);
                Assert.NotSame(before, row!.Child);
            });
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    // ---- The work group, its steps and the tool detail: what the mechanical gate's survivors left open (PR-CHAT-090, -092, -097, -101) ----

    [Fact]
    [Trait("Req", "PR-CHAT-090")]
    public void A_group_of_one_is_the_row_alone_until_a_second_call_joins_it_and_then_it_gets_its_header() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var first = ToolEntry(Call(ToolCall.Kind.Read, "a.cs"));
        var second = ToolEntry(Call(ToolCall.Kind.Command, "ls"));
        WorkGroup? group = null;
        AvaloniaWindow? window = null;
        h.Main.OnMain(() =>
        {
            group = new WorkGroup(new ValueList<TimelineEntry>([first]), h.Runtime);
            window = Show(group);
            Assert.IsType<ToolRow>(group.Child);
            Assert.Empty(LogicalQuery.SelfAndDescendants<Button>(group));
            group.Update(new ValueList<TimelineEntry>([first, second]), false);
        });
        try
        {
            h.Main.OnMain(() =>
            {
                Settle(window!);
                Assert.IsNotType<ToolRow>(group!.Child);
                var header = Assert.Single(LogicalQuery.SelfAndDescendants<Button>(group), button => Avalonia.Automation.AutomationProperties.GetName(button) == "Hide these steps");
                Assert.Contains("Read files, ran commands", LogicalTexts(header));
                group.Update(new ValueList<TimelineEntry>([second]), false);
            });
            h.Main.OnMain(() =>
            {
                Settle(window!);
                Assert.IsType<ToolRow>(group!.Child); // and back to the row alone
            });
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-090")]
    public void The_group_header_is_a_full_width_hit_target_with_its_glyph_summary_and_chevron_in_the_secondary_style() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        WorkGroup? group = null;
        AvaloniaWindow? window = null;
        h.Main.OnMain(() =>
        {
            group = new WorkGroup(new ValueList<TimelineEntry>([ToolEntry(Call(ToolCall.Kind.Edit, "a.cs")), ToolEntry(Call(ToolCall.Kind.Command, "ls"))]), h.Runtime);
            window = Show(group);
        });
        try
        {
            h.Main.OnMain(() =>
            {
                Settle(window!);
                var header = LogicalQuery.SelfAndDescendants<Button>(group!).First();
                var row = Assert.IsType<StackPanel>(header.Content);
                Assert.Equal(Orientation.Horizontal, row.Orientation);
                Assert.Equal(TimelineMetrics.IconSpacing, row.Spacing);
                Assert.Equal(new Thickness(0, 0, 12, 0), row.Margin);
                Assert.Equal(0, BrushColor(row.Background).A);
                var glyph = LogicalQuery.SelfAndDescendants<SwiftImage>(row).First(image => image.SymbolName == "pencil");
                Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Caption), glyph.GetValue(SwiftImage.FontProperty));
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, glyph), BrushColor(glyph.Foreground));
                Assert.Equal(TimelineMetrics.IconWidth, glyph.Width);
                var summary = TextNamed(row, "Edited files, ran commands");
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, summary), BrushColor(summary.Foreground));
                var chevron = LogicalQuery.SelfAndDescendants<SwiftImage>(row).Single(image => image.SymbolName == "chevron.right");
                Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Caption2, SwiftFont.Weight.Semibold), chevron.GetValue(SwiftImage.FontProperty));
                Assert.Equal(Resolved(ChromeTokens.SecondaryText, chevron), BrushColor(chevron.Foreground));
            });
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-092")]
    public void The_earlier_steps_line_comes_back_when_more_steps_arrive_after_it_went_and_is_a_full_width_hit_target() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var manual = new ManualFrameSource();
        using var frames = FrameClock.Source.Override(manual);
        var entries = Enumerable.Range(1, 5).Select(i => ToolEntry(Call(ToolCall.Kind.Read, "file" + i))).ToArray();
        WorkGroup? group = null;
        AvaloniaWindow? window = null;
        void Tick() { for (var i = 0; i < 30; i++) { h.Main.Advance(TimeSpan.FromMilliseconds(16)); manual.Advance(TimeSpan.FromMilliseconds(16)); } }
        Button? More() => LogicalQuery.SelfAndDescendants<WorkSteps>(group!).SingleOrDefault() is { } steps ? LogicalQuery.SelfAndDescendants<Button>(steps).FirstOrDefault() : null;
        h.Main.OnMain(() =>
        {
            group = new WorkGroup(new ValueList<TimelineEntry>(entries.Take(3)), h.Runtime);
            window = Show(group);
            var line = Assert.IsType<StackPanel>(Assert.IsType<Button>(More()).Content);
            Assert.Contains("1 earlier step", LogicalTexts(line));
            Assert.Equal(TimelineMetrics.IconSpacing, line.Spacing);
            Assert.Equal(new Thickness(0, 0, 12, 0), line.Margin);
            Assert.Equal(0, BrushColor(line.Background).A);
            var ellipsis = LogicalQuery.SelfAndDescendants<SwiftImage>(line).Single();
            Assert.Equal("ellipsis", ellipsis.SymbolName);
            Assert.Equal(TimelineMetrics.IconWidth, ellipsis.Width);
            Assert.Equal(ChatFont.Chat(SwiftFont.TextStyle.Caption), ellipsis.GetValue(SwiftImage.FontProperty));
            Assert.Equal(Resolved(ChromeTokens.SecondaryText, ellipsis), BrushColor(ellipsis.Foreground));
            Assert.Equal(Resolved(ChromeTokens.SecondaryText, line), BrushColor(TextNamed(line, "1 earlier step").Foreground));
            group.Update(new ValueList<TimelineEntry>(entries.Take(2)), false); // nothing is hidden any more
        });
        try
        {
            h.Main.Loop.RunUntilIdle();
            Tick();
            h.Main.OnMain(() =>
            {
                Assert.Null(More());
                group!.Update(new ValueList<TimelineEntry>(entries), false); // three are hidden again
            });
            h.Main.Loop.RunUntilIdle();
            Tick();
            h.Main.OnMain(() =>
            {
                Assert.Contains("3 earlier steps", LogicalTexts(Assert.IsType<Button>(More())));
                Press(More()!);
            });
            h.Main.Loop.RunUntilIdle();
            Tick();
            h.Main.OnMain(() => Assert.Null(More()));
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-091")]
    public void A_kind_reads_in_the_present_tense_when_any_of_its_calls_is_still_running_wherever_in_the_list_it_is() => headless.OnUiThread(() =>
    {
        TimelineEntry Entry(ToolCall.Kind kind, ToolCall.Status status) => ToolEntry(Call(kind, "x", status));
        string Text(params TimelineEntry[] entries) => WorkGroupSummary.Text(entries);
        Assert.Equal("Read files", Text(Entry(ToolCall.Kind.Read, ToolCall.Status.Completed), Entry(ToolCall.Kind.Read, ToolCall.Status.Completed)));
        Assert.Equal("Reading files", Text(Entry(ToolCall.Kind.Read, ToolCall.Status.Completed), Entry(ToolCall.Kind.Read, ToolCall.Status.Running)));
        Assert.Equal("Reading files", Text(Entry(ToolCall.Kind.Read, ToolCall.Status.Running), Entry(ToolCall.Kind.Read, ToolCall.Status.Completed)));
        Assert.Equal("Reading files", Text(Entry(ToolCall.Kind.Read, ToolCall.Status.Running), Entry(ToolCall.Kind.Read, ToolCall.Status.Completed), Entry(ToolCall.Kind.Read, ToolCall.Status.Failed)));
        Assert.Equal("Read files, ran commands", Text(Entry(ToolCall.Kind.Read, ToolCall.Status.Completed), Entry(ToolCall.Kind.Command, ToolCall.Status.Completed), Entry(ToolCall.Kind.Read, ToolCall.Status.Failed)));
        Assert.Equal("Read files, running commands", Text(Entry(ToolCall.Kind.Read, ToolCall.Status.Completed), Entry(ToolCall.Kind.Command, ToolCall.Status.Running), Entry(ToolCall.Kind.Read, ToolCall.Status.Declined)));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-101")]
    public void The_detail_stacks_the_detail_the_diff_files_and_the_output_top_to_bottom_with_the_path_directly_above_its_diff() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        h.Main.OnMain(() =>
        {
            // Built, not laid out: the diff lines are drawn in a monospaced key that only a themed app resolves.
            var edit = new FileEdit("src/a.cs", "@@ -1 +1 @@\n-a\n+b\n", 1, 1);
            var detail = new ToolDetailView(Call(ToolCall.Kind.Edit, "src/a.cs") with { Detail = "the detail", Edits = new ValueList<FileEdit>([edit]), Output = "the output" });
            var stack = Assert.IsType<StackPanel>(detail.Child);
            Assert.Equal(Orientation.Vertical, stack.Orientation);
            Assert.Equal(TimelineMetrics.RowSpacing, stack.Spacing);
            Assert.Equal(3, stack.Children.Count);
            var plate = Assert.IsType<Border>(stack.Children[1]);
            var file = Assert.IsType<StackPanel>(plate.Child);
            Assert.Equal(Orientation.Vertical, file.Orientation);
            Assert.Equal(0, file.Spacing); // the path sits directly above its diff
            Assert.Equal(new Thickness(10, 6), TextNamed(file, "src/a.cs").Margin);
            Assert.Equal(2, file.Children.Count);
        });
    });

    // ---- The merge track: initial values per mode, the tap target, the reduced-motion hops and the resting size (PR-CHAT-151, PR-HYDRAMERGE-102, -103) ----

    [Fact]
    [Trait("Req", "PR-CHAT-151")]
    [Trait("Req", "PR-HYDRAMERGE-103")]
    public void A_track_starts_from_the_values_of_its_mode_and_is_a_tap_target_across_its_frame() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            (double Position, (double Done, double Resting) Outcome) Start(HydraMergeTrack.Mode mode)
            {
                var track = new HydraMergeTrack(step: 6, count: 7, mode: mode);
                var window = Show(track);
                try { return LogicalQuery.SelfAndDescendants<HydraMergeTrackBody>(track).Single().AnimatableData; }
                finally { Close(window); }
            }
            Assert.Equal((6.0, (0.0, 0.0)), Start(HydraMergeTrack.Mode.Running));
            Assert.Equal((6.0, (1.0, 0.0)), Start(HydraMergeTrack.Mode.Done));
            Assert.Equal((6.0, (1.0, 1.0)), Start(HydraMergeTrack.Mode.Resting));
            var body = new HydraMergeTrackBody(position: 6, done: 1, rest: 1, count: 7, hops: true);
            var canvas = Assert.IsType<Canvas>(body.Child);
            Assert.Equal(0, BrushColor(canvas.Background).A);
            Assert.NotNull(canvas.Background); // transparent, not absent: it takes the tap across the whole frame
            // Built already at rest: the head at its resting size and the dots gone, with no animation to get there.
            var window = Show(body);
            try
            {
                Assert.Equal(18, Assert.Single(LogicalQuery.SelfAndDescendants<HydraMarkImage>(body)).Width);
                Assert.Equal(18, body.Width);
                Assert.Equal(0, canvas.Children.OfType<Canvas>().Single().Opacity);
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-151")]
    [Trait("Req", "PR-HYDRAMERGE-102")]
    public void The_head_hops_unless_the_reader_asked_for_reduced_motion_wherever_that_setting_is_made_above_the_track() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            double HeadTop(bool reduced)
            {
                var track = new HydraMergeTrack(step: 0, count: 7, mode: HydraMergeTrack.Mode.Running);
                var host = new Border { Child = track };
                EnvironmentValues.SetAccessibilityReduceMotion(host, reduced);
                var window = Show(host);
                try
                {
                    var body = LogicalQuery.SelfAndDescendants<HydraMergeTrackBody>(track).Single();
                    body.AnimatableData = (2.5, (0, 0)); // half way between two dots: the top of the arc
                    return Canvas.GetTop(LogicalQuery.SelfAndDescendants<HydraMarkImage>(body).Single());
                }
                finally { Close(window); }
            }
            Assert.Equal(-4, HeadTop(reduced: false), 9);
            Assert.Equal(0, HeadTop(reduced: true), 9);
        });
    });

    // ---- The sent message row and the attachment thumbnail: what the gate's survivors left open (PR-CHAT-077, -079, -088) ----

    [Fact]
    [Trait("Req", "PR-CHAT-077")]
    public void The_sent_row_is_a_hit_target_across_its_width_with_the_bubble_drawn_behind_its_text_and_the_pencil_frame_a_22_point_target() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        UserMessageRow? row = null;
        AvaloniaWindow? window = null;
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            row = new UserMessageRow(UserEntry("hello there", Guid.NewGuid()), h.Runtime, canRevert: true);
            window = Show(row);
            var outer = Assert.IsType<Border>(row.Child);
            Assert.Equal(new Thickness(96, 0, 0, 0), outer.Padding);
            Assert.NotNull(outer.Background);
            Assert.Equal(0, BrushColor(outer.Background).A);
            // The bubble's shape is behind the text's padding, never over it.
            var shape = Assert.Single(LogicalQuery.SelfAndDescendants<Avalonia.Controls.Shapes.Path>(row));
            var bubble = Assert.IsType<Panel>(shape.Parent);
            Assert.Same(shape, bubble.Children[0]);
            Assert.Equal(new Thickness(14, 9, 14 + UserBubble.Tail, 9), Assert.IsType<Border>(bubble.Children[1]).Padding);
            var frame = LogicalQuery.SelfAndDescendants<Border>(row).Single(border => border.Width == 22 && border.Height == 22);
            Assert.NotNull(frame.Background);
            Assert.Equal(0, BrushColor(frame.Background).A);
        });
        h.Main.OnMain(() => Close(window!));
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-080 (STOP #11): TimelineRows.swift:87, :161
    public void A_message_being_edited_keeps_its_hover_line_up_with_no_pointer_over_it() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        var pencilClock = new ManualFrameSource(); using var frames = FrameClock.Source.Override(pencilClock);
        var turn = Guid.NewGuid();
        UserMessageRow? row = null;
        AvaloniaWindow? window = null;
        h.Main.Loop.Run(() => h.Runtime.EnsureLoaded(), AppModelHarness.Bound);
        h.Main.OnMain(() =>
        {
            row = new UserMessageRow(UserEntry("edit me", turn), h.Runtime, canRevert: true);
            window = Show(row);
            Assert.Equal(0, HoverLine080(row).Opacity);
            h.Runtime.MessageEdit = new PromptEdit(turn, "edit me", ValueList<Attachment>.Empty);
        });
        try
        {
            pencilClock.Advance(TimeSpan.Zero); h.Main.AdvanceMilliseconds(200); pencilClock.Advance(TimeSpan.FromMilliseconds(200)); // OD-0076: let the pencil line's fade finish
            h.Main.OnMain(() =>
            {
                Settle(window!);
                Assert.Equal(1, HoverLine080(row!).Opacity);
                h.Runtime.MessageEdit = null;
            });
            pencilClock.Advance(TimeSpan.Zero); h.Main.AdvanceMilliseconds(200); pencilClock.Advance(TimeSpan.FromMilliseconds(200)); // OD-0076: let the pencil line's fade finish
            h.Main.OnMain(() =>
            {
                Settle(window!);
                Assert.Equal(0, HoverLine080(row!).Opacity);
            });
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-088")]
    public void The_summary_line_reads_a_duration_of_minutes_as_minutes_and_not_as_a_count_of_seconds() => headless.OnUiThread(() =>
    {
        var turn = Guid.NewGuid();
        string Label(double seconds) => TurnEndRow.Label(new TurnSummary { TurnID = turn, Status = TurnStatus.Completed, Duration = seconds, FilesChanged = 0, Additions = 0, Deletions = 0 });
        Assert.StartsWith("Worked for ", Label(75), StringComparison.Ordinal);
        Assert.Contains("15", Label(75), StringComparison.Ordinal);
        Assert.DoesNotContain("75", Label(75), StringComparison.Ordinal);
        Assert.DoesNotContain("3725", Label(3725), StringComparison.Ordinal);
        Assert.NotEqual(Label(75), Label(15));
    });

    [Fact]
    [Trait("Req", "PR-CHAT-079")]
    public void A_thumbnail_mounts_its_anchor_only_after_a_hover_and_only_when_it_is_not_anchorless_and_asks_the_thumbnail_cache_only_for_images() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var cache = new ThumbnailCache();
        using var cacheSeam = ThumbnailCache.Shared.Override(cache);
        var path = System.IO.Path.Combine(h.Project.Path, "seeded.png");
        h.Main.OnMain(() => ThumbnailMemory.Store(new DecodedBitmap(2, 2, 8, new byte[16]), path, 56));
        var image = new Attachment { Name = "seeded.png", Path = path, MimeType = "image/png" };
        var text = new Attachment { Name = "notes.txt", Path = System.IO.Path.Combine(h.Project.Path, "notes.txt"), MimeType = "text/plain" };
        AttachmentThumbnail? withPanel = null, anchorless = null, plain = null;
        AvaloniaWindow? window = null;
        h.Main.OnMain(() =>
        {
            var strip = new StackPanel { Spacing = 10 };
            withPanel = new AttachmentThumbnail(image, new AttachmentPreviewCoordinator());
            anchorless = new AttachmentThumbnail(image, new AttachmentPreviewSlot(), anchorless: true);
            plain = new AttachmentThumbnail(text, new AttachmentPreviewSlot());
            foreach (var thumbnail in new[] { withPanel, anchorless, plain }) strip.Children.Add(thumbnail);
            window = Show(strip);
            foreach (var thumbnail in new[] { withPanel, anchorless, plain })
                Assert.Empty(LogicalQuery.SelfAndDescendants<AttachmentAnchorCapture>(thumbnail));
            // A photo shown before starts out drawn: in the plate at once, with no fade wrapper.
            var drawn = Assert.Single(LogicalQuery.SelfAndDescendants<Image>(withPanel));
            var plateContent = Assert.IsAssignableFrom<Panel>(drawn.Parent);
            Assert.Same(drawn, Assert.Single(plateContent.Children)); // straight in, not inside a fade wrapper
            Assert.Equal(1, drawn.Opacity);
            Assert.Equal(56, Assert.IsType<Border>(plateContent.Parent).Width);
            Hover(window, LogicalQuery.SelfAndDescendants<Button>(withPanel).Single());
        });
        try
        {
            h.Main.OnMain(() =>
            {
                Settle(window!);
                Assert.Single(LogicalQuery.SelfAndDescendants<AttachmentAnchorCapture>(withPanel!));
                Hover(window!, LogicalQuery.SelfAndDescendants<Button>(anchorless!).Single());
            });
            h.Main.OnMain(() =>
            {
                Settle(window!);
                Assert.Empty(LogicalQuery.SelfAndDescendants<AttachmentAnchorCapture>(anchorless!));
            });
            Assert.Equal(0, h.Main.Loop.Run(() => cache.Decodes(), AppModelHarness.Bound)); // the text file never asked the cache for a photo
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    // ---- The plan card, the checklist and the finished turn: what the gate's survivors left open (PR-CHAT-104, -106, -111 to -115) ----

    private static void RaiseClosed(Avalonia.Controls.Primitives.Popup popup) =>
        ((MulticastDelegate?)typeof(Avalonia.Controls.Primitives.Popup).GetField("Closed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(popup))?.DynamicInvoke(popup, EventArgs.Empty);

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof PR-CHAT-113 (STOP #11): reopening after a dismissal; the record's open-popover result conflicts with the source (setFixedContent, ChatBadge.swift:138-147)
    [Trait("Req", "PR-CHAT-112")]
    public void A_plan_popover_dismissed_from_outside_can_be_opened_again_and_its_two_buttons_sit_8_apart_the_first_one_prominent() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        PlanCard? card = null;
        AvaloniaWindow? window = null;
        var entry = PlanEntry(ProposedPlan.State.Proposed);
        h.Main.OnMain(() =>
        {
            _ = AppCurrentEvent.CurrentWindow;
            card = new PlanCard(entry, h.Runtime);
            window = Show(card);
            Press(LogicalQuery.SelfAndDescendants<Button>(card).First());
        });
        try
        {
            h.Main.OnMain(() =>
            {
                Settle(window!);
                var shown = Assert.Single(presenter.Calls, call => call.Presented);
                var implement = LogicalQuery.SelfAndDescendants<Button>(shown.Popup.Child!).Single(button => LogicalTexts(button).Contains("Implement plan"));
                var dismiss = LogicalQuery.SelfAndDescendants<Button>(shown.Popup.Child!).Single(button => LogicalTexts(button).Contains("Dismiss"));
                var row = Assert.IsType<HStack>(implement.Parent);
                Assert.Equal(8, row.Spacing);
                Assert.Same(row, dismiss.Parent);
                Assert.True(row.Children.IndexOf(implement) < row.Children.IndexOf(dismiss));
                // Only the first is prominent: an accent fill at 0.3 behind its label.
                var implementFrame = Assert.IsType<Border>(implement.Content);
                Assert.Equal(Resolved(ChromeTokens.Accent.Opacity(0.3), implementFrame), BrushColor(implementFrame.Background));
                Assert.Null(Assert.IsType<Border>(dismiss.Content).Background);
                RaiseClosed(shown.Popup); // the reader clicks away
            });
            h.Main.OnMain(() =>
            {
                Settle(window!);
                Press(LogicalQuery.SelfAndDescendants<Button>(card!).First());
            });
            h.Main.OnMain(() =>
            {
                Settle(window!);
                Assert.Equal(2, presenter.Calls.Count(call => call.Presented)); // it opens again on the next tap, not closes
            });
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-115")]
    public void A_baseline_row_lays_its_children_out_left_to_right_by_their_spacing_on_one_fractional_baseline_through_wrappers() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        h.Main.OnMain(() =>
        {
            Assert.False(new TimelineBaselineRow().UseLayoutRounding); // a font's fractional first baseline is kept
            var plain = new TextBlock { Text = "plain", FontSize = 14 };
            var wrapped = new TextBlock { Text = "wrapped", FontSize = 14 };
            var wrapper = new Border { Padding = new Thickness(0, 6, 0, 0), Child = wrapped };
            var row = new TimelineBaselineRow { Spacing = 9 };
            row.Children.Add(plain);
            row.Children.Add(wrapper);
            var window = Show(row);
            try
            {
                Settle(window);
                Assert.Equal(plain.Bounds.Right + 9, wrapper.Bounds.Left, precision: 1);
                // The text inside the wrapper shares the plain text's baseline: the wrapper's own top is lifted by its padding.
                var plainBaseline = plain.Bounds.Top + plain.TextLayout.TextLines[0].Baseline;
                var wrappedBaseline = wrapper.Bounds.Top + 6 + wrapped.TextLayout.TextLines[0].Baseline;
                Assert.Equal(plainBaseline, wrappedBaseline, precision: 1);
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-104")]
    public void A_finished_turn_stacks_its_parts_without_a_gap_and_its_header_row_is_a_hit_target_6_points_wide_between_label_and_chevron() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            var turn = Guid.NewGuid();
            var prompt = UserEntry("the prompt", turn);
            var block = new TurnFinishedBlock(h.Runtime, turn, Summary(turn), new ValueList<TimelineEntry>([prompt]),
                new ValueList<TimelineEntry>([ToolEntry(Call(ToolCall.Kind.Read, "file"), turn), new TimelineEntry(new TimelineItem(turn, new TimelineItem.Content.Assistant(new AssistantMessage { Text = "the answer" })))]), null, false, false);
            var stack = Assert.IsType<StackPanel>(block.Child);
            Assert.Equal(new Thickness(0, 0, 0, TimelineMetrics.RowSpacing), Assert.IsType<UserMessageRow>(stack.Children[0]).Margin); // one row gap under each prompt
            Assert.Equal(Orientation.Vertical, stack.Orientation);
            Assert.Equal(0, stack.Spacing);
            var header = Assert.IsType<Button>(stack.Children.OfType<Button>().First());
            var row = Assert.IsType<HStack>(header.Content);
            Assert.Equal(6, row.Spacing);
            Assert.Equal(0, BrushColor(row.Background).A);
            Assert.NotNull(row.Background);
            var derived = Invoke(block, "DerivedFor", true)!;
            var steps = Assert.IsAssignableFrom<Control>(Invoke(block, "Steps", derived, false));
            var card = Assert.IsType<Border>(steps);
            Assert.Equal(Orientation.Vertical, Assert.IsType<StackPanel>(card.Child).Orientation);
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-144")]
    public void A_note_with_a_leading_address_keeps_its_title_less_the_stop_its_96_points_and_a_pill_that_toggles_the_report() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        HydraReportRow? row = null;
        AvaloniaWindow? window = null;
        h.Main.OnMain(() =>
        {
            _ = AppCurrentEvent.CurrentWindow;
            row = new HydraReportRow(Note143("Hydra merged the team's work.\nhttps://gitlab.com/g/p/-/merge_requests/7\nThree heads landed."));
            window = Show(row);
            Assert.Equal(new Thickness(0, 0, 96, 0), row.Padding);
            Assert.Contains("Hydra merged the team's work", Texts(row));
            Assert.DoesNotContain("Hydra merged the team's work.", Texts(row));
            Press(LogicalQuery.SelfAndDescendants<Button>(row).Single(button => !LogicalTexts(button).Contains("Open")));
        });
        try
        {
            h.Main.OnMain(() =>
            {
                Settle(window!);
                Assert.Equal(1, presenter.Calls.Count(call => call.Presented));
                Press(LogicalQuery.SelfAndDescendants<Button>(row!).Single(button => !LogicalTexts(button).Contains("Open")));
            });
            h.Main.Loop.RunUntilIdle();
            h.Main.OnMain(() => Assert.False(presenter.Calls[^1].Presented)); // a second tap folds it away again
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-079")]
    public void An_image_attachments_plate_is_rounded_12_points_in_the_continuous_style() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            var path = System.IO.Path.Combine(h.Project.Path, "plate.png");
            ThumbnailMemory.Store(new DecodedBitmap(2, 2, 8, new byte[16]), path, 56);
            var thumbnail = new AttachmentThumbnail(new Attachment { Name = "plate.png", Path = path, MimeType = "image/png" }, new AttachmentPreviewSlot());
            var window = Show(thumbnail);
            try
            {
                var plate = LogicalQuery.SelfAndDescendants<Border>(thumbnail).First(border => border.Width == 56);
                Assert.Equal(new CornerRadius(12), plate.CornerRadius);
                Assert.Equal(RoundedCornerStyle.Continuous, SwiftShapes.GetCornerStyle(plate));
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-108")] // TimelineRows.swift:2735-2827; the card's corner at 40 pt is Apple's at the largest radius that fits (OD-0102, ContinuousCornerReproofTests)
    public void The_file_cards_title_row_is_a_hit_target_across_its_width() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        h.Main.OnMain(() =>
        {
            var card = new TurnFileCard(Changes108(2), canUndo: false, onRevert: () => { }, onReview: _ => { });
            var window = Show(card);
            try
            {
                var titleRow = LogicalQuery.SelfAndDescendants<StackPanel>(card).First(stack => LogicalTexts(stack).Contains("Edited 2 files") && stack.Spacing == 8 && stack.Orientation == Orientation.Horizontal && stack.Background is not null);
                Assert.Equal(0, BrushColor(titleRow.Background).A);
            }
            finally { Close(window); }
        });
    });

    // ---- The merge pill's track tap, its sizing copies and its width glide (PR-CHAT-150, PR-HYDRAMERGE-105, -107) ----

    private static void MergeFrames(TestRuntime main, ManualFrameSource frames, double milliseconds)
    {
        frames.Advance(TimeSpan.Zero);
        for (var elapsed = 0.0; elapsed < milliseconds; elapsed += 16)
        {
            main.AdvanceMilliseconds(16);
            frames.Advance(TimeSpan.FromMilliseconds(16));
        }
    }

    [Fact]
    [Trait("Req", "PR-CHAT-150")]
    [Trait("Req", "PR-HYDRAMERGE-105")]
    [Trait("Req", "PR-HYDRAMERGE-107")]
    public void Tapping_the_track_of_a_landed_merge_toggles_its_report_without_passing_the_tap_on_and_its_sizing_copies_match_the_drawn_words() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        var presenter = new RecordingPresenter();
        using var seam = Presentation.Presenter.Override(presenter);
        HydraMergeRow? row = null;
        AvaloniaWindow? window = null;
        var bubbled = 0;
        h.Main.OnMain(() =>
        {
            _ = AppCurrentEvent.CurrentWindow;
            h.Runtime.IsHydraMerging = true;
            row = new HydraMergeRow(h.Runtime, new HydraMergePhase.Merging());
            row.AddHandler(Avalonia.Input.InputElement.PointerReleasedEvent, (_, _) => bubbled++, Avalonia.Interactivity.RoutingStrategies.Bubble);
            window = Show(row);
            h.Runtime.IsHydraMerging = false;
            row.Update(new HydraMergePhase.Outcome(new UserMessage { Text = "The merge stopped.\nhttps://gitlab.com/g/p/-/merge_requests/7\nBecause of a conflict." }));
        });
        try
        {
            MergeFrames(h.Main, frames, 2000);
            h.Main.OnMain(() =>
            {
                Settle(window!);
                // Every "Open" the pill holds, the drawn link and the hidden sizing copy, is set the same way.
                var opens = LogicalQuery.SelfAndDescendants<TextBlock>(row!).Where(block => block.Text == "Open").ToList();
                Assert.True(opens.Count >= 2);
                var expected = ChatFont.Chat(SwiftFont.TextStyle.Caption, SwiftFont.Weight.Semibold).Resolve();
                Assert.All(opens, block => { Assert.Equal(expected.FontWeight, block.FontWeight); Assert.Equal(expected.FontSize, block.FontSize); });
                // The sizing slots hang 6 points past the pill's vertical padding, the outer one 2 past its sides.
                var slots = LogicalQuery.SelfAndDescendants<Panel>(row!).Where(panel => panel.GetType().Name == "MorphSlot").ToList();
                Assert.Contains(slots, slot => slot.Margin == new Thickness(-2, -6));
                var track = Assert.Single(LogicalQuery.SelfAndDescendants<HydraMergeTrack>(row!));
                Click(window!, track);
            });
            h.Main.OnMain(() =>
            {
                Settle(window!);
                Assert.Equal(0, bubbled); // the track takes its own tap
                Assert.Single(presenter.Calls, call => call.Presented);
                Click(window!, Assert.Single(LogicalQuery.SelfAndDescendants<HydraMergeTrack>(row!)));
            });
            h.Main.Loop.RunUntilIdle();
            h.Main.OnMain(() => Assert.False(presenter.Calls[^1].Presented));
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

    [Fact]
    [Trait("Req", "PR-CHAT-150")]
    [Trait("Req", "PR-HYDRAMERGE-107")]
    public void The_capsule_grows_to_the_width_of_each_longer_stage_as_the_merge_advances() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        var frames = new ManualFrameSource();
        using var frameSeam = FrameClock.Source.Override(frames);
        using var genie = ChromeTokens.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        HydraMergeRow? row = null;
        AvaloniaWindow? window = null;
        double Width() => LogicalQuery.SelfAndDescendants<Border>(row!).First(border => border.Padding == new Thickness(TimelineMetrics.PillLeading, TimelineMetrics.PillVertical, TimelineMetrics.PillTrailing, TimelineMetrics.PillVertical)).Bounds.Width;
        h.Main.OnMain(() =>
        {
            h.Runtime.IsHydraMerging = true;
            h.Runtime.HydraMergeStep = HydraMergeStage.Merging;
            h.Runtime.HydraMergeStage = HydraMergeStage.Merging.Words;
            row = new HydraMergeRow(h.Runtime, new HydraMergePhase.Merging());
            window = Show(row);
        });
        try
        {
            double before = 0;
            MergeFrames(h.Main, frames, 800);
            h.Main.OnMain(() =>
            {
                Settle(window!);
                // While merging, the pill's slot hangs 2 past its sides and the stage's own slot none, both 6 past the padding.
                var slots = LogicalQuery.SelfAndDescendants<Panel>(row!).Where(panel => panel.GetType().Name == "MorphSlot").Select(panel => panel.Margin).ToList();
                Assert.Contains(new Thickness(-2, -6), slots);
                Assert.Contains(new Thickness(0, -6), slots);
                before = Width();
                h.Runtime.HydraMergeStep = HydraMergeStage.Syncing;
                h.Runtime.HydraMergeStage = HydraMergeStage.Syncing.Words;
            });
            h.Main.Loop.RunUntilIdle();
            h.Main.OnMain(() => Settle(window!)); // the layout pass is what starts the width's glide
            MergeFrames(h.Main, frames, 1500);
            h.Main.OnMain(() =>
            {
                Settle(window!);
                Assert.True(Width() > before + 20, $"{before} -> {Width()}");
            });
        }
        finally { h.Main.OnMain(() => Close(window!)); }
    });

}
