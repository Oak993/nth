using Avalonia.Controls;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.UI.Common;
using Xunit;

namespace Marrow.App.Tests.Views.Chat;

// REVIEW (adversarial review of unit/blitz-tail, UI-HYDRA-019 "Head change: the capsule fades and slides with Chrome.panelSlide, unless a window
// live resize is running"). HydraPanel.swift:136-165 builds the capsule inside `if let selected, let info = selected.hydra { HStack { ... }
// .transition(.softAppear) }` with no `.id(selected.id)`: when the stage moves from one head to another both are non-nil, the `if` keeps its
// branch, so SwiftUI keeps the SAME capsule view and its contents change in place under `.animation(liveResize.isActive ? nil :
// Chrome.panelSlide, value: selected?.id)` (:199). The soft appear (opacity 0 and a 4-point rise over .smooth(0.32)) is only the capsule's
// insertion, when there was no head on stage. The port instead removes the capsule and soft-appears a new one on every head change (two
// capsules on screen, the new one at opacity 0), which is neither the record's panelSlide nor the source's one kept view.
[Collection("Headless")]
public sealed class HydraPanelStageCapsuleReviewTests(HeadlessSession headless)
{
    private static Control[] Capsules(HydraPanelContext c) =>
        c.Find<HydraGlyph>().Where(g => g.Bounds.Width == 16).Select(glyph => (Control)glyph.Parent!.Parent!.Parent!.Parent!).ToArray();

    [Fact]
    [Trait("Req", "UI-HYDRA-019")] // HydraPanel.swift:136-165 (no .id on the capsule), :199 (.animation(panelSlide, value: selected?.id))
    [Trait("Rule", "R-TEST-4")] // partial proof of UI-HYDRA-019: the head-change state
    public void REVIEW_a_head_change_keeps_the_one_capsule_on_screen_instead_of_soft_appearing_a_second_one() => ChatHeadlessFixtures.Run(headless, () =>
    {
        using var c = new HydraPanelContext(visible: true);
        c.Tick(1);
        Assert.Single(Capsules(c));

        c.Select(c.Second);

        var capsule = Assert.Single(Capsules(c)); // one capsule throughout: the source keeps the view, it does not insert a new one
        Assert.Equal(1, capsule.Opacity); // never faded out to nothing and back in: only its contents change, on the panel slide
    });
}
