using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.UI.Chrome;

namespace Marrow.App.Tests.Views.Sidebar;

// REVIEW (pass 2, round 3 re-review, adversarial) of unit parity-sidebarview's round-3 fix to PR-WIN-200.
// SettingsView.swift:148-164 `ForEach(visiblePages) { item in SidebarRow(title:, isSelected:, action:) { SidebarIconBadge
// { ... }.overlay(alignment: .topTrailing) { if item == .about, UpdateChecker.shared.updateAvailable { UpdateAvailableDot() } } } }`:
// the update dot is a conditional INSIDE the About row's icon overlay. The About row keeps its ForEach id (.about) when
// the dot comes or goes, so the row keeps its @State, its isHovering (UI/Chrome/Chrome.swift:689, :735-737) with it, and
// its fill (:741-745) does not move. The fix rebuilds the About row whenever UpdateChecker.Shared.UpdateAvailable changes
// (SettingsView.cs `pageRows.Remove(SettingsPage.About); RebuildSidebar();`), so a pointer resting on About loses the
// hover wash by a cut the moment an update check lands (OD-0076 also forbids the cut).
[Collection("Headless")]
public sealed class SidebarRowUpdateDotReviewTests(HeadlessSession headless) : SettingsFinishFixture(headless)
{
    [Fact]
    [Trait("Req", "PR-WIN-200")] // Chrome.swift:689, :735-737, :741-745; SettingsView.swift:148-164
    public void REVIEW_Round3b_The_About_rows_hover_wash_stays_when_the_update_dot_appears()
    {
        With(() => new SettingsView(), (view, _, h, w) =>
        {
            byte Alpha(string title) => Assert.IsAssignableFrom<ISolidColorBrush>(Assert.IsType<Border>(RowButton(view, title).Content).Background).Color.A;
            var hovered = Chrome.Overlay(0.06).Resolve(view.ActualThemeVariant).A; // :743
            Assert.Equal(0, Alpha("About")); // General is selected; About is clear
            Reveal(w, h, RowButton(view, "About")); // with Ignis and the Assistant listed, the sidebar scrolls to reach About
            Hover(w, RowButton(view, "About"));
            Tick(h, 150);
            Assert.Equal(hovered, Alpha("About")); // the pointer rests on About: its hover wash is whole
            typeof(UpdateChecker).GetProperty("Update")!.SetValue(UpdateChecker.Shared, new AvailableUpdate { Version = "999.0.0", Tag = "v999.0.0", DownloadURL = SwiftURL.Create("https://fixture.invalid/download")!, PageURL = SwiftURL.Create("https://fixture.invalid/release")!, Notes = "" });
            Drain(w);
            Assert.Single(Nodes<UpdateAvailableDot>(view)); // the dot is on the About row now
            Assert.True(Alpha("About") == hovered, $"the dot's frame: About's wash is {Alpha("About")}, not the hover's {hovered}"); // the row kept its hover: its wash has not moved
            Tick(h, 20);
            Assert.True(Alpha("About") == hovered, $"20 ms after the dot: About's wash is {Alpha("About")}, not the hover's {hovered}");
            Tick(h, 150);
            Assert.True(Alpha("About") == hovered, $"170 ms after the dot: About's wash is {Alpha("About")}, not the hover's {hovered}");
        });
    }
}
