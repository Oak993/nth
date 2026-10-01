using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeTokens = Marrow.UI.Chrome.Chrome;
using static Marrow.App.Tests.Fixtures.TimelineRowsFinChecks;
using static Marrow.App.Tests.Views.Chat.TimelineRowsPrSupport;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/TimelineRows.swift (B3 52bc792), :236-388 HydraReportRow: the pill the heads' report (or
// a note from Hydra itself) takes in the conversation, and the Open link beside it.
[Collection("Headless")]
public sealed class TimelineRowsFinReportRowTests(HeadlessSession headless)
{
    private static UserMessage Report(string text, params long[] heads) => new() { Text = text, HydraHeads = new ValueList<long>(heads) };

    private static UserMessage Note(string text) => new() { Text = text, HydraHeads = null };

    private static Border PillOf(Control row) => Mounted<Border>(row).First(border => border.Padding == new Thickness(12, 8, 14, 8));

    [Fact]
    [Trait("Req", "UI-CHAT-048")] // TimelineRows.swift:241-329, :340-387
    public void The_heads_report_pill_overlaps_their_glyphs_and_names_who_is_done_in_a_tinted_pill_with_96_points_clear() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        runtime.OnMain(() =>
        {
            var two = new HydraReportRow(Report("## Hank: a\nbody\n## Walter: b\nbody", 0, 1));
            var host = new Border { Width = 400, Child = two };
            var panel = new StackPanel { Spacing = 20 };
            panel.Children.Add(host);
            var window = Show(panel);
            try
            {
                var pill = PillOf(two);
                // :313-316 12 leading, 14 trailing, 8 vertical, radius 18, .tint at 0.1, then :325-326 lead-aligned with 96 clear.
                Assert.Equal(new CornerRadius(18), pill.CornerRadius);
                Paints(pill.Background, ChromeTokens.Accent.Opacity(0.1), pill, "pill fill");
                Assert.Equal(new Thickness(0, 0, 96, 0), two.Padding);
                Near("nReportRow:1", Left(host, window), Left(pill, window));
                Assert.True(pill.Bounds.Width <= 400 - 96 + 0.5);
                // :271-275 the glyphs 18 by 18 overlapped by -4 points.
                var glyphs = Mounted<HydraGlyph>(two).ToList();
                Assert.Equal(["Hank", "Walter"], glyphs.Select(glyph => glyph.Persona.Name));
                Assert.All(glyphs, glyph => Assert.Equal(18, glyph.Size));
                Near("nReportRow:2", 18 - 4, Left(glyphs[1], window) - Left(glyphs[0], window));
                Assert.Empty(Mounted<HydraMarkImage>(two));
                // :277-281 the title: callout medium, Chrome.primaryText at 0.9, one line, tail-truncated, 8 points after the glyphs.
                var title = TextNamed(two, "Hank and Walter are done");
                Face(title, 12, FontWeight.Medium, ".chat(.callout, weight: .medium)");
                Paints(title.Foreground, ChromeTokens.PrimaryText.Opacity(0.9), title, "title");
                Assert.Equal(1, title.MaxLines);
                Assert.Equal(TextTrimming.CharacterEllipsis, title.TextTrimming);
                Near("nReportRow:3", 8, Left(title, window) - (Left(glyphs[1], window) + 18));
                // :282-286 the chevron follows 8 points on: caption2 semibold in Chrome.secondaryText.
                var chevron = Assert.Single(Mounted<SwiftImage>(two), image => image.SymbolName == "chevron.right");
                var font = SwiftImage.GetFont(chevron)!.Resolve();
                Assert.Equal(10, font.FontSize);
                Assert.Equal(FontWeight.SemiBold, font.FontWeight);
                Paints(chevron.Foreground, ChromeTokens.SecondaryText, chevron, "chevron");
                Near("nReportRow:4", 8, Left(chevron, window) - (Left(title, window) + title.Bounds.Width));
                // :263 HStack(spacing: 8): the glyphs, title and chevron share one centre line.
                OnOneCentreLine(window, glyphs[0], title, chevron);
                // :292 the tooltip when there is something behind the pill; :328 the accessibility label is the title.
                var button = Assert.Single(Mounted<Button>(two));
                Assert.True(button.IsEnabled);
                Assert.Equal("Show the message", ToolTip.GetTip(button));
                Assert.Equal("Hank and Walter are done", AutomationProperties.GetName(two));
            }
            finally { Close(window); }
        });
    });

    [Theory]
    [InlineData("## Hank: tidy\nall good", new long[] { 0 }, "Hank is done")]
    [InlineData("## Hank: tidy\n## Walter: build", new long[] { 0, 1 }, "Hank and Walter are done")]
    [InlineData("## Hank: a\n## Walter: b\n## Ada: c", new long[] { 0, 1, 2 }, "Hank, Walter and Ada are done")]
    [InlineData("## Hank: a\n## Walter: b (Failed: tests)\n## Ada: c", new long[] { 0, 1, 2 }, "Hank and Ada are done, Walter failed")]
    [InlineData("  ## Ada: c (stopped by you)", new long[] { 2 }, "Ada stopped")]
    [InlineData("## Hank: a\n## Walter: b (failure noted)", new long[] { 0, 1 }, "Hank and Walter are done")] // the marker is "(failed" exactly
    [InlineData("## Hank: a\n## Walter: b failed to build", new long[] { 0, 1 }, "Hank and Walter are done")]
    [InlineData("## Ada: c, stopped by you", new long[] { 2 }, "Ada is done")] // and "(stopped" needs its parenthesis
    [Trait("Req", "UI-CHAT-048")] // TimelineRows.swift:340-373 reportSummary: named with a failed or stopped outcome instead of done
    public void The_pill_title_lists_the_heads_that_are_done_and_names_a_failed_or_stopped_head_with_its_outcome(string text, long[] heads, string expected) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        runtime.OnMain(() =>
        {
            var row = new HydraReportRow(Report(text, heads));
            var window = Show(new StackPanel { Children = { row } });
            try { Assert.Contains(expected, Texts(row)); }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-048")] // TimelineRows.swift:350-351 one line, capped at 140 characters
    public void A_title_for_very_many_heads_is_one_line_cut_at_140_characters() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        runtime.OnMain(() =>
        {
            var many = Enumerable.Range(0, 25).Select(i => (long)i).ToArray();
            var names = many.Select(i => HydraRoster.Persona(i).Name).ToList();
            var full = string.Join(", ", names.Take(24)) + " and " + names[24] + " are done";
            Assert.True(full.Length > 140);
            var row = new HydraReportRow(Report("report", many));
            var window = Show(new StackPanel { Children = { row } });
            try { Assert.Contains(full[..139] + "…", Texts(row)); }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-048")] // TimelineRows.swift:248-250, :264-268, :288-292, :332-334 a note from Hydra itself
    public void A_note_from_Hydra_wears_the_mark_in_place_of_the_glyphs_and_its_first_line_less_one_stop_and_is_disabled_without_details() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        using var frameSeam = FrameClock.Source.Override(new ManualFrameSource());
        runtime.OnMain(() =>
        {
            var note = new HydraReportRow(Note("Held the heads back.\n\n  The lead is busy.  "));
            var bare = new HydraReportRow(Note("Just a title."));
            var empty = new HydraReportRow(Note(""));
            var panel = new StackPanel { Spacing = 20 };
            foreach (var row in new Control[] { note, bare, empty }) panel.Children.Add(row);
            var window = Show(panel);
            try
            {
                // :332-334 exactly one trailing full stop is dropped.
                Assert.Contains("Held the heads back", Texts(note));
                Assert.Contains("Just a title", Texts(bare));
                Assert.Contains("Hydra", Texts(empty));
                Assert.Equal("Held the heads back", AutomationProperties.GetName(note));
                // :265-269 the mark takes a glyph's slot: 18 by 18 in Chrome.secondaryText, no head glyphs.
                var mark = Assert.Single(Mounted<HydraMarkImage>(note));
                Assert.Equal(18, mark.Bounds.Width);
                Assert.Equal(18, mark.Bounds.Height);
                Paints(Assert.Single(Mounted<SwiftImage>(mark)).Foreground, ChromeTokens.SecondaryText, mark, "mark");
                Assert.Empty(Mounted<HydraGlyph>(note));
                var title = TextNamed(note, "Held the heads back");
                Near("nReportRow:5", 8, Left(title, window) - (Left(mark, window) + 18));
                OnOneCentreLine(window, mark, title, Assert.Single(Mounted<SwiftImage>(note), image => image.SymbolName == "chevron.right"));
                // :291-292 details behind it: enabled with the tooltip. Nothing behind it: disabled, no chevron, no tooltip at all.
                var enabled = Assert.Single(Mounted<Button>(note));
                Assert.True(enabled.IsEnabled);
                Assert.Equal("Show the message", ToolTip.GetTip(enabled));
                var disabled = Assert.Single(Mounted<Button>(bare));
                Assert.False(disabled.IsEnabled);
                Assert.Null(ToolTip.GetTip(disabled));
                Assert.DoesNotContain(Mounted<SwiftImage>(bare), image => image.SymbolName == "chevron.right");
            }
            finally { Close(window); }
        });
    });

    [Fact]
    [Trait("Req", "UI-CHAT-049")] // TimelineRows.swift:304-311 the Open link beside a Hydra note, :252-257 the leading address
    public void A_note_that_leads_with_an_address_gets_a_sibling_Open_link_in_caption_semibold_8_points_after_the_pill_button() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime();
        var opened = new List<string>();
        using var browser = Marrow.Services.Port.Workspace.Open.Override(url => { opened.Add(url.AbsoluteString); return true; });
        runtime.OnMain(() =>
        {
            var withLink = new HydraReportRow(Note("Hydra merged the team's work.\nhttps://gitlab.com/g/p/-/merge_requests/7\nThree heads landed."));
            var noLink = new HydraReportRow(Note("Hydra merged the team's work.\nThree heads landed."));
            var otherScheme = new HydraReportRow(Note("Hydra merged the team's work.\nftp://x.example/a\nThree heads landed."));
            var panel = new StackPanel { Spacing = 20 };
            panel.Children.Add(withLink);
            panel.Children.Add(noLink);
            panel.Children.Add(otherScheme);
            var window = Show(panel);
            try
            {
                var buttons = Mounted<Button>(withLink).ToList();
                Assert.Equal(2, buttons.Count);
                var open = buttons.Single(button => Texts(button).Contains("Open"));
                var pillButton = buttons.Single(button => !ReferenceEquals(button, open));
                // :1121-1126 a sibling control, never nested in the pill's button (a link in a button takes its clicks).
                Assert.DoesNotContain(open, Mounted<Button>(pillButton));
                Assert.Equal("Open in the browser", ToolTip.GetTip(open));
                var word = TextNamed(open, "Open");
                Face(word, 10, FontWeight.SemiBold, ".chat(.caption, weight: .semibold)");
                Paints(word.Foreground, ChromeTokens.SecondaryText, word, "Open");
                // :258 HStack(spacing: 8): the link follows the button 8 points on, on the same centre line.
                Near("nReportRow:6", 8, Left(open, window) - (Left(pillButton, window) + pillButton.Bounds.Width));
                OnOneCentreLine(window, pillButton, word);
                // The address is the link; what the note says after it is the report behind the chevron.
                Assert.True(pillButton.IsEnabled);
                Assert.Contains("chevron.right", Mounted<SwiftImage>(pillButton).Select(image => image.SymbolName));
                Assert.DoesNotContain(Mounted<Button>(noLink), button => Texts(button).Contains("Open"));
                Assert.DoesNotContain(Mounted<Button>(otherScheme), button => Texts(button).Contains("Open")); // only http and https
                Click(window, open);
                Assert.Equal(["https://gitlab.com/g/p/-/merge_requests/7"], opened);
            }
            finally { Close(window); }
        });
    });
}
