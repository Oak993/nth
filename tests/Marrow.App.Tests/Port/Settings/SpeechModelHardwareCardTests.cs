using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Port.Settings;
using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Models;
using Marrow.Services.Port.Voice.Probe;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Tests.Port.Settings;

// VT-14, the Speech model section's hardware card (voice-SPEC V-16.3.2, V-10.6, V-T91, V-T93; Q-V-18; OD-0099 Q-V-1 C; OD-0076): the title row, the probe rows resolving one by one, the
// recommendation box, the preference row and the note. The probe, the store and the self-test are scripted doubles; no hardware is read and nothing is fetched.
[Collection("Headless")]
public sealed class SpeechModelHardwareCardTests(HeadlessSession headless)
{
    private const string Candidate = "large-v3-turbo-q8_0";

    private static VoiceHardwareCard Card(VoiceTypingScene scene) => scene.Page.SpeechModel.HardwareCard;

    private static GatedHardwareProbe Probe() => (GatedHardwareProbe)HardwareProbeSeams.Probe.Current;

    private static string[] Texts(Control root) => LogicalQuery.SelfAndDescendants<TextBlock>(root).Select(block => block.Text ?? "").Where(text => text.Length > 0).ToArray();

    private static void WithModel(AppModel model, string id) => model.Settings.VoiceTyping = model.Settings.VoiceTyping with { Model = id };

    private static ScriptedSpeechModelBackend Installed(params string[] ids)
    {
        var backend = new ScriptedSpeechModelBackend();
        backend.Install(ids);
        return backend;
    }

    // Runs a whole check: the button, then every row and the end.
    private static void RunCheck(VoiceTypingScene scene)
    {
        Card(scene).CheckButton.Activate();
        Probe().Release(5);
        scene.Until(() => scene.Controller.Phase == HardwareCardPhase.Done);
        scene.Advance(700);
    }

    [Fact]
    [Trait("Spec", "V-T90")]
    [Trait("Decision", "ED-267")] // V-16.3.2: the section's heading, the card's words and the note, byte for byte
    public void The_hardware_card_has_the_words_of_V_16_3_2() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var card = Card(scene);
        var texts = Texts(scene.Page.SpeechModel);
        Assert.Contains("Speech model", texts);
        Assert.Equal("Recommended for this PC", card.TitleRow.TitleText);
        Assert.Equal("Not checked on this computer yet", card.TitleRow.Detail!.Text);
        Assert.Equal("Check my hardware", card.CheckButton.Label.Text);
        Assert.Equal("The first time voice typing is set up, this check runs by itself and installs the model it recommends. Run it again any time.", card.Note.Text);
        Assert.Equal(["Processor", "Memory", "Graphics", "GPU backend"], card.ProbeRows.Select(r => r.Key));
        Assert.False(card.ProbeFold.Open);
        Assert.False(card.RecommendationFold.Open);
        Assert.False(card.Preference.Fold.Open);
        Assert.Equal("Recommended for this PC", Avalonia.Automation.AutomationProperties.GetName(card.CheckButton)); // the row names its control (CZ-15), as every row of the page does
    });

    [Fact]
    [Trait("Spec", "V-T91")]
    [Trait("Decision", "ED-267")] // V-16.3.2: the section folds away for the cloud engine and comes back for the local one; the gap above the privacy line is the same either way
    public void The_Speech_model_section_folds_away_for_the_cloud_engine() => VoiceTypingPageRig.Run(headless, scene =>
    {
        Assert.Same(scene.Page.SpeechFold, scene.Page.Sections.Children[1]);
        Assert.True(scene.Page.SpeechFold.Open);
        var privacyTop = scene.Page.PrivacyLine.TranslatePoint(default, scene.Page)!.Value.Y;
        scene.Page.EngineControl.Select("cloud");
        Assert.False(scene.Page.SpeechFold.Open);
        scene.Advance(80);
        Assert.InRange(scene.Page.SpeechFold.Fraction, .01, .99); // it folds, it does not cut
        scene.Advance(600);
        Assert.Equal(0, scene.Page.SpeechFold.Fraction);
        var closedTop = scene.Page.PrivacyLine.TranslatePoint(default, scene.Page)!.Value.Y;
        Assert.True(closedTop < privacyTop);
        scene.Page.EngineControl.Select("local");
        scene.Advance(700);
        Assert.Equal(1, scene.Page.SpeechFold.Fraction);
        Assert.Equal(privacyTop, scene.Page.PrivacyLine.TranslatePoint(default, scene.Page)!.Value.Y, 0.5);
    });

    [Fact]
    [Trait("Spec", "V-T91")]
    [Trait("Decision", "ED-267")] // a stored cloud engine opens the page with the section already folded away
    public void A_stored_cloud_engine_opens_the_page_without_the_section() => VoiceTypingPageRig.Run(headless, scene =>
    {
        Assert.False(scene.Page.SpeechFold.Open);
        Assert.Equal(0, scene.Page.SpeechFold.Fraction);
    }, arrange: model => model.Settings.VoiceTyping = model.Settings.VoiceTyping with { Engine = VoiceEngine.Cloud });

    [Fact]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // V-16.3.2, V-10.6: the rows resolve in order as the probe answers, each with a spinner then a check; the recommendation folds open at the end
    public void The_probe_rows_resolve_one_by_one_then_the_recommendation_opens() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var card = Card(scene);
        card.CheckButton.Activate();
        Assert.Equal("Checking this computer", card.TitleRow.Detail!.Text);
        Assert.Equal("Checking", card.CheckButton.Label.Text);
        Assert.True(card.CheckButton.Busy);
        Assert.False(card.CheckButton.IsEnabled); // inert while it runs
        Assert.True(card.ProbeFold.Open);
        Assert.True(card.ProbeRows[0].IsBusy);
        Assert.All(card.ProbeRows.Skip(1), row => Assert.False(row.IsBusy));
        scene.Advance(80);
        Assert.InRange(card.CheckButton.SpinnerRoom, .01, 15.99); // the spinner grows in
        Assert.InRange(card.ProbeFold.Fraction, .01, .99);
        scene.Advance(500);
        Assert.Equal(16, card.CheckButton.SpinnerRoom, 0.01);

        var expected = new[] { "AMD Ryzen 7 9800X3D 8-Core Processor, 8 cores", "31 GB", "NVIDIA GeForce RTX 4080, 16 GB", "Vulkan, working" };
        for (var i = 0; i < 4; i++)
        {
            Probe().Release();
            scene.Until(() => scene.Controller.Rows.Count == i + 1);
            scene.Advance(30);
            Assert.True(card.ProbeRows[i].IsDone);
            Assert.Equal(expected[i], card.ProbeRows[i].Value.Text);
            Assert.False(card.ProbeRows[i].IsBusy);
            if (i < 3) Assert.True(card.ProbeRows[i + 1].IsBusy);
            Assert.False(card.RecommendationFold.Open);
            Assert.Equal("Checking this computer", card.TitleRow.Detail.Text);
        }
        scene.Advance(400);
        Assert.Equal(1, card.ProbeRows[3].CheckOpacity, 0.01);
        Assert.Equal(0, card.ProbeRows[3].SpinnerOpacity, 0.01);
        Assert.All(card.ProbeRows, row => Assert.Equal(1, row.RowOpacity, 0.01));

        Probe().Release();
        scene.Until(() => scene.Controller.Phase == HardwareCardPhase.Done);
        Assert.Equal("Checked just now", card.TitleRow.Detail.Text);
        Assert.Equal("Check again", card.CheckButton.Label.Text);
        Assert.False(card.CheckButton.Busy);
        Assert.True(card.CheckButton.IsEnabled);
        Assert.True(card.RecommendationFold.Open);
        scene.Advance(80);
        Assert.InRange(card.RecommendationFold.Fraction, .01, .99);
        scene.Advance(600);
        Assert.Equal(1, card.RecommendationFold.Fraction);
        Assert.Equal("Large v3 Turbo, balanced 874 MB", card.RecommendationName.Text);
        Assert.Equal(0, card.CheckButton.SpinnerRoom, 0.01);
    }, probe: GatedHardwareProbe.OwnersDesktop(), backend: new ScriptedSpeechModelBackend());

    [Fact]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // V-16.3.2: with a model in use the box offers Use recommended and Keep {model}; with none there is nothing to keep
    public void The_box_offers_Use_recommended_and_Keep_the_current_model() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var card = Card(scene);
        RunCheck(scene);
        Assert.True(card.UseFold.Open);
        Assert.False(card.StateFold.Open);
        Assert.Equal("Use recommended", card.UseButton.Child is TextBlock text ? text.Text : "");
        Assert.True(card.KeepButton.IsVisible);
        Assert.Equal("Keep Small", card.KeepButton.Label.Text);
        Assert.Equal("", card.Reason.Text); // no time was measured, so the reason says nothing (nothing is made up)
        Assert.False(card.ReasonFold.Open);
    }, arrange: model => WithModel(model, "small-q5_1"), probe: GatedHardwareProbe.OwnersDesktop(), backend: Installed("small-q5_1"));

    [Fact]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // V-16.3.2: with no model in use there is no Keep button
    public void With_no_model_in_use_there_is_no_Keep_button() => VoiceTypingPageRig.Run(headless, scene =>
    {
        RunCheck(scene);
        Assert.True(Card(scene).UseFold.Open);
        Assert.False(Card(scene).KeepButton.IsVisible);
    }, probe: GatedHardwareProbe.OwnersDesktop(), backend: new ScriptedSpeechModelBackend());

    [Fact]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // V-16.3.2: Keep swaps the buttons for the state line `You kept {model}`, folding
    public void Keep_swaps_the_buttons_for_You_kept() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var card = Card(scene);
        RunCheck(scene);
        card.KeepButton.Activate();
        Assert.False(card.UseFold.Open);
        Assert.True(card.StateFold.Open);
        Assert.Equal("You kept Small", card.StateText.Text);
        scene.Advance(80);
        Assert.InRange(card.UseFold.Fraction, .01, .99);
        scene.Advance(600);
        Assert.Equal(0, card.UseFold.Fraction);
        Assert.Equal(1, card.StateFold.Fraction);
        Assert.Equal("small-q5_1", scene.Model.Settings.VoiceTyping.Model);
    }, arrange: model => WithModel(model, "small-q5_1"), probe: GatedHardwareProbe.OwnersDesktop(), backend: Installed("small-q5_1"));

    [Fact]
    [Trait("Spec", "V-T95")]
    [Trait("Decision", "ED-267")] // V-16.3.2: Use recommended with the model not there reads `Downloading, then in use`, then `In use` when it arrives
    public void Use_recommended_reads_Downloading_then_in_use_and_then_In_use() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var card = Card(scene);
        var backend = (ScriptedSpeechModelBackend)scene.Backend;
        RunCheck(scene);
        card.UseButton.Activate();
        Assert.False(card.UseFold.Open);
        Assert.True(card.StateFold.Open);
        Assert.Equal("Downloading, then in use", card.StateText.Text);
        scene.Advance(600);
        backend.Transfer(Candidate).Move(.4);
        scene.Until(() => scene.Page.SpeechModel.ModelList.Rows.Single(r => r.ModelName == "Large v3 Turbo").StateColumn.PercentText == "40%");
        backend.Finish(Candidate);
        scene.Until(() => scene.Model.Settings.VoiceTyping.Model == Candidate);
        scene.Advance(80);
        Assert.Equal("In use", card.StateText.Text);
        Assert.False(card.UseFold.Open);
    }, arrange: model => WithModel(model, "small-q5_1"), probe: GatedHardwareProbe.OwnersDesktop(), backend: Installed("small-q5_1"));

    [Fact]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // Q-V-18: after a check ran, a later look reads `Checked on {date}`; a first-run result reads as set up by itself
    public void The_detail_reads_Checked_on_the_date_for_an_older_manual_check_and_by_itself_for_the_first_run() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var detail = Card(scene).TitleRow.Detail!.Text;
        Assert.StartsWith("Checked on ", detail, StringComparison.Ordinal);
        Assert.Contains("2026", detail, StringComparison.Ordinal);
        Assert.Equal("Check again", Card(scene).CheckButton.Label.Text);
    }, arrange: model => model.Settings.VoiceHardware = new VoiceHardwareRecord { Origin = HardwareOrigin.Manual, At = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero), Backend = VoiceBackend.Vulkan });

    [Fact]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // V-16.3.2: the first-run check's own wording
    public void A_first_run_result_reads_Checked_by_itself() => VoiceTypingPageRig.Run(headless, scene =>
    {
        Assert.Equal("Checked by itself when voice typing was set up", Card(scene).TitleRow.Detail!.Text);
    }, arrange: model => model.Settings.VoiceHardware = new VoiceHardwareRecord { Origin = HardwareOrigin.Auto, At = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero) });

    [Fact]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // V-2.5: a probe that cannot run leaves the card as it was; the button comes back
    public void A_failed_check_leaves_the_card_as_it_was() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var card = Card(scene);
        card.CheckButton.Activate();
        scene.Until(() => scene.Controller.Phase == HardwareCardPhase.Idle);
        scene.Advance(600);
        Assert.Equal("Not checked on this computer yet", card.TitleRow.Detail!.Text);
        Assert.Equal("Check my hardware", card.CheckButton.Label.Text);
        Assert.True(card.CheckButton.IsEnabled);
        Assert.False(card.ProbeFold.Open);
        Assert.False(card.RecommendationFold.Open);
    });

    [Fact]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // OD-0076: the title row's detail and the button's label crossfade, and the button's width eases to its label
    public void The_card_changes_crossfade_and_the_capsule_eases_its_width() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var card = Card(scene);
        var before = card.CheckButton.LabelWidth;
        card.CheckButton.Activate();
        Assert.Equal(0, card.TitleRow.Detail!.IncomingOpacity);
        Assert.Equal(1, card.TitleRow.Detail.OutgoingOpacity);
        Assert.Equal(0, card.CheckButton.Label.IncomingOpacity);
        scene.Advance(60);
        Assert.InRange(card.CheckButton.LabelWidth, 1, before - 1); // `Checking` is narrower than `Check my hardware`: it is on its way, not there
        scene.Advance(600);
        Assert.Equal(1, card.TitleRow.Detail.IncomingOpacity);
        Assert.True(card.CheckButton.LabelWidth < before - 20);
    }, probe: GatedHardwareProbe.OwnersDesktop());

    [Fact]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // the spinner turns with the frame clock
    public void The_spinner_turns_with_the_frame_clock() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var card = Card(scene);
        card.CheckButton.Activate();
        scene.Advance(100);
        var spinner = LogicalQuery.SelfAndDescendants<Marrow.UI.Port.Nth.NthSpinner>(card.ProbeRows[0]).Single();
        // the nth spinner turns on the render thread: here it is shown (fading in), 12 pt, and the frame it sent keeps it moving
        Assert.Equal(new Avalonia.Size(12, 12), spinner.Bounds.Size);
        Assert.True(card.ProbeRows[0].SpinnerOpacity > 0);
        Assert.False(spinner.Calm);
        using var painter = new Marrow.UI.Port.Nth.NthSpinnerPainter();
        var frame = spinner.Snapshot();
        Assert.True(painter.Moving(frame, Marrow.UI.Port.Nth.NthFrame.Seconds(frame.Clock)));
    }, probe: GatedHardwareProbe.OwnersDesktop());

    // ---- the preference row (OD-0099 Q-V-1 C) -------------------------------------------------------------------------------------------------------------------

    private static void SlowResult(AppModel model, string modelId, bool live) =>
        model.Settings.VoiceHardware = new VoiceHardwareRecord
        {
            Origin = HardwareOrigin.Auto, Backend = VoiceBackend.Cpu, Candidate = "large-v3-turbo-q5_0", Model = modelId, Rtf = 0.3, LiveWords = live,
            At = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
        };

    [Fact]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // Q-V-1 C, V2 words: offered for a processor result that was too slow for live words; the question is the detail; the accurate model shows chosen
    public void The_preference_row_asks_the_question_on_a_slow_processor_result() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var row = Card(scene).Preference;
        Assert.True(row.Fold.Open);
        Assert.Equal("How should voice typing work here?", row.Row.TitleText);
        Assert.Equal("Your PC can’t write your words live with the accurate model. Which matters more to you?", row.Row.Detail!.Text);
        Assert.Equal(["See my words as I speak", "Get every word right"], row.Options.Buttons.Select(b => Avalonia.Automation.AutomationProperties.GetName(b)));
        Assert.Equal("accurate", row.Options.SelectedId);
    }, arrange: model => { SlowResult(model, "large-v3-turbo-q5_0", live: false); WithModel(model, "large-v3-turbo-q5_0"); });

    [Fact]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // Q-V-1 C: the row is not there for a result that was fast, a GPU result, or no result
    public void The_preference_row_is_absent_where_the_question_does_not_apply() => VoiceTypingPageRig.Run(headless, scene =>
    {
        Assert.False(Card(scene).Preference.Fold.Open);
        Assert.Equal(0, Card(scene).Preference.Fold.Fraction);
    }, arrange: model => SlowResult(model, "large-v3-turbo-q5_0", live: true));

    [Fact]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // Q-V-1 C: choosing is stored with the hardware's key, applied, and the detail follows; the answer round-trips through the page
    public void Choosing_live_words_stores_the_answer_selects_the_smaller_model_and_follows_in_the_detail() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var row = Card(scene).Preference;
        var settings = scene.Model.Settings;
        row.Options.Select("live");
        scene.Until(() => settings.VoiceTyping.SlowChoice == LiveWordsChoice.Live && settings.VoiceTyping.Model == "medium-q8_0");
        scene.Advance(700);
        Assert.Equal("live", row.Options.SelectedId);
        Assert.StartsWith("Uses a smaller model (", row.Row.Detail!.Text, StringComparison.Ordinal);
        Assert.True(settings.VoiceHardware!.LiveWords);
        Assert.NotNull(settings.VoiceTyping.SlowChoiceKey);
        // pressing the chosen option again is no change and stores nothing new
        row.Options.Buttons.Single(b => b.Id == "live").Activate();
        scene.Advance(50);
        Assert.Equal(LiveWordsChoice.Live, settings.VoiceTyping.SlowChoice);

        row.Options.Select("accurate");
        scene.Until(() => settings.VoiceTyping.SlowChoice == LiveWordsChoice.Accurate && settings.VoiceTyping.Model == "large-v3-turbo-q5_0");
        scene.Advance(700);
        Assert.Equal("Keeps the accurate model; your text appears when you stop speaking.", row.Row.Detail.Text);
        Assert.False(settings.VoiceHardware.LiveWords);
        Assert.True(row.Fold.Open); // an answer is stored, so the row stays to be changed back
    }, arrange: model => { SlowResult(model, "large-v3-turbo-q5_0", live: false); WithModel(model, "large-v3-turbo-q5_0"); },
        probe: GatedHardwareProbe.CpuOnlyLaptop(gated: false), backend: Installed("large-v3-turbo-q5_0", "medium-q8_0"));

    [Fact]
    [Trait("Spec", "V-T93")]
    [Trait("Decision", "ED-267")] // a stored answer shows chosen on arrival, with its own detail
    public void A_stored_answer_is_shown_chosen_on_arrival() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var row = Card(scene).Preference;
        Assert.True(row.Fold.Open);
        Assert.Equal("live", row.Options.SelectedId);
        Assert.StartsWith("Uses a smaller model (", row.Row.Detail!.Text, StringComparison.Ordinal);
    }, arrange: model =>
    {
        SlowResult(model, "medium-q8_0", live: true);
        model.Settings.VoiceTyping = model.Settings.VoiceTyping with { Model = "medium-q8_0", SlowChoice = LiveWordsChoice.Live, SlowChoiceKey = "k" };
    });
}
