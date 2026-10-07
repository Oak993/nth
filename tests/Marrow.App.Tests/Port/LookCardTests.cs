using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Theme;

namespace Marrow.App.Tests.Port;

// OD-0088 (ED-250): the Look card's place, layout, words, colours, states and the setting it writes, to
// E:/Droppy/work/design/accent-build/SPEC.md sections 1 to 5. Expected numbers are the spec's (the segment widths its 2.3 table,
// ED-254); the label boxes are measured in the port's own type.
[Collection("Headless")]
public sealed class LookCardTests(HeadlessSession headless) : LookCardFixture(headless)
{
    private static Color C(string hex) => Color.Parse(hex);

    private static string[] Texts(CrossfadeText line) => line.Lines.Where(l => l.Opacity > 0).Select(l => l.Text).ToArray();

    [Fact]
    [Trait("Decision", "ED-250")]
    public void The_appearance_page_starts_with_the_look_card_and_the_first_card_keeps_its_other_rows() => Card((card, _, _, w) =>
    {
        var cards = Nodes<ChromeCard>(w);
        Assert.Same(card.Card, cards[0]);
        Assert.Equal(5, cards.Length); // the Look card, the rows card, the picture card, the zoom card, then OD-0127's Colours card (ED-636 (4))
        Assert.Single(Avalonia.VisualTree.VisualExtensions.GetVisualAncestors(cards[4]).OfType<Marrow.App.Port.ModelsPalette.PaletteColoursSection>());
        var content = Assert.IsType<StackPanel>(Assert.IsType<StackPanel>(Assert.IsType<Border>(card.Card.Child).Child).Children[0]);
        Assert.Collection(content.Children,
            c => Assert.Same(card.Mini, c),
            c => Assert.Same(card.ThemeRow, c),
            c => Assert.IsType<ChromeRowDivider>(c),
            c => Assert.Same(card.AccentRow, c),
            c => Assert.Same(card.Fold, c));
        Assert.Equal(["Transparency", "Sidebar", "Activity list", "Open the sidebar at launch"], Nodes<ChromeRow>(cards[1]).Select(r => Text(r).First()));
        Assert.Empty(Nodes<ThemePickerButton>(w)); // the source's Theme capsule is replaced (OD-0088)
        Assert.Equal(1, card.ZIndex); // its menu hangs over the cards below
    });

    [Fact]
    [Trait("Decision", "ED-250")]
    public void The_card_is_laid_out_as_the_spec_measures_it_shut_and_open() => Card((card, _, h, w) =>
    {
        // SPEC 2.1: 520 wide; the mini window flush with the card's top, 12 in each side, 496 x 128. LEAD RULING (rec-linebox, 2026-10-04, ED-290 (1)):
        // the two rows are ChromeRows and follow parity, 58 like the Mac's ChromeRow (11 + body 18 + 2 + subheadline 16 + 11; 1-settings-tour.mov
        // S 23.5 and S 194.0), not SPEC 2.1's 54 (drawn in Inter's own boxes), and the card's height follows its content: 128 + 58 + 1 + 58 = 245 shut
        // (SPEC 2.1's 237), 245 + the 36 fold = 281 open (273). The board's other geometry is kept: each control keeps the board's offset from
        // its own row top (segments 13, the Classic line 15) and the Signature line opens at the fold's top.
        const double row = 58;
        Assert.Equal(520, card.Bounds.Width);
        Assert.Equal(new Rect(12, 0, 496, 128), In(card.Mini, card));
        Assert.Equal(128, In(card.ThemeRow, card).Y);
        Assert.Equal(row, card.ThemeRow.Bounds.Height);
        Assert.Equal(row, card.AccentRow.Bounds.Height);
        Assert.Equal(128 + row + 1, In(card.AccentRow, card).Y);
        Assert.Equal(0, card.Fold.Bounds.Height);
        Assert.Equal(128 + row + 1 + row, card.Bounds.Height);
        // SPEC 2.3: the control right-aligned at the row's trailing padding, 28 tall, centred in the row; the segments are the
        // board's: 61, 50, 48 and 106 ("More themes"), 269 with the track's padding; the pill under Dark.
        var segments = In(card.Segments, card);
        Assert.Equal(520 - Chrome.RowControlTrailingPadding, segments.Right);
        Assert.Equal(28, segments.Height);
        Assert.Equal(128 + 13, segments.Y); // the board's 13 below the row top
        var widths = new double[] { 61, 50, 48, 106 };
        for (var i = 0; i < 4; i++)
        {
            var b = card.Segments.Buttons[i];
            Assert.Equal(widths[i], b.Bounds.Width);
            Assert.Equal(24, b.Bounds.Height);
            Assert.Equal(2, b.Bounds.Y);
        }
        Assert.Equal(2, card.Segments.Buttons[0].Bounds.X);
        Assert.Equal(269, segments.Width);
        Assert.Equal(card.Segments.Buttons.Sum(b => b.Bounds.Width) + 4, segments.Width);
        // SPEC 2.1, 4.3: 28 between each row's text column and its control, which leaves the Accent detail 156 (the board's
        // 155 with the port's 10 trailing padding); the Theme detail the room beside the control.
        Assert.Equal(156, card.AccentDetail.Bounds.Width);
        Assert.Equal(520 - 16 - 10 - 28 - 269, card.ThemeDetail.Bounds.Width);
        Assert.Equal(card.Segments.Buttons[2].Bounds.X, card.Segments.PillLeft.Value);
        Assert.Equal(card.Segments.Buttons[2].Bounds.Width, card.Segments.PillWidth.Value);
        // SPEC 2.5: the Classic line 310 x 24, 15 below the row top, its right edge at the row's trailing padding; swatches
        // 24 on a 26 pitch; More 50 x 24 in the last two slots.
        var line = In(card.Classic, card);
        Assert.Equal(new Rect(520 - 10 - 310, 128 + row + 1 + 15, 310, 24), line); // the board's 15 below the row top
        for (var i = 0; i < 10; i++) Assert.Equal(new Rect(26 * i, 0, 24, 24), card.Classic.Swatches[i].Bounds);
        Assert.Equal(new Rect(260, 0, 50, 24), card.More.Bounds);

        // Open (SPEC 2.1, 2.6): the fold's 36 more (273 in SPEC 2.1, 281 with the 58-point rows); the Signature line at the fold's top, below the Classic one, the same left and right edges.
        Click(w, card.More);
        Settle(h, w);
        Assert.Equal(36, card.Fold.Bounds.Height);
        Assert.Equal(128 + row + 1 + row + 36, card.Bounds.Height);
        var signature = In(card.Signature, card);
        Assert.Equal(line.X, signature.X);
        Assert.Equal(line.Right, signature.Right);
        Assert.Equal(128 + row + 1 + row, signature.Y); // at the fold's top, the Accent row's bottom (SPEC 2.6: 15 below the Classic line in the 54-point row, 19 in the 58-point one)
        for (var j = 0; j < 12; j++) Assert.Equal(new Rect(26 * j, 0, 24, 24), card.Signature.Swatches[j].Bounds);
        var label = In(card.Fold.Label, card);
        Assert.Equal(16, label.X);
        Assert.Equal(signature.Center.Y, label.Center.Y, 0.5);
        Assert.Equal(12, card.Fold.Label.FontSize);
    });

    // SPEC 2.3's table: each named theme's label width on the board; the segment is that + 33.
    public static TheoryData<AppTheme, double> BoardSegments() => new()
    {
        { AppTheme.CatppuccinMocha, 102 }, { AppTheme.CatppuccinLatte, 92 }, { AppTheme.Dracula, 42 }, { AppTheme.TokyoNight, 67 },
        { AppTheme.Nord, 29 }, { AppTheme.Gruvbox, 47 }, { AppTheme.GruvboxLight, 78 }, { AppTheme.OneDark, 52 },
        { AppTheme.Everforest, 55 }, { AppTheme.Kanagawa, 56 }, { AppTheme.RosePine, 53 }, { AppTheme.SolarizedDark, 79 },
        { AppTheme.SolarizedLight, 81 }, { AppTheme.GithubDark, 68 }, { AppTheme.GithubLight, 70 }, { AppTheme.Ayu, 21 },
        { AppTheme.NightOwl, 56 }, { AppTheme.Monokai, 49 }, { AppTheme.Claude, 38 }, { AppTheme.ClaudeLight, 69 },
        { AppTheme.Codex, 35 }, { AppTheme.Cursor, 36 }, { AppTheme.Matrix, 36 },
    };

    [Theory]
    [MemberData(nameof(BoardSegments))]
    [Trait("Decision", "ED-254")]
    public void Under_a_named_theme_the_control_is_the_boards_and_the_label_and_chevron_sit_inside_their_segment(AppTheme theme, double boardLabel) => Card((card, _, h, w) =>
    {
        var segments = card.Segments;
        Assert.Equal(boardLabel + 22 + 11, segments.Buttons[3].Bounds.Width);
        Assert.Equal(61 + 50 + 48 + boardLabel + 33 + 4, segments.Bounds.Width);
        Assert.Equal(520 - Chrome.RowControlTrailingPadding, In(segments, card).Right);
        if (theme == AppTheme.CatppuccinMocha) Assert.Equal(298, segments.Bounds.Width); // SPEC 2.3: the widest control
        // The label as the port draws it, 4 and the 7 wide chevron, centred: at least 6 clear of each end (OD-0069).
        foreach (var b in segments.Buttons)
        {
            var label = b.Label;
            var content = Measured(label, SwiftFont.System(12, SwiftFont.Weight.Medium)) + (b.Index == 3 ? 11 : 0);
            Assert.True((b.Bounds.Width - content) / 2 >= 6, $"{theme} '{label}': {content:F1} in {b.Bounds.Width}");
        }
        // The Theme row's words and the control never meet.
        var detail = In(card.ThemeDetail, card.ThemeRow);
        Assert.True(detail.X + Measured(card.ThemeDetail.Text, LookCard.DetailFont) <= In(segments, card.ThemeRow).X - LookCard.RowGap + 0.01, $"{theme}: {card.ThemeDetail.Text}");
    }, theme: theme);

    private static double Measured(string text, SwiftFont font)
    {
        var resolved = font.Resolve();
        using var layout = new Avalonia.Media.TextFormatting.TextLayout(text, new Typeface(resolved.FontFamily, resolved.FontStyle, resolved.FontWeight), resolved.FontSize, null);
        return layout.WidthIncludingTrailingWhitespace;
    }

    // OD-0069: a detail line is never cut. Every theme, every pick, and every hover (each swatch and More) shows words that fit
    // their column whole, measured as the text is laid out; the column is the board's (155, here 156).
    [Fact]
    [Trait("Decision", "ED-254")]
    public void Every_detail_line_fits_its_column_whole_for_every_theme_pick_and_hover() => Card((card, model, h, w) =>
    {
        var cut = new List<string>();
        void Rest() { for (var i = 0; i < 30; i++) { Frame(h); Drain(w); } } // half a second: every line has settled
        void Check(string when)
        {
            foreach (var (name, line) in new[] { ("theme", card.ThemeDetail), ("accent", card.AccentDetail) })
            {
                foreach (var (text, opacity) in line.Lines)
                {
                    if (opacity <= 0) continue;
                    var width = Measured(text, LookCard.DetailFont);
                    if (width > line.Bounds.Width + 0.01) cut.Add($"{when} {name}: '{text}' {width:F1} > {line.Bounds.Width:F1}");
                }
            }
        }
        Assert.Equal(156, card.AccentDetail.Bounds.Width);
        foreach (var theme in SwiftEnum<AppTheme>.AllCases)
        {
            model.Settings.Theme = theme;
            foreach (var accent in AccentPalette.All)
            {
                model.Settings.Accent = accent.Id;
                Rest();
                Check($"{theme} {accent.Id}");
            }
            model.Settings.Accent = "windows";
            foreach (var hover in AccentPalette.All.Select(a => a.Id).Append("more"))
            {
                card.SetHover(hover);
                Rest();
                Check($"{theme} hover {hover}");
                card.SetHover(null);
            }
        }
        Assert.True(cut.Count == 0, string.Join("\n", cut));
        // The four the old column cut, whole (SPEC 4.3's own words).
        foreach (var (theme, words) in new[] { (AppTheme.Claude, "Terracotta"), (AppTheme.ClaudeLight, "Terracotta"), (AppTheme.Kanagawa, "Wave blue"), (AppTheme.Codex, "Magenta") })
        {
            model.Settings.Theme = theme;
            Settle(h, w);
            Assert.Equal(words + " \u00B7 the theme's own", card.AccentDetail.Text);
            card.SetHover("windows");
            Settle(h, w);
            Assert.Equal(words + " \u00B7 the theme's own", card.AccentDetail.Text);
            card.SetHover(null);
        }
    });

    [Fact]
    [Trait("Decision", "ED-250")]
    public void The_detail_lines_name_the_accent_without_a_hex_for_every_case_of_the_spec()
    {
        Card((card, model, h, w) =>
        {
            void Expect(string theme, string accent)
            {
                Settle(h, w);
                Assert.Equal(theme, card.ThemeDetail.Text);
                Assert.Equal(accent, card.AccentDetail.Text);
                Assert.Equal([theme], Texts(card.ThemeDetail));
                Assert.Equal([accent], Texts(card.AccentDetail));
                Assert.DoesNotContain('#', theme + accent); // OD-0088: names only
            }
            Expect("Dark \u00B7 Windows accent", "Windows accent \u00B7 default");
            model.Settings.Accent = "blue";
            Expect("Dark \u00B7 Blue", "Blue");
            model.Settings.Theme = AppTheme.System;
            Expect("System \u00B7 Blue", "Blue");
            model.Settings.Accent = "windows";
            Expect("System \u00B7 Windows accent", "Windows accent \u00B7 default");
            model.Settings.Theme = AppTheme.Light;
            Expect("Light \u00B7 Windows accent", "Windows accent \u00B7 default");
            model.Settings.Theme = AppTheme.Dracula;
            Expect("Dark \u00B7 Purple", "Purple \u00B7 the theme's own");
            model.Settings.Theme = AppTheme.Kanagawa;
            Expect("Dark \u00B7 Wave blue", "Wave blue \u00B7 the theme's own");
            model.Settings.Accent = "orchid";
            Expect("Dark \u00B7 Orchid", "Orchid \u00B7 over Kanagawa");
            model.Settings.Theme = AppTheme.CatppuccinMocha; // "Orchid · over Catppuccin Mocha" does not fit beside the swatches
            Expect("Dark \u00B7 Orchid", "Orchid \u00B7 over the theme");
            Assert.True(LookDraw.Width("Orchid \u00B7 over Catppuccin Mocha", SwiftFont.System(SwiftFont.TextStyle.Subheadline)) > card.AccentDetail.Bounds.Width);
            model.Settings.Theme = AppTheme.CatppuccinLatte;
            Expect("Light \u00B7 Orchid", "Orchid \u00B7 over the theme");
            // Whatever is shown fits the text column.
            foreach (var theme in SwiftEnum<AppTheme>.AllCases)
            {
                model.Settings.Theme = theme;
                Settle(h, w);
                Assert.True(LookDraw.Width(card.AccentDetail.Text, SwiftFont.System(SwiftFont.TextStyle.Subheadline)) <= card.AccentDetail.Bounds.Width, $"{theme}: {card.AccentDetail.Text}");
            }
        });
    }

    [Fact]
    [Trait("Decision", "ED-250")]
    public void Hovering_a_swatch_or_more_names_it_in_the_detail_line_and_leaving_restores_the_pick() => Card((card, model, h, w) =>
    {
        Hover(w, card.Classic.Swatches[1]);
        Settle(h, w);
        Assert.Equal("Blue", card.AccentDetail.Text);
        Hover(w, card.More);
        Settle(h, w);
        Assert.Equal("12 Signature accents", card.AccentDetail.Text);
        w.MouseMove(new Point(1, 1));
        Settle(h, w);
        Assert.Equal("Windows accent \u00B7 default", card.AccentDetail.Text);
        Assert.Equal("windows", model.Settings.Accent); // a hover picks nothing
        // Under a named theme the first swatch is the theme's own.
        model.Settings.Theme = AppTheme.Dracula;
        model.Settings.Accent = "teal";
        Settle(h, w);
        Hover(w, card.Classic.Swatches[0]);
        Settle(h, w);
        Assert.Equal("Purple \u00B7 the theme's own", card.AccentDetail.Text);
        // More open: no hover line (the board's moreEnter acts only shut).
        w.MouseMove(new Point(1, 1));
        Click(w, card.More);
        Hover(w, card.More);
        Settle(h, w);
        Assert.Equal("Teal \u00B7 over Dracula", card.AccentDetail.Text);
    });

    [Fact]
    [Trait("Decision", "ED-250")]
    public void The_swatches_and_the_mini_window_wear_the_palette_and_the_effective_accent_of_the_appearance() => Card((card, model, h, w) =>
    {
        // Dark, the default: the first swatch is the Windows accent lifted (#0067C0 -> #4B9FFC) with its white dot.
        for (var i = 1; i < 22; i++)
        {
            var swatch = i < 10 ? card.Classic.Swatches[i] : card.Signature.Swatches[i - 10];
            Assert.Equal(C(AccentPalette.All[i].Dark), swatch.Fill.Value);
        }
        Assert.Equal(C("#4B9FFC"), card.Classic.Swatches[0].Fill.Value);
        Assert.Equal(1, card.Classic.Swatches[0].WindowsDot.Value);
        Assert.Equal(C("#4B9FFC"), card.Accent.Value);
        Assert.Equal(C("#111111"), card.OnAccent.Value);
        Assert.Equal(C("#1E1E1E"), card.Surface.Value);
        Assert.Equal(1, card.Turn.Value);
        // Light: every swatch takes its Light hex; the mini window turns over.
        model.Settings.Theme = AppTheme.Light;
        Settle(h, w);
        for (var i = 1; i < 22; i++)
        {
            var swatch = i < 10 ? card.Classic.Swatches[i] : card.Signature.Swatches[i - 10];
            Assert.Equal(C(AccentPalette.All[i].Light), swatch.Fill.Value);
        }
        Assert.Equal(C("#0067C0"), card.Classic.Swatches[0].Fill.Value);
        Assert.Equal(C("#FFFFFF"), card.OnAccent.Value);
        Assert.Equal(Colors.White, card.Surface.Value);
        Assert.Equal(0, card.Turn.Value);
        // A named theme: the first swatch is its own accent lifted for the card, no Windows dot; the app accent is raw.
        model.Settings.Theme = AppTheme.Claude;
        Settle(h, w);
        Assert.Equal(C("#E6805C"), card.Classic.Swatches[0].Fill.Value);
        Assert.Equal(0, card.Classic.Swatches[0].WindowsDot.Value);
        Assert.Equal(C("#C15F3C"), card.Accent.Value);
        Assert.Equal(C("#1A1816"), card.Surface.Value);
        Assert.Equal("Claude terracotta", AutomationProperties.GetName(card.Classic.Swatches[0]));
        // A pick over it: the palette hex, as given.
        Click(w, card.Classic.Swatches[8]);
        Settle(h, w);
        Assert.Equal(C("#40C8E0"), card.Accent.Value);
        Assert.Equal(C("#E6805C"), card.Classic.Swatches[0].Fill.Value);
    });

    [Fact]
    [Trait("Decision", "ED-250")]
    public void A_click_on_a_swatch_writes_the_setting_at_once_and_a_signature_pick_is_held_round_more_when_the_line_shuts() => Card((card, model, h, w) =>
    {
        Click(w, card.Classic.Swatches[4]);
        Assert.Equal("red", model.Settings.Accent);
        Assert.Equal("red", h.Defaults.Object("accent"));
        Settle(h, w);
        Assert.Equal(4 * 26, card.Classic.Ring.X.Value);
        Assert.Equal(1, card.Classic.Ring.Fade.Value);
        Click(w, card.More);
        Settle(h, w);
        Assert.True(card.Open);
        Click(w, card.Signature.Swatches[4]); // Orchid
        Settle(h, w);
        Assert.Equal("orchid", model.Settings.Accent);
        Assert.Equal(0, card.Classic.Ring.Fade.Value);
        Assert.Equal(1, card.Signature.Ring.Fade.Value);
        Assert.Equal(4 * 26, card.Signature.Ring.X.Value);
        Click(w, card.More); // Less: shut over the Signature pick (held)
        Settle(h, w);
        Assert.False(card.Open);
        Assert.Equal("orchid", model.Settings.Accent); // still picked
        Assert.Equal(0, card.Signature.Ring.Fade.Value);
        Assert.Equal(1, card.More.RingOpacity.Value);
        Assert.Equal(1, card.More.RingScale.Value);
        Assert.Equal(6, card.More.DotWidth.Value);
        Assert.Equal(1, card.More.DotOpacity.Value);
        Assert.Equal(C("#E298C6"), card.More.DotColor.Value);
        Assert.Equal(0, card.More.ChevronWidth.Value);
        Assert.Equal(0, card.More.ChevronOpacity.Value);
        Assert.Equal(1, card.More.Lit.Value); // its label at --tx while held
        Assert.Equal("Orchid", card.AccentDetail.Text);
        // A theme change keeps the pick.
        Click(w, card.Segments.Buttons[1]);
        Settle(h, w);
        Assert.Equal(AppTheme.Light, model.Settings.Theme);
        Assert.Equal("orchid", model.Settings.Accent);
        Assert.Equal(C("#733E67"), card.More.DotColor.Value);
    });

    [Fact]
    [Trait("Decision", "ED-250")]
    public void The_segments_pick_the_base_themes_and_the_fourth_reads_the_named_theme_and_opens_the_menu() => Card((card, model, h, w) =>
    {
        Click(w, card.Segments.Buttons[0]);
        Assert.Equal(AppTheme.System, model.Settings.Theme);
        Click(w, card.Segments.Buttons[1]);
        Assert.Equal(AppTheme.Light, model.Settings.Theme);
        Click(w, card.Segments.Buttons[2]);
        Assert.Equal(AppTheme.Dark, model.Settings.Theme);
        Assert.Equal("dark", h.Defaults.Object("appTheme"));
        Assert.False(card.Menu.IsOpen);
        Click(w, card.Segments.Buttons[3]);
        Assert.True(card.Menu.IsOpen);
        Settle(h, w);
        // SPEC 2.4: 264 wide, its right edge at the control's, 15 below it; the 23 named themes under Dark and Light.
        var menu = In(card.Menu, card);
        var segments = In(card.Segments, card);
        Assert.Equal(264, menu.Width);
        Assert.Equal(segments.Right, menu.Right);
        Assert.Equal(segments.Bottom + 15, menu.Y);
        Assert.Equal(["Dark", "Light"], card.Menu.Headers.Select(t => t.Text));
        Assert.Equal(["Catppuccin Mocha", "Dracula", "Tokyo Night", "Nord", "Gruvbox", "One Dark", "Everforest", "Kanagawa", "Ros\u00E9 Pine", "Solarized Dark", "GitHub Dark", "Ayu", "Night Owl", "Monokai", "Claude", "Codex", "Cursor", "Matrix", "Catppuccin Latte", "Gruvbox Light", "Solarized Light", "GitHub Light", "Claude Light"], card.Menu.Rows.Select(r => r.RowTheme.DisplayName()));
        Assert.Equal(260, card.Menu.List.Bounds.Height); // the list stops at 260 and scrolls (its content 690)
        Assert.Equal(690, card.Menu.List.Extent.Height);
        Assert.Equal(272, card.Menu.Bounds.Height);
        foreach (var row in card.Menu.Rows) Assert.Equal(28, row.Bounds.Height);
        // A pick: the theme, the menu closes, the fourth segment reads its name and carries the pill.
        Click(w, card.Menu.Rows.Single(r => r.RowTheme == AppTheme.Nord));
        Assert.Equal(AppTheme.Nord, model.Settings.Theme);
        Assert.False(card.Menu.IsOpen);
        Settle(h, w);
        Assert.False(card.Menu.IsVisible);
        Assert.Equal("Nord", card.Segments.Buttons[3].Label);
        Assert.Equal(62, card.Segments.Buttons[3].Bounds.Width); // the board's: 29 + 22 + 11
        Assert.Equal(card.Segments.Buttons[3].Bounds.X, card.Segments.PillLeft.Value, 6);
        Assert.Equal("Dark \u00B7 Frost", card.ThemeDetail.Text);
        // A click outside closes it.
        Click(w, card.Segments.Buttons[3]);
        Assert.True(card.Menu.IsOpen);
        Click(w, card.Mini);
        Assert.False(card.Menu.IsOpen);
    });

    [Fact]
    [Trait("Decision", "ED-250")]
    public void The_menu_shows_the_current_theme_checked_and_scrolled_64_below_the_list_top_with_its_fades() => Card((card, model, h, w) =>
    {
        model.Settings.Theme = AppTheme.Monokai;
        Settle(h, w);
        Click(w, card.Segments.Buttons[3]);
        Settle(h, w);
        var monokai = card.Menu.Rows.Single(r => r.RowTheme == AppTheme.Monokai);
        var top = monokai.TranslatePoint(default, card.Menu.List)!.Value.Y;
        Assert.Equal(64, top, 0.5);
        foreach (var row in card.Menu.Rows) Assert.Equal(row.RowTheme == AppTheme.Monokai ? 1 : 0, row.Check.Value);
        Assert.Equal(16, card.Menu.TopFade.Value); // scrolled: the top fades
        Assert.Equal(22, card.Menu.BottomFade.Value); // not at the end: the bottom fades
        card.Menu.List.Offset = new Vector(0, 10000);
        Settle(h, w);
        Assert.Equal(0, card.Menu.BottomFade.Value);
        card.Menu.List.Offset = new Vector(0, 0);
        Settle(h, w);
        Assert.Equal(0, card.Menu.TopFade.Value);
        // A theme not in the list (Dark): the list at its top.
        Press(w, Key.Escape);
        model.Settings.Theme = AppTheme.Dark;
        Settle(h, w);
        Click(w, card.Segments.Buttons[3]);
        Settle(h, w);
        Assert.Equal(0, card.Menu.List.Offset.Y);
    });

    [Fact]
    [Trait("Decision", "ED-250")]
    public void The_controls_carry_their_accessible_names_roles_and_states() => Card((card, model, h, w) =>
    {
        Assert.Equal("Accent", AutomationProperties.GetName(card.Classic));
        Assert.Equal("Theme", AutomationProperties.GetName(card.Segments));
        Assert.Equal("Theme", AutomationProperties.GetName(card.Menu));
        Assert.Equal("Signature accents", AutomationProperties.GetName(card.More));
        Assert.Equal("Windows accent", AutomationProperties.GetName(card.Classic.Swatches[0]));
        Assert.Equal("Slate", AutomationProperties.GetName(card.Signature.Swatches[11]));
        var more = (IExpandCollapseProvider)ControlAutomationPeer.CreatePeerForElement(card.More)!;
        Assert.Equal(ExpandCollapseState.Collapsed, more.ExpandCollapseState);
        more.Expand();
        Assert.True(card.Open);
        Assert.Equal(ExpandCollapseState.Expanded, more.ExpandCollapseState);
        var teal = (ISelectionItemProvider)ControlAutomationPeer.CreatePeerForElement(card.Classic.Swatches[8])!;
        Assert.False(teal.IsSelected);
        teal.Select();
        Assert.Equal("teal", model.Settings.Accent);
        Assert.True(teal.IsSelected);
        model.Settings.Theme = AppTheme.Dracula;
        Settle(h, w);
        Assert.Equal("Dracula purple", AutomationProperties.GetName(card.Classic.Swatches[0]));
    });
}
