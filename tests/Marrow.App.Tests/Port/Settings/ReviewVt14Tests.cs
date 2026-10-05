using Marrow.App.Port.Settings;
using Marrow.App.Port.Voice;
using Marrow.Core.Port.Voice;
using Marrow.Testing;
using Marrow.App.Tests.Fixtures;
using Marrow.Services.Port.Voice.Models;

namespace Marrow.App.Tests.Port.Settings;

// Independent review of VT-14 (red until fixed): the trash armed for one variant must not stay armed for another.
[Collection("Headless")]
public sealed class ReviewVt14Tests(HeadlessSession headless)
{
    [Fact]
    [Trait("Spec", "V-T95")]
    [Trait("Decision", "ED-267")] // Q-V-13 B: the second click confirms the removal of the file the first one armed
    public void REVIEW_Arming_the_trash_does_not_survive_a_change_of_the_variant_it_would_remove() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var backend = (ScriptedSpeechModelBackend)scene.Backend;
        var row = scene.Page.SpeechModel.ModelList.Rows.Single(r => r.ModelName == "Base");
        row.VersionButton.Activate(); // the versions fold open
        scene.Advance(700);
        row.SetPointerOver(true);
        scene.Advance(400);
        row.StateColumn.Trash.Activate(); // armed for base-q5_1 (Compact)
        Assert.True(row.StateColumn.Trash.Armed);
        row.Versions.Select("Balanced"); // the pointer is still in the row; the trash now stands for base-q8_0
        Assert.Equal("base-q8_0", row.ChosenFile.Id);
        Assert.False(row.StateColumn.Trash.Armed); // one click must not remove a file the user did not arm it for
        row.StateColumn.Trash.Activate();
        Assert.Empty(backend.Removed);
    }, arrange: model => model.Settings.VoiceTyping = model.Settings.VoiceTyping with { Model = "tiny-q5_1" }, backend: Installed("tiny-q5_1", "base-q5_1", "base-q8_0"));

    // The start-up composition hands the model store `StoredTyping().Model` as the model in use (VoiceComposition.Install): the persisted value, which AppSettings' write coalescer (CZ-13.5)
    // holds back for 250 ms when voiceTyping was written a moment before. SpeechModelController.Answer writes voiceTyping twice in a row (the answer, then the model), so for that long the store
    // (and every row's state face and the Remove guard) reads the model before the answer.
    [Fact]
    [Trait("Spec", "V-T95")]
    [Trait("Decision", "ED-267")] // CZ-13.5: the readers bound at start-up see the settings object's value, not the store a write behind
    public void REVIEW_The_model_in_use_the_store_reads_is_the_one_just_selected() => headless.OnUiThread(() =>
    {
        using var harness = new AppModelHarness();
        var settings = harness.MakeModel().Settings;
        using var actor = Marrow.Core.Port.MainActor.Executor.Override(Marrow.UI.Port.AvaloniaMainExecutor.Instance);
        settings.VoiceTyping = settings.VoiceTyping with { SlowChoice = LiveWordsChoice.Live };
        settings.VoiceTyping = settings.VoiceTyping with { Model = "tiny-q5_1" };
        Assert.Equal("tiny-q5_1", VoiceComposition.StoredTyping().Model);
    });

    private static ScriptedSpeechModelBackend Installed(params string[] ids)
    {
        var backend = new ScriptedSpeechModelBackend();
        backend.Install(ids);
        return backend;
    }
}
