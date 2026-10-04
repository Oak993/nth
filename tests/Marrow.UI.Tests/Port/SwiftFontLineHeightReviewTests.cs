using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.Core.Models;
using Marrow.UI.Common;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// REVIEW (adversarial review of unit rec-linebox, 2026-10-04): ED-290 (1) keys the line box on the font a SwiftUI Text is given, and
// the nearest .font wins. The port's concatenated Text (StyledText, ED-158) carries its font on the runs and renders them into a
// host TextBlock with StyledText.Render; that path resolves each run's family, size and weight onto an Avalonia Run but never sets
// the host's SwiftFont.LineBoxProperty. So:
// (1) an explicitly written text style on a concatenated Text does not get its box: TimelineRows.swift:454-455, the Hydra finished
//     pill's `(HydraNameStyle.text(...) + Text(...)).font(.chat(.callout, weight: .medium))` (TimelineRows.cs:761-762) draws in
//     Inter's own 14.5-point box instead of the callout's 17 (masked at that one site today by the 18-point glyph beside it);
// (2) a fixed size on a concatenated Text does not cut off an outer text style's box: HydraNameText (HydraGlyph.swift :239-241,
//     `.font(.system(size: size, weight: weight))`) takes the 17 of any `.font(.callout)` above it, where SwiftUI's nearer fixed font
//     keeps the face's own box.
// Neither is visible at today's call sites; both are the mechanism the unit's ED-NEW-rec-linebox (2) says is complete.
[Collection("Headless")]
public sealed class SwiftFontLineHeightReviewTests(HeadlessSession headless)
{
    private static double Natural(SwiftFont font)
    {
        var resolved = font.Resolve();
        Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(resolved.FontFamily, resolved.FontStyle, resolved.FontWeight), out var face));
        var scale = resolved.FontSize / face.Metrics.DesignEmHeight;
        return (face.Metrics.Descent - face.Metrics.Ascent + face.Metrics.LineGap) * scale;
    }

    private static Window Show(Control content)
    {
        var window = new Window { Width = 600, Height = 300, Content = content };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    [Fact]
    [Trait("Req", "PR-MCP-199")]
    [Trait("Decision", "ED-290")]
    public void REVIEW_a_concatenated_text_given_an_explicit_text_style_draws_in_that_styles_line_box() => headless.OnUiThread(() =>
    {
        // TimelineRows.swift:454-455 (the Hydra finished pill): (a + b).font(.chat(.callout, weight: .medium)).
        var pill = new TextBlock { MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis };
        (new StyledText("Hank") + new StyledText(" finished working"))
            .Font(ChatFont.Chat(SwiftFont.TextStyle.Callout, SwiftFont.Weight.Medium)).Render(pill);
        var window = Show(new StackPanel { Children = { pill } });
        try
        {
            Assert.All(pill.Inlines!.OfType<Avalonia.Controls.Documents.Run>(), run => Assert.Equal(12, run.FontSize)); // the callout's 12 points reach the runs
            Assert.Equal(17, pill.TextLayout.Height, 3); // the callout's box (ED-290 (1)), as SwiftFont.Callout.Apply gives a plain Text
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-MCP-199")]
    [Trait("Decision", "ED-290")]
    public void REVIEW_a_concatenated_text_given_a_fixed_size_keeps_the_faces_own_box_under_an_outer_text_style() => headless.OnUiThread(() =>
    {
        // HydraGlyph.swift :239-241 HydraNameText: .font(.system(size: 13, weight: .medium)), the nearer font, under a .font(.callout).
        var name = new HydraNameText(new HydraPersona("Hank", "hydra-head-01", 0xFF8A3D), 13, SwiftFont.Weight.Medium);
        var window = Show(new StackPanel { Children = { name } }.Font(SwiftFont.Callout));
        try
        {
            var block = Assert.IsType<TextBlock>(name.Child);
            Assert.Equal(Natural(SwiftFont.System(13, SwiftFont.Weight.Medium)), block.TextLayout.Height, 3); // not the outer callout's 17
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-MCP-199")]
    [Trait("Decision", "ED-290")]
    public void REVIEW_a_link_paragraph_an_NSTextView_with_an_NSFont_keeps_its_own_line_under_a_text_style_container() => headless.OnUiThread(() =>
    {
        // LinkParagraphView.swift (an NSTextView drawing NSFont.systemFont(ofSize: pointSize), MarkdownView.swift:1137-1145) takes no
        // line box from SwiftUI's .font environment; ED-NEW-rec-linebox (2) says so ("an NSFont has none") for ParagraphTextPresenter,
        // but the LinkParagraphView path draws into LinkTextView's SelectableTextBlock, which inherits SwiftFont.LineBoxProperty. The
        // Swift hosts that put link paragraphs under an explicit .font(.system(.callout)): TimelineRows.swift:625-631 (HydraReportPopover,
        // TimelineRows.cs:884), HydraHeadPopovers.swift:82-93 and :154-160, HydraReportsPopover.swift:640-647.
        const string text = "See [the report](https://la-look.example/report) and [the log](https://la-look.example/log) for every step the head took today.";
        LinkParagraphView Paragraph() => new(text, pointSize: 12) { Width = 220 };
        var plain = Paragraph();
        var underCallout = Paragraph();
        var window = Show(new StackPanel { Children = { plain, new StackPanel { Children = { underCallout } }.Font(SwiftFont.Callout) } });
        try
        {
            var plainBlock = (SelectableTextBlock)((LinkTextView)plain.Child!).Child!;
            var calloutBlock = (SelectableTextBlock)((LinkTextView)underCallout.Child!).Child!;
            Assert.True(plainBlock.TextLayout.TextLines.Count > 1, "the paragraph wraps");
            Assert.Equal(plainBlock.TextLayout.TextLines.Count, calloutBlock.TextLayout.TextLines.Count);
            Assert.True(double.IsNaN(calloutBlock.LineHeight), $"the link paragraph took a text style's line box: {calloutBlock.LineHeight}");
            Assert.Equal(plain.Bounds.Height, underCallout.Bounds.Height, 0.5);
        }
        finally { window.Close(); }
    });
}
