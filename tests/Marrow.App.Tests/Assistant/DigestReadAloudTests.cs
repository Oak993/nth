using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;

namespace Marrow.App.Tests.Assistant;

[Trait("Decision", "ED-NEW-dextra-open-3")]
public sealed class DigestReadAloudTests
{
    [Fact]
    public void Read_aloud_speaks_the_digest_without_a_microphone_and_Stop_cuts_it()
    {
        using var rig = new VoiceRig();
        rig.Conversation.ReadAloud("One task finished. Nothing needs you.");
        Assert.Equal(VoicePhase.Speaking, rig.Conversation.Phase);
        Assert.Equal("One task finished. Nothing needs you.", rig.Conversation.Reply);
        Assert.False(rig.Capture.IsRunning);
        Assert.Empty(rig.Brain.Asked);
        rig.Conversation.Stop();
        Assert.True(rig.Player.StopCount > 0);
        Assert.False(rig.Capture.IsRunning);
    }
}
