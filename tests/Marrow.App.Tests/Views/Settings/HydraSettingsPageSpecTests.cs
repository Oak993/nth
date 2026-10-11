using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.HydraSettingsPageRig;

namespace Marrow.App.Tests.Views.Settings;

// Swift-derived tests (test-first) for DroppyCode/App/Views/Settings/HydraSettingsPage.swift (B3 52bc792): the page itself.
// The pair row, the editor and the cookbook button are in HydraSettingsPagePairSpecTests.
[Collection("Headless")]
public sealed class HydraSettingsPageSpecTests(HeadlessSession headless)
{
    private void Page(Action<HydraSettingsScene> check, double width = 900, Action<AppModel>? arrange = null) =>
        Run(headless, _ => new HydraSettingsPage(), check, width, arrange);

    private static bool SwitchIsOn(ChromeRow row)
    {
        var inner = Of<SettingsSwitch>(row).Single().Child!;
        return Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(inner)).ToggleState == ToggleState.On;
    }

    private static void Flip(ChromeRow row)
    {
        var inner = Of<SettingsSwitch>(row).Single().Child!;
        Assert.IsAssignableFrom<IToggleProvider>(ControlAutomationPeer.CreatePeerForElement(inner)).Toggle();
        Sync();
    }

    // A toggle row's whole contract: the exact title and detail, the default the switch shows, and a flip writing the setting.
    private void ToggleRowContract(string title, string detail, bool expectedDefault, Func<AppSettings, bool> read)
    {
        Page(scene =>
        {
            var row = Row(scene.View, title);
            Assert.Equal(detail, Detail(row));
            Assert.Equal(expectedDefault, read(scene.Model.Settings));
            Assert.Equal(expectedDefault, SwitchIsOn(row));
            Flip(row);
            Assert.Equal(!expectedDefault, read(scene.Model.Settings));
            row = Row(scene.View, title);
            Assert.Equal(!expectedDefault, SwitchIsOn(row));
            Flip(row);
            Assert.Equal(expectedDefault, read(scene.Model.Settings));
        });
    }

    // ---- PR-SETTINGS-147 -------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-SETTINGS-147")] // HydraSettingsPage.swift:24 InfoHoverButton(help: "How Hydra works") { HydraInfoPopover(topic: .hydra) }
    public void The_master_cards_info_mark_is_How_Hydra_works_and_opens_the_Hydra_explainer() => Page(scene =>
    {
        var mark = Of<InfoHoverButton>(scene.View).First();
        Assert.Contains(mark, Of<InfoHoverButton>(Of<ChromeCard>(scene.View).First()));
        Assert.Single(Of<SettingsSwitch>(Of<ChromeCard>(scene.View).First()));
        AssertMark(scene, mark, "How Hydra works", "The helpers a chat sends out, one brief each.");
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-147")] // HydraSettingsPage.swift:48 ChromeSection(title: "Heads", accessory: InfoHoverButton(help: "How heads work") { HydraInfoPopover(topic: .heads) })
    public void The_Heads_section_headers_info_mark_is_How_heads_work_and_opens_the_heads_explainer() => Page(scene =>
    {
        var heads = Of<ChromeSection>(scene.View).First(section => Of<TextBlock>(section).First().Text == "Heads");
        var mark = Of<InfoHoverButton>(heads).Single();
        Assert.Equal(2, Of<InfoHoverButton>(scene.View).Count());
        AssertMark(scene, mark, "How heads work", "The helpers a chat sends out, one brief each.");
    });

    private static void AssertMark(HydraSettingsScene scene, InfoHoverButton mark, string help, string headsSubtitle)
    {
        var image = Of<SwiftImage>(mark).Single(candidate => candidate.SymbolName == "info.circle");
        Assert.Equal(help, ToolTip.GetTip(image));
        Assert.Equal(help, AutomationProperties.GetName(image));
        Assert.Equal(20, image.Width);
        Assert.Equal(20, image.Height);
        Assert.Equal(360, mark.PopoverWidth);
        var button = Of<Button>(mark).First();
        Assert.False(button.Focusable);
        Assert.False(Avalonia.Input.KeyboardNavigation.GetIsTabStop(button));

        Assert.False(mark.IsPopoverPresented);
        Click(button);
        Assert.True(mark.IsPopoverPresented);
        var popup = LastPresented(scene);
        var body = Assert.IsType<Border>(PopoverHost.Content(popup)); // the site's view, not the card the presenter hangs it on (OD-0095)
        Assert.Equal(360, body.Width);
        var topic = Of<HydraInfoPopover>(body).Single();
        var text = Text(topic);
        var isHydra = help == "How Hydra works";
        Assert.Contains(isHydra ? "One chat leads a team of heads on big jobs." : headsSubtitle, text, StringComparison.Ordinal);
        Assert.DoesNotContain(isHydra ? headsSubtitle : "One chat leads a team of heads on big jobs.", text, StringComparison.Ordinal);

        Click(button);
        Assert.False(mark.IsPopoverPresented);
    }

    // ---- PR-SETTINGS-151, UI-SETTINGS-031 -------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-SETTINGS-151")] // HydraSettingsPage.swift:61-70 title and the two detail sentences, nine segments
    public void Heads_at_once_is_nine_segments_and_its_detail_is_not_pluralised() => Page(scene =>
    {
        var row = Row(scene.View, "Heads at once");
        Assert.Equal("As many heads as the job needs.", Detail(row));
        var picker = Of<ChromeSegmentedPicker<long?>>(row).Single();
        var buttons = Of<Button>(picker).ToArray();
        Assert.Equal(new[] { "No cap", "1", "2", "3", "4", "5", "6", "7", "8" }, buttons.Select(AutomationProperties.GetName).ToArray());
        // :66 "No cap" draws the infinity symbol in place of its words; the others draw their numbers.
        Assert.Equal("infinity", Of<SwiftImage>(buttons[0]).Single().SymbolName);
        Assert.Empty(Of<TextBlock>(buttons[0]));
        for (var i = 1; i < buttons.Length; i++) Assert.Equal(i.ToString(System.Globalization.CultureInfo.InvariantCulture), Of<TextBlock>(buttons[i]).Single().Text);

        for (var cap = 1; cap <= 8; cap++)
        {
            Click(Of<Button>(Of<ChromeSegmentedPicker<long?>>(Row(scene.View, "Heads at once")).Single()).ElementAt(cap));
            Assert.Equal(cap, scene.Model.Settings.HydraMaxHeads);
            Assert.Equal($"At most {cap} heads in parallel.", Detail(Row(scene.View, "Heads at once")));
        }
        Click(Of<Button>(Of<ChromeSegmentedPicker<long?>>(Row(scene.View, "Heads at once")).Single()).First());
        Assert.Null(scene.Model.Settings.HydraMaxHeads);
        Assert.Equal("As many heads as the job needs.", Detail(Row(scene.View, "Heads at once")));
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-031")] // HydraSettingsPage.swift:65-69 over ChromeSegmentedPicker.swift:29-43,47-82: one 28 point capsule, segments 22 high, 26 wide at least, 9 padded
    public void Heads_at_once_segments_have_the_sources_geometry() => Page(scene =>
    {
        var picker = Of<ChromeSegmentedPicker<long?>>(Row(scene.View, "Heads at once")).Single();
        var strip = Of<StackPanel>(picker).Single(stack => stack.Orientation == Orientation.Horizontal);
        Assert.Equal(2, strip.Spacing);
        Assert.Equal(9, strip.Children.Count);
        var frame = Of<Border>(picker).First(border => ReferenceEquals(border.Child, strip));
        Assert.Equal(28, frame.Height);
        Assert.Equal(new Thickness(3, 0), frame.Padding);
        var faces = Of<Border>(picker).Where(border => border.Height == 22).ToArray();
        Assert.Equal(9, faces.Length);
        foreach (var face in faces)
        {
            Assert.Equal(26, face.MinWidth);
            Assert.Equal(new Thickness(9, 0), face.Padding);
        }
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-031")] // ChromeSegmentedPicker.swift:57-63 selected: semibold primary text on accent at 0.22; the rest medium secondary text on nothing
    public void Heads_at_once_selected_segment_is_semibold_on_an_accent_capsule_and_the_rest_are_plain() => Page(scene =>
    {
        scene.Model.Settings.HydraMaxHeads = 3;
        Sync();
        var picker = Of<ChromeSegmentedPicker<long?>>(Row(scene.View, "Heads at once")).Single();
        var theme = picker.ActualThemeVariant;
        var labels = Of<TextBlock>(picker).ToArray();
        Assert.Equal(8, labels.Length);
        foreach (var label in labels)
        {
            var selected = label.Text == "3";
            Assert.Equal(selected ? FontWeight.SemiBold : FontWeight.Medium, label.FontWeight);
            Assert.Equal(12, label.FontSize); // .callout
            var expected = (selected ? Chrome.PrimaryText : Chrome.SecondaryText).Resolve(theme);
            Assert.Equal(expected, Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color);
            var capsule = Of<Border>(picker).First(border => border.CornerRadius == new CornerRadius(11) && Of<TextBlock>(border).Contains(label));
            var fill = Assert.IsAssignableFrom<ISolidColorBrush>(capsule.Background).Color;
            var want = selected ? Chrome.Accent.Opacity(0.22).Resolve(theme) : Chrome.Overlay(0).Resolve(theme);
            Assert.InRange(fill.A, want.A - 1, want.A + 1);
        }
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-031")] // ChromeSegmentedPicker.swift:63 hovering a plain segment fills it with Chrome.overlay(0.08)
    public void Heads_at_once_hovered_segment_takes_the_eight_percent_overlay() => Page(scene =>
    {
        var picker = Of<ChromeSegmentedPicker<long?>>(Row(scene.View, "Heads at once")).Single();
        var segment = Of<Button>(picker).ElementAt(5);
        var capsule = Of<Border>(segment).First(border => border.CornerRadius == new CornerRadius(11));
        var theme = picker.ActualThemeVariant;
        var idle = Assert.IsAssignableFrom<ISolidColorBrush>(capsule.Background).Color;
        Assert.InRange(idle.A, Chrome.Overlay(0).Resolve(theme).A - 1, Chrome.Overlay(0).Resolve(theme).A + 1);
        var centre = segment.TranslatePoint(new Point(segment.Bounds.Width / 2, segment.Bounds.Height / 2), scene.Window)!.Value;
        scene.Window.MouseMove(centre);
        Sync();
        // The fill animates over Chrome.hover; its target is the 8% overlay.
        Advance(1);
        var hovered = Assert.IsAssignableFrom<ISolidColorBrush>(capsule.Background).Color;
        Assert.InRange(hovered.A, Chrome.Overlay(0.08).Resolve(theme).A - 1, Chrome.Overlay(0.08).Resolve(theme).A + 1);
        scene.Window.MouseMove(new Point(1, 1));
        Sync();
    });

    // ---- PR-SETTINGS-152, 153, 156 (the toggle rows) ---------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-SETTINGS-152")] // HydraSettingsPage.swift:72
    public void Clear_finished_heads_is_a_switch_off_by_default_with_its_exact_detail() =>
        ToggleRowContract("Clear finished heads", "A finished head moves from the panel to the sidebar.", false, s => s.HydraAutoClearFinished);

    [Fact]
    [Trait("Req", "PR-SETTINGS-153")] // HydraSettingsPage.swift:74
    public void Auto_hide_heads_in_the_sidebar_is_a_switch_off_by_default_with_its_exact_detail() =>
        ToggleRowContract("Auto-hide heads in the sidebar",
            "A chat's heads show in the sidebar while one of them runs; the whole card leaves once every head of that chat has finished, and comes back on its own when one starts again.",
            false, s => s.HydraAutoHidesIdleHeads);

    [Fact]
    [Trait("Req", "PR-SETTINGS-156")] // HydraSettingsPage.swift:91
    public void Heads_work_at_a_working_effort_is_a_switch_on_by_default_with_its_exact_detail() =>
        ToggleRowContract("Heads work at a working effort",
            "A lead thinking above medium sends its heads out at medium on the same model — on a scale without a medium (Z.ai), at the scale's own middle rung; a pair that names the heads' effort keeps it.",
            true, s => s.HydraTempersHeadEffort);

    [Fact]
    [Trait("Req", "PR-SETTINGS-156")] // HydraSettingsPage.swift:91 the U+2014 em dash, one on each side a single space, the (Z.ai) parenthetical, the semicolon and the final full stop
    public void The_working_effort_detail_holds_one_em_dash_between_single_spaces() => Page(scene =>
    {
        var detail = Detail(Row(scene.View, "Heads work at a working effort"))!;
        Assert.Equal(1, detail.Count(c => c == '—'));
        var at = detail.IndexOf('—', StringComparison.Ordinal);
        Assert.Equal(' ', detail[at - 1]);
        Assert.Equal(' ', detail[at + 1]);
        Assert.Equal("A lead thinking above medium sends its heads out at medium on the same model", detail[..(at - 1)]);
        Assert.Contains("(Z.ai), at the scale's own middle rung; a pair that names the heads' effort keeps it.", detail, StringComparison.Ordinal);
        Assert.EndsWith(".", detail, StringComparison.Ordinal);
    });

    // ---- PR-SETTINGS-154, 155 (the visual pickers) -----------------------------------------------------------------

    private static ChromeVisualTile[] Tiles(ChromeRow row) => Of<ChromeVisualTile>(row).ToArray();

    private static string[] TileSymbols(ChromeVisualTile[] tiles) =>
        tiles.Select(tile => Of<SwiftImage>(tile).First().SymbolName!).ToArray();

    [Fact]
    [Trait("Req", "PR-SETTINGS-154")] // HydraSettingsPage.swift:76-80 false-first order, fixed detail
    [Trait("Req", "UI-SETTINGS-065")] // HydraSettingsPage.swift:76-80 over ChromeVisualPicker.swift:10-105: the Head panel picker, its detail fixed (:76)
    public void Head_panel_lists_Progress_before_Every_step_and_keeps_one_detail() => Page(scene =>
    {
        var row = Row(scene.View, "Head panel");
        Assert.Equal("The head's progress bar, or each of its steps as it goes.", Detail(row));
        var tiles = Tiles(row);
        Assert.Equal(new[] { "Progress", "Every step" }, tiles.Select(tile => AutomationProperties.GetName(tile)).ToArray());
        Assert.Equal(new[] { "chart.bar", "list.number" }, TileSymbols(tiles));
        Assert.False(scene.Model.Settings.HydraShowsHeadDetails);
        Assert.True(tiles[0].IsSelected);
        Assert.False(tiles[1].IsSelected);

        Click(Of<Button>(tiles[1]).First());
        Assert.True(scene.Model.Settings.HydraShowsHeadDetails);
        row = Row(scene.View, "Head panel");
        Assert.Equal("The head's progress bar, or each of its steps as it goes.", Detail(row));
        Assert.True(Tiles(row)[1].IsSelected);
        Click(Of<Button>(Tiles(row)[0]).First());
        Assert.False(scene.Model.Settings.HydraShowsHeadDetails);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-155")] // HydraSettingsPage.swift:82-89 One panel / A panel each and the dependent detail
    [Trait("Req", "UI-SETTINGS-065")] // HydraSettingsPage.swift:82-89 over ChromeVisualPicker.swift:10-105: the Heads' panels picker, its detail follows the choice
    public void Heads_panels_lists_One_panel_before_A_panel_each_and_its_detail_follows_the_choice() => Page(scene =>
    {
        var row = Row(scene.View, "Heads' panels");
        Assert.Equal("One panel over the chat lists every head.", Detail(row));
        var tiles = Tiles(row);
        Assert.Equal(new[] { "One panel", "A panel each" }, tiles.Select(tile => AutomationProperties.GetName(tile)).ToArray());
        Assert.Equal(new[] { "rectangle", "square.grid.2x2" }, TileSymbols(tiles));
        Assert.False(scene.Model.Settings.HydraAutoPopsHeads);

        Click(Of<Button>(tiles[1]).First());
        Assert.True(scene.Model.Settings.HydraAutoPopsHeads);
        Assert.Equal("Each head after the first gets a panel of its own beside the chat while there is room, left and right until each side is full; the usage panel keeps its spot.",
            Detail(Row(scene.View, "Heads' panels")));
        Click(Of<Button>(Tiles(Row(scene.View, "Heads' panels"))[0]).First());
        Assert.False(scene.Model.Settings.HydraAutoPopsHeads);
        Assert.Equal("One panel over the chat lists every head.", Detail(Row(scene.View, "Heads' panels")));
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-065")] // HydraSettingsPage.swift:50-59 Checkout: options [(true, "Own copy"), (false, "Shared")], doc.on.doc / folder, hydraIsolateHeads default true (AppSettings.swift:765), the detail follows the choice
    public void Checkout_lists_Own_copy_before_Shared_and_its_detail_follows_the_choice() => Page(scene =>
    {
        const string ownCopy = "Each head works in a copy of its own; its changes land in the chat's checkout when it reports.";
        const string shared = "Heads work in the chat's checkout itself.";
        var row = Row(scene.View, "Checkout");
        var tiles = Tiles(row);
        Assert.Equal(new[] { "Own copy", "Shared" }, tiles.Select(tile => AutomationProperties.GetName(tile)).ToArray());
        Assert.Equal(new[] { "doc.on.doc", "folder" }, TileSymbols(tiles));
        Assert.True(scene.Model.Settings.HydraIsolateHeads);
        Assert.True(tiles[0].IsSelected);
        Assert.False(tiles[1].IsSelected);
        Assert.Equal(ownCopy, Detail(row));

        Click(Of<Button>(tiles[1]).First());
        Assert.False(scene.Model.Settings.HydraIsolateHeads);
        row = Row(scene.View, "Checkout");
        Assert.Equal(shared, Detail(row));
        Assert.False(Tiles(row)[0].IsSelected);
        Assert.True(Tiles(row)[1].IsSelected);

        Click(Of<Button>(Tiles(row)[0]).First());
        Assert.True(scene.Model.Settings.HydraIsolateHeads);
        Assert.Equal(ownCopy, Detail(Row(scene.View, "Checkout")));
    });

    // ---- PR-SETTINGS-158, 159 (the two toggle sections) ------------------------------------------------------------

    private static ChromeSection Section(Control view, string title) =>
        Of<ChromeSection>(view).First(section => Of<TextBlock>(section).First().Text == title);

    private static void AssertTwoRows(ChromeSection section, string first, string firstDetail, string second, string secondDetail)
    {
        var rows = Of<ChromeRow>(section).ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Equal(first, Of<TextBlock>(rows[0]).First().Text);
        Assert.Equal(firstDetail, Detail(rows[0]));
        Assert.Equal(second, Of<TextBlock>(rows[1]).First().Text);
        Assert.Equal(secondDetail, Detail(rows[1]));
        Assert.Single(Of<ChromeRowDivider>(section));
        // the divider sits between the two rows
        var card = Of<StackPanel>(section).Single(stack => stack.Children.Count == 3);
        Assert.Same(rows[0], card.Children[0]);
        Assert.IsType<ChromeRowDivider>(card.Children[1]);
        Assert.Same(rows[1], card.Children[2]);
    }

    [Fact]
    [Trait("Req", "PR-SETTINGS-158")] // HydraSettingsPage.swift:94-100
    public void Messages_holds_the_two_toggles_in_order_with_one_divider() => Page(scene =>
    {
        AssertTwoRows(Section(scene.View, "Messages"),
            "Queued follow-ups start on heads", "A prompt queued behind a running turn goes out at once.",
            "Every message goes to a head", "The lead only hears the reports.");
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-158")] // HydraSettingsPage.swift:96,98 hydraQueueHeads on by default, hydraAlwaysHeads off by default; a flip writes the setting
    public void Messages_toggles_default_on_and_off_and_write_their_settings() => Page(scene =>
    {
        Assert.True(scene.Model.Settings.HydraQueueHeads);
        Assert.False(scene.Model.Settings.HydraAlwaysHeads);
        Assert.True(SwitchIsOn(Row(scene.View, "Queued follow-ups start on heads")));
        Assert.False(SwitchIsOn(Row(scene.View, "Every message goes to a head")));
        Flip(Row(scene.View, "Queued follow-ups start on heads"));
        Assert.False(scene.Model.Settings.HydraQueueHeads);
        Flip(Row(scene.View, "Every message goes to a head"));
        Assert.True(scene.Model.Settings.HydraAlwaysHeads);
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-159")] // HydraSettingsPage.swift:101-107
    public void When_the_team_is_done_holds_the_two_toggles_in_order_with_one_divider() => Page(scene =>
    {
        AssertTwoRows(Section(scene.View, "When the team is done"),
            "Lead checks the heads' work", "Reads every changed file and corrects it. Slower, more careful.",
            "Merge automatically", "The team's changes go out as a merge request and are merged.");
    });

    [Fact]
    [Trait("Req", "PR-SETTINGS-159")] // HydraSettingsPage.swift:103,105 both off by default; a flip writes the setting
    public void When_the_team_is_done_toggles_default_off_and_write_their_settings() => Page(scene =>
    {
        Assert.False(scene.Model.Settings.HydraReviewHeads);
        Assert.False(scene.Model.Settings.HydraAutoMerge);
        Assert.False(SwitchIsOn(Row(scene.View, "Lead checks the heads' work")));
        Assert.False(SwitchIsOn(Row(scene.View, "Merge automatically")));
        Flip(Row(scene.View, "Lead checks the heads' work"));
        Assert.True(scene.Model.Settings.HydraReviewHeads);
        Flip(Row(scene.View, "Merge automatically"));
        Assert.True(scene.Model.Settings.HydraAutoMerge);
    });

    // ---- UI-SETTINGS-029 -------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Req", "UI-SETTINGS-029")] // HydraSettingsPage.swift:11-37 five glyphs at 26 spaced -9, text block spacing 2, padding 16 / 10 / 14, one-line subtitle
    public void The_master_card_has_the_sources_geometry() => Page(scene =>
    {
        var card = Of<ChromeCard>(scene.View).First();
        var glyphs = Of<StackPanel>(card).Single(stack => stack.Children.Count == 5 && stack.Children.All(child => child is HydraGlyph));
        Assert.Equal(-9, glyphs.Spacing);
        Assert.All(glyphs.Children.Cast<HydraGlyph>(), glyph => Assert.Equal(26, glyph.Size));
        Assert.Equal(Enumerable.Range(0, 5).Select(index => HydraRoster.Persona((long)index)).ToArray(), glyphs.Children.Cast<HydraGlyph>().Select(glyph => glyph.Persona).ToArray());
        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(glyphs));

        var subtitle = Of<TextBlock>(card).Single(block => block.Text == "One chat leads a team of heads on big jobs.");
        Assert.Equal(0, subtitle.MaxLines); // ED-NEW-textsize-1: it wraps in a narrow window instead of being cut off
        var column = Assert.IsType<StackPanel>(subtitle.Parent);
        Assert.Equal(2, column.Spacing);
        Assert.Equal(2, column.Children.Count);

        var title = Of<TextBlock>(card).First(block => block.Text == "Hydra");
        Assert.Equal(FontWeight.SemiBold, title.FontWeight);
        Assert.Equal(15, title.FontSize); // .title3
        var theme = card.ActualThemeVariant;
        Assert.Equal(Chrome.PrimaryText.Resolve(theme), Assert.IsAssignableFrom<ISolidColorBrush>(title.Foreground).Color);
        Assert.Equal(Chrome.SecondaryText.Resolve(theme), Assert.IsAssignableFrom<ISolidColorBrush>(subtitle.Foreground).Color);
        Assert.Equal(11, subtitle.FontSize); // .subheadline
        var titleRow = Assert.IsType<StackPanel>(title.Parent);
        Assert.Equal(4, titleRow.Spacing);

        var row = Assert.IsType<Grid>(glyphs.Parent);
        Assert.Equal(new Thickness(16, 14, Chrome.RowControlTrailingPadding, 14), row.Margin);
        Assert.Equal(10, Chrome.RowControlTrailingPadding);
        var root = Assert.IsType<StackPanel>(card.Parent);
        Assert.Equal(Chrome.SectionHeaderSpacing, root.Spacing);
        Assert.Equal(10, root.Spacing);
    });

    [Fact]
    [Trait("Req", "UI-SETTINGS-029")] // HydraSettingsPage.swift:38-45 off: the note appears under the card with 4 points of horizontal padding and the sections dim to 0.5
    public void With_Hydra_off_the_note_sits_under_the_card_and_the_sections_dim() => Page(scene =>
    {
        scene.Model.Settings.HydraEnabled = false;
        Sync();
        var card = Of<ChromeCard>(scene.View).First();
        var root = Assert.IsType<StackPanel>(card.Parent);
        var note = Of<TextBlock>(scene.View).Single(block => block.Text == "Switch Hydra on to set up its heads.");
        Assert.Equal(new Thickness(4, 0), note.Margin);
        Assert.Equal(11, note.FontSize);
        Assert.Equal(Chrome.SecondaryText.Resolve(note.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(note.Foreground).Color);
        Assert.Same(note, root.Children[1]);
        var group = root.Children[2];
        Assert.False(group.IsEnabled);
        Assert.Equal(4, Of<ChromeSection>(group).Count(section => section.Parent is StackPanel stack && ReferenceEquals(stack, group)));
        Assert.Equal(5, Of<ChromeSection>(scene.View).Count());
    });
}
