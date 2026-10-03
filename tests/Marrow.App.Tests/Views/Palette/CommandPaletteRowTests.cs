using System;
using System.Linq;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Palette;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using static Marrow.App.Tests.Fixtures.CommandPaletteRig;
using AvaloniaWindow = Avalonia.Controls.Window;
using ChromeColors = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Tests.Views.Palette;

// Source: DroppyCode/App/Views/Palette/CommandPalette.swift (B3 52bc792), :77-94 and :110-147 (the palette result row).
// UI-WIN-032 is proven by the five tests below together with the existing PR-WIN-167/168/169/170/175/176 tests in
// CommandPaletteTests (list insets and spacing, truncation, the chord at the trailing edge, hover, click, subtitles).
[Collection("Headless")]
public sealed class CommandPaletteRowTests(HeadlessSession headless)
{
    private static Color Fill(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    [Fact]
    [Trait("Req", "UI-WIN-032")] // :117, :141: selected fill is the accent at 0.16, unselected is clear, symbol accent versus secondary, radius 12; roles swap with the arrow keys
    [Trait("Rule", "R-TEST-4")] // partial proof of UI-WIN-032: colours and radius; layout, project mark, accessibility and click are the tests below
    public void A_selected_row_is_tinted_accent_at_0_16_and_an_unselected_one_is_clear_with_a_secondary_symbol()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            AvaloniaWindow window = null!;
            CommandPalette palette = null!;
            harness.Main.OnMain(() =>
            {
                (_, window, palette) = Build(model);
                var theme = window.ActualThemeVariant;
                var first = RowParts(palette.RowsPanel.Children[0]);
                var second = RowParts(palette.RowsPanel.Children[1]);

                Assert.Equal(ChromeColors.Accent.Opacity(0.16).Resolve(theme), Fill(first.Background.Background));
                Assert.Equal(0, Fill(second.Background.Background).A); // Color.clear
                Assert.Equal(new CornerRadius(12), first.Background.CornerRadius);
                Assert.Equal(new CornerRadius(12), second.Background.CornerRadius);
                Assert.Equal(ChromeColors.Accent.Resolve(theme), Fill(Assert.IsType<SwiftImage>(Assert.Single(first.Icon.Children)).Foreground));
                Assert.Equal(SwiftColor.Secondary.Resolve(theme), Fill(Assert.IsType<SwiftImage>(Assert.Single(second.Icon.Children)).Foreground));
                Assert.Equal(ChromeColors.SecondaryText.Resolve(theme), Fill(first.Chord.Foreground)); // chord: caption, secondary text

                // The roles swap when the selection moves down one row.
                Press(window, Key.Down, PhysicalKey.ArrowDown);
            });
            harness.Main.OnMain(() =>
            {
                Settle(window);
                var theme = window.ActualThemeVariant;
                var first = RowParts(palette.RowsPanel.Children[0]);
                var second = RowParts(palette.RowsPanel.Children[1]);
                Assert.Equal(0, Fill(first.Background.Background).A);
                Assert.Equal(ChromeColors.Accent.Opacity(0.16).Resolve(theme), Fill(second.Background.Background));
                Assert.Equal(SwiftColor.Secondary.Resolve(theme), Fill(Assert.IsType<SwiftImage>(Assert.Single(first.Icon.Children)).Foreground));
                Assert.Equal(ChromeColors.Accent.Resolve(theme), Fill(Assert.IsType<SwiftImage>(Assert.Single(second.Icon.Children)).Foreground));
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-WIN-032")] // :111-137, :139-140: padding 12/8, an 18-point icon column, a 12-point gap, a 1-point title/subtitle gap, body title, caption subtitle and chord
    [Trait("Rule", "R-TEST-4")] // partial proof of UI-WIN-032: the row layout and type
    public void A_row_is_laid_out_as_padding_12_8_with_an_18_point_icon_column_12_point_gap_and_caption_subtitle()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var directory = harness.NewFolder("p");
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", directory).Url);
                model.InsertThread(AppModelSamples.Thread(project) with { Title = "A thread with a subtitle" });
                var (_, window, palette) = Build(model);
                var row = RowParts(RowTitled(palette, "A thread with a subtitle"));
                Assert.Equal(new Thickness(12, 8), row.Background.Padding);
                Assert.Equal(18, row.Icon.Width);
                Assert.Equal(18, row.Icon.Bounds.Width);
                Assert.Equal(12, row.TextStack.Margin.Left);
                Assert.Equal(row.Icon.Bounds.Right + 12, row.TextStack.Bounds.X, 1); // the text starts one 12-point gap after the icon column
                Assert.Equal(1, row.TextStack.Spacing);
                Assert.Equal(row.Title.Bounds.Bottom + 1, row.Subtitle.Bounds.Y, 1);
                Assert.Equal(SwiftFont.Body.PointSize, row.Title.FontSize);
                Assert.Equal(13, row.Title.FontSize);
                Assert.Equal(SwiftFont.Caption.PointSize, row.Subtitle.FontSize);
                Assert.Equal(SwiftFont.Caption.PointSize, row.Chord.FontSize);
                Assert.Equal(10, row.Subtitle.FontSize);
                Assert.Equal(1, row.Title.MaxLines);
                Assert.Equal(1, row.Subtitle.MaxLines);
                Assert.Equal(ChromeColors.SecondaryText.Resolve(window.ActualThemeVariant), Fill(row.Subtitle.Foreground));
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-WIN-032")] // :113-114: a project row with an icon draws ProjectIconMark at size 13, one without draws the folder symbol
    [Trait("Rule", "R-TEST-4")] // partial proof of UI-WIN-032: the project row's mark
    public void A_project_row_shows_its_project_mark_at_size_13_or_the_folder_symbol_without_one()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var marked = harness.NewFolder("marked");
            var plain = harness.NewFolder("plain");
            AvaloniaWindow window = null!;
            CommandPalette palette = null!;
            string markedName = "", plainName = "";
            harness.Main.OnMain(() =>
            {
                var a = model.AddProject(new Project("Marked", marked).Url);
                model.UpdateProject(a.Id, project => project with { Icon = new ProjectIcon.Emoji("\U0001F680") });
                var b = model.AddProject(new Project("Plain", plain).Url);
                markedName = a.Name;
                plainName = b.Name;
                (_, window, palette) = Build(model);
                palette.QueryField.Text = "mark";
            });
            harness.Main.OnMain(() =>
            {
                Settle(window);
                var mark = Assert.IsType<ProjectIconMark>(Assert.Single(RowParts(RowTitled(palette, "New thread in " + markedName)).Icon.Children));
                var emoji = Assert.IsType<TextBlock>(mark.Child);
                Assert.Equal("\U0001F680", emoji.Text);
                Assert.Equal(13, emoji.FontSize);
                palette.QueryField.Text = "plain";
            });
            harness.Main.OnMain(() =>
            {
                Settle(window);
                var symbol = Assert.IsType<SwiftImage>(Assert.Single(RowParts(RowTitled(palette, "New thread in " + plainName)).Icon.Children));
                Assert.Equal("folder", symbol.SymbolName);
            });
        });
    }

    [Fact]
    [Trait("Req", "UI-WIN-032")] // :143-146: one combined button element that reports selection and can be invoked
    [Trait("Rule", "R-TEST-4")] // partial proof of UI-WIN-032: the accessibility element
    public void A_row_is_one_button_element_that_reports_selection_and_runs_when_invoked()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var directory = harness.NewFolder("p");
            AvaloniaWindow window = null!;
            CommandPalette palette = null!;
            Project project = null!;
            harness.Main.OnMain(() =>
            {
                project = model.AddProject(new Project("P", directory).Url);
                var thread = model.NewThread(project)!;
                model.UpdateThread(thread.Id, value => value with { Title = "Accessible thread" });
                model.SelectedThreadID = thread.Id;
                (_, window, palette) = Build(model);
                model.IsCommandPalettePresented = true;

                var threadPeer = ControlAutomationPeer.CreatePeerForElement(RowTitled(palette, "Accessible thread"));
                var terminalRow = RowTitled(palette, "Toggle terminal");
                var terminalPeer = ControlAutomationPeer.CreatePeerForElement(terminalRow);
                Assert.Equal(AutomationControlType.Button, threadPeer.GetAutomationControlType());
                Assert.Equal("Accessible thread, " + project.Name, threadPeer.GetName()); // title and subtitle read as one
                Assert.Equal("Toggle terminal", terminalPeer.GetName()); // no subtitle: the title alone
                Assert.Empty(threadPeer.GetChildren()); // children are combined into the one element

                // Only the selected row (row 0 at open) reports IsSelected.
                var selectedIndex = palette.Selection;
                Assert.Equal(0, selectedIndex);
                foreach (var (row, index) in palette.RowsPanel.Children.Select((row, index) => (row, index)))
                {
                    var peer = (ISelectionItemProvider)ControlAutomationPeer.CreatePeerForElement(row);
                    Assert.Equal(index == selectedIndex, peer.IsSelected);
                }

                // Invoke runs the row and closes the palette first.
                var runtime = model.Runtime(thread.Id);
                Assert.False(runtime.IsTerminalVisible);
                ((IInvokeProvider)terminalPeer).Invoke();
                Assert.False(model.IsCommandPalettePresented);
                Assert.True(runtime.IsTerminalVisible);
            });
            harness.Main.OnMain(() => Settle(window));
        });
    }

    [Fact]
    [Trait("Req", "UI-WIN-032")] // :86-89: a tap selects the row and runs it
    [Trait("Rule", "R-TEST-4")] // partial proof of UI-WIN-032: the click's selection and effect
    public void Clicking_a_row_selects_it_and_runs_its_action()
    {
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            var directory = harness.NewFolder("p");
            harness.Main.OnMain(() =>
            {
                var project = model.AddProject(new Project("P", directory).Url);
                model.SelectedThreadID = model.NewThread(project)!.Id;
                var (_, window, palette) = Build(model);
                model.IsCommandPalettePresented = true;
                var index = Array.IndexOf(Titles(palette), "Toggle terminal");
                Assert.True(index > 0);
                var runtime = model.Runtime(model.SelectedThreadID!.Value);
                Assert.False(runtime.IsTerminalVisible);
                Click(window, Centre(palette.RowsPanel.Children[index], window));
                Assert.Equal(index, palette.Selection); // the tap selected the row it landed on
                Assert.True(runtime.IsTerminalVisible); // and ran its action
                Assert.False(model.IsCommandPalettePresented);
            });
        });
    }
}
