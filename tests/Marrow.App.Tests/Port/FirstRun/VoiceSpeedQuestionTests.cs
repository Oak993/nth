using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;

namespace Marrow.App.Tests.Port.FirstRun;

// WV-1 (BUILD-PLAN row WV-1, OD-0098 welcome-voice pick, Q-V-1 C): the Welcome voice question as a state machine over a scripted preparation, pc and slow profiles.
public sealed class VoiceSpeedQuestionTests
{
    [Fact]
    [Trait("Feature", "WV-1")]
    [Trait("Decision", "ED-289")]
    public void The_question_starts_in_ask_and_touches_nothing()
    {
        var rig = VoiceQuestionRig.Pc();
        rig.Prep.Models.ThrowOnAnyTransfer = true;
        Assert.Equal(VoiceQuestionState.Ask, rig.Question.State);
        Assert.False(rig.Question.WelcomeGone);
        Assert.False(rig.Question.AsksAtOrb);
        Assert.False(rig.Prep.Preparation.HasConsent);
        Assert.Null(rig.Record);
    }

    [Fact]
    [Trait("Feature", "WV-1")]
    [Trait("Decision", "ED-289")]
    public void No_downloads_nothing_even_with_a_downloader_that_throws()
    {
        var rig = VoiceQuestionRig.Pc();
        rig.Prep.Models.ThrowOnAnyTransfer = true;
        rig.Question.No();
        Assert.Equal([VoiceQuestionState.Ask, VoiceQuestionState.No], rig.States);
        Assert.False(rig.Prep.Preparation.HasConsent);
        Assert.Empty(rig.Prep.Models.Installs);
        Assert.Empty(rig.Prep.Models.Verifies);
        Assert.Empty(rig.Prep.Engine.Loads);
        Assert.Null(rig.Record);
    }

    [Fact]
    [Trait("Feature", "WV-1")]
    [Trait("Decision", "ED-289")]
    public async Task No_after_a_yes_changes_nothing()
    {
        var rig = VoiceQuestionRig.Pc();
        await rig.Question.YesAsync(null, CancellationToken.None);
        rig.Question.No();
        Assert.Equal(VoiceQuestionState.Ready, rig.Question.State);
    }

    [Fact]
    [Trait("Feature", "WV-1")]
    [Trait("Decision", "ED-289")]
    public async Task Yes_on_a_pc_grants_consent_downloads_and_times_then_is_ready()
    {
        var rig = VoiceQuestionRig.Pc();
        await rig.Question.YesAsync(rig.Prep.Items, CancellationToken.None);
        Assert.Equal([VoiceQuestionState.Ask, VoiceQuestionState.Yes, VoiceQuestionState.Ready], rig.States);
        Assert.True(rig.Prep.Preparation.HasConsent);
        Assert.Equal(["large-v3-turbo-q8_0"], rig.Prep.Models.Installs);
        Assert.NotNull(rig.Record);
        Assert.True(rig.Record!.LiveWords);
        Assert.False(rig.Question.AsksAtOrb);
        Assert.NotEmpty(rig.Prep.Items.Of(VoicePreparation.DownloadId));
    }

    [Fact]
    [Trait("Feature", "WV-1")]
    [Trait("Decision", "ED-289")]
    public async Task Yes_is_in_yes_while_the_download_runs()
    {
        var rig = VoiceQuestionRig.Pc();
        rig.Prep.Models.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = rig.Question.YesAsync(null, CancellationToken.None);
        Assert.Equal(VoiceQuestionState.Yes, rig.Question.State);
        Assert.False(run.IsCompleted);
        rig.Prep.Models.Gate.SetResult();
        await run;
        Assert.Equal(VoiceQuestionState.Ready, rig.Question.State);
    }

    [Fact]
    [Trait("Feature", "WV-1")]
    [Trait("Decision", "ED-289")]
    public async Task Yes_on_a_slow_pc_asks_the_slow_question_and_stores_no_answer()
    {
        var rig = VoiceQuestionRig.Slow();
        await rig.Question.YesAsync(null, CancellationToken.None);
        Assert.Equal([VoiceQuestionState.Ask, VoiceQuestionState.Yes, VoiceQuestionState.Slow], rig.States);
        Assert.False(rig.Record!.LiveWords);
        Assert.Null(rig.Prep.Settings.Typing.SlowChoice);
        Assert.Null(rig.Prep.Settings.Typing.SlowChoiceKey);
        Assert.False(rig.Question.AsksAtOrb);
    }

    [Fact]
    [Trait("Feature", "WV-1")]
    [Trait("Decision", "ED-289")]
    public async Task Answering_live_stores_the_answer_with_its_hardware_part_and_takes_the_smaller_model_with_live_words()
    {
        var rig = VoiceQuestionRig.Slow();
        await rig.Question.YesAsync(null, CancellationToken.None);
        var kept = rig.Record!.Model;
        await rig.Question.AnswerAsync(LiveWordsChoice.Live, CancellationToken.None);
        Assert.Equal(VoiceQuestionState.SlowLive, rig.Question.State);
        Assert.Equal(LiveWordsChoice.Live, rig.Prep.Settings.Typing.SlowChoice);
        Assert.Equal(rig.Prep.Preparation.HardwarePart, rig.Prep.Settings.Typing.SlowChoiceKey);
        var smaller = SelfTestPlan.ApplyAnswer(kept, LiveWordsChoice.Live).ModelId;
        Assert.NotEqual(kept, smaller);
        Assert.Equal(smaller, rig.Record!.Model);
        Assert.True(rig.Record.LiveWords);
        Assert.Contains(smaller, rig.Prep.Models.Installs);
    }

    [Fact]
    [Trait("Feature", "WV-1")]
    [Trait("Decision", "ED-289")]
    public async Task Answering_accurate_keeps_the_model_with_text_after_you_stop()
    {
        var rig = VoiceQuestionRig.Slow();
        await rig.Question.YesAsync(null, CancellationToken.None);
        var kept = rig.Record!.Model;
        var installs = rig.Prep.Models.Installs.Count;
        await rig.Question.AnswerAsync(LiveWordsChoice.Accurate, CancellationToken.None);
        Assert.Equal(VoiceQuestionState.SlowAcc, rig.Question.State);
        Assert.Equal(LiveWordsChoice.Accurate, rig.Prep.Settings.Typing.SlowChoice);
        Assert.Equal(rig.Prep.Preparation.HardwarePart, rig.Prep.Settings.Typing.SlowChoiceKey);
        Assert.Equal(kept, rig.Record!.Model);
        Assert.False(rig.Record.LiveWords);
        Assert.Equal(installs, rig.Prep.Models.Installs.Count);
    }

    [Fact]
    [Trait("Feature", "WV-1")]
    [Trait("Decision", "ED-289")]
    public async Task An_answer_outside_the_slow_question_changes_nothing()
    {
        var rig = VoiceQuestionRig.Pc();
        await rig.Question.AnswerAsync(LiveWordsChoice.Live, CancellationToken.None);
        Assert.Equal(VoiceQuestionState.Ask, rig.Question.State);
        await rig.Question.YesAsync(null, CancellationToken.None);
        await rig.Question.AnswerAsync(LiveWordsChoice.Live, CancellationToken.None);
        Assert.Equal(VoiceQuestionState.Ready, rig.Question.State);
        Assert.Null(rig.Prep.Settings.Typing.SlowChoice);
    }

    [Fact]
    [Trait("Feature", "WV-1")]
    [Trait("Decision", "ED-289")]
    public async Task A_failed_run_returns_to_ask_with_nothing_stored_and_the_reason_kept()
    {
        var rig = VoiceQuestionRig.Pc();
        rig.Prep.Models.Outcome = Marrow.Services.Port.Voice.Models.ModelDownloadStatus.Failed;
        await rig.Question.YesAsync(null, CancellationToken.None);
        Assert.Equal([VoiceQuestionState.Ask, VoiceQuestionState.Yes, VoiceQuestionState.Ask], rig.States);
        Assert.Null(rig.Record);
        Assert.False(string.IsNullOrEmpty(rig.Question.Error));
    }

    [Fact]
    [Trait("Feature", "WV-1")]
    [Trait("Decision", "ED-289")]
    public void Closing_welcome_in_ask_or_no_is_gone_and_a_later_yes_is_ignored()
    {
        var ask = VoiceQuestionRig.Pc();
        ask.Question.CloseWelcome();
        Assert.Equal(VoiceQuestionState.Gone, ask.Question.State);
        Assert.True(ask.Question.WelcomeGone);
        var no = VoiceQuestionRig.Pc();
        no.Question.No();
        no.Question.CloseWelcome();
        Assert.Equal(VoiceQuestionState.Gone, no.Question.State);
    }

    [Fact]
    [Trait("Feature", "WV-1")]
    [Trait("Decision", "ED-289")]
    public async Task Closing_welcome_on_an_open_slow_question_keeps_it_for_the_next_orb_click()
    {
        var rig = VoiceQuestionRig.Slow();
        await rig.Question.YesAsync(null, CancellationToken.None);
        rig.Question.CloseWelcome();
        Assert.Equal(VoiceQuestionState.Slow, rig.Question.State);
        Assert.True(rig.Question.WelcomeGone);
        Assert.True(rig.Question.AsksAtOrb);
        await rig.Question.AnswerAsync(LiveWordsChoice.Accurate, CancellationToken.None);
        Assert.Equal(VoiceQuestionState.SlowAcc, rig.Question.State);
        Assert.False(rig.Question.AsksAtOrb);
    }

    [Fact]
    [Trait("Feature", "WV-1")]
    [Trait("Decision", "ED-289")]
    public async Task Closing_welcome_while_the_model_downloads_shows_the_slow_question_at_the_orb_when_the_test_is_slow()
    {
        var rig = VoiceQuestionRig.Slow();
        rig.Prep.Models.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = rig.Question.YesAsync(null, CancellationToken.None);
        rig.Question.CloseWelcome();
        Assert.Equal(VoiceQuestionState.Yes, rig.Question.State);
        rig.Prep.Models.Gate.SetResult();
        await run;
        Assert.Equal(VoiceQuestionState.Slow, rig.Question.State);
        Assert.True(rig.Question.AsksAtOrb);
    }

    [Fact]
    [Trait("Feature", "WV-1")]
    [Trait("Decision", "ED-289")]
    public async Task Closing_welcome_after_ready_or_an_answer_is_gone()
    {
        var pc = VoiceQuestionRig.Pc();
        await pc.Question.YesAsync(null, CancellationToken.None);
        pc.Question.CloseWelcome();
        Assert.Equal(VoiceQuestionState.Gone, pc.Question.State);
        var slow = VoiceQuestionRig.Slow();
        await slow.Question.YesAsync(null, CancellationToken.None);
        await slow.Question.AnswerAsync(LiveWordsChoice.Live, CancellationToken.None);
        slow.Question.CloseWelcome();
        Assert.Equal(VoiceQuestionState.Gone, slow.Question.State);
    }
}
