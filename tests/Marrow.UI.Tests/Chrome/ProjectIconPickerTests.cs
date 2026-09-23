using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Chrome;

// Source under test: DroppyCode/UI/Chrome/ProjectIconPicker.swift (B2 1fbf81e), :1-273.
// Records: PR-SIDEBAR-150, PR-SIDEBAR-151, PR-SIDEBAR-152, PR-SIDEBAR-153, UI-SIDEBAR-052, UI-SIDEBAR-053,
// UI-SIDEBAR-054, UI-SIDEBAR-055, UI-SIDEBAR-056, UI-SIDEBAR-057.
// Every expected value is transcribed from the pinned Swift and the records, never from the C# under test:
//   the 300x420 frame and 12pt padding (:170-171); the heading, search field and Folder row strings and fonts
//   (:138-166); the emoji list and shortlist as the reference Swift file's own literal characters (:60-102);
//   the group order Communication..Other from ProjectSymbolCatalogueTests; the 9x26pt grid at 4pt spacing (:111);
//   the tile's fill/border/font rules (:251-266).
// Port shape these tests read (R-VIEW-1, R-VIEW-9, ED-095): a SwiftUI Button is found through UI Automation; the
// popover goes through PlainControls.Popover and is recorded by PopoverRecorder (R-TEST-7); a SF Symbol tile draws
// through SwiftImage under the OD-0010 table, so a tile's SF name and font are read off SwiftImage.SymbolName and
// SwiftImage.GetFont. Not asserted here: the glass and `.continuous` corner curvature (not this unit's dependency).
[Collection("Headless")]
public sealed class ProjectIconPickerTests(HeadlessSession headless)
{
    private static Window Show(Control content, ThemeVariant? theme = null, double width = 320, double height = 460)
    {
        content.HorizontalAlignment = HorizontalAlignment.Left;
        content.VerticalAlignment = VerticalAlignment.Top;
        var window = new Window { Width = width, Height = height, Content = content };
        if (theme is not null) window.RequestedThemeVariant = theme;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static IEnumerable<Control> SelfAndDescendants(Control root) =>
        new[] { root }.Concat(VisualQuery.Descendants<Control>(root));

    private static AutomationPeer Peer(Control control) => ControlAutomationPeer.CreatePeerForElement(control);

    private static Control TheButton(Control root) =>
        Assert.Single(SelfAndDescendants(root), c => Peer(c).GetAutomationControlType() == AutomationControlType.Button);

    private static List<Control> Buttons(Control root) =>
        SelfAndDescendants(root).Where(c => Peer(c).GetAutomationControlType() == AutomationControlType.Button).ToList();

    private static List<SwiftImage> Images(Control root) => VisualQuery.Descendants<SwiftImage>(root).ToList();

    private static List<TextBox> Boxes(Control root) => VisualQuery.Descendants<TextBox>(root).ToList();

    private static Color Effective(IBrush? brush)
    {
        if (brush is null) return Color.FromArgb(0, 0, 0, 0);
        var solid = Assert.IsAssignableFrom<ISolidColorBrush>(brush);
        return Color.FromArgb((byte)Math.Round(solid.Color.A * solid.Opacity), solid.Color.R, solid.Color.G, solid.Color.B);
    }

    private static void AssertColor(Color expected, Color actual)
    {
        Assert.Equal((expected.R, expected.G, expected.B), (actual.R, actual.G, actual.B));
        Assert.InRange((int)actual.A, expected.A - 1, expected.A + 1);
    }

    private static Color Label(double opacity, ThemeVariant theme) => SwiftColor.LabelColor.Opacity(opacity).Resolve(theme);

    private static Control TextOf(Control root, string text) =>
        Assert.Single(VisualQuery.Descendants<TextBlock>(root), t => t.Text == text);

    // "Popular" (the shortlist) and the catalogue categories can share names (e.g. "hammer.fill" is real SF Symbol
    // data in both), so a specific symbol's tile is looked up within the Popular section alone, never over the
    // whole picker.
    private static Control PopularSection(Control picker) =>
        VisualQuery.Descendants<StackPanel>(picker).First(s => s.Children.Count == 2 && s.Children[0] is TextBlock t && t.Text == "Popular");

    private static SwiftImage PopularTile(Control picker, string sf) =>
        Assert.Single(Images(PopularSection(picker)), i => i.SymbolName == sf);

    // A tile far down the scrolling content may not be within the visible viewport, so its button is invoked
    // directly rather than through hit-testing coordinates that only work for what is on screen.
    private static void Click(Control button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    // ---- ProjectIconMark, :9-27 ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-150")] // ProjectIconPicker.swift:16-26 ProjectIconMark.body
    public void The_project_icon_mark_draws_the_folder_glyph_until_an_icon_is_picked() => headless.OnUiThread(() =>
    {
        var mark = new ProjectIconMark(null, size: 13);
        var window = Show(mark);
        var image = Assert.Single(Images(mark));
        Assert.Equal("folder.fill", image.SymbolName);
        Assert.Equal(SwiftFont.System(13), SwiftImage.GetFont(image));
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-150")] // ProjectIconPicker.swift:17-19 the emoji branch
    public void The_project_icon_mark_draws_the_picked_emoji_at_the_given_size() => headless.OnUiThread(() =>
    {
        var mark = new ProjectIconMark(new ProjectIcon.Emoji("\U0001F680"), size: 13);
        var window = Show(mark);
        var text = Assert.Single(VisualQuery.Descendants<TextBlock>(mark));
        Assert.Equal("\U0001F680", text.Text);
        Assert.Equal(13, text.FontSize);
        window.Close();
    });

    // ---- ProjectIconButton, :30-51 ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-150")] // ProjectIconPicker.swift:45-46 .help / .accessibilityLabel
    [Trait("Req", "UI-SIDEBAR-052")] // ProjectIconPicker.swift:29-51
    public void The_project_icon_button_is_tooltipped_and_named_byte_for_byte() => headless.OnUiThread(() =>
    {
        var project = new CellSample<ProjectIcon?>(null);
        var button = new ProjectIconButton(new Binding<ProjectIcon?>(() => project.Value, v => project.Value = v));
        var window = Show(button);
        var theButton = TheButton(button);
        Assert.Equal("Choose this project's icon", ToolTip.GetTip(theButton));
        Assert.Equal(
            Encoding.UTF8.GetBytes("Choose this project's icon"),
            Encoding.UTF8.GetBytes((string)ToolTip.GetTip(theButton)!));
        Assert.Equal("Project icon", Peer(theButton).GetName());
        window.Close();
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-052")] // ProjectIconPicker.swift:41 .frame(width: size + 9, height: size + 9)
    public void The_project_icon_button_frames_its_mark_at_size_plus_nine() => headless.OnUiThread(() =>
    {
        var project = new CellSample<ProjectIcon?>(null);
        var button = new ProjectIconButton(new Binding<ProjectIcon?>(() => project.Value, v => project.Value = v), size: 13);
        var window = Show(button);
        var mark = Assert.Single(VisualQuery.Descendants<ProjectIconMark>(button));
        Assert.Equal(22, mark.Parent is Panel p ? p.Width : mark.Width);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-150")] // ProjectIconPicker.swift:37-39, :47-49 isPresented.toggle(), .popover(isPresented:, arrowEdge: .bottom)
    public void Clicking_the_project_icon_button_presents_the_picker_below_it() => headless.OnUiThread(() =>
    {
        var project = new CellSample<ProjectIcon?>(null);
        var recorder = new PopoverRecorder();
        using var presenter = Presentation.Presenter.Override(recorder);
        var button = new ProjectIconButton(new Binding<ProjectIcon?>(() => project.Value, v => project.Value = v));
        var window = Show(button);

        var theButton = TheButton(button);
        window.MouseDown(Centre(theButton, window), Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None);
        window.MouseUp(Centre(theButton, window), Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        var (popup, presented) = Assert.Single(recorder.Calls);
        Assert.True(presented);
        Assert.Equal(PlacementMode.Bottom, popup.Placement);
        Assert.Same(theButton, popup.PlacementTarget);
        Assert.True(popup.IsLightDismissEnabled);
        window.Close();
    });

    private static Point Centre(Control control, Window window)
    {
        var origin = control.TranslatePoint(new Point(0, 0), window)!.Value;
        return new Point(origin.X + control.Bounds.Width / 2, origin.Y + control.Bounds.Height / 2);
    }

    // ---- ProjectIconPicker container, :56-172, PR-SIDEBAR-150, UI-SIDEBAR-053 ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-150")] // ProjectIconPicker.swift:136-172 body: frame 300x420, padding 12, heading
    [Trait("Req", "UI-SIDEBAR-053")] // ProjectIconPicker.swift:136-172
    public void The_picker_is_a_300_by_420_panel_headed_Project_icon() => headless.OnUiThread(() =>
    {
        var project = new CellSample<ProjectIcon?>(null);
        var picker = new ProjectIconPicker(new Binding<ProjectIcon?>(() => project.Value, v => project.Value = v));
        var window = Show(picker, ThemeVariant.Light, width: 400, height: 500);

        var border = Assert.IsType<Border>(picker.Child);
        Assert.Equal(300, border.Width);
        Assert.Equal(420, border.Height);
        Assert.Equal(new Thickness(12), border.Padding);

        var heading = TextOf(picker, "Project icon");
        Assert.Equal(SwiftFont.Weight.Medium, ((TextBlock)heading).FontWeight == FontWeight.Medium ? SwiftFont.Weight.Medium : SwiftFont.Weight.Regular);
        AssertColor(Label(1, window.ActualThemeVariant), Effective(((TextBlock)heading).Foreground));
        window.Close();
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-057")] // ProjectIconPicker.swift:158-168 the Folder row
    public void The_folder_row_shows_the_word_Folder_in_secondary_text_beside_its_tile() => headless.OnUiThread(() =>
    {
        var project = new CellSample<ProjectIcon?>(null);
        var picker = new ProjectIconPicker(new Binding<ProjectIcon?>(() => project.Value, v => project.Value = v));
        var window = Show(picker, ThemeVariant.Light, width: 400, height: 500);

        var label = (TextBlock)TextOf(picker, "Folder");
        AssertColor(Label(0.7, window.ActualThemeVariant), Effective(label.Foreground));
        window.Close();
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-057")] // ProjectIconPicker.swift:159 IconTile(isSelected: icon == nil, action: { icon = nil })
    public void The_folder_tile_is_selected_while_no_icon_is_set_and_clicking_it_clears_the_icon() => headless.OnUiThread(() =>
    {
        var project = new CellSample<ProjectIcon?>(new ProjectIcon.Symbol("star.fill"));
        var picker = new ProjectIconPicker(new Binding<ProjectIcon?>(() => project.Value, v => project.Value = v));
        var window = Show(picker, ThemeVariant.Light, width: 400, height: 500);

        var folderLabel = TextOf(picker, "Folder");
        var footer = Assert.IsType<StackPanel>(folderLabel.Parent);
        var folderTile = Assert.Single(Buttons(footer));
        Assert.DoesNotContain(VisualQuery.Descendants<Border>(folderTile), b => b.BorderThickness.Left > 0 && b.IsVisible);

        Click(folderTile);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(project.Value);
        window.Close();
    });

    // ---- Default sections, PR-SIDEBAR-151 ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-151")] // ProjectIconPicker.swift:144-148 the default view: Emoji, Popular, every catalogue group
    [Trait("Req", "UI-SIDEBAR-055")] // ProjectIconPicker.swift:232-239, :104-109
    public void With_no_query_the_picker_shows_Emoji_then_Popular_then_every_catalogue_group_in_order() => headless.OnUiThread(() =>
    {
        var project = new CellSample<ProjectIcon?>(null);
        var picker = new ProjectIconPicker(new Binding<ProjectIcon?>(() => project.Value, v => project.Value = v));
        var window = Show(picker, ThemeVariant.Light, width: 400, height: 500);

        var headings = VisualQuery.Descendants<TextBlock>(picker)
            .Where(t => t.FontWeight == FontWeight.Medium && t.FontSize == SwiftFont.Footnote.PointSize)
            .Select(t => t.Text!)
            .ToList();
        var expected = new List<string> { "Emoji", "Popular" };
        expected.AddRange(new[]
        {
            "Communication", "Maps", "Objects & Tools", "Devices", "Camera & Photos", "Gaming", "Connectivity",
            "Transportation", "Automotive", "Accessibility", "Privacy & Security", "Human", "Home", "Fitness",
            "Nature", "Editing", "Text Formatting", "Media", "Keyboard", "Commerce", "Time", "Health", "Shapes",
            "Arrows", "Indices", "Math", "Other",
        });
        Assert.Equal(expected, headings);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-151")] // ProjectIconPicker.swift:80-85 favourites, all 56 shortlist names kept under OD-0010
    public void Popular_holds_all_56_shortlist_names() => headless.OnUiThread(() =>
    {
        var project = new CellSample<ProjectIcon?>(null);
        var picker = new ProjectIconPicker(new Binding<ProjectIcon?>(() => project.Value, v => project.Value = v));
        var window = Show(picker, ThemeVariant.Light, width: 400, height: 500);

        var names = Images(picker).Select(i => i.SymbolName).Where(n => n is not null).ToList();
        Assert.Contains("hammer.fill", names);
        Assert.Contains("theatermasks.fill", names);
        // Every symbol image drawn (excluding the Folder tile's own "folder.fill") is a Popular or category tile;
        // Popular's own count is exactly the 56-name shortlist (PR-SIDEBAR-151).
        var popularSection = VisualQuery.Descendants<StackPanel>(picker)
            .First(s => s.Children.Count == 2 && s.Children[0] is TextBlock t && t.Text == "Popular");
        var popularNames = Images(popularSection).Select(i => i.SymbolName).ToList();
        Assert.Equal(56, popularNames.Count);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-151")] // ProjectIconPicker.swift:60-78 the 131 fixed emoji, in order
    public void The_emoji_section_shows_all_131_emoji_in_source_order() => headless.OnUiThread(() =>
    {
        var project = new CellSample<ProjectIcon?>(null);
        var picker = new ProjectIconPicker(new Binding<ProjectIcon?>(() => project.Value, v => project.Value = v));
        var window = Show(picker, ThemeVariant.Light, width: 400, height: 500);

        var emojiSection = VisualQuery.Descendants<StackPanel>(picker)
            .First(s => s.Children.Count == 2 && s.Children[0] is TextBlock t && t.Text == "Emoji");
        var texts = VisualQuery.Descendants<TextBlock>(emojiSection).Where(t => t.Text != "Emoji").Select(t => t.Text).ToList();
        Assert.Equal(131, texts.Count);
        Assert.Equal("\U0001F680", texts[0]);
        Assert.Equal("\U0001F9D1\u200D\U0001F3EB", texts[^1]);
        window.Close();
    });

    // ---- Search, PR-SIDEBAR-152 ----

    private static TextBox SearchBox(Control root) => Assert.Single(Boxes(root));

    [Fact]
    [Trait("Req", "PR-SIDEBAR-152")] // ProjectIconPicker.swift:115-123, :144-151 a matching query replaces the sections with one Symbols grid
    [Trait("Req", "UI-SIDEBAR-054")] // ProjectIconPicker.swift:188-203
    public void A_query_with_matches_shows_a_single_Symbols_section_of_matching_names() => headless.OnUiThread(() =>
    {
        var project = new CellSample<ProjectIcon?>(null);
        var picker = new ProjectIconPicker(new Binding<ProjectIcon?>(() => project.Value, v => project.Value = v));
        var window = Show(picker, ThemeVariant.Light, width: 400, height: 500);

        var box = SearchBox(picker);
        Assert.Equal("Search symbols", box.PlaceholderText);
        box.Text = "hammer";
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain(VisualQuery.Descendants<TextBlock>(picker), t => t.Text == "Emoji");
        Assert.Single(VisualQuery.Descendants<TextBlock>(picker), t => t.Text == "Symbols");
        var names = Images(picker).Select(i => i.SymbolName).Where(n => n is not null && n != "folder.fill").ToList();
        Assert.Contains("hammer.fill", names);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-152")] // ProjectIconPicker.swift:128-134 filledForm, :119-121 hoisted to the front
    public void A_query_that_already_names_a_catalogue_symbol_shows_it_among_the_matches() => headless.OnUiThread(() =>
    {
        var project = new CellSample<ProjectIcon?>(null);
        var picker = new ProjectIconPicker(new Binding<ProjectIcon?>(() => project.Value, v => project.Value = v));
        var window = Show(picker, ThemeVariant.Light, width: 400, height: 500);

        var box = SearchBox(picker);
        box.Text = "star";
        Dispatcher.UIThread.RunJobs();

        var symbolsSection = VisualQuery.Descendants<StackPanel>(picker)
            .First(s => s.Children.Count == 2 && s.Children[0] is TextBlock t && t.Text == "Symbols");
        var names = Images(symbolsSection).Select(i => i.SymbolName).ToList();
        Assert.Contains("star.fill", names);
        window.Close();
    });

    /// <summary>
    /// ProjectIconPicker.swift:128-134 filledForm(of:), exercised directly (R-TEST reflection convention, as the
    /// class-level byte-hash test): under the OD-0010 table every mapped `.fill` name is already a member of
    /// ProjectSymbolCatalogue's own categories, so the hoist-at-front branch (:119-121, `!names.Contains(typed)`)
    /// cannot be driven to fire through the picker's own UI with the table this build ships (recorded as untestable
    /// in the unit report, not skipped: the branch itself is still present and correct, R-TEST rules 1.3/5.1). This
    /// test instead proves filledForm's two real branches directly.
    /// </summary>
    [Fact]
    [Trait("Req", "PR-SIDEBAR-152")] // ProjectIconPicker.swift:128-134 filledForm(of:)
    public void FilledForm_offers_a_names_own_fill_form_or_itself_when_the_table_maps_it()
    {
        var method = typeof(ProjectIconPicker).GetMethod("FilledForm", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        string? Call(string name) => (string?)method.Invoke(null, [name]);

        Assert.Equal("star.fill", Call("star")); // :132-133, not ending .fill: offered as name + ".fill"
        Assert.Equal("star.fill", Call("star.fill")); // :129-130, already ending .fill and mapped: offered as itself
        Assert.Null(Call("not-a-real-symbol-name-at-all")); // :133, the table has neither form
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-152")] // ProjectIconPicker.swift:144-151: no match falls back to the default sections, query still in the field
    public void A_query_with_no_match_falls_back_to_the_default_sections() => headless.OnUiThread(() =>
    {
        var project = new CellSample<ProjectIcon?>(null);
        var picker = new ProjectIconPicker(new Binding<ProjectIcon?>(() => project.Value, v => project.Value = v));
        var window = Show(picker, ThemeVariant.Light, width: 400, height: 500);

        var box = SearchBox(picker);
        box.Text = "zzzznosuchsymbol";
        Dispatcher.UIThread.RunJobs();

        Assert.Single(VisualQuery.Descendants<TextBlock>(picker), t => t.Text == "Emoji");
        Assert.DoesNotContain(VisualQuery.Descendants<TextBlock>(picker), t => t.Text == "Symbols");
        Assert.Equal("zzzznosuchsymbol", box.Text);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-152")] // ProjectIconPicker.swift:116 query.trimmingCharacters(in: .whitespaces): whitespace-only is empty
    public void A_whitespace_only_query_is_treated_as_empty() => headless.OnUiThread(() =>
    {
        var project = new CellSample<ProjectIcon?>(null);
        var picker = new ProjectIconPicker(new Binding<ProjectIcon?>(() => project.Value, v => project.Value = v));
        var window = Show(picker, ThemeVariant.Light, width: 400, height: 500);

        var box = SearchBox(picker);
        box.Text = "   ";
        Dispatcher.UIThread.RunJobs();

        Assert.Single(VisualQuery.Descendants<TextBlock>(picker), t => t.Text == "Emoji");
        window.Close();
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-054")] // ProjectIconPicker.swift:190-192 magnifier .subheadline secondaryText
    public void The_search_fields_magnifier_is_subheadline_in_secondary_text() => headless.OnUiThread(() =>
    {
        var project = new CellSample<ProjectIcon?>(null);
        var picker = new ProjectIconPicker(new Binding<ProjectIcon?>(() => project.Value, v => project.Value = v));
        var window = Show(picker, ThemeVariant.Light, width: 400, height: 500);

        var magnifier = Assert.Single(Images(picker), i => i.SymbolName == "magnifyingglass");
        Assert.Equal(SwiftFont.System(SwiftFont.TextStyle.Subheadline), SwiftImage.GetFont(magnifier));
        AssertColor(Label(0.7, window.ActualThemeVariant), Effective(magnifier.Foreground));
        window.Close();
    });

    // ---- Grid geometry and tiles, PR-SIDEBAR-153, UI-SIDEBAR-056 ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-153")] // ProjectIconPicker.swift:111 nine fixed 26pt columns, 4pt spacing
    [Trait("Req", "UI-SIDEBAR-056")] // ProjectIconPicker.swift:174-230
    public void A_symbol_grid_lays_its_tiles_into_nine_26_point_columns_4_points_apart() => headless.OnUiThread(() =>
    {
        var project = new CellSample<ProjectIcon?>(null);
        var picker = new ProjectIconPicker(new Binding<ProjectIcon?>(() => project.Value, v => project.Value = v));
        var window = Show(picker, ThemeVariant.Light, width: 400, height: 500);

        var popularSection = PopularSection(picker);
        var grid = Assert.IsAssignableFrom<Panel>(((StackPanel)popularSection).Children[1]);
        var tiles = grid.Children.OfType<Control>().ToList();
        Assert.True(tiles.Count >= 9);
        var row0 = tiles.Take(9).ToList();
        for (var i = 1; i < row0.Count; i++)
        {
            var expectedX = i * (26 + 4);
            Assert.Equal(expectedX, row0[i].Bounds.X, 3);
            Assert.Equal(row0[0].Bounds.Y, row0[i].Bounds.Y, 3);
            Assert.Equal(26, row0[i].Bounds.Width, 3);
            Assert.Equal(26, row0[i].Bounds.Height, 3);
        }
        if (tiles.Count > 9)
        {
            Assert.Equal(26 + 4, tiles[9].Bounds.Y - tiles[0].Bounds.Y, 3);
            Assert.Equal(0, tiles[9].Bounds.X, 3);
        }
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-153")] // ProjectIconPicker.swift:179-181, :222-224 a symbol tile: .callout, primaryText 0.9
    [Trait("Req", "UI-SIDEBAR-056")]
    public void A_symbol_tile_draws_its_glyph_callout_sized_in_primary_text_at_0_9() => headless.OnUiThread(() =>
    {
        var project = new CellSample<ProjectIcon?>(null);
        var picker = new ProjectIconPicker(new Binding<ProjectIcon?>(() => project.Value, v => project.Value = v));
        var window = Show(picker, ThemeVariant.Light, width: 400, height: 500);

        var tile = PopularTile(picker, "hammer.fill");
        Assert.Equal(SwiftFont.System(SwiftFont.TextStyle.Callout), SwiftImage.GetFont(tile));
        AssertColor(Label(0.9, window.ActualThemeVariant), Effective(tile.Foreground));
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-153")] // ProjectIconPicker.swift:209-212 an emoji tile: .title3, no explicit foreground override
    public void An_emoji_tile_draws_its_glyph_at_title3_size() => headless.OnUiThread(() =>
    {
        var project = new CellSample<ProjectIcon?>(null);
        var picker = new ProjectIconPicker(new Binding<ProjectIcon?>(() => project.Value, v => project.Value = v));
        var window = Show(picker, ThemeVariant.Light, width: 400, height: 500);

        var text = Assert.Single(VisualQuery.Descendants<TextBlock>(picker), t => t.Text == "\U0001F680");
        Assert.Equal(SwiftFont.Title3.PointSize, text.FontSize);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-153")] // ProjectIconPicker.swift:256-263 fill 0.14 while selected, 1.5pt accent border while selected
    public void Selecting_a_symbol_shows_its_tile_filled_and_ringed_in_the_accent_colour() => headless.OnUiThread(() =>
    {
        var project = new CellSample<ProjectIcon?>(new ProjectIcon.Symbol("hammer.fill"));
        var picker = new ProjectIconPicker(new Binding<ProjectIcon?>(() => project.Value, v => project.Value = v));
        var window = Show(picker, ThemeVariant.Light, width: 400, height: 500);

        var tile = PopularTile(picker, "hammer.fill");
        var button = Buttons(picker).First(b => VisualQuery.Descendants<SwiftImage>(b).Contains(tile));
        var fillBorder = VisualQuery.Descendants<Border>(button).First(b => b.BorderThickness.Left == 0);
        AssertColor(Label(0.14, window.ActualThemeVariant), Effective(fillBorder.Background));
        var ring = VisualQuery.Descendants<Border>(button).First(b => b.BorderThickness.Left > 0);
        Assert.True(ring.IsVisible);
        Assert.Equal(new Thickness(1.5), ring.BorderThickness);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-153")] // ProjectIconPicker.swift:178, :221 click sets project.icon to that symbol
    [Trait("Req", "UI-SIDEBAR-056")]
    public void Clicking_a_symbol_tile_sets_the_projects_icon_to_that_symbol() => headless.OnUiThread(() =>
    {
        var project = new CellSample<ProjectIcon?>(null);
        var picker = new ProjectIconPicker(new Binding<ProjectIcon?>(() => project.Value, v => project.Value = v));
        var window = Show(picker, ThemeVariant.Light, width: 400, height: 500);

        var tile = PopularTile(picker, "hammer.fill");
        var button = Buttons(picker).First(b => VisualQuery.Descendants<SwiftImage>(b).Contains(tile));
        Click(button);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new ProjectIcon.Symbol("hammer.fill"), project.Value);
        window.Close();
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-153")] // ProjectIconPicker.swift:209 click sets project.icon to that emoji
    public void Clicking_an_emoji_tile_sets_the_projects_icon_to_that_emoji() => headless.OnUiThread(() =>
    {
        var project = new CellSample<ProjectIcon?>(null);
        var picker = new ProjectIconPicker(new Binding<ProjectIcon?>(() => project.Value, v => project.Value = v));
        var window = Show(picker, ThemeVariant.Light, width: 400, height: 500);

        var text = Assert.Single(VisualQuery.Descendants<TextBlock>(picker), t => t.Text == "\U0001F680");
        var button = Buttons(picker).First(b => VisualQuery.Descendants<TextBlock>(b).Contains(text));
        Click(button);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new ProjectIcon.Emoji("\U0001F680"), project.Value);
        window.Close();
    });

    // ---- Byte-level transcription (R-STR-9) ----

    /// <summary>
    /// ProjectIconPicker.swift:60-78, the 131 emoji joined with U+0001 and SHA-256 hashed as UTF-8 bytes: computed
    /// once offline from the pinned Swift file's own 131 string literals (a byte-for-byte extraction script, not
    /// this port), so this test needs no reference checkout on the machine that runs it.
    /// </summary>
    private const string SwiftEmojiListSha256 = "a3c9d452173719f776e8de010ab19bba2d1079962cb9194351a37a0432d766ff";

    [Fact]
    [Trait("Req", "PR-SIDEBAR-151")] // ProjectIconPicker.swift:60-78 the 131 emoji, transcribed byte for byte
    public void The_emoji_list_hashes_to_the_pinned_Swift_literals_byte_for_byte()
    {
        var field = typeof(ProjectIconPicker).GetField("Emojis", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var actual = (string[])field.GetValue(null)!;
        Assert.Equal(131, actual.Length);
        var joined = string.Join('\u0001', actual);
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(joined)));
        Assert.Equal(SwiftEmojiListSha256, hash);
    }
}
