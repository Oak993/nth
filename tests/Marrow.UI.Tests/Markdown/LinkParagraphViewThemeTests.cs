using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Markdown;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using Marrow.UI.Tests.Fixtures;
using Xunit;
using UiChrome = Marrow.UI.Chrome.Chrome;

namespace Marrow.UI.Tests.Markdown;

// Source under test: DroppyCode/UI/Markdown/LinkParagraphView.swift (B3 52bc792). The link run's look (:134-150 and
// :481-496), the favicon attachment (:134-145) and the head mention chip (:179-244). The port draws a link paragraph
// two ways, and both are held to the same Swift: the LinkParagraphView constructor draws into a SelectableTextBlock
// (RenderText), and ProseRunView draws the attributed string that LinkParagraphView.Attributed builds (Render).
// Favicon hosts below are seeded straight into the cache memory (FaviconCacheSeed): nothing is fetched.
[Collection("Headless")]
public sealed class LinkParagraphViewThemeTests(HeadlessSession headless)
{
    private static readonly IReadOnlyDictionary<string, HydraMentionTarget> NoTargets = new Dictionary<string, HydraMentionTarget>();

    private static Window Show(Control content)
    {
        content.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        content.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        var window = new Window { Width = 400, Height = 300, Content = content };
        window.Show();
        Settle();
        return window;
    }

    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static SelectableTextBlock BlockOf(LinkParagraphView paragraph) => (SelectableTextBlock)((LinkTextView)paragraph.Child!).Child!;

    // NSFont.descender of the system face at `size`: negative, in points (glyph metrics store the descent positive).
    private static double Descender(double size)
    {
        var resolved = SwiftFont.SystemFont(size).Resolve();
        Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(resolved.FontFamily, resolved.FontStyle, resolved.FontWeight), out var glyph));
        return -glyph.Metrics.Descent * resolved.FontSize / glyph.Metrics.DesignEmHeight;
    }

    private static void AssertColour(SwiftColor expected, SwiftColor? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Resolve(ThemeVariant.Light), actual!.Resolve(ThemeVariant.Light));
        Assert.Equal(expected.Resolve(ThemeVariant.Dark), actual.Resolve(ThemeVariant.Dark));
    }

    private static Color BrushColour(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static Run RunWithText(SelectableTextBlock block, string text) =>
        block.Inlines!.OfType<Run>().Single(run => run.Text == text);

    // The layout box of the character `into` places into the first Run holding `text`; inline containers count one.
    private static Rect CharacterRect(SelectableTextBlock block, string text, int into = 1)
    {
        var offset = 0;
        foreach (var inline in block.Inlines!)
        {
            if (inline is Run run)
            {
                var value = run.Text ?? "";
                var found = value.IndexOf(text, StringComparison.Ordinal);
                if (found >= 0) return block.TextLayout.HitTestTextRange(offset + found + into, 1).First();
                offset += value.Length;
            }
            else if (inline is InlineUIContainer)
            {
                offset++;
            }
        }
        throw new InvalidOperationException("no run holds " + text);
    }

    // A point `fraction` of the way across a layout box, on its middle line.
    private static Point At(Rect rect, double fraction) => new(rect.X + rect.Width * fraction, rect.Center.Y);

    private static T WithTheme<T>(AppTheme theme, Func<T> body)
    {
        var original = ThemeManager.Current;
        try
        {
            ThemeManager.Current = theme;
            return body();
        }
        finally
        {
            ThemeManager.Current = original;
        }
    }

    private static HydraMentionTarget Target() => new() { ThreadID = Guid.NewGuid(), Task = "t", Status = HydraHeadInfo.Status.Running };

    // ---- PR-THEME-141, UI-THEME-041: the favicon attachment, attributed string path (:134-145) ----

    [Fact]
    [Trait("Req", "PR-THEME-141")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:134-150
    [Trait("Req", "UI-THEME-041")]
    public void A_cached_icon_is_a_14_point_attachment_then_one_base_font_space_then_the_link_text() => headless.OnUiThread(() =>
    {
        FaviconCacheSeed.Seed("tl-icon-a.example", new StandInImage());
        try
        {
            var text = LinkParagraphView.Attributed("see [my docs](https://tl-icon-a.example/a) end", 13, dimmed: false);

            Assert.Equal(["see ", ParagraphText.AttachmentCharacter, " ", "my docs", " end"], text.Runs.Select(run => run.Text));
            var attachment = text.Runs[1].Attachment!; // :137-140
            Assert.IsType<StandInImage>(attachment.Image);
            Assert.Equal(0, attachment.X);
            Assert.Equal(14, attachment.Width);
            Assert.Equal(14, attachment.Height);
            var descender = Descender(13);
            Assert.InRange(descender, -6, -1.5); // a descender lies below the baseline
            Assert.Equal(descender + 1, attachment.Y, 6); // :139 y = base.descender + 1

            var space = text.Runs[2]; // :141-144 one space, in the base font and the text colour
            Assert.Equal(SwiftFont.SystemFont(13), space.Font);
            AssertColour(SwiftColor.LabelColor, space.ForegroundColor);
            Assert.Null(space.Link);

            var link = text.Runs[3]; // :147-150 the link text carries the font and the link only
            Assert.Equal(SwiftURL.Create("https://tl-icon-a.example/a"), link.Link);
            Assert.Equal(SwiftFont.BoldSystemFont(13), link.Font);
            Assert.Null(link.ForegroundColor);
        }
        finally { FaviconCacheSeed.Remove("tl-icon-a.example"); }
    });

    [Fact]
    [Trait("Req", "PR-THEME-141")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:135-136, :147-150
    [Trait("Req", "UI-THEME-041")]
    public void A_link_whose_host_has_no_cached_icon_has_no_attachment_and_no_space() => headless.OnUiThread(() =>
    {
        var text = LinkParagraphView.Attributed("see [my docs](https://tl-icon-none.example/a) end", 13, dimmed: false);

        Assert.Equal(["see ", "my docs", " end"], text.Runs.Select(run => run.Text));
        Assert.DoesNotContain(text.Runs, run => run.Attachment is not null);
        Assert.Equal("see my docs end", text.String);
    });

    [Fact]
    [Trait("Req", "PR-THEME-141")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:139: the base font's descender, so it follows the point size
    [Trait("Req", "UI-THEME-041")]
    public void The_attachment_baseline_offset_is_the_base_fonts_own_descender_plus_one() => headless.OnUiThread(() =>
    {
        FaviconCacheSeed.Seed("tl-icon-b.example", new StandInImage());
        try
        {
            var small = LinkParagraphView.Attributed("[my docs](https://tl-icon-b.example/a)", 13, dimmed: false).Runs[0].Attachment!;
            var large = LinkParagraphView.Attributed("[my docs](https://tl-icon-b.example/a)", 26, dimmed: false).Runs[0].Attachment!;

            Assert.Equal(Descender(13) + 1, small.Y, 6);
            Assert.Equal(Descender(26) + 1, large.Y, 6);
            Assert.True(large.Y < small.Y); // a bigger face descends further
            Assert.Equal(14, large.Width); // the icon itself stays 14 by 14 at every size
            Assert.Equal(14, large.Height);
        }
        finally { FaviconCacheSeed.Remove("tl-icon-b.example"); }
    });

    [Fact]
    [Trait("Req", "PR-THEME-141")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:135: `url.host?.lowercased()` keys the cache
    public void The_icon_is_looked_up_by_the_lowercased_host() => headless.OnUiThread(() =>
    {
        FaviconCacheSeed.Seed("tl-icon-c.example", new StandInImage());
        try
        {
            var text = LinkParagraphView.Attributed("[my docs](https://TL-Icon-C.Example/a)", 13, dimmed: false);

            Assert.NotNull(text.Runs[0].Attachment);
        }
        finally { FaviconCacheSeed.Remove("tl-icon-c.example"); }
    });

    [Fact]
    [Trait("Req", "PR-THEME-141")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:121, :141-144: the space wears the text colour, dimmed or not
    public void The_space_after_the_icon_takes_the_paragraphs_text_colour_dimmed_or_not() => headless.OnUiThread(() =>
    {
        FaviconCacheSeed.Seed("tl-icon-d.example", new StandInImage());
        try
        {
            var dimmed = LinkParagraphView.Attributed("[my docs](https://tl-icon-d.example/a)", 13, dimmed: true);

            AssertColour(SwiftColor.LabelColor.Opacity(0.7), dimmed.Runs[1].ForegroundColor);
            Assert.Null(dimmed.Runs[2].ForegroundColor); // the link text still takes its colour from the view
        }
        finally { FaviconCacheSeed.Remove("tl-icon-d.example"); }
    });

    // ---- PR-THEME-141, UI-THEME-041: the same on the SelectableTextBlock path (RenderText) ----

    [Fact]
    [Trait("Req", "PR-THEME-141")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:134-150
    [Trait("Req", "UI-THEME-041")]
    public void The_drawn_paragraph_puts_the_icon_inline_before_one_space_and_the_link_text() => headless.OnUiThread(() =>
    {
        FaviconCacheSeed.Seed("tl-icon-e.example", new StandInImage());
        try
        {
            var paragraph = new LinkParagraphView("see [my docs](https://tl-icon-e.example/a) end", pointSize: 13);
            var block = BlockOf(paragraph);
            var inlines = block.Inlines!.ToList();

            Assert.Equal(5, inlines.Count);
            Assert.Equal("see ", Assert.IsType<Run>(inlines[0]).Text);
            var icon = Assert.IsType<Image>(Assert.IsType<InlineUIContainer>(inlines[1]).Child); // an inline run, not a separate view
            Assert.IsType<StandInImage>(icon.Source);
            Assert.Equal(14, icon.Width);
            Assert.Equal(14, icon.Height);
            var space = Assert.IsType<Run>(inlines[2]);
            Assert.Equal(" ", space.Text);
            Assert.Equal(13, space.FontSize); // the base font
            Assert.Equal(SwiftColor.LabelColor.Resolve(block.ActualThemeVariant), BrushColour(space.Foreground));
            Assert.Equal("my docs", Assert.IsType<Run>(inlines[3]).Text);
            Assert.Equal(" end", Assert.IsType<Run>(inlines[4]).Text);
        }
        finally { FaviconCacheSeed.Remove("tl-icon-e.example"); }
    });

    [Fact]
    [Trait("Req", "PR-THEME-141")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:135-145
    [Trait("Req", "UI-THEME-041")]
    public void The_drawn_paragraph_has_no_icon_and_no_space_without_a_cached_icon() => headless.OnUiThread(() =>
    {
        var block = BlockOf(new LinkParagraphView("see [my docs](https://tl-icon-none.example/a) end", pointSize: 13));

        Assert.Equal(["see ", "my docs", " end"], block.Inlines!.Cast<Run>().Select(run => run.Text));
    });

    [Fact]
    [Trait("Req", "PR-THEME-141")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:139 `bounds = NSRect(x: 0, y: base.descender + 1, ...)`
    [Trait("Req", "UI-THEME-041")]
    public void The_drawn_icons_bottom_edge_hangs_the_descender_plus_one_below_the_baseline() => headless.OnUiThread(() =>
    {
        FaviconCacheSeed.Seed("tl-icon-f.example", new StandInImage());
        try
        {
            var paragraph = new LinkParagraphView("see [my docs](https://tl-icon-f.example/a) end", pointSize: 13);
            var block = BlockOf(paragraph);
            var window = Show(paragraph);
            try
            {
                var icon = (Image)block.Inlines!.OfType<InlineUIContainer>().Single().Child!;
                var iconBottom = icon.TranslatePoint(new Point(0, icon.Bounds.Height), block)!.Value.Y;
                var line = block.TextLayout.TextLines[0];

                // bounds.y is measured up from the baseline, so a negative one puts the bottom edge below it.
                Assert.Equal(line.Baseline - (Descender(13) + 1), iconBottom, 0.6);
                Assert.Equal(14, icon.Bounds.Height, 0.6); // the margin that offsets it does not squash the image
            }
            finally { window.Close(); }
        }
        finally { FaviconCacheSeed.Remove("tl-icon-f.example"); }
    });

    // ---- PR-THEME-140, UI-THEME-040: the link's look ----

    [Fact]
    [Trait("Req", "PR-THEME-140")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:485-489 linkTextAttributes; :147-150
    [Trait("Req", "UI-THEME-040")]
    public void A_drawn_link_is_bold_in_the_theme_accent_with_no_underline_and_prose_keeps_the_label_colour() => headless.OnUiThread(() =>
    {
        WithTheme(AppTheme.Dracula, () =>
        {
            var paragraph = new LinkParagraphView("go [my docs](https://tl-look.example/a) now", pointSize: 17);
            var block = BlockOf(paragraph);
            var accent = UiChrome.AccentNSColor.Resolve(block.ActualThemeVariant);
            var label = SwiftColor.LabelColor.Resolve(block.ActualThemeVariant);
            Assert.NotEqual(label, accent); // set up: a colour that can tell the two apart

            var link = RunWithText(block, "my docs");
            Assert.Equal(accent, BrushColour(link.Foreground));
            Assert.Equal(FontWeight.Bold, link.FontWeight); // always bold
            Assert.Equal(17, link.FontSize); // at the paragraph's own point size
            Assert.True(link.TextDecorations is null or { Count: 0 }); // underline style 0
            Assert.Equal(label, BrushColour(RunWithText(block, "go ").Foreground));
            Assert.Equal(label, BrushColour(RunWithText(block, " now").Foreground));
            return 0;
        });
    });

    [Fact]
    [Trait("Req", "PR-THEME-140")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:481-489: set per render so a theme change recolours links on screen
    [Trait("Req", "UI-THEME-040")]
    public void A_theme_change_recolours_links_already_on_screen_and_leaves_prose_alone() => headless.OnUiThread(() =>
    {
        var original = ThemeManager.Current;
        var paragraph = new LinkParagraphView("go [my docs](https://tl-look.example/a) now", pointSize: 13);
        var block = BlockOf(paragraph);
        var window = Show(paragraph);
        try
        {
            ThemeManager.Current = AppTheme.Dracula;
            var first = UiChrome.AccentNSColor.Resolve(block.ActualThemeVariant);
            ThemeManager.Current = AppTheme.Nord;
            var second = UiChrome.AccentNSColor.Resolve(block.ActualThemeVariant);
            Assert.NotEqual(first, second); // set up: two themes with two accents

            window.RequestedThemeVariant = ThemeVariant.Dark; // the appearance change AppKit re-renders on
            Settle();
            window.RequestedThemeVariant = ThemeVariant.Light;
            Settle();

            Assert.Equal(second, BrushColour(RunWithText(block, "my docs").Foreground));
            Assert.Equal(SwiftColor.LabelColor.Resolve(block.ActualThemeVariant), BrushColour(RunWithText(block, "go ").Foreground));
        }
        finally
        {
            window.Close();
            ThemeManager.Current = original;
        }
    });

    [Fact]
    [Trait("Req", "PR-THEME-140")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:485-494: linkTextAttributes set before the equality test
    [Trait("Req", "UI-THEME-040")]
    public void Rendering_equal_text_again_recolours_links_without_replacing_or_relaying_the_text() => headless.OnUiThread(() =>
    {
        var original = ThemeManager.Current;
        try
        {
            ThemeManager.Current = AppTheme.Dracula;
            var view = new LinkTextView();
            var first = LinkParagraphView.Attributed("go [my docs](https://tl-look.example/a) now", 13, dimmed: false);
            view.Render(first);
            var presenter = Assert.IsType<ParagraphTextPresenter>(view.Child);
            AssertColour(UiChrome.AccentNSColor, presenter.LinkColor);
            var draggedHeight = view.HeightForWidth(120);

            ThemeManager.Current = AppTheme.Nord;
            var again = LinkParagraphView.Attributed("go [my docs](https://tl-look.example/a) now", 13, dimmed: false);
            Assert.NotSame(first, again);
            view.Render(again);

            AssertColour(UiChrome.AccentNSColor, presenter.LinkColor); // the new accent
            Assert.Same(first, view.TextStorageForTests); // equal text: the storage is not replaced
            Assert.Equal(draggedHeight, view.HeightForWidth(120)); // and the measurement is not thrown away
            Assert.Equal(2, view.RenderCountForTests);

            view.Render(LinkParagraphView.Attributed("go [other docs](https://tl-look.example/a) now", 13, dimmed: false));

            Assert.NotSame(first, view.TextStorageForTests); // different text: replaced
            Assert.Equal("go other docs now", view.TextStorageForTests!.String);
        }
        finally { ThemeManager.Current = original; }
    });

    [Fact]
    [Trait("Req", "PR-THEME-140")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:485-489 .cursor: pointingHand over a link, the text cursor over prose
    [Trait("Req", "UI-THEME-040")]
    public void The_pointer_over_a_link_shows_the_hand_and_over_prose_the_text_cursor() => headless.OnUiThread(() =>
    {
        var paragraph = new LinkParagraphView("see [my docs](https://tl-look.example/a) here", pointSize: 13);
        var block = BlockOf(paragraph);
        var window = Show(paragraph);
        try
        {
            var onLink = block.TranslatePoint(CharacterRect(block, "my docs").Center, window)!.Value;
            var onProse = block.TranslatePoint(CharacterRect(block, "see ", 1).Center, window)!.Value;

            window.MouseMove(onLink);
            Assert.Equal("Hand", block.Cursor?.ToString());
            window.MouseMove(onProse);
            Assert.Equal("Ibeam", block.Cursor?.ToString());
            window.MouseMove(onLink);
            Assert.Equal("Hand", block.Cursor?.ToString());
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-THEME-140")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:417-419, :485-489: the hand needs the point to be inside a link glyph's own box
    [Trait("Req", "UI-THEME-040")]
    public void A_link_is_hit_across_the_whole_width_of_each_of_its_characters_and_not_beyond() => headless.OnUiThread(() =>
    {
        var url = SwiftURL.Create("https://tl-look.example/a");
        var paragraph = new LinkParagraphView("see [my docs](https://tl-look.example/a) here", pointSize: 13);
        var view = (LinkTextView)paragraph.Child!;
        var block = BlockOf(paragraph);
        var window = Show(paragraph);
        try
        {
            var first = CharacterRect(block, "my docs", 0);
            var last = CharacterRect(block, "my docs", 6); // the final "s"
            var before = CharacterRect(block, "see ", 3); // the space before the link
            var after = CharacterRect(block, " here", 0); // the space after it

            Assert.Equal(url, view.Link(At(first, 0.1)));
            Assert.Equal(url, view.Link(At(last, 0.9))); // the right edge of the last letter is still the link
            Assert.Equal(url, view.Link(At(last, 0.1)));
            Assert.Null(view.Link(At(before, 0.9)));
            Assert.Null(view.Link(At(after, 0.1)));
            Assert.False(view.HasLink(At(after, 0.5)));
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "PR-THEME-140")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:312-332: selectable, no background, zero inset, zero line padding
    public void The_drawn_paragraph_is_selectable_with_no_inset_no_background_and_no_focus_ring() => headless.OnUiThread(() =>
    {
        var paragraph = new LinkParagraphView("see [my docs](https://tl-look.example/a) here", pointSize: 13);
        var view = (LinkTextView)paragraph.Child!;
        var block = BlockOf(paragraph);

        Assert.IsType<SelectableTextBlock>(block); // :313 selectable (and, a text block, never editable)
        Assert.Equal(new Thickness(0), block.Padding); // :319 textContainerInset zero
        Assert.Equal(new Thickness(0), view.Margin);
        Assert.Null(block.Background); // :314 draws no background
        Assert.Equal(0, block.MinHeight); // :310 minSize zero
    });

    // ---- UI-THEME-046: the head mention chip (:179-244) ----

    [Fact]
    [Trait("Req", "UI-THEME-046")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:187-219
    public void A_mention_with_a_target_is_a_glyph_a_space_and_a_bold_name_in_the_persona_colour() => headless.OnUiThread(() =>
    {
        var persona = new HydraPersona("Hank", "dragon-test-asset", 0xFF8000);
        var target = Target();
        var targets = new Dictionary<string, HydraMentionTarget> { ["Hank"] = target };

        var text = LinkParagraphView.Attributed("Ping Hank now", 13, dimmed: false, mentions: [persona], targets: targets);

        Assert.Equal(["Ping ", ParagraphText.AttachmentCharacter, " ", "Hank", " now"], text.Runs.Select(run => run.Text));
        var glyph = text.Runs[1];
        Assert.Equal(16, glyph.Attachment!.Width); // :187 (13 * 1.25).rounded() = 16.25 -> 16
        Assert.Equal(16, glyph.Attachment.Height);
        Assert.Equal(0, glyph.Attachment.X);
        Assert.Equal(Descender(13) + 1, glyph.Attachment.Y, 6); // :202
        Assert.Equal(target.Url, glyph.HydraHead); // :207-210 the custom attribute, never .link
        Assert.Null(glyph.Link);

        var space = text.Runs[2]; // :214 one space, base font, the paragraph's text colour, no head attribute
        Assert.Equal(SwiftFont.SystemFont(13), space.Font);
        AssertColour(SwiftColor.LabelColor, space.ForegroundColor);
        Assert.Null(space.HydraHead);

        var name = text.Runs[3]; // :216
        Assert.Equal(SwiftFont.BoldSystemFont(13), name.Font);
        AssertColour(new SwiftColor(1, 128 / 255.0, 0), name.ForegroundColor); // :161 the persona's own colour
        Assert.Equal(target.Url, name.HydraHead);
        Assert.Null(name.Link); // so the name never takes the link accent or the pointing hand
    });

    [Fact]
    [Trait("Req", "UI-THEME-046")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:187, :201-202: the glyph is 1.25 times the point size, rounded
    public void The_glyph_size_is_one_and_a_quarter_times_the_point_size_rounded() => headless.OnUiThread(() =>
    {
        var persona = new HydraPersona("Hank", "dragon-test-asset", 0x00FF00);

        var at20 = LinkParagraphView.Attributed("Hank", 20, dimmed: false, mentions: [persona]);
        var at15 = LinkParagraphView.Attributed("Hank", 15, dimmed: false, mentions: [persona]);

        Assert.Equal(25, at20.Runs[0].Attachment!.Width); // 20 * 1.25
        Assert.Equal(25, at20.Runs[0].Attachment!.Height);
        Assert.Equal(Descender(20) + 1, at20.Runs[0].Attachment!.Y, 6); // the base font's descender at this size
        Assert.Equal(19, at15.Runs[0].Attachment!.Width); // 18.75 rounds up
        Assert.Equal(19, at15.Runs[0].Attachment!.Height);
    });

    [Fact]
    [Trait("Req", "UI-THEME-046")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:211-219: no target, no head attribute (a plain coloured name)
    public void A_mention_with_no_target_carries_no_head_attribute_but_keeps_the_persona_colour() => headless.OnUiThread(() =>
    {
        var persona = new HydraPersona("Hank", "dragon-test-asset", 0x0000FF);

        var text = LinkParagraphView.Attributed("Ping Hank", 13, dimmed: false, mentions: [persona], targets: NoTargets);

        Assert.Null(text.Runs[1].HydraHead);
        Assert.Null(text.Runs[3].HydraHead);
        AssertColour(new SwiftColor(0, 0, 1), text.Runs[3].ForegroundColor);
        Assert.Equal(SwiftFont.BoldSystemFont(13), text.Runs[3].Font);
    });

    [Fact]
    [Trait("Req", "UI-THEME-046")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:187-219 on the drawn paragraph
    public void The_drawn_mention_is_a_glyph_a_space_and_a_bold_name_in_the_persona_colour_never_the_accent() => headless.OnUiThread(() =>
    {
        WithTheme(AppTheme.Dracula, () =>
        {
            var persona = new HydraPersona("Hank", "dragon-test-asset", 0xFF8000);
            var targets = new Dictionary<string, HydraMentionTarget> { ["Hank"] = Target() };
            var paragraph = new LinkParagraphView("Ping Hank now", pointSize: 13, mentions: [persona], targets: targets);
            var block = BlockOf(paragraph);
            var inlines = block.Inlines!.ToList();

            Assert.Equal("Ping ", Assert.IsType<Run>(inlines[0]).Text);
            var glyph = Assert.IsType<Image>(Assert.IsType<InlineUIContainer>(inlines[1]).Child);
            Assert.Equal(16, glyph.Width);
            Assert.Equal(16, glyph.Height);
            var space = Assert.IsType<Run>(inlines[2]);
            Assert.Equal(" ", space.Text);
            Assert.Equal(13, space.FontSize);
            var name = Assert.IsType<Run>(inlines[3]);
            Assert.Equal("Hank", name.Text);
            Assert.Equal(FontWeight.Bold, name.FontWeight);
            Assert.Equal(Color.FromRgb(0xFF, 0x80, 0x00), BrushColour(name.Foreground));
            Assert.NotEqual(UiChrome.AccentNSColor.Resolve(block.ActualThemeVariant), BrushColour(name.Foreground));
            return 0;
        });
    });

    [Fact]
    [Trait("Req", "UI-THEME-046")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:139's twin at :202: the glyph's bottom edge is descender + 1 above the baseline
    public void The_drawn_glyphs_bottom_edge_hangs_the_descender_plus_one_below_the_baseline() => headless.OnUiThread(() =>
    {
        var persona = new HydraPersona("Hank", "dragon-test-asset", 0xFF8000);
        var paragraph = new LinkParagraphView("Ping Hank now", pointSize: 13, mentions: [persona]);
        var block = BlockOf(paragraph);
        var window = Show(paragraph);
        try
        {
            var glyph = (Image)block.Inlines!.OfType<InlineUIContainer>().Single().Child!;
            var glyphBottom = glyph.TranslatePoint(new Point(0, glyph.Bounds.Height), block)!.Value.Y;
            var line = block.TextLayout.TextLines[0];

            Assert.Equal(line.Baseline - (Descender(13) + 1), glyphBottom, 0.6);
            Assert.Equal(16, glyph.Bounds.Height, 0.6);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "UI-THEME-046")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:207-216: the head attribute is on the glyph and the name, not the space between
    public void The_head_is_hit_on_the_glyph_and_the_name_but_not_on_the_space_between_them() => headless.OnUiThread(() =>
    {
        var persona = new HydraPersona("Hank", "dragon-test-asset", 0xFF8000);
        var target = Target();
        var targets = new Dictionary<string, HydraMentionTarget> { ["Hank"] = target };
        var paragraph = new LinkParagraphView("Ping Hank now", pointSize: 13, mentions: [persona], targets: targets);
        var view = (LinkTextView)paragraph.Child!;
        var block = BlockOf(paragraph);
        var window = Show(paragraph);
        try
        {
            // "Ping " is five characters; the glyph is the sixth, the space the seventh. Points a quarter and three
            // quarters across a box, so a hit that rounds to the nearest caret edge would land on the wrong character.
            var glyphBox = block.TextLayout.HitTestTextRange(5, 1).First();
            var spaceBox = block.TextLayout.HitTestTextRange(6, 1).First();
            var nameBox = block.TextLayout.HitTestTextRange(10, 1).First(); // the last letter of "Hank"
            var glyphPoint = At(glyphBox, 0.75);
            var spacePoint = At(spaceBox, 0.25);
            var namePoint = At(nameBox, 0.75);

            Assert.Equal(target.Url, view.HeadMention(glyphPoint)?.Url);
            Assert.Equal(target.Url, view.HeadMention(At(glyphBox, 0.25))?.Url);
            Assert.Null(view.HeadMention(spacePoint));
            Assert.Null(view.HeadMention(At(spaceBox, 0.75)));
            Assert.Equal(target.Url, view.HeadMention(namePoint)?.Url);
            Assert.Null(view.HeadMention(CharacterRect(block, "Ping").Center)); // prose is no head
            Assert.Equal("Hank", view.HeadMentionRect(glyphPoint)?.Name); // the glyph run names nothing itself, so the name beside it is used
            Assert.Equal("Hank", view.HeadMentionRect(namePoint)?.Name);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Req", "UI-THEME-046")] // DroppyCode/UI/Markdown/LinkParagraphView.swift:203-206: a head keeps the normal arrow, unlike a link
    public void The_pointer_over_a_head_mention_stays_the_arrow_while_a_link_beside_it_shows_the_hand() => headless.OnUiThread(() =>
    {
        var persona = new HydraPersona("Hank", "dragon-test-asset", 0xFF8000);
        var targets = new Dictionary<string, HydraMentionTarget> { ["Hank"] = Target() };
        var paragraph = new LinkParagraphView("Hank and [my docs](https://tl-look.example/a)", pointSize: 13, mentions: [persona], targets: targets);
        var block = BlockOf(paragraph);
        var window = Show(paragraph);
        try
        {
            var onName = block.TranslatePoint(CharacterRect(block, "Hank").Center, window)!.Value;
            var onLink = block.TranslatePoint(CharacterRect(block, "my docs").Center, window)!.Value;

            window.MouseMove(onName);
            Assert.Equal("Arrow", block.Cursor?.ToString());
            window.MouseMove(onLink);
            Assert.Equal("Hand", block.Cursor?.ToString());
        }
        finally { window.Close(); }
    });
}
