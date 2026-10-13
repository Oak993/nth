using Marrow.App.Tests.Fixtures;
using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.App.Tests.Port;

// PORT-ONLY: installer C4b U8 finding 2; actual model application over the in-memory rig only.
[Collection("Headless")]
public sealed class OnThisPcProbeRecoveryTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_cached_shortcut_read_refusal_keeps_the_model_on_until_absence_is_proven()
    {
        headless.OnUiThread(() =>
        {
            var rig = new OnThisPcRig();
            var path = FakeShellLinks.DesktopLink;
            rig.Shell.Seed(path, OnThisPcRig.Made());
            rig.Model.Refresh(pageOpened: true);
            rig.Pump();
            Assert.True(rig.Model.DesktopShortcutOn);

            rig.Shell.Seed(path, OnThisPcRig.Made());
            rig.Shell.Fail(FakeShellLinks.Call.ReadLink, unchecked((int)0x80070020), times: 1, path: path);
            rig.Clock.Advance(TimeSpan.FromSeconds(3));
            rig.Model.Refresh();
            rig.Pump();

            Assert.True(rig.Model.DesktopShortcutOn);
            Assert.Null(rig.Model.DesktopShortcutNote);
            rig.Shell.DeleteLink(path);
            rig.Clock.Advance(TimeSpan.FromSeconds(3));
            rig.Model.Refresh();
            rig.Pump();
            Assert.False(rig.Model.DesktopShortcutOn);
        });
    }
}
