using Avalonia.Controls;
using Marrow.App.Port.Web;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Tests;

// PORT-ONLY: web surfaces W10 landing check (ED-NEW-web-w10-9). The first-time question appears and leaves without movement when Reduce Motion is on
// (E:/Droppy/work/design/web-surfaces/DESIGN.md 10 "Reduce Motion: springs become short fades"; ED-NEW-web-w10-4 point 6). A link opened from somewhere with no
// control of its own (an MCP App's card, the git menu's Open repository, a Tabula artifact) has no anchor: the sheet then sits over the main window, and the
// system's setting must still stop its motion. Before the landing check an absent anchor always meant movement.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w10-9")]
public sealed class WebW10ChatLinkSheetStillTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void With_no_anchor_the_systems_Reduce_Motion_decides_whether_the_sheet_moves(bool reduceMotion, bool expectedStill) => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var motion = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = reduceMotion });
        Assert.Equal(expectedStill, ChatLinkSheet.StillFor(null));
    });

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void With_an_anchor_the_windows_Reduce_Motion_decides_whether_the_sheet_moves(bool reduceMotion, bool expectedStill) => headless.OnUiThread(() =>
    {
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var motion = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = false });
        var anchor = new Border();
        EnvironmentValues.SetAccessibilityReduceMotion(anchor, reduceMotion);
        Assert.Equal(expectedStill, ChatLinkSheet.StillFor(anchor));
    });

    [Fact]
    public void With_no_frame_source_the_sheet_never_moves() => headless.OnUiThread(() =>
    {
        using var motion = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform { AccessibilityDisplayShouldReduceMotion = false });
        Assert.True(ChatLinkSheet.StillFor(null));
        Assert.True(ChatLinkSheet.StillFor(new Border()));
    });
}
