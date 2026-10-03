using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Sidebar;

// REVIEW (pass 2, round 3, adversarial) of unit parity-sidebarview's PR-WIN-200 claim.
// UI/Chrome/Chrome.swift:729 `.background { shape.fill(fill).animation(Chrome.hover, value: isSelected) }` and :741-745
// `fill`: a SidebarRow whose selection changes eases its wash on Chrome.hover (easeOut 0.1 s); PR-WIN-200 says "with the
// selection change animated on the 0.1-second hover curve". The one SidebarRow whose selection changes is the Settings
// sidebar's (SettingsView.swift:148-164 `SidebarRow(title:, isSelected: selection == page, ...)` in a ForEach over the
// pages, so a page keeps its row). 1-settings-tour.mov 47.72-47.94 s (crop 200:80:890:485, General -> Hydra; CATALOG
// "selection wash cross-fades between rows"): the General row's wash falls and the Hydra row's rises together over
// 47.765-47.85 s, about 0.1 s (mean grey of the two rows 93.8 -> 77.2 and 78.8 -> 90.4 across six frames), not in one
// frame. The port's SidebarRow takes isSelected once, at construction, and SettingsView builds every row again when the
// page changes, so the wash moves by a cut.
[Collection("Headless")]
public sealed class SidebarRowSelectionReviewTests(HeadlessSession headless) : SettingsFinishFixture(headless)
{
    [Fact]
    [Trait("Req", "PR-WIN-200")] // Chrome.swift:729, :741-745; SettingsView.swift:148-164; 1-settings-tour.mov 47.765-47.85 s
    public void REVIEW_Round3_A_sidebar_rows_selection_wash_moves_to_another_row_on_chrome_hover_not_by_a_cut()
    {
        With(() => new SettingsView(), (view, _, h, w) =>
        {
            byte Alpha(string title) => Assert.IsAssignableFrom<ISolidColorBrush>(Assert.IsType<Border>(RowButton(view, title).Content).Background).Color.A;
            var selected = Chrome.Overlay(0.12).Resolve(view.ActualThemeVariant).A; // :742
            var hovered = Chrome.Overlay(0.06).Resolve(view.ActualThemeVariant).A; // :743
            Assert.Equal(selected, Alpha("General")); // the first page is selected
            Hover(w, RowButton(view, "Hydra"));
            Tick(h, 150);
            Assert.Equal(hovered, Alpha("Hydra")); // the pointer rests on Hydra: its hover wash is whole
            Click(w, RowButton(view, "Hydra")); // the pointer stays where it was: Hydra is selected
            Tick(h, 50); // half of Chrome.hover's easeOut 0.1 s
            Assert.InRange(Alpha("General"), (byte)1, (byte)(selected - 1)); // the wash leaving General, part way
            Assert.InRange(Alpha("Hydra"), (byte)(hovered + 1), (byte)(selected - 1)); // the wash coming to Hydra, part way
            Tick(h, 60);
            Assert.Equal(0, Alpha("General"));
            Assert.Equal(selected, Alpha("Hydra"));
        });
    }
}
