using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Diff;
using Marrow.Core.Models;
using Marrow.Core.Port;
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
    // declined, the kind's (or an override) glyph when completed, all in a caption-size 16-point-wide column (:2067-2092) ----

    [Fact]
    [Trait("Req", "PR-CHAT-099")]
    public void Running_shows_an_indeterminate_progress_control() => headless.OnUiThread(() =>
    {
        var call = new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = "run", ToolCallStatus = ToolCall.Status.Running };
        var icon = new ToolStatusIcon(call);
        var column = Assert.IsType<Border>(icon.Child);
        Assert.Equal(TimelineMetrics.IconWidth, column.Width);
        Assert.IsType<ProgressBar>(column.Child);
        Assert.True(((ProgressBar)column.Child!).IsIndeterminate);
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
    public void Completed_shows_the_kinds_own_glyph_unless_an_override_is_given() => headless.OnUiThread(() =>
    {
        var call = new ToolCall { ToolCallKind = ToolCall.Kind.Read, Title = "read", ToolCallStatus = ToolCall.Status.Completed };
        var plain = (SwiftImage)((Border)new ToolStatusIcon(call).Child!).Child!;
        Assert.Equal("doc.text", plain.SymbolName); // the kind's own glyph (:2222-2233), no override given
        Assert.Equal(ChromeTokens.SecondaryText.Resolve(plain.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(plain.Foreground).Color);

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

    // ---- PR-CHAT-116: TurnEndRow.label - the three phrasings, over RelativeTime.duration fixtures (:2448-2455).
    // WINDOWS-ONLY this session: TimelineRows.Duration reads SwiftFormatters.CurrentLocale.Current, which resolves
    // the process's default locale through Marrow.Core.Port.IcuFormatting.ForLanguageTag -> icu.dll, absent on this
    // Linux container (DllNotFoundException, confirmed by a direct run). The tests are otherwise complete and ready
    // for the Windows re-run; kept as Rule=R-TEST-4 rather than a Req claim I could not watch pass myself. ----

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

    // ---- PR-CHAT-144: leadingURL / withoutFirstLine (:375-387). leadingURL calls SwiftCharacter.IsWhitespace,
    // which needs icu.dll (Marrow.Core.Port.IcuNative.HasBinaryProperty), absent on this Linux container
    // (DllNotFoundException, confirmed by a direct run): its own tests are WINDOWS-ONLY this session. withoutFirstLine
    // does not touch ICU and is fully green here, but proves only half the record, so it stays Rule=R-TEST-4 too. ----

    [Theory]
    [Trait("Rule", "R-TEST-4")] // WINDOWS-ONLY here: leadingURL needs icu.dll (SwiftCharacter.IsWhitespace).
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
    [Trait("Rule", "R-TEST-4")] // WINDOWS-ONLY here: same icu.dll dependency.
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
    [Trait("Rule", "R-TEST-4")] // green here, but proves only withoutFirstLine, half of PR-CHAT-144.
    [InlineData("https://example.com/x\nsecond\nthird", "second\nthird")]
    [InlineData("https://example.com/x\n  second  ", "second")] // Trimmed() strips the remainder's outer whitespace
    [InlineData("https://example.com/x", "")] // no newline at all: the empty string
    [InlineData("https://example.com/x\n", "")]
    public void Without_first_line_is_everything_after_the_first_newline_trimmed(string body, string expected) =>
        Assert.Equal(expected, HydraReportRow.WithoutFirstLine(body));

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

    // Excerpt and the quote chip that renders it are WINDOWS-ONLY this session: Excerpt's word split calls
    // SwiftCharacter.IsWhitespace, which needs icu.dll (Marrow.Core.Port.IcuNative.HasBinaryProperty), absent on
    // this Linux container (DllNotFoundException, confirmed by a direct run). Peel above is fully proven; PR-CHAT-078
    // as a whole needs both, so these stay Rule=R-TEST-4 rather than a Req claim I could not watch pass myself.

    [Theory]
    [Trait("Rule", "R-TEST-4")] // WINDOWS-ONLY here: Excerpt needs icu.dll (SwiftCharacter.IsWhitespace).
    [InlineData("one line", "one line")]
    [InlineData("  leading and trailing  \nsecond", "leading and trailing")] // the first non-empty line, trimmed
    [InlineData("\n\nfirst real line", "first real line")] // blank lines before the first real one are skipped
    [InlineData("word1    word2\tword3\nsecond", "word1 word2 word3")] // internal whitespace runs collapse to one space
    public void The_excerpt_is_the_first_nonempty_line_whitespace_collapsed(string text, string expected) =>
        Assert.Equal(expected, new ReplyQuote { Text = text }.Excerpt);

    [Fact]
    [Trait("Rule", "R-TEST-4")] // WINDOWS-ONLY here: same icu.dll dependency.
    public void The_excerpt_cuts_at_72_characters_with_a_trailing_ellipsis()
    {
        var exact72 = new string('a', 72);
        Assert.Equal(exact72, new ReplyQuote { Text = exact72 }.Excerpt);
        var over = new string('a', 73);
        Assert.Equal(new string('a', 72) + "…", new ReplyQuote { Text = over }.Excerpt);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // WINDOWS-ONLY here: QuoteChip calls quote.Excerpt, which needs icu.dll.
    public void The_quote_chip_shows_the_excerpt_glyph_tooltip_and_automation_name() => headless.OnUiThread(() =>
    {
        var rowType = typeof(UserMessageRow);
        var method = rowType.GetMethod("QuoteChip", HiddenStatic)!;
        var quote = new ReplyQuote { Text = "  quoted   words  here  " };
        var chip = (Control)method.Invoke(null, [quote])!;

        // :110 .modifier(ChipChrome()): a 9/7/5 padded capsule filled with Chrome.overlay(0.12) (ComposerView.cs:1619-1628).
        var capsule = Assert.IsType<Border>(chip);
        Assert.Equal(new Avalonia.Thickness(9, 5, 7, 5), capsule.Padding);
        Assert.Equal(ChromeTokens.Overlay(0.12).Resolve(capsule.ActualThemeVariant),
            Assert.IsAssignableFrom<ISolidColorBrush>(capsule.Background).Color);
        Assert.Equal(new Avalonia.CornerRadius(1000), capsule.CornerRadius); // Capsule(): fully round per its own helper

        var stack = Assert.IsType<StackPanel>(capsule.Child);
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

    // WINDOWS-ONLY here: TurnFinishedBlock's constructor always calls Header(), which reads TurnEndRow.Label ->
    // TimelineRows.Duration -> SwiftFormatters.CurrentLocale.Current, needing icu.dll (absent on this Linux
    // container, DllNotFoundException confirmed by a direct run; the same limitation already noted for PR-CHAT-116
    // and the pre-existing TimelineRowsPrDerivationTests). The title-singular clause above is unaffected and fully
    // proven; this "shown only with files" clause needs the full block and stays Rule=R-TEST-4.
    [Fact]
    [Trait("Rule", "R-TEST-4")]
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

        var shown = BlockFor(withFile);
        var shownWindow = Show(shown);
        Assert.NotEmpty(shown.GetVisualDescendants().OfType<TurnFileCard>());
        Close(shownWindow);

        var emptyFiles = BlockFor(noFiles);
        var emptyWindow = Show(emptyFiles);
        Assert.Empty(emptyFiles.GetVisualDescendants().OfType<TurnFileCard>());
        Close(emptyWindow);

        var none = BlockFor(noChanges);
        var noneWindow = Show(none);
        Assert.Empty(none.GetVisualDescendants().OfType<TurnFileCard>());
        Close(noneWindow);
    });

    // ---- PR-CHAT-098: a tool row's chevron points down for a popover/head and right (rotating) for an in-place
    // expansion, beside the subject, and is absent otherwise (:1799-1810).
    // Not claimed as PROVEN this session: two of its three branches are proven below, but the third (showsOutput ->
    // chevron.right) cannot be observed on this container. Rendering a ToolRow whose call has non-empty Output
    // throws System.InvalidOperationException "Could not create glyphTypeface. Font family: Cascadia Mono" from
    // Avalonia's Skia text formatter (confirmed reproducible, not order-dependent: fails alone, and a warm-up
    // Measure() of the same FontFamily beforehand does not avoid it, unlike Inter's own warm-up in
    // HeadlessFixtures.WarmFonts). The embedded CascadiaMono-*.ttf files are genuine, valid, Microsoft-signed
    // TrueType data (`file` confirms this), so the font asset itself is not the problem; nothing in this codebase's
    // existing tests renders actual Cascadia Mono glyph text anywhere (only SwiftFontTests.cs checks the
    // FontFamily's name/key metadata, never lays text out with it), so this looks like a standing Linux-headless
    // Skia font-loading gap for this specific embedded font, not something introduced by this session. Filed as
    // STOP cloud-TimelineRows-pr #14: same class of container limitation as icu.dll, but for Cascadia Mono glyph
    // rendering; it will also block PR-CHAT-101 and PR-CHAT-100's monospaced-digit "exit N" text once reached.

    private static string[] Symbols(Avalonia.Controls.Control control) =>
        control.GetVisualDescendants().OfType<SwiftImage>().Select(image => image.SymbolName ?? "").ToArray();

    [Fact]
    [Trait("Rule", "R-TEST-4")] // two of three branches of PR-CHAT-098; see STOP #14 above for the third.
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

    // WINDOWS-ONLY here: throws on this container as soon as the row's showsOutput branch tries to lay out its
    // Cascadia Mono content; see STOP #14 above. Kept for the Windows re-run: it is the only test in this file that
    // exercises the chevron.right / rotation branch.
    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void A_call_that_shows_output_draws_chevron_right_rotating_ninety_degrees_when_expanded() => headless.OnUiThread(() =>
    {
        using var h = Harness();
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
        h.Main.OnMain(() =>
        {
            Assert.Contains("chevron.right", Symbols(row!));
            Assert.DoesNotContain("chevron.down", Symbols(row!));
            var chevron = row!.GetVisualDescendants().OfType<SwiftImage>().Single(image => image.SymbolName == "chevron.right");
            var before = ((RotateTransform?)chevron.RenderTransform)?.Angle ?? 0;
            Assert.Equal(0, before);
            var button = row!.GetVisualDescendants().OfType<Button>().First();
            Click(window!, button);
        });
        h.Main.OnMain(() =>
        {
            var chevron = row!.GetVisualDescendants().OfType<SwiftImage>().Single(image => image.SymbolName == "chevron.right");
            var after = ((RotateTransform?)chevron.RenderTransform)?.Angle ?? 0;
            Assert.Equal(90, after);
            Close(window!);
        });
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // two of three branches of PR-CHAT-098; see STOP #14 above for the third.
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
    // tapped (:1522-1557). Not claimed as PROVEN: the "only two show, with the right hidden count" clause below is
    // real, mutation-tested proof, but the "tapping the line reveals every step" clause cannot be observed here -
    // rendering the fifth ToolRow after a click throws System.InvalidOperationException "Could not create
    // glyphTypeface. Font family: Inter", reproducibly, alone, using the exact same Inter text that renders fine in
    // every other test in this file. This looks like a third distinct Linux-headless Skia font-loading gap (after
    // icu.dll and STOP #14's Cascadia Mono one), triggered specifically by several new text layouts appearing at
    // once after a click-driven re-render, not by anything about this record's own logic. Filed as STOP
    // cloud-TimelineRows-pr #15. ----

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
            // Command-kind entries with no output/detail (no chevron, no ICU/monospace font needed to render them -
            // see STOP #14) stand in for "a step of the work": PR-CHAT-092 is about which/how-many mount, not their
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

    // WINDOWS-ONLY here: throws mid-render after the click; see STOP #15 above. Kept for the Windows re-run: it is
    // the only test in this file that exercises the reveal-on-tap clause.
    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void Tapping_the_earlier_steps_line_reveals_every_step() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        AvaloniaWindow? window = null;
        WorkSteps? steps = null;
        var showsAll = new ObservableBoolBox();
        h.Main.OnMain(() =>
        {
            var entries = new ValueList<TimelineEntry>(Enumerable.Range(0, 5).Select(_ => Tool()));
            steps = new WorkSteps(entries, h.Runtime, showsAll.AsBinding());
            window = Show(steps);
            Assert.Equal(2, steps.GetVisualDescendants().OfType<ToolRow>().Count());
        });
        h.Main.OnMain(() =>
        {
            var more = steps!.GetVisualDescendants().OfType<Button>().Single();
            Click(window!, more);
        });
        h.Main.OnMain(() =>
        {
            Assert.Equal(5, steps!.GetVisualDescendants().OfType<ToolRow>().Count());
            Assert.Empty(steps!.GetVisualDescendants().OfType<Button>()); // the earlier-steps line itself is removed once shown
            Close(window!);
        });
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
    [Trait("Req", "PR-CHAT-080")]
    public void A_revertable_message_shows_a_pencil_only_on_hover_and_a_locked_one_shows_none() => headless.OnUiThread(() =>
    {
        using var h = Harness();
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
        h.Main.OnMain(() =>
        {
            Assert.Equal(1, HoverLine080(row!).Opacity);
            Assert.Equal(0, HoverLine080(locked!).Opacity);
            Close(window!);
        });
    });

    [Fact]
    [Trait("Req", "PR-CHAT-080")]
    public void The_pencil_opens_the_edit_on_the_runtime_through_the_editor_popover_and_a_second_tap_closes_it() => headless.OnUiThread(() =>
    {
        using var h = Harness();
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
        h.Main.OnMain(() =>
        {
            var shown = Assert.Single(presenter.Calls, call => call.Presented);
            Assert.IsType<MessageEditor>(shown.Popup.Child);
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

    // WINDOWS-ONLY here: AssistantMessageRow.Body builds `new TextBlock { Text = TurnEndRow.Label(summary) }`
    // eagerly at construction (TimelineRows.cs:1576), and TurnEndRow.Label calls TimelineRows.Duration, which needs
    // icu.dll for the current locale (same root cause as PR-CHAT-116/PR-GIT-151's downgrades) -- so a row with a
    // non-null summary throws on construction, before any assertion runs. Confirmed reproducible alone below.
    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void A_reply_with_a_summary_renders_its_markdown_and_a_hidden_hover_line_carrying_the_turn_label() => headless.OnUiThread(() =>
    {
        using var h = Harness();
        h.Main.OnMain(() =>
        {
            var turn = Guid.NewGuid();
            var row = new AssistantMessageRow(Reply("some `code` here"), Summary(turn), h.Runtime);
            var window = Show(row);
            var markdown = Assert.Single(row.GetVisualDescendants().OfType<MarkdownView>());
            Assert.False(markdown.IsStreaming);
            var line = row.GetVisualDescendants().OfType<Control>().Single(control => control.Height == TimelineMetrics.HoverLineHeight && double.IsNaN(control.Width));
            Assert.Equal(0, line.Opacity);
            var label = line.GetVisualDescendants().OfType<TextBlock>().Single();
            Assert.StartsWith("Worked for ", label.Text, StringComparison.Ordinal);
            Assert.Equal(Resolved(ChromeTokens.SecondaryText, label), BrushColor(label.Foreground));
            var outer = row.GetVisualDescendants().OfType<Control>().First(control => control.Margin.Bottom == -(TimelineMetrics.HoverLineHeight + 2));
            Assert.NotNull(outer);
            Close(window);
        });
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
            // (App.Views.Diff.DiffInspector's) does not resolve headless (a distinct font gap from icu.dll/STOP #14/#15,
            // not investigated further since nothing in this clause needs a layout pass to verify).
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
}
