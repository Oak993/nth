using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.UI.Chrome;

namespace Marrow.App.Tests.Views.Settings;

// SettingsView.swift:100-107 (the page filter), :130-133 (a filter that drops the page moves it to the first match),
// :148-149 `ForEach(visiblePages) { item in SidebarRow(title:, isSelected: page == item, ...) }`; UI/Chrome/Chrome.swift:729
// `.background { shape.fill(fill).animation(Chrome.hover, value: isSelected) }` (B3 52bc792). A page keeps its row under
// its ForEach id while it stays listed, so a selection that a search moves eases onto the row the page had, as a click does
// (SidebarRowSelectionReviewTests); a page whose id leaves the list loses its view and its state, and comes back as a new one.
[Collection("Headless")]
public sealed class SettingsSidebarSelectionTests(HeadlessSession headless) : SettingsFinishFixture(headless)
{
    [Fact]
    [Trait("Req", "PR-WIN-200")] // Chrome.swift:729, :741-745; SettingsView.swift:100-107, :130-133, :148-149
    public void A_search_that_moves_the_selection_eases_the_wash_onto_the_row_the_page_had_and_a_page_that_left_comes_back_new()
    {
        With(() => new SettingsView(), (view, _, h, w) =>
        {
            SidebarRow RowOf(string title) => Nodes<SidebarRow>(view).Single(r => Text(r).Single() == title);
            byte Alpha(string title) => Assert.IsAssignableFrom<ISolidColorBrush>(Assert.IsType<Border>(RowButton(view, title).Content).Background).Color.A;
            var selected = Chrome.Overlay(0.12).Resolve(view.ActualThemeVariant).A; // :742
            var general = RowOf("General");
            var appearance = RowOf("Appearance");
            Assert.Equal(selected, Alpha("General")); // the first page is selected
            Assert.Equal(0, Alpha("Appearance"));

            var field = Assert.Single(Nodes<TextBox>(Assert.Single(Nodes<SidebarSearchField>(view))));
            Type(w, field, "theme"); // :100-107 a keyword of Appearance alone; :130-133 General is filtered out, so the page moves
            Assert.Equal(new[] { "Appearance" }, Titles(view));
            Assert.Single(Nodes<AppearanceSettingsPage>(view));
            Assert.Same(appearance, RowOf("Appearance")); // the row the page had
            Assert.True(appearance.IsSelected);
            Assert.Equal(0, Alpha("Appearance")); // no frame yet: the wash has not jumped
            Tick(h, 50);
            Assert.InRange(Alpha("Appearance"), (byte)1, (byte)(selected - 1)); // part way along Chrome.hover's easeOut 0.1 s
            Tick(h, 60);
            Assert.Equal(selected, Alpha("Appearance"));

            Press(w, Key.Escape); // Chrome.swift:814 the field empties: every page is listed again
            Assert.Equal(Marrow.App.Port.Settings.SettingsSidebar.Pages("").Count, Titles(view).Length);
            Assert.Same(appearance, RowOf("Appearance")); // still the row it had, still selected
            Assert.NotSame(general, RowOf("General")); // General left the list: it comes back as a new view
            Assert.False(RowOf("General").IsSelected);
            Tick(h, 110);
            Assert.Equal(selected, Alpha("Appearance"));
            Assert.Equal(0, Alpha("General"));
        });
    }
}
