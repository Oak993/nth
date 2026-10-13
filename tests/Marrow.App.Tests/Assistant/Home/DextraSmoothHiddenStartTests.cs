using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Tests.Fixtures;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Assistant.Home;

[Trait("Decision", "ED-NEW-dextra-smooth-11")]
public sealed class DextraSmoothHiddenStartTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Hidden_voice_registration_uses_a_later_main_turn_without_any_render_frame(bool stopBeforePost)
    {
        using var temp = new AssistantTempRoot(); using var rig = new BackgroundLaunchRig(temp, Guid.NewGuid());
        var frames = new ManualFrameSource(); var calls = 0;
        var host = rig.OnMain(() => new DextraHost(rig.Model, rig.Model.AssistantHost.Settings,
            startup: rig.Seams, voiceFrames: frames, voiceAsync: (_, _, _, _) => { calls++; return Task.FromResult<DextraVoice?>(null); }));
        try
        {
            rig.OnMain(() =>
            {
                Assert.NotNull(host.EnsureStarted(mode: new StartupMode(true, null, null)));
                Assert.Equal(0, calls); // The factory is never on the synchronous startup path.
                if (stopBeforePost) host.Stop();
            }); // Deterministic loop drains posts, but the frame source is never advanced.
            rig.Run(() => host.VoiceStartup);
            Assert.Equal(stopBeforePost ? 0 : 1, calls);
            Assert.Equal(0, frames.Pending);
            if (stopBeforePost)
            {
                rig.OnMain(() => Assert.NotNull(host.EnsureStarted(mode: new StartupMode(true, null, null))));
                rig.Run(() => host.VoiceStartup);
                Assert.Equal(1, calls); // The canceled post cannot revive an earlier generation.
            }
        }
        finally { rig.OnMain(host.Dispose); }
    }
}
