using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Git;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Xunit;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

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
}
