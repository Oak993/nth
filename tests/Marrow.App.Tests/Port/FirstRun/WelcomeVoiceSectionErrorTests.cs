using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Port.FirstRun;
using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views;
using Marrow.Services.Port.Voice.Models;
using Marrow.Testing;

namespace Marrow.App.Tests.Port.FirstRun;

// WV-1 review (BUILD-PLAN row VT-15 and ED-NEW-cloud-vt-15 item 8): the sentences of a failed preparation are diagnostics, never UI words. The section shows V2's words only: after a failed
// yes it is back at the ask with V2's own heading and body, and neither the preparation's sentence nor the question's reason is drawn anywhere on the page.
[Collection("Headless")]
public sealed class WelcomeVoiceSectionErrorTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(ModelDownloadStatus.Failed)]
    [InlineData(ModelDownloadStatus.Corrupt)]
    [InlineData(ModelDownloadStatus.Cancelled)]
    [Trait("Feature", "WV-1")]
    [Trait("Decision", "ED-289")]
    public void A_failed_yes_draws_the_ask_words_and_none_of_the_error_sentences(ModelDownloadStatus outcome)
    {
        // The probe awaits pool work. Complete it before entering either UI synchronization context.
        var rig = VoiceQuestionRig.Pc();
        rig.Prep.Models.Outcome = outcome;
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            harness.Main.OnMain(() =>
            {
                var view = new WelcomeView(rig.Question);
                var host = RootViewRig.Show(model, view, 900);
                try
                {
                    RootViewRig.All<WelcomeVoiceSection>(host).Single().Choose(1).GetAwaiter().GetResult(); // yes
                    for (var pass = 0; pass < 3; pass++)
                    {
                        RootViewRig.Layout(host);
                        Dispatcher.UIThread.RunJobs();
                    }
                    Assert.Equal(VoiceQuestionState.Ask, rig.Question.State);
                    Assert.False(string.IsNullOrEmpty(rig.Question.Error)); // the reason is kept for diagnostics
                    var drawn = RootViewRig.AllText(host);
                    Assert.Contains(WelcomeVoiceWords.Heading(VoiceQuestionState.Ask)!, drawn, StringComparison.Ordinal);
                    Assert.DoesNotContain(rig.Question.Error!, drawn, StringComparison.Ordinal);
                    Assert.DoesNotContain("The voice model", drawn, StringComparison.Ordinal);
                    Assert.DoesNotContain("could not be applied", drawn, StringComparison.Ordinal);
                    Assert.DoesNotContain("was not prepared", drawn, StringComparison.Ordinal);
                }
                finally { RootViewRig.Close(host); }
            });
        });
    }
}
