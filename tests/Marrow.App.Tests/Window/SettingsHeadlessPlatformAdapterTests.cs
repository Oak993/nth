using Marrow.App.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Marrow.App.Tests.Window;

// Infrastructure controls for the IWindowImpl + IHeadlessWindow adapter.
// Product UI assertions remain in the unchanged WindowManagerSettingsSurfaceTests.
[Collection("Headless")]
public sealed class SettingsHeadlessPlatformAdapterTests(HeadlessSession headless, ITestOutputHelper output)
{
    [Fact]
    [Trait("Rule", "R-TEST-7")]
    public void Headless_mouse_input_reaches_the_control_while_the_drag_spy_stays_installed()
    {
        SettingsHeadlessPointerExercise.Run(headless, output, startDrag: false);
    }

    [Fact]
    [Trait("Rule", "R-TEST-7")]
    public void A_drag_requested_inside_the_actual_pointer_callback_is_seen_by_the_installed_spy()
    {
        SettingsHeadlessPointerExercise.Run(headless, output, startDrag: true);
    }
}
