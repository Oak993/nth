using Marrow.App.Port.FirstRun;
using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views;
using Marrow.Core.Models;

namespace Marrow.App.Tests.Port.FirstRun;

// WV-1 (OD-0098 welcome-voice pick): the detail pane gives the Welcome page its voice question and tells the question when Welcome has gone (a project exists), so a question still open is asked at the next orb click.
[Collection("Headless")]
public sealed class DetailViewVoiceQuestionTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Feature", "WV-1")]
    [Trait("Decision", "ED-289")]
    public void The_empty_detail_pane_shows_the_voice_section_when_it_has_a_question_and_does_not_when_it_has_none()
    {
        var rig = VoiceQuestionRig.Pc(); // built off the UI loop: its probe is awaited
        headless.OnUiThread(() =>
        {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        harness.Main.OnMain(() =>
        {
            var without = RootViewRig.Show(model, new DetailView());
            try { Assert.Empty(RootViewRig.All<WelcomeVoiceSection>(without)); }
            finally { RootViewRig.Close(without); }
            var with = RootViewRig.Show(model, new DetailView { VoiceQuestion = rig.Question });
            try
            {
                Assert.Single(RootViewRig.All<WelcomeVoiceSection>(with));
                Assert.False(rig.Question.WelcomeGone);
            }
            finally { RootViewRig.Close(with); }
        });
        });
    }

    [Fact]
    [Trait("Feature", "WV-1")]
    [Trait("Decision", "ED-289")]
    public void A_project_ends_welcome_the_section_goes_and_the_question_knows()
    {
        var rig = VoiceQuestionRig.Pc(); // built off the UI loop: its probe is awaited
        headless.OnUiThread(() =>
        {
        using var harness = new AppModelHarness();
        var model = harness.MakeModel();
        var directory = harness.NewFolder("project");
        var host = harness.Main.OnMain(() =>
        {
            var shown = RootViewRig.Show(model, new DetailView { VoiceQuestion = rig.Question });
            Assert.Single(RootViewRig.All<WelcomeVoiceSection>(shown));
            model.AddProject(new Project("Project", directory).Url);
            return shown;
        });
        try
        {
            harness.Main.OnMain(() => { }); // the main loop delivers the observed change
            harness.Main.OnMain(() =>
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                RootViewRig.Layout(host);
                Assert.Empty(RootViewRig.All<WelcomeVoiceSection>(host));
                Assert.True(rig.Question.WelcomeGone);
                Assert.Equal(VoiceQuestionState.Gone, rig.Question.State);
            });
        }
        finally { harness.Main.OnMain(() => RootViewRig.Close(host)); }
    });
    }
}
