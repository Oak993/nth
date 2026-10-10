using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Session;
using Marrow.Services.Port.Voice.Probe;
using Marrow.Testing;
using static Marrow.App.Tests.Fixtures.VoicePillProbe;
using static Marrow.App.Tests.Views.Composer.ComposerViewTests;

namespace Marrow.App.Tests.Port.Voice;

[Collection("Headless")]
[Trait("Decision", "ED-351")]
public sealed class MountedVoicePreparationTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_orb_click_shows_the_measured_preparation_pill_and_cancels_the_actual_shared_transfer(bool joinedWelcome)
    {
        headless.OnUiThread(() =>
        {
            using var rig = new VoiceComposerRig();
            var models = new HeldVoicePreparationModels();
            var clock = new ManualTimeProvider();
            var engine = new VoicePrepEngine(clock, VoiceQuestionRig.PcSeconds);
            var prep = new VoicePreparation(new ScriptedProbe(ScriptedMachines.OwnersDesktop), models, () => engine,
                new AppSettingsVoiceAccess(rig.App.Settings), () => new float[160320], VoicePrepRig.App, VoicePrepRig.Runtime, clock);
            var question = new VoiceSpeedQuestion(prep);
            var flow = new VoiceFirstRun(prep, question, models);
            VoiceFirstRun.Bind(rig.App.Settings, flow);
            Task? welcome = null;
            if (joinedWelcome) welcome = rig.Main.OnMain(() => question.YesAsync(null, CancellationToken.None));
            else question.No();
            question.CloseWelcome();
            rig.QuietRoom();
            var (view, window) = rig.Open(1000);
            try
            {
                rig.Main.OnMain(() => view.Voice!.Activate());
                rig.Main.Loop.RunUntil(() => models.Installs == 1 && view.Voice!.PreparationProgress?.Id == VoicePreparation.DownloadId);
                rig.PumpWindow(window);
                rig.Elapse(window, .8);
                Assert.True(Pill(view).Open);
                Assert.Equal(0, rig.Capture.StartCount);
                Assert.Equal(VoiceSessionState.Rest, view.Voice!.State);
                Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(Pill(view)), text => text.Text == "Getting voice ready");
                Assert.True(LogicalQuery.SelfAndDescendants<TextBlock>(Pill(view)).Count(text => text.Text == "315 of 874 MB") == 1,
                    "Joined=" + joinedWelcome + "; progress=" + view.Voice.PreparationProgress + "; shared=" + flow.Progress + "; installs=" + models.Installs);
                Assert.DoesNotContain(LogicalQuery.SelfAndDescendants<Popup>(view), popup => popup.IsOpen);
                Assert.Equal(1, models.Installs);
                Assert.Empty(engine.Loads);
                var ring = LogicalQuery.SelfAndDescendants<VoicePreparationRing>(Pill(view)).Single();
                Assert.Equal(18, ring.Bounds.Width); // the nth ring at 18 (motion pack step 4: 14, 18 or 20), where the locked qring was 16
                Assert.Equal(18, ring.Bounds.Height);
                Assert.Equal(.36, ring.Ring.Value, 6); // the arc is heading for the measured fraction; the nth ring springs there (motion pack step 3 spring), so the drawn arc is a hair short of it
                Assert.Equal(.36, ring.Fraction, 2);
                var point = ring.TranslatePoint(default, window)!.Value;
                var crop = new Avalonia.Rect(point, ring.Bounds.Size);
                var painted = PreviewPixels.Region(window, crop);
                rig.Main.OnMain(() => ring.Opacity = 0);
                var without = PreviewPixels.Region(window, crop);
                Assert.True(PreviewPixels.MaxDifference(painted, without) > 8, "The measured ring is actually painted in the capsule.");
                rig.Main.OnMain(() => ring.Opacity = 1);
                rig.Elapse(window, 10);
                Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(Pill(view)), text => text.Text == "315 of 874 MB"); // no time estimate
                rig.Main.OnMain(() => models.Report(.52));
                rig.PumpWindow(window);
                rig.Elapse(window, .15);
                // the ring springs (motion pack step 3 spring: critically damped, stiffness 110) from .36 to .52, read 0.15 s after
                Assert.Equal(.52 + (.36 - .52) * (1 + Marrow.UI.Port.Nth.NthSpring.Omega * .15) * Math.Exp(-Marrow.UI.Port.Nth.NthSpring.Omega * .15), ring.Fraction, .02);
                rig.Elapse(window, .15);
                Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(Pill(view)), text => text.Text == "455 of 874 MB");
                rig.Main.OnMain(() => Click(Pill(view).Cross));
                rig.Main.Loop.RunUntil(() => models.CancellationObserved && (welcome is null || welcome.IsCompleted));
                rig.Elapse(window, .8);
                Assert.True(models.CancellationObserved);
                Assert.Null(rig.App.Settings.VoiceHardware);
                Assert.Equal(0, rig.Capture.StartCount);
                Assert.False(Pill(view).Open);
                Assert.False(view.Voice.SetupOpen);
                if (welcome is not null) Assert.True(welcome.IsCanceled);
                models.Retry();
                rig.Main.OnMain(() => view.Voice.Activate());
                rig.Elapse(window, .8);
                Assert.Equal(2, models.Installs);
                Assert.True(Pill(view).Open);
                rig.Main.OnMain(() => models.ReportPrevious(.9));
                rig.PumpWindow(window);
                rig.Elapse(window, .4);
                Assert.Single(LogicalQuery.SelfAndDescendants<TextBlock>(Pill(view)), text => text.Text == "315 of 874 MB");
                Assert.Equal(.36, ring.Ring.Value, 6); // a stale report moves nothing
                Assert.Equal(.36, ring.Fraction, 3); // the spring has all but arrived
                rig.Main.OnMain(models.Complete);
                rig.Main.Loop.RunUntil(() => !view.Voice.Preparing);
                rig.Elapse(window, .8);
                Assert.NotNull(rig.App.Settings.VoiceHardware);
                Assert.False(view.Voice.SetupOpen);
                Assert.Equal(0, rig.Capture.StartCount);
                rig.Main.OnMain(() => view.Voice.Activate());
                rig.Elapse(window, .2);
                Assert.Equal(1, rig.Capture.StartCount);
                Assert.Equal(VoiceSessionState.Listening, view.Voice.State);
                Assert.Equal(2, models.Installs);
            }
            finally { rig.Close(window); }
        });
    }
}
