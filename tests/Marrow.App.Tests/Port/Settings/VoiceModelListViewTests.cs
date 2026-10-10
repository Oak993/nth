using Avalonia.Automation;
using Avalonia.Controls;
using Marrow.App.Port.Settings;
using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Models;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Tests.Port.Settings;

// VT-14, the Speech model section's model list (voice-SPEC V-16.3.3, V-16.3.4, V-T94, V-T95, V-T96; OD-0099 Q-V-13 B; OD-0076): the six rows, their versions, meters, state column, progress,
// badge and trash, and the footer. The store and the downloads are a scripted double; nothing is fetched or removed.
[Collection("Headless")]
public sealed class VoiceModelListViewTests(HeadlessSession headless)
{
    private static VoiceModelList List(VoiceTypingScene scene) => scene.Page.SpeechModel.ModelList;

    private static VoiceModelRow Row(VoiceTypingScene scene, string name) => List(scene).Rows.Single(r => r.ModelName == name);

    private static ScriptedSpeechModelBackend Installed(params string[] ids)
    {
        var backend = new ScriptedSpeechModelBackend();
        backend.Install(ids);
        return backend;
    }

    private static void WithModel(AppModel model, string id) => model.Settings.VoiceTyping = model.Settings.VoiceTyping with { Model = id };

    private static string[] Texts(Control root) => LogicalQuery.SelfAndDescendants<TextBlock>(root).Select(block => block.Text ?? "").Where(text => text.Length > 0).ToArray();

    [Fact]
    [Trait("Spec", "V-T94")]
    [Trait("Decision", "ED-267")] // V-16.3.3: the caption, its subtitle and six rows in the design's order, with the Q-V-14 notes
    public void The_list_has_the_caption_and_six_models_in_order() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var texts = Texts(List(scene));
        Assert.Contains("Whisper", texts);
        Assert.Contains("OpenAI, 99 languages. Speed as on a graphics card; the processor alone is slower", texts);
        Assert.Equal(["Tiny", "Base", "Small", "Medium", "Large v3 Turbo", "Large v3"], List(scene).Rows.Select(r => r.ModelName));
        Assert.Equal(["English only is a little more accurate for English and knows no other language."], List(scene).Rows.Take(3).Select(r => r.Note.Text).Distinct());
        Assert.Equal("", Row(scene, "Medium").Note.Text); // Q-V-14: the translate notes are dropped
        Assert.Equal("About as accurate as Large v3, far quicker; it does not translate.", Row(scene, "Large v3 Turbo").Note.Text);
        Assert.Equal("", Row(scene, "Large v3").Note.Text);
    });

    [Fact]
    [Trait("Spec", "V-T94")]
    [Trait("Decision", "ED-267")] // V-16.3.3 (ED): with nothing in use and nothing recommended every row shows Compact
    public void Rows_show_Compact_until_something_is_in_use_or_recommended() => VoiceTypingPageRig.Run(headless, scene =>
    {
        Assert.Equal(["Compact, 32 MB", "Compact, 60 MB", "Compact, 190 MB", "Compact, 539 MB", "Compact, 574 MB", "Compact, 1.08 GB"], List(scene).Rows.Select(r => r.VersionButton.Label.Text));
    });

    [Fact]
    [Trait("Spec", "V-T94")]
    [Trait("Decision", "ED-267")] // V-16.3.3: the in-use variant shows in its row, with its English only form
    public void The_in_use_variant_shows_in_its_row() => VoiceTypingPageRig.Run(headless, scene =>
    {
        Assert.Equal("Balanced, 264 MB, English only", Row(scene, "Small").VersionButton.Label.Text);
        Assert.True(Row(scene, "Small").EnglishSwitch!.Checked);
        Assert.Equal("Balanced", Row(scene, "Small").Versions.SelectedId);
        Assert.Equal(VoiceModelState.InUse, Row(scene, "Small").StateColumn.State);
        Assert.Equal("Compact, 32 MB", Row(scene, "Tiny").VersionButton.Label.Text);
    }, arrange: model => WithModel(model, "small.en-q8_0"), backend: Installed("small.en-q8_0"));

    [Fact]
    [Trait("Spec", "V-T94")]
    [Trait("Decision", "ED-267")] // V-16.3.3: the version button folds the versions open; one row's at a time; the chevron turns
    public void The_version_button_folds_the_versions_open_one_row_at_a_time() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var small = Row(scene, "Small");
        var medium = Row(scene, "Medium");
        Assert.False(small.VersionFold.Open);
        small.VersionButton.Activate();
        Assert.True(small.VersionFold.Open);
        Assert.Equal("Small", List(scene).OpenModel);
        scene.Advance(80);
        Assert.InRange(small.VersionFold.Fraction, .01, .99);
        Assert.InRange(small.VersionButton.Chevron.Angle, .1, 179.9);
        scene.Advance(600);
        Assert.Equal(1, small.VersionFold.Fraction);
        Assert.Equal(180, small.VersionButton.Chevron.Angle, 0.5);
        medium.VersionButton.Activate();
        Assert.False(small.VersionFold.Open);
        Assert.True(medium.VersionFold.Open);
        medium.VersionButton.Activate();
        Assert.False(medium.VersionFold.Open);
        Assert.Null(List(scene).OpenModel);
        scene.Advance(700);
        Assert.Equal(0, medium.VersionFold.Fraction);
    });

    [Fact]
    [Trait("Spec", "V-T94")]
    [Trait("Decision", "ED-267")] // V-16.3.3: a segment per version `{Version} · {size}` and, for Tiny to Medium, an English only switch
    public void The_versions_are_a_segmented_control_and_Tiny_to_Medium_have_an_English_only_switch() => VoiceTypingPageRig.Run(headless, scene =>
    {
        Assert.Equal(["Compact · 574 MB", "Balanced · 874 MB", "Full · 1.62 GB"], Row(scene, "Large v3 Turbo").Versions.Buttons.Select(b => AutomationProperties.GetName(b)));
        Assert.Equal(["Compact · 1.08 GB", "Full · 3.1 GB"], Row(scene, "Large v3").Versions.Buttons.Select(b => AutomationProperties.GetName(b)));
        Assert.Equal("Version of Small", AutomationProperties.GetName(Row(scene, "Small").Versions));
        Assert.All(new[] { "Tiny", "Base", "Small", "Medium" }, name => Assert.NotNull(Row(scene, name).EnglishSwitch));
        Assert.All(new[] { "Large v3 Turbo", "Large v3" }, name => Assert.Null(Row(scene, name).EnglishSwitch));
        Assert.Equal("English only", AutomationProperties.GetName(Row(scene, "Tiny").EnglishSwitch!));
    });

    [Fact]
    [Trait("Spec", "V-T94")]
    [Trait("Decision", "ED-267")] // V-16.3.3: picking a version or the English only form changes the button's text, crossfading, and the state of the variant shown
    public void Picking_a_version_or_English_only_changes_the_meta_text_and_the_state() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var small = Row(scene, "Small");
        small.VersionButton.Activate();
        scene.Advance(700);
        small.Versions.Select("Balanced");
        Assert.Equal("Balanced, 264 MB", small.VersionButton.Label.Text);
        Assert.Equal(0, small.VersionButton.Label.IncomingOpacity);
        scene.Advance(400);
        Assert.Equal(1, small.VersionButton.Label.IncomingOpacity);
        Assert.Equal(VoiceModelState.Installed, small.StateColumn.State); // small-q8_0 is installed, not in use
        small.EnglishSwitch!.Toggle();
        Assert.Equal("Balanced, 264 MB, English only", small.VersionButton.Label.Text);
        Assert.Equal("small.en-q8_0", small.ChosenFile.Id);
        Assert.Equal(VoiceModelState.NotInstalled, small.StateColumn.State);
        small.EnglishSwitch.Toggle();
        Assert.Equal("small-q8_0", small.ChosenFile.Id);
    }, backend: Installed("small-q8_0"));

    [Fact]
    [Trait("Spec", "V-T94")]
    [Trait("Decision", "ED-267")] // V-16.3.3: the meters are named with both scores and show them as dots
    public void The_meters_are_named_and_show_the_scores_as_dots() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var turbo = Row(scene, "Large v3 Turbo").Meters;
        Assert.Equal("Large v3 Turbo: speed 4 of 5, accuracy 5 of 5", AutomationProperties.GetName(turbo));
        Assert.Equal((4, 5), (turbo.Speed.Lit, turbo.Accuracy.Lit));
        Assert.Equal(112, Row(scene, "Tiny").Meters.Width);
        Assert.Equal((5, 1), (Row(scene, "Tiny").Meters.Speed.Lit, Row(scene, "Tiny").Meters.Accuracy.Lit));
        Assert.Contains("Speed", Texts(turbo));
        Assert.Contains("Accuracy", Texts(turbo));
    });

    [Fact]
    [Trait("Spec", "V-T94")]
    [Trait("Decision", "ED-267")] // V-16.3.3: the state column shows In use / progress / Use / Download for the variant shown, one face at a time, crossfading
    public void The_state_column_shows_one_face_per_install_state() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var backend = (ScriptedSpeechModelBackend)scene.Backend;
        Assert.Equal(VoiceModelState.InUse, Row(scene, "Large v3 Turbo").StateColumn.State);
        Assert.Equal(VoiceModelState.Installed, Row(scene, "Base").StateColumn.State);
        Assert.Equal(VoiceModelState.NotInstalled, Row(scene, "Tiny").StateColumn.State);
        var column = Row(scene, "Tiny").StateColumn;
        Assert.Equal(1, column.FaceOpacity(VoiceModelState.NotInstalled));
        Assert.Equal(0, column.FaceOpacity(VoiceModelState.InUse));
        Assert.Equal(0.94, column.FaceScale(VoiceModelState.InUse), 0.001);
        Assert.Equal("Download", AutomationProperties.GetName(column.DownloadButton));
        Assert.Equal("Use", AutomationProperties.GetName(Row(scene, "Base").StateColumn.UseButton));
        Assert.Equal("Cancel the download", AutomationProperties.GetName(column.CancelButton));
    }, arrange: model => WithModel(model, "large-v3-turbo-q5_0"), backend: Installed("large-v3-turbo-q5_0", "base-q5_1"));

    [Fact]
    [Trait("Spec", "V-T95")]
    [Trait("Decision", "ED-267")] // V-16.3.3: Download downloads without selecting; the row shows the percentage and the 2 pt bar; the faces crossfade as the state changes
    public void Download_shows_the_progress_and_the_bar_and_ends_as_Use() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var backend = (ScriptedSpeechModelBackend)scene.Backend;
        var tiny = Row(scene, "Tiny");
        tiny.StateColumn.DownloadButton.Activate();
        Assert.Equal(["tiny-q5_1"], backend.Started);
        Assert.Equal(VoiceModelState.Downloading, tiny.StateColumn.State);
        Assert.Equal("0%", tiny.StateColumn.PercentText);
        scene.Advance(60);
        Assert.InRange(tiny.StateColumn.FaceOpacity(VoiceModelState.NotInstalled), .01, .99); // the Download face is on its way out
        Assert.InRange(tiny.Bar.Visibility, .01, .99); // the bar fades in
        scene.Advance(600);
        Assert.Equal(0, tiny.StateColumn.FaceOpacity(VoiceModelState.NotInstalled));
        Assert.Equal(1, tiny.StateColumn.FaceOpacity(VoiceModelState.Downloading));
        backend.Transfer("tiny-q5_1").Move(.37);
        scene.Until(() => tiny.StateColumn.PercentText == "37%");
        Assert.Equal(.37, tiny.Bar.Target, 0.001);
        scene.Advance(50);
        Assert.InRange(tiny.Bar.Fraction, 0, .37); // linear over 0.18 s
        scene.Advance(300);
        Assert.Equal(.37, tiny.Bar.Fraction, 0.001);
        Assert.Null(scene.Model.Settings.VoiceTyping.Model); // Download never selects
        backend.Finish("tiny-q5_1");
        scene.Until(() => tiny.StateColumn.State == VoiceModelState.Installed);
        scene.Advance(700);
        Assert.Equal(1, tiny.StateColumn.FaceOpacity(VoiceModelState.Installed));
        Assert.Equal(0, tiny.Bar.Visibility);
        Assert.Null(scene.Model.Settings.VoiceTyping.Model);
    }, backend: new ScriptedSpeechModelBackend());

    [Fact]
    [Trait("Spec", "V-T95")]
    [Trait("Decision", "ED-267")] // V-9.5 item 4: the cross cancels
    public void The_cross_cancels_a_download_and_the_row_goes_back_to_Download() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var tiny = Row(scene, "Tiny");
        tiny.StateColumn.DownloadButton.Activate();
        scene.Advance(600);
        tiny.StateColumn.CancelButton.Activate();
        scene.Until(() => tiny.StateColumn.State == VoiceModelState.NotInstalled);
        scene.Advance(700);
        Assert.Equal(1, tiny.StateColumn.FaceOpacity(VoiceModelState.NotInstalled));
        Assert.Equal(0, tiny.Bar.Visibility);
    }, backend: new ScriptedSpeechModelBackend());

    [Fact]
    [Trait("Spec", "V-T95")]
    [Trait("Decision", "ED-267")] // V-16.3.3: Use selects an installed variant and its row says In use, the one before says Use
    public void Use_selects_the_variant_and_the_rows_follow() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var base_ = Row(scene, "Base");
        var tiny = Row(scene, "Tiny");
        Assert.Equal(VoiceModelState.InUse, tiny.StateColumn.State);
        base_.StateColumn.UseButton.Activate();
        Assert.Equal("base-q5_1", scene.Model.Settings.VoiceTyping.Model);
        // the settings change is tracked: the page follows on the next turn
        VoiceTypingScene.Sync();
        Assert.Equal(VoiceModelState.InUse, base_.StateColumn.State);
        Assert.Equal(VoiceModelState.Installed, tiny.StateColumn.State);
    }, arrange: model => WithModel(model, "tiny-q5_1"), backend: Installed("tiny-q5_1", "base-q5_1"));

    [Fact]
    [Trait("Spec", "V-T94")]
    [Trait("Decision", "ED-267")] // V-16.3.3: the Recommended badge sits on the recommended model's row and scales in from .85
    public void The_Recommended_badge_scales_in_on_the_recommended_row() => VoiceTypingPageRig.Run(headless, scene =>
    {
        Assert.All(List(scene).Rows, row => Assert.False(row.Badge.Shown));
        Assert.Equal(0, Row(scene, "Large v3 Turbo").Badge.BadgeOpacity);
        scene.Controller.Check();
        scene.Until(() => scene.Controller.Phase == HardwareCardPhase.Done);
        Assert.True(Row(scene, "Large v3 Turbo").Badge.Shown);
        Assert.All(List(scene).Rows.Where(r => r.ModelName != "Large v3 Turbo"), row => Assert.False(row.Badge.Shown));
        scene.Advance(60);
        Assert.InRange(Row(scene, "Large v3 Turbo").Badge.BadgeOpacity, .01, .99);
        Assert.InRange(Row(scene, "Large v3 Turbo").Badge.BadgeScale, .851, .999);
        scene.Advance(600);
        Assert.Equal(1, Row(scene, "Large v3 Turbo").Badge.BadgeOpacity);
        Assert.Equal(1, Row(scene, "Large v3 Turbo").Badge.BadgeScale);
        Assert.Contains("Recommended", Texts(Row(scene, "Large v3 Turbo")));
        // the row shows the recommended variant until the user picks another or one is in use (ED)
        Assert.Equal("Balanced, 874 MB", Row(scene, "Large v3 Turbo").VersionButton.Label.Text);
    }, probe: GatedHardwareProbe.OwnersDesktop(gated: false), backend: new ScriptedSpeechModelBackend());

    [Fact]
    [Trait("Spec", "V-T96")]
    [Trait("Decision", "ED-267")] // V-16.3.4: the footer totals the installed models and follows them, crossfading
    public void The_footer_totals_the_installed_models_and_follows_them() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var backend = (ScriptedSpeechModelBackend)scene.Backend;
        Assert.Equal("Installed: 0 MB, in nth’s models folder", List(scene).Footer.Text);
        Row(scene, "Tiny").StateColumn.DownloadButton.Activate();
        backend.Finish("tiny-q5_1");
        scene.Until(() => List(scene).Footer.Text == "Installed: 32 MB, in nth’s models folder");
        Assert.Equal(0, List(scene).Footer.IncomingOpacity);
        scene.Advance(400);
        Assert.Equal(1, List(scene).Footer.IncomingOpacity);
    }, backend: new ScriptedSpeechModelBackend());

    [Fact]
    [Trait("Spec", "V-T96")]
    [Trait("Decision", "ED-267")] // V-16.3.4 / V-9.2: sizes as the design formats them
    public void The_footer_uses_gigabytes_from_a_thousand_megabytes() => VoiceTypingPageRig.Run(headless, scene =>
    {
        Assert.Equal("Installed: 3.1 GB, in nth’s models folder", List(scene).Footer.Text);
    }, backend: Installed("large-v3"));

    // ---- Remove (OD-0099 Q-V-13 B) -------------------------------------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Spec", "V-T95")]
    [Trait("Decision", "ED-267")] // Q-V-13 B: the trash is shown on hover for an installed model that is not in use, and a second click removes it
    public void The_trash_shows_on_hover_and_a_second_click_removes_the_model() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var backend = (ScriptedSpeechModelBackend)scene.Backend;
        var base_ = Row(scene, "Base");
        var trash = base_.StateColumn.Trash;
        Assert.Equal(0, trash.RevealOpacity);
        Assert.False(trash.IsHitTestVisible);
        base_.SetPointerOver(true);
        scene.Advance(60);
        Assert.InRange(trash.RevealOpacity, .01, .99); // it fades in, it does not cut
        scene.Advance(300);
        Assert.Equal(1, trash.RevealOpacity);
        Assert.Equal("Remove Base, compact 60 MB", AutomationProperties.GetName(trash));
        var inkBefore = trash.InkNow;
        trash.Activate(); // the first click arms it
        Assert.True(trash.Armed);
        Assert.Empty(backend.Removed);
        Assert.Equal("Click again to remove Base, compact 60 MB", AutomationProperties.GetName(trash));
        scene.Advance(300);
        Assert.NotEqual(inkBefore, trash.InkNow); // the glyph moved to the error colour
        trash.Activate(); // the second removes it
        Assert.Equal(["base-q5_1"], backend.Removed);
        VoiceTypingScene.Sync();
        Assert.Equal(VoiceModelState.NotInstalled, base_.StateColumn.State);
        Assert.Equal("Installed: 32 MB, in nth’s models folder", List(scene).Footer.Text);
    }, arrange: model => WithModel(model, "tiny-q5_1"), backend: Installed("tiny-q5_1", "base-q5_1"));

    [Fact]
    [Trait("Spec", "V-T95")]
    [Trait("Decision", "ED-267")] // Q-V-13 B: leaving the row disarms the trash, so a stray second click later removes nothing
    public void Leaving_the_row_disarms_the_trash() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var backend = (ScriptedSpeechModelBackend)scene.Backend;
        var base_ = Row(scene, "Base");
        base_.SetPointerOver(true);
        scene.Advance(400);
        base_.StateColumn.Trash.Activate();
        Assert.True(base_.StateColumn.Trash.Armed);
        base_.SetPointerOver(false);
        Assert.False(base_.StateColumn.Trash.Armed);
        scene.Advance(400);
        Assert.Equal(0, base_.StateColumn.Trash.RevealOpacity);
        Assert.Empty(backend.Removed);
    }, arrange: model => WithModel(model, "tiny-q5_1"), backend: Installed("tiny-q5_1", "base-q5_1"));

    [Fact]
    [Trait("Spec", "V-T95")]
    [Trait("Decision", "ED-267")] // Q-V-13 B: the model in use has no trash (its face is In use), and a model not installed has none either
    public void The_model_in_use_and_a_model_not_installed_cannot_be_removed() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var backend = (ScriptedSpeechModelBackend)scene.Backend;
        var tiny = Row(scene, "Tiny");
        tiny.SetPointerOver(true);
        scene.Advance(400);
        Assert.False(tiny.StateColumn.Trash.IsEffectivelyEnabled);
        Assert.False(Row(scene, "Small").StateColumn.Trash.IsEffectivelyEnabled);
        Assert.Empty(backend.Removed);
    }, arrange: model => WithModel(model, "tiny-q5_1"), backend: Installed("tiny-q5_1"));

    [Fact]
    [Trait("Spec", "V-T95")]
    [Trait("Decision", "ED-267")] // Q-V-13 B: the trash is reachable from the keyboard: focusing it shows it
    public void Focusing_the_trash_shows_it() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var base_ = Row(scene, "Base");
        base_.StateColumn.Trash.Focus();
        VoiceTypingScene.Sync();
        scene.Advance(400);
        Assert.True(base_.StateColumn.Trash.Revealed);
        Assert.Equal(1, base_.StateColumn.Trash.RevealOpacity);
    }, arrange: model => WithModel(model, "tiny-q5_1"), backend: Installed("tiny-q5_1", "base-q5_1"));
}
