using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App;
using Marrow.App.Port;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Composer;

// Source: DroppyCode/App/Views/Composer/ComposerView.swift (B3 52bc792), :239-262 (the suggestion panel), :1061-1101 (its rows),
// DroppyCode/UI/Chrome/Popovers.swift :117-150 (the section header and the note): the blitz-composerview unit's proofs for
// UI-COMPOSER-019, 020, 021 and 022.
public sealed partial class ComposerViewTests
{
    // A pill on screen whose list is hosted in a window of 340 wide as it is when presented, the first suggestions shown.
    private sealed class HostedList : IDisposable
    {
        public required ThreadRuntimePart12Harness H { get; init; }
        public required ComposerView View { get; init; }
        public required AvaloniaWindow PillWindow { get; init; }
        public required AvaloniaWindow ListWindow { get; init; }
        public required Decorator Host { get; init; }
        public required IDisposable Seam { get; init; }

        public void Dispose()
        {
            Close(H, ListWindow);
            Close(H, PillWindow);
            Seam.Dispose();
        }
    }

    private static HostedList HostedSuggestions(ThreadRuntimePart12Harness h, ThreadRuntime runtime, string? directory, string typed, Func<ComposerView, bool> shown)
    {
        var host = new Decorator();
        AvaloniaWindow listWindow = null!;
        var presenter = new HostingPresenter(host, () => listWindow.UpdateLayout());
        var seam = Presentation.Presenter.Override(presenter);
        listWindow = h.Main.OnMain(() => Show(host, h.App, 340));
        var (view, pillWindow, _, _) = OnScreen(h, runtime, directory);
        h.Main.OnMain(() => Type(view, typed));
        Until(h, pillWindow, () => shown(view) && presenter.Shown > 0);
        Pump(h, listWindow);
        return new HostedList { H = h, View = view, PillWindow = pillWindow, ListWindow = listWindow, Host = host, Seam = seam };
    }

    // ---- UI-COMPOSER-019 (:244; Popovers.swift:117-133): the section header ----

    [Fact]
    [Trait("Req", "UI-COMPOSER-019")] // App/Views/Composer/ComposerView.swift:244; UI/Chrome/Popovers.swift:117-133
    [Trait("Rule", "R-TEST-4")] // The texts, the style, the margins and the colour: The_file_list_is_shown_in_the_panel_under_the_header_Files, The_command_list_is_headed_..., B3_suggestion_header_and_error_note_use_the_source_fonts_margins_and_secondary_colour
    public void The_header_of_the_file_list_and_of_the_command_list_spans_the_rows_and_its_text_starts_6_plus_8_in_from_the_panel_edge()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var directory = h.Store.NewFolder("header-geometry");
            Touch(directory, "main.swift", "mail.txt");
            Indexed(h, directory);
            using (var files = HostedSuggestions(h, Unresolved(h), directory, "@mai", view => view.Suggestions.IsVisible))
                AssertHeader(files, "Files");
            using (var commands = HostedSuggestions(h, h.Runtime, null, "/", view => view.Suggestions.IsVisible))
                AssertHeader(commands, "Commands · Recently used first");
        });
    }

    private static void AssertHeader(HostedList list, string title)
    {
        list.H.Main.OnMain(() =>
        {
            var frame = Assert.IsType<Border>(list.Host.Child);
            var header = One<PopoverSectionHeader>(frame);
            var text = Assert.IsType<TextBlock>(header.Child);
            Assert.Equal(title, text.Text);
            var row = LogicalQuery.SelfAndDescendants<SuggestionPopoverRow>(frame).First();
            var headerFrame = Frame(header, frame);
            Assert.Equal(6, headerFrame.X, 3); // the menu's own 6 point inset
            Assert.Equal(Frame(row, frame).Width, headerFrame.Width, 3); // frame(maxWidth: .infinity): the header is as wide as the rows
            Assert.Equal(Frame(row, frame).X, headerFrame.X, 3);
            var textFrame = Frame(text, frame);
            Assert.Equal(6 + 8, textFrame.X, 3); // .padding(.horizontal, 8) leading
            Assert.Equal(HorizontalAlignment.Left, text.HorizontalAlignment); // leading aligned
            Assert.Equal(headerFrame.Y + 6, textFrame.Y, 1); // .padding(.top, 6)
            Assert.Equal(textFrame.Bottom + 3, headerFrame.Bottom, 1); // .padding(.bottom, 3)
            Assert.Equal(headerFrame.Bottom, Frame(row, frame).Y, 3); // the rows follow it directly
        });
    }

    // ---- UI-COMPOSER-020 (:1061-1101): the row ----

    [Fact]
    [Trait("Req", "UI-COMPOSER-020")] // App/Views/Composer/ComposerView.swift:1061-1101, :260
    [Trait("Rule", "R-TEST-4")] // The row's geometry, fonts, plate and commit keys: B3_suggestion_row_*, The_highlighted_row_is_filled_*, the PR-COMPOSER-045 tests; the hover fill: The_hovered_rows_fill_fades_in_over_a_tenth_of_a_second_on_an_ease_out
    public void The_row_reads_primary_text_with_a_secondary_detail_and_Up_and_Down_move_the_highlight_and_scroll_it_into_view_both_ways()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var directory = h.Store.NewFolder("up-down");
            var commands = Enumerable.Range(0, 30).Select(index => new SlashCommand
            {
                Name = "cmd" + index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture),
                Detail = "Command " + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            }).ToArray();
            h.Main.OnMain(() => h.App.Providers.UpdateCommands(new ValueList<SlashCommand>(commands), ProviderKind.Codex, directory));
            using var list = HostedSuggestions(h, h.Runtime, directory, "/cmd", view => view.Suggestions.Items.Count == 30);
            var view = list.View;
            h.Main.OnMain(() =>
            {
                var row = LogicalQuery.SelfAndDescendants<SuggestionPopoverRow>(list.Host).First();
                var title = LogicalQuery.SelfAndDescendants<TextBlock>(row).First();
                var detail = LogicalQuery.SelfAndDescendants<TextBlock>(row).Last();
                Assert.Equal("/cmd00", title.Text);
                Assert.Equal("Command 0", detail.Text);
                var theme = title.ActualThemeVariant;
                Assert.Equal(Chrome.PrimaryText.Resolve(theme), Assert.IsAssignableFrom<ISolidColorBrush>(title.Foreground).Color); // foreground Chrome.primaryText
                Assert.Equal(Chrome.SecondaryText.Resolve(theme), Assert.IsAssignableFrom<ISolidColorBrush>(detail.Foreground).Color); // the detail Chrome.secondaryText
                var symbol = LogicalQuery.SelfAndDescendants<SwiftImage>(row).First();
                Assert.Equal(Chrome.PrimaryText.Resolve(theme), Assert.IsAssignableFrom<ISolidColorBrush>(symbol.Foreground).Color); // the symbol takes the row's foreground
            });
            ScrollViewer Scroll() => h.Main.OnMain(() => One<ScrollViewer>(list.Host));
            void Step(Key key)
            {
                Assert.True(h.Main.OnMain(() => Press(view, key)));
                Pump(h, list.ListWindow);
            }
            Assert.Equal(0, Scroll().Offset.Y);
            for (var step = 0; step < 20; step++) Step(Key.Down);
            Assert.Equal(20, view.Suggestions.Selected);
            AssertSelectedInView(h, list, 20);
            var down = Scroll().Offset.Y;
            Assert.True(down > 0, "Down scrolled the list");
            for (var step = 0; step < 18; step++) Step(Key.Up);
            Assert.Equal(2, view.Suggestions.Selected);
            AssertSelectedInView(h, list, 2);
            Assert.True(Scroll().Offset.Y < down, "Up scrolled it back");
        });
    }

    private static void AssertSelectedInView(ThreadRuntimePart12Harness h, HostedList list, int index) => h.Main.OnMain(() =>
    {
        var scroll = One<ScrollViewer>(list.Host);
        var selected = LogicalQuery.SelfAndDescendants<SuggestionPopoverRow>(list.Host).ElementAt(index);
        var top = selected.TranslatePoint(default, scroll)!.Value.Y;
        Assert.InRange(top, -0.5, scroll.Viewport.Height - selected.Bounds.Height + 0.5); // the whole highlighted row is inside the viewport
    });

    // ---- UI-COMPOSER-021 / 022 (:245-247; Popovers.swift:135-150): the failed load's note and its retry row ----

    [Fact]
    [Trait("Req", "UI-COMPOSER-021")] // App/Views/Composer/ComposerView.swift:245-246; UI/Chrome/Popovers.swift:135-150
    [Trait("Rule", "R-TEST-4")] // The text, colour, margins, size and the condition: A_failed_command_load_shows_its_description_then_a_Retry_..., B3_suggestion_header_and_error_note_use_the_source_fonts_margins_and_secondary_colour
    public void A_long_load_error_wraps_inside_the_340_wide_panel_with_no_sideways_scroll_and_nothing_cut_at_the_right_edge()
    {
        headless.OnUiThread(() =>
        {
            using var h = RuntimeHarness();
            var text = string.Join(" ", Enumerable.Range(0, 30).Select(index => "word" + index.ToString(System.Globalization.CultureInfo.InvariantCulture))); // about 200 characters
            Assert.InRange(text.Length, 190, 260);
            var host = new Decorator();
            var window = h.Main.OnMain(() => Show(host, h.App, 340));
            h.Main.OnMain(() =>
            {
                // The panel as the composer frames it: a 340 wide, fixed-height frame round the menu (ComposerTextView.swift:70-71).
                host.Child = new Border { Width = 340, Height = 340, Child = new PopoverMenu(() => new StackPanel { Children = { new PopoverNote(text) } }) };
            });
            Pump(h, window);
            h.Main.OnMain(() =>
            {
                var note = One<PopoverNote>(host);
                var block = Assert.IsType<TextBlock>(note.Child);
                Assert.Equal(TextWrapping.Wrap, block.TextWrapping);
                var lineHeight = block.FontSize * 1.2;
                Assert.True(block.Bounds.Height > 2 * lineHeight, $"a 200 character note takes several lines: {block.Bounds.Height}");
                var scroll = One<ScrollViewer>(host);
                Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
                Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 0.5, $"nothing to scroll sideways: {scroll.Extent} in {scroll.Viewport}");
                var frame = Frame(block, host);
                Assert.True(frame.Right <= 340 - 6 - 8 + 0.5, $"the text stays inside the panel's insets: {frame}");
                Assert.Equal(new Thickness(8, 5), block.Margin); // the note's own margins hold
            });
            Close(h, window);
        });
    }
}
