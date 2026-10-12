using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

[Collection("Headless")]
public sealed class SettingsRestAboutTests(HeadlessSession headless) : SettingsRestFixture(headless)
{
    // SettingsView.swift:953-956,997-1002; OD-0012 H, OD-0072, ED-272: the terminal is Marrow's own, built on Windows ConPTY (the Terminal credit row has no link).
    // Resolved STOP opencode-SettingsView-rest #2: ED-272 settles the row; AboutCreditsTerminalTests proves it.
    // SettingsView.swift:1046-1056. RESOLVED (defects-priority): Marrow.App.csproj ships LICENSE, THIRD_PARTY_NOTICES.md and
    // TRADEMARK.md (Legal\ content beside the executable); see LicensesShippingTests. (It said: Marrow.App.csproj still includes only icon.png.)

    [Fact]
    [Trait("Req", "PR-UPDATE-082")] // App/Views/Settings/SettingsView.swift:968-1004; Chrome.swift:1012-1026,1082-1087
    [Trait("Req", "UI-UPDATE-023")] // App/Views/Settings/SettingsView.swift:991-995; OD-0012 H
    public void App_card_keeps_exact_order_dividers_fill_and_description()
    {
        With(() => new AboutSettingsPage(), (view, _, _, _) =>
        {
            var root = Assert.IsType<StackPanel>(((AboutSettingsPage)view).Child); Assert.Equal(20, root.Spacing);
            var card = Assert.IsType<ChromeCard>(root.Children[1]); var fill = Assert.IsType<Border>(card.Child);
            Assert.Equal(new CornerRadius(16), fill.CornerRadius); Ink(fill, fill.Background, Chrome.Overlay(0.03)); Assert.False(fill.ClipToBounds);
            var rows = Assert.IsType<StackPanel>(Assert.IsType<StackPanel>(fill.Child).Children[0]); Assert.Equal(9, rows.Children.Count);
            Assert.IsType<StackPanel>(rows.Children[0]); Assert.Equal("Welcome tour", Text(Assert.IsType<ChromeRow>(rows.Children[2])).First()); Assert.IsType<AboutUpdateCheckRow>(rows.Children[4]);
            foreach (var index in new[] { 1, 3, 5, 7 }) { var divider = Assert.IsType<ChromeRowDivider>(rows.Children[index]); Assert.Equal(new Thickness(16, 0, 0, 0), divider.Margin); }
            var description = Assert.IsType<TextBlock>(rows.Children[6]); Assert.Equal("A native home for your coding agents, built in C# with Avalonia.", description.Text); Font(description, 13); Assert.Equal(new Thickness(16, 11, 10, 11), description.Margin); Assert.Equal(TextWrapping.Wrap, description.TextWrapping);
            var licence = Assert.IsType<TextBlock>(rows.Children[8]); Assert.Equal("nth is licensed under the GNU AGPL v3.0 with attribution terms; the terminal is nth's own, built on Windows ConPTY.", licence.Text);
        });
    }

    [Fact]
    [Trait("Req", "UI-UPDATE-025")] // App/Views/Settings/SettingsView.swift:946-964; Chrome.swift:1037-1052
    [Trait("Req", "PR-UPDATE-085")] // the section header clause: Credits, .body semibold, secondary, 10 above the card (SettingsView.swift:946-947; Chrome.swift:1037-1052)
    public void Credits_heading_has_exact_type_colour_spacing_and_no_accessories()
    {
        With(() => new AboutSettingsPage(), (view, _, _, _) =>
        {
            var root = Assert.IsType<StackPanel>(((AboutSettingsPage)view).Child); Assert.Equal(20, root.Spacing);
            var section = Assert.IsType<ChromeSection>(root.Children[2]); var stack = Assert.IsType<StackPanel>(section.Child); Assert.Equal(10, stack.Spacing); Assert.Equal(2, stack.Children.Count);
            var header = Assert.IsAssignableFrom<StackPanel>(stack.Children[0]); Assert.Equal(4, header.Spacing); var text = Assert.IsType<TextBlock>(Assert.Single(header.Children)); Assert.Equal("Credits", text.Text); Font(text, Chrome.SettingsSectionTitleSize, FontWeight.SemiBold); Ink(text, text.Foreground, Chrome.SecondaryText);
            Assert.IsType<ChromeCard>(stack.Children[1]); Assert.Empty(Nodes<Button>(header));
        });
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-059")] // App/Views/Settings/SettingsView.swift:1066 HStack(spacing: 12), centre alignment by default
    public void Attribution_row_centres_the_credit_line_and_the_link_on_the_row()
    {
        With(() => new MadeByDroppyRow(SwiftURL.Create("https://getdroppy.app")!), (view, _, h, w) =>
        {
            var grid = Assert.IsType<Grid>(Assert.Single(Nodes<Button>(view)).Content);
            var centre = grid.Bounds.Height / 2;
            // OD-0135: the row has no logo any more; each child sits at its own height on the row's centre line, not at the top of the row.
            foreach (var child in new Control[] { grid.Children[0], grid.Children[1] })
            {
                Assert.Equal(child.DesiredSize.Height - child.Margin.Top - child.Margin.Bottom, child.Bounds.Height, 0.5);
                Assert.Equal(centre, child.Bounds.Center.Y, 0.5);
            }
            Assert.DoesNotContain(Nodes<SwiftImage>(view), image => image.AssetName is not null); // no logo
        }, height: 220);
    }

    [Fact]
    [Trait("Req", "UI-SETTINGS-059")] // App/Views/Settings/SettingsView.swift:1060-1102; OD-0012 F3
    [Trait("Req", "UI-UPDATE-026")] // App/Views/Settings/SettingsView.swift:1060-1102
    [Trait("Req", "UI-UPDATE-027")] // App/Views/Settings/SettingsView.swift:1081-1096
    [Trait("Req", "PR-SETTINGS-248")] // the Made by Droppy row clauses (SettingsView.swift:1060-1102; OD-0012 F3 the subtitle); the Terminal row is AboutCreditsTerminalTests (ED-272)
    public void Attribution_whole_row_click_and_hover_match_all_visual_clauses()
    {
        With(() => new MadeByDroppyRow(SwiftURL.Create("https://getdroppy.app")!), (view, _, h, w) =>
        {
            var button = Assert.Single(Nodes<Button>(view)); var grid = Assert.IsType<Grid>(button.Content); Assert.Equal(12, grid.ColumnSpacing); Assert.Equal(new Thickness(16, 12, 14, 12), grid.Margin);
            Assert.Equal(2, grid.Children.Count); Assert.DoesNotContain(Nodes<SwiftImage>(view), image => image.AssetName is not null); /* OD-0135: no Droppy logo */ Assert.DoesNotContain(Text(view), line => line.Contains("Made by Droppy", StringComparison.Ordinal));
            var labels = Assert.IsType<StackPanel>(grid.Children[0]); Assert.Equal(2, labels.Spacing); var subtitle = Assert.IsType<TextBlock>(Assert.Single(labels.Children));
            Assert.Equal("Based on Droppy Code by Jordy Spruit (Droppy), https://getdroppycode.app", subtitle.Text); Font(subtitle, 11); Ink(subtitle, subtitle.Foreground, Chrome.SecondaryText);
            var trailing = Assert.IsType<StackPanel>(grid.Children[1]); Assert.Equal(4, trailing.Spacing); var url = Assert.IsType<TextBlock>(trailing.Children[0]); Assert.Equal("getdroppy.app", url.Text); Font(url, 12, FontWeight.Medium); Ink(url, url.Foreground, Chrome.Accent);
            var arrow = Assert.IsType<SwiftImage>(trailing.Children[1]); Assert.Equal("arrow.up.right", arrow.SymbolName); Assert.Equal(SwiftFont.System(10, SwiftFont.Weight.Semibold), SwiftImage.GetFont(arrow)); Ink(arrow, arrow.Foreground, Chrome.Accent);
            Assert.Equal("Open getdroppy.app", ToolTip.GetTip(button)); Assert.Equal("Based on Droppy Code by Jordy Spruit (Droppy). Open getdroppy.app", AutomationProperties.GetName(button)); Assert.Equal(0.9, trailing.Opacity);
            var middle = new Point(grid.Bounds.Width / 2, grid.Bounds.Height / 2); Hover(w, grid, middle);
            Assert.Equal(0.9, trailing.Opacity); Tick(h, 50); Assert.Equal(0.9 + 0.1 * Animation.EaseOut(0.1).Progress(0.05), trailing.Opacity, 5); Tick(h, 50); Assert.Equal(1, trailing.Opacity);
            SwiftURL? opened = null; using var browser = Marrow.Services.Port.Workspace.Open.Override(u => { opened = u; return true; }); Click(w, grid, middle); Assert.Equal("https://getdroppy.app", opened?.AbsoluteString);
            w.MouseMove(new Point(1, 1)); Drain(w); Tick(h, 50); Assert.Equal(1 - 0.1 * Animation.EaseOut(0.1).Progress(0.05), trailing.Opacity, 5); Tick(h, 50); Assert.Equal(0.9, trailing.Opacity);
        }, height: 220);
    }

    [Fact]
    [Trait("Req", "UI-UPDATE-024")] // SettingsView.swift:953-956; OD-0012 H, OD-0072
    public void Terminal_credit_row_names_the_terminal_as_Marrows_own_without_a_link()
    {
        With(() => new AboutSettingsPage(), (view, _, _, _) =>
        {
            var row = Assert.Single(Nodes<ChromeRow>(view), r => Text(r).Contains("nth's own terminal, built on Windows ConPTY."));
            CheckRow(row, "Terminal", "nth's own terminal, built on Windows ConPTY."); Assert.Empty(Nodes<CreditLink>(row));
        });
    }

    [Fact]
    [Trait("Req", "UI-UPDATE-028")] // App/Views/Settings/SettingsView.swift:950-952,1104-1121
    [Trait("Req", "UI-UPDATE-030")] // App/Views/Settings/SettingsView.swift:958-960,1104-1121
    [Trait("Req", "UI-SETTINGS-072")] // the two links the Windows build keeps (zeronsh/zeron, rampatra/TourKit; the Terminal row has none, ED-272): title, arrow, tooltip, click, no hover state of their own (SettingsView.swift:1104-1121)
    [Trait("Req", "PR-SETTINGS-248")] // the links clause: each shows its title at 12pt medium with an up-right arrow at 10pt semibold in the accent colour and carries its full URL as its tooltip
    public void Repository_credit_rows_match_exact_labels_fonts_colours_tooltips_and_real_clicks()
    {
        With(() => new AboutSettingsPage(), (view, _, h, w) =>
        {
            foreach (var (title, detail, label, address) in new[] { ("Working indicators", "Ported from Zeron by Wing, MIT License.", "zeronsh/zeron", "https://github.com/zeronsh/zeron"), ("Welcome tour", "Ported from TourKit by Ram Patra, MIT License.", "rampatra/TourKit", "https://github.com/rampatra/TourKit") })
            {
                var row = Nodes<ChromeRow>(view).Single(r => Text(r).Contains(detail)); CheckRow(row, title, detail); var credit = Assert.Single(Nodes<CreditLink>(row)); var button = Assert.Single(Nodes<Button>(credit)); Assert.True(button.IsEffectivelyEnabled);
                var parts = Assert.IsType<StackPanel>(button.Content); Assert.Equal(4, parts.Spacing); var text = Assert.IsType<TextBlock>(parts.Children[0]); Assert.Equal(label, text.Text); Font(text, 12, FontWeight.Medium); Ink(text, text.Foreground, Chrome.Accent);
                var arrow = Assert.IsType<SwiftImage>(parts.Children[1]); Assert.Equal("arrow.up.right", arrow.SymbolName); Assert.Equal(SwiftFont.System(10, SwiftFont.Weight.Semibold), SwiftImage.GetFont(arrow)); Ink(arrow, arrow.Foreground, Chrome.Accent); Assert.Equal(address, ToolTip.GetTip(button));
                Hover(w, button); Tick(h, 100); Assert.Equal(1, button.Opacity); Assert.Equal(1, parts.Opacity);
                string? opened = null; using var browser = Marrow.Services.Port.Workspace.Open.Override(u => { opened = u.AbsoluteString; return true; }); Click(w, button); Assert.Equal(address, opened);
            }
        });
    }
}
