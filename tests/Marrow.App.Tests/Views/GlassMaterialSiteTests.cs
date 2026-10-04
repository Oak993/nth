// Source: DroppyCode/App/Views/Chat/HydraProgressBar.swift (B3 52bc792), :564-579.
using Avalonia.Controls;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views;

[Collection("Headless")]
public sealed class GlassMaterialSiteTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-WIN-180")] // App/Views/Chat/HydraProgressBar.swift:571-577 flat inside glass, regular glass otherwise
    public void ProgressCardUsesGlassOnlyOutsideAGlassPanel() => headless.OnUiThread(() =>
    {
        var outside = Assert.IsType<Border>(new ProgressCardSurface(false, true).Body(new TextBlock()));
        Assert.NotNull(SwiftGlass.GetEffect(outside));
        Assert.Null(outside.Background);
        var inside = Assert.IsType<Border>(new ProgressCardSurface(true, true).Body(new TextBlock()));
        Assert.Null(SwiftGlass.GetEffect(inside));
        Assert.NotNull(inside.Background);
    });

    // RESOLVED F.GlassMaterial #1 (was a STOP: unported dependency).
    // Swift: App/Views/Chat/ChatView.swift:1120-1124 (GlassEffectContainer spacing: 2). Records: PA-0001 glass site inventory / PR-WIN-180 material foundation; PR-CHAT-171 / UI-CHAT-073.
    // ChatChromeRow is built (Views/Chat/ChatView.cs): its GlassEffectContainer calls SwiftGlass.SetContainerSpacing(this, 2), so the chrome row's glass is drawn in one material pass.
    // Proven by ChatViewFramesReviewTests (the pass follows every frame of the Threads and Hydra buttons' soft appear and fade out, with no layout pass) and by GlassMaterialTests
    // (a container refreshes in the frame an opacity or render transform between it and a piece changes).

    // STOP F.GlassMaterial #2
    // Kind: unported dependency. Swift: App/Views/Composer/EffortSlider.swift:784-793 (.regular white tint 0.55/0.32, interactive Circle).
    // Records: PA-0001 glass site inventory / PR-WIN-180 material foundation.
    // EffortSliderCard and EffortSlider bodies remain MARROW_STUB, so the thumb's source glass shape has no target control.
    // Needed: effort-slider unit's control tree; call SetEffect on its Circle with the exact white tint and flag.

    // STOP F.GlassMaterial #3
    // Kind: pre-existing test/runtime ownership failure, reproduced with all changed src restored to ffe2096ca.
    // Swift: UI/Markdown/HydraHeadHoverPopover.swift:19-49; LinkHover.swift:31-40; records: PR-THEME-144 / PR-HYDRA-197.
    // The shared popover's Popup is accessed from another test thread. Four LinkHoverTests fail in the full UI suite;
    // isolated hover tests pass. Serializing collections instead exposes two HydraHeadHoverPopoverTests failures.
    // Needed: hover-popover/test-runtime owner to settle shared Popup thread ownership. No test was skipped or weakened.

    // STOP F.GlassMaterial #4
    // Kind: pre-existing composer/provider command-refresh failure, reproduced with all changed src restored to ffe2096ca.
    // Swift: App/Views/Composer/ComposerView.swift:205-207 (command refresh); record: PR-COMPOSER-052.
    // B3_commands_landing_refresh_the_focused_on_screen_composer times out waiting for the /review suggestion after UpdateCommands.
    // Needed: composer/provider owner to repair the refresh path. This task changes only the composer's glass-site hunks.
}
