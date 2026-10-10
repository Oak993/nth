using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

[Collection("Headless")]
public sealed class SettingsRestSidebarTests(HeadlessSession headless) : SettingsRestFixture(headless)
{
    [Fact]
    [Trait("Req", "UI-SETTINGS-061")] // App/Views/Settings/SettingsView.swift:148-182; Chrome.swift:670-757
    [Trait("Req", "UI-SETTINGS-062")] // App/Views/Settings/SettingsView.swift:151-157
    [Trait("Req", "UI-SETTINGS-063")] // App/Views/Settings/SettingsView.swift:148-182
    [Trait("Req", "UI-SETTINGS-064")] // App/Views/Settings/SettingsView.swift:148-182
    [Trait("Req", "UI-SETTINGS-073")] // App/Views/Settings/SettingsView.swift:148-182
    [Trait("Req", "UI-SETTINGS-074")] // App/Views/Settings/SettingsView.swift:148-182
    [Trait("Req", "UI-SETTINGS-075")] // App/Views/Settings/SettingsView.swift:148-182
    [Trait("Req", "UI-SETTINGS-076")] // App/Views/Settings/SettingsView.swift:148-182
    [Trait("Req", "UI-SETTINGS-077")] // App/Views/Settings/SettingsView.swift:148-182
    [Trait("Req", "UI-SETTINGS-078")] // App/Views/Settings/SettingsView.swift:148-182
    public void Sidebar_rows_have_exact_geometry_colour_symbols_order_and_real_hover_selection()
    {
        With(() => new SettingsView(), (view, _, h, w) =>
        {
            var titles = Marrow.App.Port.Settings.SettingsSidebar.Pages("").Select(page => page.Title).ToArray();
            var symbols = Marrow.App.Port.Settings.SettingsSidebar.Pages("").Select(page => page is SettingsPage.Hydra or SettingsPage.Ignis ? "" : page.Symbol).ToArray(); // Hydra and Ignis (OD-0134) draw their own marks
            Assert.Equal(titles, Nodes<SidebarRow>(view).Select(r => Text(r).Single()));
            var list = Field<StackPanel>(view, "rows"); Assert.Equal(1, list.Spacing); Assert.Equal(new Thickness(10, 12, 10, 8), list.Margin);
            Check(Nodes<SidebarRow>(view)[0], 0, true);
            for (var index = 1; index < titles.Length; index++)
            {
                var row = Nodes<SidebarRow>(view)[index]; Check(row, index, false); var button = Assert.Single(Nodes<Button>(row)); var shape = Assert.IsType<Border>(button.Content);
                Reveal(w, h, button); Hover(w, button); Tick(h, 50); Ink(shape, shape.Background, Chrome.Overlay(0.06 * Animation.EaseOut(0.1).Progress(0.05))); Tick(h, 50); Ink(shape, shape.Background, Chrome.Overlay(0.06));
                // Chrome.swift:729 the wash comes to the clicked row on Chrome.hover (easeOut 0.1 s), and the press look lets go over 0.1 s
                // (ED-280): read the row once both are over (UI-SETTINGS-004).
                Click(w, button); Tick(h, 150); row = Nodes<SidebarRow>(view)[index]; Check(row, index, true);
                // Selecting rebuilds the page; current selection must survive moving the pointer away.
                w.MouseMove(new Point(1, 1)); Drain(w); Tick(h, 100); Check(row, index, true);
            }
            var search = Assert.Single(Nodes<TextBox>(Assert.Single(Nodes<SidebarSearchField>(view))));
            foreach (var title in titles) { search.Text = title; Drain(w); Assert.Contains(Nodes<SidebarRow>(view), r => Text(r).Contains(title)); search.Text = "no-matching-settings-value"; Drain(w); Assert.Empty(Nodes<SidebarRow>(view)); search.Text = ""; Drain(w); }
            // SettingsView.swift:182 applies the shared scroll freeze to the sidebar, even for page scrolling.
            var tracker = Field<SettingsScrollTracker>(view, "scrollTracker"); tracker.Note(); Drain(w); Assert.False(list.IsHitTestVisible); Tick(h, 150); Drain(w); Assert.True(list.IsHitTestVisible);
            void Check(SidebarRow row, int index, bool selected)
            {
                var button = Assert.Single(Nodes<Button>(row)); Assert.Equal(titles[index], AutomationProperties.GetName(button)); var shape = Assert.IsType<Border>(button.Content);
                Assert.Equal(28, shape.Height); Assert.Equal(new CornerRadius(7), shape.CornerRadius); Assert.Equal(new Thickness(8, 0), shape.Padding);
                var line = Assert.IsAssignableFrom<StackPanel>(shape.Child); Assert.Equal(8, line.Spacing); var text = Assert.Single(Nodes<TextBlock>(row)); Font(text, 13, selected ? FontWeight.Medium : FontWeight.Normal); Assert.Equal(1, text.MaxLines); Ink(text, text.Foreground, Chrome.PrimaryText.Opacity(selected ? 1 : 0.92));
                Ink(shape, shape.Background, selected ? Chrome.Overlay(0.12) : SwiftColor.Clear);
                var badge = Assert.Single(Nodes<SidebarIconBadge>(row)); var frame = Assert.IsType<Panel>(badge.Child); Assert.Equal(20, frame.Width); Assert.Equal(20, frame.Height);
                var glyph = frame.Children[0]; Ink(glyph, Avalonia.Controls.Documents.TextElement.GetForeground(glyph), Chrome.PrimaryText.Opacity(0.85));
                if (index == 1) { var mark = Assert.Single(Nodes<HydraMarkImage>(row)); Assert.Equal(16.1, mark.Width, 5); Assert.Equal(16.1, mark.Height, 5); Assert.Empty(Nodes<SidebarSymbol>(row)); }
                else if (titles[index] == "Ignis") { var flame = Assert.Single(Nodes<Marrow.App.Port.Ignis.Settings.IgnisFlameMark>(row)); Assert.Equal(16.1, flame.Width, 5); Assert.Equal(16.1, flame.Height, 5); Assert.Empty(Nodes<SidebarSymbol>(row)); } // OD-0134: the lit flame, Hydra's size
                else { var image = Assert.Single(Nodes<SwiftImage>(row)); Assert.Equal(symbols[index], image.SymbolName); Assert.Equal(index == 0 ? 16.1 : 14, image.Width, 5); Assert.Equal(index == 0 ? 16.1 : 14, image.Height, 5); }
                Assert.Empty(Nodes<UpdateAvailableDot>(row));
            }
        });
    }

    [Fact]
    [Trait("Req", "UI-UPDATE-011")] // App/Views/Settings/SettingsView.swift:159-163; AboutUpdateViews.swift:527-535
    public void Update_dot_is_only_on_About_and_keeps_selected_corner_geometry_and_accent()
    {
        With(() => new SettingsView(), (view, _, h, w) =>
        {
            Assert.Empty(Nodes<UpdateAvailableDot>(view));
            typeof(UpdateChecker).GetProperty("Update")!.SetValue(UpdateChecker.Shared, new AvailableUpdate { Version = "999.0.0", Tag = "v999.0.0", DownloadURL = SwiftURL.Create("https://fixture.invalid/download")!, PageURL = SwiftURL.Create("https://fixture.invalid/release")!, Notes = "" }); Drain(w);
            var about = Nodes<SidebarRow>(view).Single(r => Text(r).Contains("About")); Reveal(w, h, about); Click(w, Assert.Single(Nodes<Button>(about)));
            var dot = Assert.Single(Nodes<UpdateAvailableDot>(view)); var parent = Nodes<SidebarRow>(view).Single(r => Nodes<UpdateAvailableDot>(r).Length == 1); Assert.Equal("About", Text(parent).Single());
            Assert.Equal(Avalonia.Layout.HorizontalAlignment.Right, dot.HorizontalAlignment); Assert.Equal(Avalonia.Layout.VerticalAlignment.Top, dot.VerticalAlignment);
            var circle = Assert.IsType<Avalonia.Controls.Shapes.Ellipse>(dot.Child); Assert.Equal(7, circle.Width); Assert.Equal(7, circle.Height); Ink(circle, circle.Fill, Chrome.Accent); var offset = Assert.IsType<TranslateTransform>(circle.RenderTransform); Assert.Equal(2.5, offset.X); Assert.Equal(-2.5, offset.Y);
            Assert.Equal(FontWeight.Medium, Assert.Single(Nodes<TextBlock>(parent)).FontWeight);
            typeof(UpdateChecker).GetProperty("Update")!.SetValue(UpdateChecker.Shared, null); Drain(w); Assert.Empty(Nodes<UpdateAvailableDot>(view));
        });
    }
}
