using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Xunit;
using static Marrow.App.Tests.Fixtures.HydraMergeCardProbe;

namespace Marrow.App.Tests.Views.Chat;

// Source: DroppyCode/App/Views/Chat/HydraMergePopover.swift (B3 52bc792), :78-286 HydraMergePopover.
// The card's layout, text, fonts, colours and interactions. Parsing is in HydraMergeNoteParseTests.
[Collection("Headless")]
public sealed class HydraMergePopoverCardTests(HeadlessSession headless)
{
    private const string FaviconHost = "gitlab.com";

    private static string Body(params string[] lines) => string.Join("\n", lines);

    private static HydraMergeNote Plain(string url = GitlabUrl) => Parse(FilesLine(), url);

    private HydraMergePopover Build(HydraMergeNote note)
    {
        HydraMergePopover? result = null;
        headless.OnUiThread(() => result = new HydraMergePopover(note));
        return result!;
    }

    private static StackPanel RootStack(HydraMergePopover popover) =>
        Assert.IsType<StackPanel>(Assert.IsType<Border>(popover.Child).Child);

    private static HydraGlyph[] Faces(ILogical root) => Descendants<HydraGlyph>(root);

    private static Popup[] Popups(ILogical root) => Descendants<Popup>(root);

    private static Popup PopupOf(HydraGlyph glyph) =>
        Assert.IsType<Popup>(Assert.IsType<StackPanel>(glyph.Parent).Children.Last());

    private static void Hover(Control target, RoutedEvent<PointerEventArgs> routedEvent) =>
        target.RaiseEvent(new PointerEventArgs(routedEvent, target, new Pointer(1, PointerType.Mouse, true), target, default, 0, new PointerPointProperties(), KeyModifiers.None));

    private static void Enter(Control target) => Hover(target, InputElement.PointerEnteredEvent);

    private static void Leave(Control target) => Hover(target, InputElement.PointerExitedEvent);

    // ------------------------------------------------------------------ UI-HYDRAMERGE-008 the card

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-008")] // App/Views/Chat/HydraMergePopover.swift:98-100, :217-218
    public void The_card_is_a_380_wide_stack_with_18_padding_and_14_spacing_that_sizes_to_its_content()
    {
        var popover = Build(Plain());
        headless.OnUiThread(() =>
        {
            var border = Assert.IsType<Border>(popover.Child);
            var stack = RootStack(popover);

            Assert.Equal(380, border.Width); // :218 .frame(width: 380)
            Assert.Equal(new Thickness(18), border.Padding); // :217 .padding(18)
            Assert.Equal(14, stack.Spacing); // :100 VStack(alignment: .leading, spacing: 14)

            popover.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Assert.Equal(380, popover.DesiredSize.Width); // fixed width, however wide the content
            Assert.True(popover.DesiredSize.Height > 36); // and a height that is the content's plus the 36 of padding
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-008")] // :99-216 the rows, top to bottom: header, stats, heads, branch, remarks, actions
    public void The_card_rows_run_header_stats_heads_branch_remarks_and_actions_in_that_order()
    {
        var note = Parse(Body(
            FilesLine(), "Left `main` as it was.", "Another remark.", "Heads", HeadLine("Hank", 1, "Fix", "a.cs")));
        var popover = Build(note);
        headless.OnUiThread(() =>
        {
            var rows = RootStack(popover).Children;
            Assert.Equal(7, rows.Count);

            Assert.NotNull(Text(rows[0], "Merged")); // header
            Assert.NotNull(Text(rows[1], "added")); // stats
            Assert.NotNull(Text(rows[2], "1 head landed")); // heads
            Assert.NotNull(Text(rows[3], "hydra/fix-a1b2c3")); // branch row
            Assert.NotNull(Text(rows[4], "Left main as it was.")); // remarks, in order
            Assert.NotNull(Text(rows[5], "Another remark."));
            Assert.Contains(Descendants<Button>(rows[6]), button => Text(button, "Copy link") is not null); // actions
            Assert.Contains(Descendants<Button>(rows[6]), button => Text(button, "Open merge request") is not null);
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-008")] // :135 the heads row exists only for a note with heads; :171 a remark row per remark
    public void A_note_without_heads_or_remarks_has_only_header_stats_branch_and_actions()
    {
        var popover = Build(Plain());
        headless.OnUiThread(() =>
        {
            var rows = RootStack(popover).Children;

            Assert.Equal(4, rows.Count);
            Assert.NotNull(Text(rows[0], "Merged"));
            Assert.NotNull(Text(rows[1], "added"));
            Assert.NotNull(Text(rows[2], "hydra/fix-a1b2c3"));
            Assert.Contains(Descendants<Button>(rows[3]), button => Text(button, "Copy link") is not null);
        });
    }

    // ------------------------------------------------------------------ UI-HYDRAMERGE-009 / PR-096 the mark

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-009")] // :102-114 the fallback glyph, a 22 by 22 frame clipped to a 6-point rounded rectangle
    [Trait("Req", "PR-HYDRAMERGE-096")]
    [Trait("Decision", "ED-059")]
    public void Without_a_favicon_the_mark_is_the_merge_glyph_in_a_22_square_clipped_to_radius_6()
    {
        var popover = Build(Plain());
        headless.OnUiThread(() =>
        {
            var header = Assert.IsType<Grid>(RootStack(popover).Children[0]);
            var mark = Assert.IsType<Border>(header.Children[0]);
            var glyph = Assert.IsType<SwiftImage>(mark.Child);

            Assert.Equal(10, header.ColumnSpacing); // :101 HStack(spacing: 10)
            Assert.Equal(0, Grid.GetColumn(mark)); // leading
            Assert.Equal(22, mark.Width); // :113 .frame(width: 22, height: 22)
            Assert.Equal(22, mark.Height);
            Assert.Equal(new CornerRadius(6), mark.CornerRadius); // :114
            Assert.True(mark.ClipToBounds);
            Assert.Equal("arrow.triangle.merge", glyph.SymbolName); // :108
            Assert.Equal(SwiftFont.System(18), SwiftImage.GetFont(glyph)); // :109
            AssertForeground(glyph, Chrome.Accent); // :110
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-009")] // :220-222 a cached favicon is used as soon as the card appears; :103-106 aspect fit
    [Trait("Req", "PR-HYDRAMERGE-096")]
    public void A_cached_favicon_replaces_the_glyph_when_the_card_appears_and_is_drawn_aspect_fit()
    {
        headless.OnUiThread(() =>
        {
            var icon = new Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(4, 4), new Vector(96, 96),
                Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Unpremul);
            using var seed = SeedFavicon(FaviconHost, icon);
            var popover = new HydraMergePopover(Plain());
            var header = Assert.IsType<Grid>(RootStack(popover).Children[0]);
            var mark = Assert.IsType<Border>(header.Children[0]);
            Assert.IsType<SwiftImage>(mark.Child); // not on screen yet: still the glyph

            // Not Show(): that pumps the dispatcher. The cache is read when the card appears (:221-222), so the
            // favicon is already there the moment the window shows it, before any queued job runs.
            var window = new Avalonia.Controls.Window { Width = 420, Height = 400, Content = popover };
            window.Show();
            try
            {
                var image = Assert.IsType<Image>(mark.Child);
                Assert.Same(icon, image.Source);
                Assert.Equal(Stretch.Uniform, image.Stretch); // .aspectRatio(contentMode: .fit)
                Assert.Equal(22, mark.Width);
                Assert.Equal(22, mark.Height);
                Assert.Empty(Descendants<SwiftImage>(mark));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-009")] // :224 an uncached favicon is fetched; the glyph stays until it arrives
    [Trait("Req", "PR-HYDRAMERGE-096")]
    public void An_uncached_favicon_is_awaited_and_the_glyph_stays_until_it_arrives()
    {
        headless.OnUiThread(() =>
        {
            var icon = new Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(4, 4), new Vector(96, 96),
                Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Unpremul);
            var pending = new TaskCompletionSource<Avalonia.Media.IImage?>();
            using var seed = SeedInFlightFavicon(FaviconHost, pending.Task); // FaviconCache.image awaits it: no network
            var popover = new HydraMergePopover(Plain());
            var mark = Assert.IsType<Border>(Assert.IsType<Grid>(RootStack(popover).Children[0]).Children[0]);

            var window = Show(popover);
            try
            {
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("arrow.triangle.merge", Assert.IsType<SwiftImage>(mark.Child).SymbolName); // fetch still running

                pending.SetResult(icon);
                Dispatcher.UIThread.RunJobs();
                Assert.Same(icon, Assert.IsType<Image>(mark.Child).Source);
            }
            finally
            {
                window.Close();
                _ = seed; // the cache entry the fetch left behind is removed with the seed
            }
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-009")] // :112 a host that serves no favicon keeps the glyph
    [Trait("Req", "PR-HYDRAMERGE-096")]
    public void A_host_without_a_favicon_keeps_the_glyph()
    {
        headless.OnUiThread(() =>
        {
            // "localhost" is never fetched (FaviconCache.IsPublic), so this reaches no network either.
            var popover = new HydraMergePopover(Plain("http://localhost/acme/app/-/merge_requests/7"));
            var mark = Assert.IsType<Border>(Assert.IsType<Grid>(RootStack(popover).Children[0]).Children[0]);
            var window = Show(popover);
            try
            {
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("arrow.triangle.merge", Assert.IsType<SwiftImage>(mark.Child).SymbolName);
                Assert.Empty(Descendants<Image>(mark));
            }
            finally
            {
                window.Close();
            }
        });
    }

    // ------------------------------------------------------------------ UI-HYDRAMERGE-010 / 011 / 012 the header

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-010")] // :115-118
    [Trait("Decision", "ED-059")]
    public void The_header_title_reads_merged_in_title3_semibold_primary_text()
    {
        var popover = Build(Plain());
        headless.OnUiThread(() =>
        {
            var title = RequiredText(popover, "Merged");
            var column = Assert.IsType<StackPanel>(title.Parent);

            AssertFont(title, SwiftFont.System(SwiftFont.TextStyle.Title3, weight: SwiftFont.Weight.Semibold));
            Assert.Equal(15, title.FontSize);
            AssertForeground(title, Chrome.PrimaryText);
            Assert.Equal(2, column.Spacing); // :115 VStack(alignment: .leading, spacing: 2)
            Assert.Same(title, column.Children[0]);
            Assert.Equal(1, Grid.GetColumn(column)); // between the mark and the seal
        });
    }

    [Theory]
    [Trait("Req", "UI-HYDRAMERGE-010")] // :116 "Merged" whatever the forge
    [Trait("Req", "UI-HYDRAMERGE-011")] // :119 "<label> on <host>"; the label is "!n" on GitLab, "#n" on GitHub and Gitea
    [Trait("Decision", "ED-059")]
    [Trait("Decision", "ED-062")]
    [InlineData("https://gitlab.com/acme/app/-/merge_requests/7", "!7 on gitlab.com")]
    [InlineData("https://github.com/acme/app/pull/12", "#12 on github.com")]
    [InlineData("https://gitea.example/acme/app/pulls/3", "#3 on gitea.example")]
    public void The_header_subtitle_reads_the_label_on_the_host_in_subheadline_secondary_text(string url, string subtitle)
    {
        var popover = Build(Plain(url));
        headless.OnUiThread(() =>
        {
            var title = RequiredText(popover, "Merged");
            var line = RequiredText(popover, subtitle);
            var column = Assert.IsType<StackPanel>(line.Parent);

            AssertFont(line, SwiftFont.System(SwiftFont.TextStyle.Subheadline));
            Assert.Equal(11, line.FontSize);
            AssertForeground(line, Chrome.SecondaryText);
            Assert.Same(column, title.Parent);
            Assert.Same(line, column.Children[1]); // the second line of the header's text column
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-011")] // :99 `note.link.url.host ?? ""`: a URL with no host reads "!7 on "
    public void A_request_url_without_a_host_gives_a_subtitle_ending_in_on_and_an_empty_host()
    {
        var url = Url("file:///acme/app/-/merge_requests/7");
        Assert.Null(url.HostComponent()); // the fixture really has no host
        var popover = Build(Parse(FilesLine(), "file:///acme/app/-/merge_requests/7"));
        headless.OnUiThread(() => Assert.NotNull(Text(popover, "!7 on ")));
    }

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-012")] // :123-126
    [Trait("Decision", "ED-059")]
    public void The_success_seal_closes_the_header_row_at_20_points_in_the_success_colour()
    {
        var popover = Build(Plain());
        headless.OnUiThread(() =>
        {
            var header = Assert.IsType<Grid>(RootStack(popover).Children[0]);
            var seal = Symbol(popover, "checkmark.seal.fill");

            Assert.Equal(SwiftFont.System(20), SwiftImage.GetFont(seal)); // :125 .font(.system(size: 20))
            AssertForeground(seal, Chrome.Success); // :126
            Assert.Same(header, seal.Parent);
            Assert.Equal(2, Grid.GetColumn(seal)); // after the spacer: the trailing end
            Assert.Equal(3, header.ColumnDefinitions.Count);
            Assert.Equal(GridUnitType.Star, header.ColumnDefinitions[1].Width.GridUnitType); // :123 Spacer()
            Assert.Equal(GridUnitType.Auto, header.ColumnDefinitions[2].Width.GridUnitType);
        });
    }

    // ------------------------------------------------------------------ UI-HYDRAMERGE-013 / 014 / 015 the stat tiles

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-013")] // :128-132 the three tiles in a row of spacing 8, files, added, removed
    public void The_three_stat_tiles_sit_in_one_row_of_equal_columns_with_eight_points_between()
    {
        var popover = Build(Parse(FilesLine(files: 2, additions: 3, deletions: 1)));
        headless.OnUiThread(() =>
        {
            var row = Assert.IsType<Grid>(RootStack(popover).Children[1]);

            Assert.Equal(8, row.ColumnSpacing); // :128 HStack(spacing: 8)
            Assert.Equal(3, row.Children.Count);
            Assert.Equal(3, row.ColumnDefinitions.Count);
            Assert.All(row.ColumnDefinitions, column => Assert.Equal(new GridLength(1, GridUnitType.Star), column.Width)); // .frame(maxWidth: .infinity)
            Assert.Same(TileOf(popover, "2"), row.Children[0]);
            Assert.Same(TileOf(popover, "+3"), row.Children[1]);
            Assert.Same(TileOf(popover, "\U00002212" + "1"), row.Children[2]);
            Assert.Equal([0, 1, 2], row.Children.Select(child => Grid.GetColumn(child)));
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-013")] // :129, :258-271
    [Trait("Decision", "ED-059")]
    [Trait("Decision", "ED-062")]
    public void The_files_tile_shows_the_count_untinted_over_the_files_label()
    {
        var popover = Build(Parse(FilesLine(files: 2, additions: 3, deletions: 1)));
        headless.OnUiThread(() =>
        {
            var value = RequiredText(popover, "2");
            var label = RequiredText(popover, "files");
            var tile = TileOf(popover, "2");
            var stack = Assert.IsType<StackPanel>(tile.Child);

            AssertFont(value, SwiftFont.System(SwiftFont.TextStyle.Title2, weight: SwiftFont.Weight.Semibold).MonospacedDigit()); // :261
            Assert.Equal(17, value.FontSize);
            AssertForeground(value, Chrome.PrimaryText); // :262 no tint
            AssertFont(label, SwiftFont.System(SwiftFont.TextStyle.Footnote, weight: SwiftFont.Weight.Medium)); // :264
            Assert.Equal(10, label.FontSize);
            AssertForeground(label, Chrome.SecondaryText); // :265
            Assert.Equal([value, label], stack.Children.Cast<TextBlock>()); // the count on top, the label beneath
            Assert.Equal(2, stack.Spacing); // :259 VStack(alignment: .leading, spacing: 2)
            Assert.Equal(new Thickness(12, 9), tile.Padding); // :267-268
            Assert.Equal(new CornerRadius(10), tile.CornerRadius); // :270
            Assert.Equal(Resolved(Chrome.Overlay(0.08)), SolidColor(tile.Background)); // :270
            Assert.Equal(HorizontalAlignment.Stretch, tile.HorizontalAlignment); // :269 maxWidth: .infinity
        });
    }

    [Theory]
    [Trait("Req", "UI-HYDRAMERGE-013")] // :129 "file" for exactly one, "files" for every other count including zero
    [InlineData(1, "file")]
    [InlineData(0, "files")]
    [InlineData(2, "files")]
    [InlineData(11, "files")]
    public void The_files_label_is_singular_only_for_exactly_one_file(long files, string label)
    {
        var popover = Build(Parse(FilesLine(files: files, additions: 5, deletions: 5)));
        headless.OnUiThread(() =>
        {
            var tile = Assert.IsType<StackPanel>(TileOf(popover, files.ToString(System.Globalization.CultureInfo.InvariantCulture)).Child);

            Assert.Equal(label, Assert.IsType<TextBlock>(tile.Children[1]).Text);
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-014")] // :130, :258-271
    [Trait("Decision", "ED-059")]
    [Trait("Decision", "ED-062")]
    public void The_additions_tile_shows_an_ascii_plus_count_tinted_success_over_added()
    {
        var popover = Build(Parse(FilesLine(files: 2, additions: 1, deletions: 8)));
        headless.OnUiThread(() =>
        {
            var value = RequiredText(popover, "+1"); // an ASCII plus; the label stays "added" at a count of one
            var label = RequiredText(popover, "added");
            var tile = TileOf(popover, "+1");

            AssertFont(value, SwiftFont.System(SwiftFont.TextStyle.Title2, weight: SwiftFont.Weight.Semibold).MonospacedDigit());
            AssertForeground(value, Chrome.Success); // :130 tint: Chrome.success
            AssertFont(label, SwiftFont.System(SwiftFont.TextStyle.Footnote, weight: SwiftFont.Weight.Medium));
            AssertForeground(label, Chrome.SecondaryText); // the label is never tinted
            Assert.Same(label, Assert.IsType<StackPanel>(tile.Child).Children[1]);
            Assert.Equal(new Thickness(12, 9), tile.Padding);
            Assert.Equal(new CornerRadius(10), tile.CornerRadius);
            Assert.Equal(Resolved(Chrome.Overlay(0.08)), SolidColor(tile.Background));
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-015")] // :131, :258-271
    [Trait("Decision", "ED-059")]
    [Trait("Decision", "ED-062")]
    public void The_deletions_tile_shows_a_true_minus_sign_count_tinted_danger_over_removed()
    {
        var popover = Build(Parse(FilesLine(files: 2, additions: 3, deletions: 1)));
        headless.OnUiThread(() =>
        {
            var value = RequiredText(popover, "\U00002212" + "1"); // U+2212 MINUS SIGN, not an ASCII hyphen
            var label = RequiredText(popover, "removed");
            var tile = TileOf(popover, "\U00002212" + "1");

            Assert.Null(Text(popover, "-1"));
            AssertFont(value, SwiftFont.System(SwiftFont.TextStyle.Title2, weight: SwiftFont.Weight.Semibold).MonospacedDigit());
            AssertForeground(value, Chrome.Danger); // :131 tint: Chrome.danger
            AssertFont(label, SwiftFont.System(SwiftFont.TextStyle.Footnote, weight: SwiftFont.Weight.Medium));
            AssertForeground(label, Chrome.SecondaryText);
            Assert.Same(label, Assert.IsType<StackPanel>(tile.Child).Children[1]);
            Assert.Equal(new Thickness(12, 9), tile.Padding);
            Assert.Equal(new CornerRadius(10), tile.CornerRadius);
            Assert.Equal(Resolved(Chrome.Overlay(0.08)), SolidColor(tile.Background));
        });
    }

    // ------------------------------------------------------------------ UI-HYDRAMERGE-016 / 017 / PR-HYDRAMERGE-097 the heads row

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-016")] // :135 no heads parsed: the whole row is omitted
    [Trait("Req", "PR-HYDRAMERGE-097")]
    public void A_note_with_no_heads_draws_no_faces_and_no_count()
    {
        var popover = Build(Plain());
        headless.OnUiThread(() =>
        {
            Assert.Empty(Faces(popover));
            Assert.Empty(Popups(popover));
            Assert.DoesNotContain(Descendants<TextBlock>(popover), block => block.Text?.EndsWith("landed", StringComparison.Ordinal) == true);
        });
    }

    [Theory]
    [Trait("Req", "UI-HYDRAMERGE-016")] // :140 prefix(8): the first eight heads get a face
    [Trait("Req", "UI-HYDRAMERGE-017")] // :157 the count names every head that landed
    [Trait("Req", "PR-HYDRAMERGE-097")]
    [InlineData(1, 1, "1 head landed")]
    [InlineData(2, 2, "2 heads landed")]
    [InlineData(8, 8, "8 heads landed")]
    [InlineData(9, 8, "9 heads landed")]
    [InlineData(12, 8, "12 heads landed")]
    public void At_most_eight_faces_are_drawn_and_the_count_names_every_head(int heads, int faces, string count)
    {
        var popover = Build(NoteWithHeads(heads));
        headless.OnUiThread(() =>
        {
            var glyphs = Faces(popover);

            Assert.Equal(faces, glyphs.Length);
            Assert.Equal(faces, Popups(popover).Length); // each face carries its own popover (:149)
            Assert.NotNull(Text(popover, count));
            Assert.Equal(Enumerable.Range(0, faces).Select(i => HydraRoster.Persona(i)), glyphs.Select(glyph => glyph.Persona)); // the first heads, in order
            Assert.All(glyphs, glyph => Assert.Equal(26, glyph.Size)); // :141 size: 26
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-016")] // :136, :156-160 spacing 6, a spacer of at least 8 before the count
    [Trait("Req", "UI-HYDRAMERGE-017")]
    [Trait("Req", "PR-HYDRAMERGE-097")]
    [Trait("Decision", "ED-059")]
    [Trait("Decision", "ED-062")]
    public void The_faces_sit_six_points_apart_and_the_count_closes_the_row_in_subheadline_secondary_text()
    {
        var popover = Build(NoteWithHeads(3));
        headless.OnUiThread(() =>
        {
            var row = Assert.IsType<Grid>(RootStack(popover).Children[2]);
            var faces = Assert.IsType<StackPanel>(row.Children[0]);
            var count = RequiredText(popover, "3 heads landed");

            Assert.Equal(6, faces.Spacing); // :136 HStack(spacing: 6)
            Assert.Equal(8, row.ColumnSpacing); // :156 Spacer(minLength: 8)
            Assert.Equal(GridUnitType.Star, row.ColumnDefinitions[0].Width.GridUnitType); // the faces take what the count leaves
            Assert.Equal(GridUnitType.Auto, row.ColumnDefinitions[1].Width.GridUnitType);
            Assert.Same(row, count.Parent);
            Assert.Equal(1, Grid.GetColumn(count)); // trailing end
            AssertFont(count, SwiftFont.System(SwiftFont.TextStyle.Subheadline)); // :158
            Assert.Equal(11, count.FontSize);
            AssertForeground(count, Chrome.SecondaryText); // :159
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-016")] // :141-142 the face is the head's roster persona; the tooltip is "<name>: <task>"
    [Trait("Req", "PR-HYDRAMERGE-097")]
    public void A_face_wears_the_roster_persona_of_its_index_and_a_tooltip_of_the_notes_name_and_task()
    {
        var popover = Build(Parse(Body(FilesLine(), "Heads", HeadLine("Zed", 2, "Do the thing"), HeadLine("Hank 2", 11, "Again"))));
        headless.OnUiThread(() =>
        {
            var glyphs = Faces(popover);

            Assert.Equal(HydraRoster.Persona(2), glyphs[0].Persona);
            Assert.Equal("Zed: Do the thing", ToolTip.GetTip(glyphs[0])); // the note's own name and task, not the persona's
            Assert.Equal(HydraRoster.Persona(11), glyphs[1].Persona);
            Assert.Equal("Hank 2: Again", ToolTip.GetTip(glyphs[1]));
        });
    }

    // ------------------------------------------------------------------ PR-HYDRAMERGE-098 / UI-HYDRAMERGE-018 hover

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-098")] // :143-154 hovering a face opens that face's own popover from its bottom edge
    [Trait("Req", "UI-HYDRAMERGE-018")]
    public void Hovering_a_face_opens_its_own_card_from_its_bottom_edge_and_leaving_closes_it()
    {
        headless.OnUiThread(() =>
        {
            var presenter = new RecordingPresenter();
            using var scope = Presentation.Presenter.Override(presenter);
            var popover = new HydraMergePopover(NoteWithHeads(3));
            var glyphs = Faces(popover);
            var popup = PopupOf(glyphs[1]);

            Assert.Empty(presenter.Calls);
            Assert.Equal(PlacementMode.Bottom, popup.Placement); // :152 arrowEdge: .bottom
            Assert.Same(glyphs[1], popup.PlacementTarget); // the card hangs from the face under the cursor

            Enter(glyphs[1]);
            Assert.Equal([(popup, true)], presenter.Calls);

            Leave(glyphs[1]);
            Assert.Equal([(popup, true), (popup, false)], presenter.Calls);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-098")] // :144 moving from one face to the next hands over: the first card closes, the second opens
    [Trait("Req", "UI-HYDRAMERGE-018")]
    public void Moving_between_two_faces_hands_the_card_over_cleanly()
    {
        headless.OnUiThread(() =>
        {
            var presenter = new RecordingPresenter();
            using var scope = Presentation.Presenter.Override(presenter);
            var popover = new HydraMergePopover(NoteWithHeads(3));
            var glyphs = Faces(popover);
            var first = PopupOf(glyphs[0]);
            var second = PopupOf(glyphs[1]);
            Assert.NotSame(first, second); // each glyph carries its OWN popover

            Enter(glyphs[0]);
            Leave(glyphs[0]);
            Enter(glyphs[1]);

            Assert.Equal([(first, true), (first, false), (second, true)], presenter.Calls);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-098")] // :144 leaving clears the state only when it is still that head
    [Trait("Req", "UI-HYDRAMERGE-018")]
    public void A_late_leave_from_the_face_the_pointer_already_left_does_not_close_the_new_card()
    {
        headless.OnUiThread(() =>
        {
            var presenter = new RecordingPresenter();
            using var scope = Presentation.Presenter.Override(presenter);
            var popover = new HydraMergePopover(NoteWithHeads(3));
            var glyphs = Faces(popover);
            var first = PopupOf(glyphs[0]);
            var second = PopupOf(glyphs[1]);

            Enter(glyphs[0]);
            Enter(glyphs[1]); // the second face takes over before the first one reports its exit
            Leave(glyphs[0]); // hoveredHead is the second head, so this changes nothing
            Assert.Equal([(first, true), (first, false), (second, true)], presenter.Calls);

            Leave(glyphs[1]);
            Assert.Equal((second, false), presenter.Calls[^1]);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-098")] // :143-145 after leaving, hovering the same face opens its card again
    [Trait("Req", "UI-HYDRAMERGE-018")]
    public void Hovering_the_same_face_again_after_leaving_opens_its_card_again()
    {
        headless.OnUiThread(() =>
        {
            var presenter = new RecordingPresenter();
            using var scope = Presentation.Presenter.Override(presenter);
            var popover = new HydraMergePopover(NoteWithHeads(2));
            var glyph = Faces(popover)[0];
            var popup = PopupOf(glyph);

            Enter(glyph);
            Leave(glyph);
            Enter(glyph);

            Assert.Equal([(popup, true), (popup, false), (popup, true)], presenter.Calls);
        });
    }

    // ------------------------------------------------------------------ the head card

    private static Border HeadCardOf(HydraMergePopover popover, int face = 0) =>
        Assert.IsType<Border>(PopupOf(Faces(popover)[face]).Child);

    private static StackPanel HeadStack(Border card) => Assert.IsType<StackPanel>(card.Child);

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-018")] // :254-255 padding 12, frame width 320; :231 spacing 6
    [Trait("Req", "PR-HYDRAMERGE-099")]
    public void The_head_card_is_320_wide_with_12_padding_and_6_spacing()
    {
        var popover = Build(NoteWithHeads(2));
        headless.OnUiThread(() =>
        {
            var card = HeadCardOf(popover);

            Assert.Equal(320, card.Width);
            Assert.Equal(new Thickness(12), card.Padding);
            Assert.Equal(6, HeadStack(card).Spacing);
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-019")] // :232-233 the name, body semibold, with no secondary tint
    [Trait("Req", "PR-HYDRAMERGE-099")]
    [Trait("Decision", "ED-059")]
    public void The_head_name_is_body_semibold_in_the_default_foreground_on_the_first_line()
    {
        var popover = Build(Parse(Body(FilesLine(), "Heads", HeadLine("Hank 2", 11, "Tidy up", "a.cs"))));
        headless.OnUiThread(() =>
        {
            var card = HeadCardOf(popover);
            var name = Assert.IsType<TextBlock>(HeadStack(card).Children[0]);

            Assert.Equal("Hank 2", name.Text);
            AssertFont(name, SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Semibold));
            Assert.Equal(13, name.FontSize);
            Assert.False(name.IsSet(TextBlock.ForegroundProperty)); // no foregroundStyle: the default, not Chrome.secondaryText
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-020")] // :234-238 the task, callout, secondary, at most three lines, wrapping
    [Trait("Req", "PR-HYDRAMERGE-099")]
    [Trait("Decision", "ED-059")]
    [Trait("Decision", "ED-062")]
    public void The_head_task_is_callout_secondary_text_wrapping_to_three_lines_beneath_the_name()
    {
        var popover = Build(Parse(Body(FilesLine(), "Heads", HeadLine("Hank", 1, "Tidy up the parser", "a.cs"))));
        headless.OnUiThread(() =>
        {
            var task = Assert.IsType<TextBlock>(HeadStack(HeadCardOf(popover)).Children[1]);

            Assert.Equal("Tidy up the parser", task.Text);
            AssertFont(task, SwiftFont.System(SwiftFont.TextStyle.Callout));
            Assert.Equal(12, task.FontSize);
            AssertForeground(task, Chrome.SecondaryText);
            Assert.Equal(3, task.MaxLines); // :237 lineLimit(3)
            Assert.Equal(TextWrapping.Wrap, task.TextWrapping); // :238 fixedSize(horizontal: false, vertical: true)
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-021")] // :239 a divider between the task and the file list, even when there are no files
    [Trait("Req", "PR-HYDRAMERGE-099")]
    public void A_divider_always_sits_between_the_task_and_the_file_list()
    {
        var withFiles = Build(Parse(Body(FilesLine(), "Heads", HeadLine("Hank", 1, "T", "a.cs", "b.cs"))));
        var without = Build(Parse(Body(FilesLine(), "Heads", HeadLine("Hank", 1, "T"))));
        headless.OnUiThread(() =>
        {
            foreach (var popover in new[] { withFiles, without })
            {
                var children = HeadStack(HeadCardOf(popover)).Children;
                var divider = Assert.IsType<Border>(children[2]); // name, task, DIVIDER, then the files

                Assert.Equal(1, divider.Height);
                Assert.Equal(HorizontalAlignment.Stretch, divider.HorizontalAlignment);
                Assert.NotNull(divider.Background);
                Assert.IsType<TextBlock>(children[3]);
            }
        });
    }

    [Fact]
    [Trait("Req", "UI-HYDRAMERGE-022")] // :240-243 a head with no files says so, in subheadline secondary text
    [Trait("Req", "PR-HYDRAMERGE-099")]
    [Trait("Decision", "ED-059")]
    [Trait("Decision", "ED-062")]
    public void A_head_without_files_shows_the_explicit_empty_line_and_nothing_else_below_the_divider()
    {
        var popover = Build(Parse(Body(FilesLine(), "Heads", HeadLine("Hank", 1, "T"))));
        headless.OnUiThread(() =>
        {
            var children = HeadStack(HeadCardOf(popover)).Children;
            var empty = Assert.IsType<TextBlock>(children[3]);

            Assert.Equal(4, children.Count); // name, task, divider, the empty line
            Assert.Equal("No files of its own in this merge", empty.Text); // verbatim, no full stop
            AssertFont(empty, SwiftFont.System(SwiftFont.TextStyle.Subheadline));
            Assert.Equal(11, empty.FontSize);
            AssertForeground(empty, Chrome.SecondaryText);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-099")] // :244-251 one monospaced subheadline line per file, in order; no empty-state line
    [Trait("Decision", "ED-059")]
    public void A_head_with_files_lists_one_monospaced_line_per_file_and_no_empty_state()
    {
        var popover = Build(Parse(Body(FilesLine(), "Heads", HeadLine("Hank", 1, "T", "src/b.cs", "src/a.cs", "docs/c.md"))));
        headless.OnUiThread(() =>
        {
            var children = HeadStack(HeadCardOf(popover)).Children;

            Assert.Equal(6, children.Count); // name, task, divider, three files
            Assert.Equal(["src/b.cs", "src/a.cs", "docs/c.md"], children.Skip(3).Cast<TextBlock>().Select(file => file.Text));
            Assert.Null(Text(popover, "No files of its own in this merge"));

            foreach (var file in children.Skip(3).Cast<TextBlock>())
            {
                AssertFont(file, SwiftFont.System(SwiftFont.TextStyle.Subheadline, design: SwiftFont.Design.Monospaced));
                AssertForeground(file, Chrome.SecondaryText);
                Assert.Equal(1, file.MaxLines);
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-099")] // :250 truncationMode(.middle): a long path keeps both ends, a short one is whole
    public void A_long_file_path_is_cut_in_the_middle_so_both_ends_stay_readable_and_a_short_one_is_whole()
    {
        var longPath = "src/Marrow.App/Views/Chat/Deeply/Nested/Folder/Structure/That/Goes/On/And/On/ForeverAndEver/HydraMergePopoverImplementation.cs";
        var popover = Build(Parse(Body(FilesLine(), "Heads", HeadLine("Hank", 1, "T", longPath, "a.cs"))));
        headless.OnUiThread(() =>
        {
            var presenter = new RecordingPresenter();
            using var scope = Presentation.Presenter.Override(presenter);
            var card = HeadCardOf(popover);
            var popup = PopupOf(Faces(popover)[0]);
            popup.Child = null; // the recorded popup never opens headless: host its content in a window
            var window = Show(card);
            try
            {
                var children = HeadStack(card).Children;
                var cut = Assert.IsType<TextBlock>(children[3]);
                var whole = Assert.IsType<TextBlock>(children[4]);

                var ellipsis = cut.Text!.IndexOf('…', StringComparison.Ordinal);
                Assert.True(ellipsis > 0, "expected a middle ellipsis in: " + cut.Text);
                Assert.True(ellipsis < cut.Text.Length - 1, "text must continue after the ellipsis: " + cut.Text);
                Assert.StartsWith(cut.Text[..ellipsis], longPath, StringComparison.Ordinal); // the start of the path survives
                Assert.EndsWith(cut.Text[(ellipsis + 1)..], longPath, StringComparison.Ordinal); // and so does the end
                Assert.True(cut.Text.Length < longPath.Length);
                Assert.Equal("a.cs", whole.Text);
            }
            finally
            {
                window.Close();
            }
        });
    }

    // ------------------------------------------------------------------ PR-HYDRAMERGE-094 copy link

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-094")] // :187-189 the pasteboard is cleared and set to the request URL's absolute string
    public void Copy_link_replaces_the_clipboard_with_the_request_url()
    {
        headless.OnUiThread(() =>
        {
            using var noNetwork = new NoLinkNetwork(FaviconHost); // R-TEST-10: the card asks gitlab.com for its favicon when it appears (found by the network audit, ED-256 (10))
            using var runtime = new TestRuntime();
            using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the label morphs (numericText, :202-203)
            var popover = new HydraMergePopover(Plain());
            var window = Show(popover);
            try
            {
                window.Clipboard!.SetTextAsync("something else").GetAwaiter().GetResult();
                var button = Descendants<Button>(popover).First(candidate => Text(candidate, "Copy link") is not null);

                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                runtime.RunUntilIdle();
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(GitlabUrl, window.Clipboard!.TryGetTextAsync().GetAwaiter().GetResult());
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-094")] // :187 a second click while the button says Copied does nothing, the pasteboard included
    public void A_second_click_while_copied_leaves_the_clipboard_alone()
    {
        headless.OnUiThread(() =>
        {
            using var noNetwork = new NoLinkNetwork(FaviconHost); // R-TEST-10: the card asks gitlab.com for its favicon when it appears (found by the network audit, ED-256 (10))
            using var runtime = new TestRuntime();
            using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the label morphs (numericText, :202-203)
            var popover = new HydraMergePopover(Plain());
            var window = Show(popover);
            try
            {
                var button = Descendants<Button>(popover).First(candidate => Text(candidate, "Copy link") is not null);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                runtime.RunUntilIdle();
                Dispatcher.UIThread.RunJobs();
                Assert.NotNull(Text(popover, "Copied"));

                window.Clipboard!.SetTextAsync("changed since").GetAwaiter().GetResult();
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                runtime.RunUntilIdle();
                Dispatcher.UIThread.RunJobs();

                Assert.Equal("changed since", window.Clipboard!.TryGetTextAsync().GetAwaiter().GetResult());
                Assert.NotNull(Text(popover, "Copied"));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-094")] // :190, :193 both label changes are made inside withAnimation(.snappy(duration: 0.22)); :192 the revert is after 1.4 seconds
    public void The_label_changes_animate_with_snappy_022_both_ways_and_the_revert_comes_after_one_point_four_seconds()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the label morphs (numericText, :202-203)
            var popover = new HydraMergePopover(Plain());
            var copy = Descendants<Button>(popover).First(candidate => Text(candidate, "Copy link") is not null);
            var label = Assert.IsType<StackPanel>(copy.Content);
            var seen = new List<Animation?>();
            label.Children.CollectionChanged += (_, _) => seen.Add(Transaction.Current);

            copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            runtime.RunUntilIdle();
            Assert.NotEmpty(seen);
            Assert.All(seen, animation => Assert.Equal(Animation.Snappy(duration: 0.22), animation));
            Assert.NotNull(Text(copy, "Copied"));

            var before = seen.Count;
            runtime.AdvanceMilliseconds(1399);
            Assert.Equal(before, seen.Count); // nothing has moved yet
            runtime.AdvanceMilliseconds(1);
            Assert.True(seen.Count > before);
            Assert.All(seen, animation => Assert.Equal(Animation.Snappy(duration: 0.22), animation));
            Assert.NotNull(Text(copy, "Copy link"));
        });
    }

    // ------------------------------------------------------------------ PR-HYDRAMERGE-095 the open button

    [Theory]
    [Trait("Req", "PR-HYDRAMERGE-095")] // :90-96, :208-214 the label per forge, the arrow.up.right symbol, the default action
    [InlineData("https://gitlab.com/acme/app/-/merge_requests/7", "Open merge request")]
    [InlineData("https://github.com/acme/app/pull/7", "Open pull request")]
    [InlineData("https://gitea.example/acme/app/pulls/7", "Open pull request")]
    public void The_open_button_reads_the_forges_title_beside_an_up_right_arrow_and_answers_return(string url, string title)
    {
        var popover = Build(Plain(url));
        headless.OnUiThread(() =>
        {
            var open = Descendants<Button>(popover).Single(candidate => Text(candidate, title) is not null);
            var other = title == "Open merge request" ? "Open pull request" : "Open merge request";

            Assert.Null(Text(popover, other)); // exactly one of the two labels is ever drawn
            Assert.True(open.IsDefault); // :214 .keyboardShortcut(.defaultAction): Return triggers it
            Assert.True(open.IsEnabled);
            Assert.Contains(Descendants<SwiftImage>(open), image => image.SymbolName == "arrow.up.right"); // :211
            Assert.Equal(2, Descendants<Button>(popover).Length); // the copy button and this one
            var copy = Descendants<Button>(popover).Single(candidate => Text(candidate, "Copy link") is not null);
            Assert.False(copy.IsDefault); // only the open button is the default action
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-095")] // :208-210 the open button hands the note's own link to NSWorkspace.shared.open, once per click
    public void The_open_button_opens_the_notes_link_in_the_default_browser_once_per_click()
    {
        const string url = "https://github.com/acme/app/pull/7";
        var opened = new List<string>();
        var popover = Build(Plain(url));
        headless.OnUiThread(() =>
        {
            using var frames = FrameClock.Source.Override(new ManualFrameSource()); // the copy label morphs (numericText, :202-203)
            // On the UI thread's own flow, where the click runs (the seam is an async-local override).
            using var browser = Marrow.Services.Port.Workspace.Open.Override(link =>
            {
                opened.Add(link.AbsoluteString);
                return true;
            });
            var open = Descendants<Button>(popover).Single(candidate => Text(candidate, "Open pull request") is not null);
            var copy = Descendants<Button>(popover).Single(candidate => Text(candidate, "Copy link") is not null);

            Assert.Empty(opened); // nothing opens until the button is clicked
            open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal([url], opened);
            open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal([url, url], opened);
            copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(2, opened.Count); // the copy button never opens the link
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-095")] // :182-215 the copy button sits left of the open button, the pair pushed to the trailing edge
    public void The_actions_row_puts_copy_link_before_the_open_button_at_the_trailing_edge()
    {
        var popover = Build(Plain());
        headless.OnUiThread(() =>
        {
            var row = Assert.IsType<Grid>(RootStack(popover).Children[^1]);
            var buttons = row.Children.Cast<Button>().ToArray();

            Assert.Equal(8, row.ColumnSpacing); // :182 HStack(spacing: 8)
            Assert.Equal(GridUnitType.Star, row.ColumnDefinitions[0].Width.GridUnitType); // :183 Spacer()
            Assert.NotNull(Text(buttons[0], "Copy link"));
            Assert.NotNull(Text(buttons[1], "Open merge request"));
            Assert.Equal([1, 2], buttons.Select(Grid.GetColumn));
        });
    }
}
