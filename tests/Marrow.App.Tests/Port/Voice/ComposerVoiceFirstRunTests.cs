using Avalonia.Controls;
using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice.Session;
using Marrow.UI.Port;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Port.Voice;

[Collection("Headless")]
[Trait("Rule", "R-TEST-10")] // Scripted seams only; no model, network or hardware.
public sealed class ComposerVoiceFirstRunTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Feature", "WV-1")]
    public void The_mounted_orb_prepares_before_capture_and_shows_its_setup_card()
    {
        var prep = VoiceQuestionRig.Pc(); // Its scripted probe is awaited before entering the UI dispatcher.
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig();
            var flow = new VoiceFirstRun(prep.Prep.Preparation, prep.Question, prep.Prep.Models);
            VoiceFirstRun.Bind(rig.App.Settings, flow);
            prep.Question.No();
            prep.Question.CloseWelcome();
            rig.QuietRoom();
            var (view, window) = rig.Open();
            try
            {
                var slot = LogicalQuery.SelfAndDescendants<VoiceOrbSlot>(view).Single();
                rig.Main.OnMain(() => Click(slot.HitTarget));
                rig.Elapse(window, 0.1);
                Assert.Equal(0, rig.Capture.StartCount);
                Assert.True(view.Voice!.SetupOpen);
                Assert.Equal(VoiceSessionState.Rest, view.Voice.Session.State);
                Assert.Single(prep.Prep.Models.Installs);
                rig.Main.OnMain(() => Click(slot.HitTarget));
                rig.Elapse(window, 0.1);
                Assert.Equal(1, rig.Capture.StartCount);
                Assert.False(view.Voice.SetupOpen);
                Assert.Equal(VoiceSessionState.Listening, view.Voice.Session.State);
            }
            finally { rig.Close(window); }
        });
    }
}
