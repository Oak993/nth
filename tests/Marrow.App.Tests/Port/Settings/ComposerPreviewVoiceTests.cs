using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Marrow.App.Port.Settings;
using Marrow.App.Port.Voice;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Session;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Tests.Port.Settings;

[Collection("Headless")]
[Trait("Feature", "CZ-7b")]
[Trait("Platform", "WINDOWS-ONLY")] // shared LookMotion/AnimatedScalar read Windows' live animation preference.
public sealed class ComposerPreviewVoiceTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-292")] // CZ-T73/75: shared parts, real session, no production audio/model composition.
    public void A_style_pick_starts_from_Rest_and_uses_the_real_voice_components() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig();
        var preview = rig.Page.Stage!.Preview;
        var writes = rig.Defaults.Writes.Count;
        rig.Do(() => rig.Page.Session!.VoicePreviewTouched("lstyle", TimeSpan.Zero));
        Assert.Equal(VoiceSessionState.Listening, preview.Voice!.State);
        Assert.Equal(Marrow.App.Port.Settings.PreviewState.Rest, rig.Page.Session!.State);
        Assert.Equal(Marrow.Core.Models.ComposerLights.ComposerLightPhase.Rest, preview.Light.Phase);
        Assert.IsType<VoiceOrbSlot>(preview.VoiceOrb);
        Assert.IsType<VoicePill>(preview.VoicePill!.Pill);
        Assert.IsType<VoiceCross>(preview.VoicePill.Pill.Cross);
        Assert.IsType<VoiceGlowLayer>(preview.Glow);
        Assert.True(preview.Glow!.Glow.Active);
        Assert.Equal(writes, rig.Defaults.Writes.Count);
        rig.Advance(1500);
        Assert.NotEmpty(preview.Voice.Session.Words);
        Assert.Equal("Transcribing\U00002026", preview.VoicePill.Pill.Label);
    });

    [Fact]
    [Trait("Decision", "ED-292")] // CZ-T74: attachment geometry at t=0,.21,.42; size uses the existing slot's spring.
    public void Attachment_place_eases_the_width_and_text_inset_and_orb_size_springs() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig();
        var preview = rig.Page.Stage!.Preview;
        rig.Do(() => rig.App.Settings.VoiceOrb = rig.App.Settings.VoiceOrb with { Attachment = VoiceAttachment.Outside, OrbSize = 26 });
        Assert.Equal(472, preview.Pill.Width);
        Assert.Equal(12, preview.Pill.Margin.Left);
        rig.Advance(210);
        using var fixture = VoicePreviewFixture.Read();
        var midpoint = fixture.RootElement.GetProperty("geometry")[1];
        Assert.Equal(midpoint.GetProperty("width").GetDouble(), preview.Pill.Width, 3);
        Assert.Equal(midpoint.GetProperty("left").GetDouble(), preview.Pill.Margin.Left, 3);
        rig.Advance(210);
        Assert.Equal(428, preview.Pill.Width, 5);
        Assert.Equal(56, preview.Pill.Margin.Left, 5);
        Assert.Equal(26, preview.VoiceOrb!.Diameter, 1);
        Assert.Equal(14, preview.TextInset, 5);
        Assert.False(preview.OutsideAttach!.IsHitTestVisible);
        Assert.Equal(10, preview.OutsideAttach.Margin.Left);
        Assert.Equal(20, preview.OutsideAttach.Margin.Bottom);
    });

    [Theory]
    [InlineData(VoiceAfterVoice.Leave)]
    [InlineData(VoiceAfterVoice.Send)]
    [Trait("Decision", "ED-292")] // CZ-T76: landing .5, send .35 later, hold 3s, or leave the text. No persisted preview state.
    public void Landing_leaves_the_text_or_sends_after_landing_plus_035_and_runs_for_three_seconds(VoiceAfterVoice after) => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(seed: s => s.VoiceOrb = s.VoiceOrb with { AfterVoice = after });
        var preview = rig.Page.Stage!.Preview;
        rig.Do(() => preview.Voice!.Activate());
        rig.Advance((VoiceDesignScript.Default.End + .35 + VoicePreviewFixture.ProcessingSeconds) * 1000);
        Assert.Equal(VoiceSessionState.Landing, preview.Voice!.State);
        Assert.EndsWith("tooltip too.", preview.VoiceText, StringComparison.Ordinal);
        rig.Advance(840);
        Assert.False(preview.DotsShown);
        rig.Advance(10);
        Assert.Equal(after == VoiceAfterVoice.Send, preview.DotsShown);
        rig.Advance(3000);
        Assert.False(preview.DotsShown);
        Assert.Equal(after == VoiceAfterVoice.Send ? "" : "Add a shortcut for the theme switch, and show it in the tooltip too.", preview.VoiceText);
    });

    [Theory]
    [InlineData("cross")]
    [InlineData("escape")]
    [InlineData("state")]
    [InlineData("dots")]
    [Trait("Decision", "ED-292")] // CZ-T77: all cancel paths withdraw and prevent scheduled restart/send.
    public void Cancel_paths_withdraw_the_words(string path) => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig();
        var preview = rig.Page.Stage!.Preview;
        rig.Do(() => preview.Voice!.Activate());
        rig.Advance(1600);
        rig.Do(() =>
        {
            if (path == "cross") preview.VoicePill!.Pill.Cross.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            else if (path == "escape") rig.Window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            else if (path == "state") rig.Page.Session!.Pick(Marrow.App.Port.Settings.PreviewState.Rest);
            else rig.Page.Session!.WorkingDotsTouched();
        });
        Assert.Equal(VoiceSessionState.Rest, preview.Voice!.State);
        rig.Advance(1000);
        Assert.Equal("", preview.VoiceText);
        Assert.False(preview.Dictation!.IsActive);
    });

    [Fact]
    [Trait("Decision", "ED-292")] // CZ-T94: relabel first; replay at 200ms and replace any pending replay.
    public void Words_relabel_before_replay_and_a_new_pick_replaces_the_pending_start() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig();
        var preview = rig.Page.Stage!.Preview;
        rig.Do(() => preview.Voice!.Activate());
        rig.Advance(1400);
        rig.Do(() => { rig.App.Settings.VoiceOrb = rig.App.Settings.VoiceOrb with { Labels = VoicePillWords.L3 }; rig.Page.Session!.VoicePreviewTouched("labels", TimeSpan.FromMilliseconds(200)); });
        Assert.Equal("Transcribing\U00002026", preview.VoicePill!.Pill.Label);
        rig.Advance(140);
        Assert.Equal(0, preview.VoicePill.Pill.LabelOpacity(VoiceLabelState.Transcribing), 5);
        Assert.Equal("Transcribing\U00002026", preview.VoicePill.Pill.Label);
        rig.Advance(20);
        Assert.Equal("Writing\U00002026", preview.VoicePill.Pill.Label);
        rig.Advance(30);
        Assert.NotEmpty(preview.Voice!.Session.Words);
        rig.Advance(10);
        Assert.Empty(preview.Voice.Session.Words);
        Assert.Equal(VoiceSessionState.Listening, preview.Voice.State);
        rig.Do(() => { rig.Page.Session!.VoicePreviewTouched("labels", TimeSpan.FromMilliseconds(200)); rig.Page.Session.WorkingDotsTouched(); });
        rig.Advance(250);
        Assert.Equal(VoiceSessionState.Rest, preview.Voice.State);
    });

    [Theory]
    [InlineData(VoiceListeningStyle.Pill)]
    [InlineData(VoiceListeningStyle.Cross)]
    [Trait("Decision", "ED-292")]
    public void Either_cross_cancels_a_pending_words_replay(VoiceListeningStyle style) => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(seed: s => s.VoiceOrb = s.VoiceOrb with { ListeningStyle = style });
        var preview = rig.Page.Stage!.Preview;
        rig.Do(() => preview.Voice!.Activate());
        rig.Advance(1400);
        rig.Do(() => rig.Page.Session!.VoicePreviewTouched("labels", TimeSpan.FromMilliseconds(200)));
        rig.Do(() => (style == VoiceListeningStyle.Cross ? preview.VoiceOrb!.Cross : preview.VoicePill!.Pill.Cross).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        rig.Advance(1000);
        Assert.Equal(VoiceSessionState.Rest, preview.Voice!.State);
        Assert.Empty(preview.VoiceText);
    });

    [Fact]
    [Trait("Decision", "ED-292")]
    public void A_state_pick_after_landing_cancels_the_pending_send() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(seed: s => s.VoiceOrb = s.VoiceOrb with { AfterVoice = VoiceAfterVoice.Send });
        var preview = rig.Page.Stage!.Preview;
        rig.Do(() => preview.Voice!.Activate());
        rig.Advance((VoiceDesignScript.Default.End + .35 + VoicePreviewFixture.ProcessingSeconds + .6) * 1000);
        Assert.Equal(VoiceSessionState.Rest, preview.Voice!.State);
        rig.Do(() => rig.Page.Session!.Pick(Marrow.App.Port.Settings.PreviewState.Rest));
        rig.Advance(1000);
        Assert.False(preview.DotsShown);
        Assert.Empty(preview.VoiceText);
        Assert.Equal(Marrow.Core.Models.ComposerLights.ComposerLightPhase.Rest, preview.Light.Phase);
    });

    [Fact]
    [Trait("Decision", "ED-292")]
    public void A_second_click_finishes_only_spoken_words_and_Processing_can_be_cancelled() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig();
        var preview = rig.Page.Stage!.Preview;
        rig.Do(() => preview.Voice!.Activate());
        rig.Advance(1900);
        rig.Do(() => preview.Voice!.Activate());
        Assert.Equal(VoiceSessionState.Processing, preview.Voice!.State);
        Assert.Equal(VoiceLabelState.Finishing, preview.Voice.Session.Label);
        rig.Advance(1000);
        Assert.Equal("Add a shortcut for", preview.VoiceText);
        rig.Do(() => preview.Voice.Activate());
        rig.Advance(1400);
        rig.Do(() => preview.Voice.Activate());
        Assert.Equal(VoiceSessionState.Processing, preview.Voice.State);
        rig.Do(() => preview.VoicePill!.Pill.Cross.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        rig.Advance(1000);
        Assert.Empty(preview.VoiceText);
        Assert.Equal(VoiceSessionState.Rest, preview.Voice.State);
    });

    [Fact]
    [Trait("Decision", "ED-292")] // CZ-T78: only crossed shaped lines fade; the mask restores after closing.
    public void The_floating_pill_fades_only_the_lines_its_rectangle_crosses() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(seed: s => s.VoiceOrb = s.VoiceOrb with { ListeningStyle = VoiceListeningStyle.Float });
        var stage = rig.Page.Stage!;
        // Exercise the stage with a fourth conversation line occupying the pill's destination.
        rig.Do(() => { stage.ChatBody.Inlines!.Clear(); stage.ChatBody.Text = string.Join('\n', Enumerable.Repeat(new string('W', 40), 4)); stage.Preview.Voice!.Activate(); });
        rig.Advance(160);
        var mask = Assert.IsType<Avalonia.Media.LinearGradientBrush>(stage.ChatBody.OpacityMask);
        Assert.Contains(mask.GradientStops, s => s.Color.A == 0);
        Assert.Contains(mask.GradientStops, s => s.Color.A == 255);
        Assert.Equal(1, stage.ChatHeading.Opacity);
        rig.Do(stage.Preview.CancelVoice);
        rig.Advance(49);
        Assert.Contains(mask.GradientStops, s => s.Color.A == 0);
        rig.Advance(251);
        Assert.All(mask.GradientStops, s => Assert.Equal(255, s.Color.A));
    });

    [Fact]
    [Trait("Decision", "ED-292")]
    public void The_compacted_question_keeps_its_own_card_and_motion_through_the_production_presenter() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(scroll: true, height: 420, seed: s => s.VoiceOrb = s.VoiceOrb with { LangMode = VoiceLangMode.Ask });
        rig.ScrollTo(100);
        rig.Advance(1000);
        var preview = rig.Page.Stage!.Preview;
        rig.Do(() => rig.Page.Session!.VoicePreviewTouched("langMode", TimeSpan.Zero));
        var popup = Assert.Single(rig.Overlay.Popups);
        Assert.Same(preview.Question, popup.Child);
        Assert.IsNotType<PopoverCard>(popup.Child);
        Assert.True(popup.IsOpen);
        Assert.True(preview.Question!.IsBelow);
        Assert.Equal(PopoverTipEdge.Top, preview.Question.Card.TipEdge);
        Assert.IsType<Avalonia.Media.TranslateTransform>(preview.Question.RenderTransform);
        Assert.Equal(0, preview.Question.ShownOpacity);
        var transform = Assert.IsType<Avalonia.Media.TransformGroup>(preview.Question.Shell.RenderTransform);
        Assert.Equal(-6, Assert.Single(transform.Children.OfType<Avalonia.Media.TranslateTransform>()).Y);
        rig.Advance(180);
        Assert.Equal(1, preview.Question.ShownOpacity, 5);
        rig.Do(preview.CancelVoice);
        Assert.False(popup.IsOpen);
        rig.Advance(2000);
        Assert.Equal(VoiceSessionState.Rest, preview.Voice!.State);
    });

    [Theory]
    [InlineData(VoiceLightVoice.Away, 0)]
    [InlineData(VoiceLightVoice.Dim, .35)]
    [InlineData(VoiceLightVoice.Stay, 1)]
    [Trait("Decision", "ED-292")] // CZ-T95: shared ComposerLightView follower target throughout listen/finish/landing.
    public void The_light_gets_the_picked_level_and_the_real_glow_draws_while_listening(VoiceLightVoice setting, double level) => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(seed: s => s.VoiceOrb = s.VoiceOrb with { LightVoice = setting });
        var preview = rig.Page.Stage!.Preview;
        rig.Do(() => preview.Voice!.Activate());
        Assert.Equal(level, preview.Light.VoiceLight);
        rig.Advance((VoiceDesignScript.Default.End + .35) * 1000);
        Assert.Equal(level, preview.Light.VoiceLight);
        Assert.True(preview.Glow!.Glow.Processing);
        rig.Advance(VoicePreviewFixture.ProcessingSeconds * 1000);
        Assert.Equal(level, preview.Light.VoiceLight);
        rig.Advance(500);
        Assert.Equal(1, preview.Light.VoiceLight);
        Assert.False(preview.Glow.Glow.Active);
    });

    [Theory]
    [InlineData(VoiceLangMode.Auto)]
    [InlineData(VoiceLangMode.Show)]
    [InlineData(VoiceLangMode.Windows)]
    [Trait("Decision", "ED-292")] // CZ-T96 A/B/C: the language appears only for B, no separator dot.
    public void Only_named_auto_detect_adds_English_to_the_pill(VoiceLangMode mode) => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(seed: s => s.VoiceOrb = s.VoiceOrb with { LangMode = mode });
        var preview = rig.Page.Stage!.Preview;
        rig.Do(() => preview.Voice!.Activate());
        rig.Advance(1000);
        Assert.Null(preview.VoicePill!.Pill.Language);
        rig.Advance(500);
        Assert.Equal(mode == VoiceLangMode.Show ? "English" : null, preview.VoicePill!.Pill.Language);
        Assert.DoesNotContain("\U000000B7", preview.VoicePill.Pill.Label, StringComparison.Ordinal);
    });

    [Fact]
    [Trait("Decision", "ED-292")] // CZ-T96 D: pick self-answers at 1.6, click waits/focuses, answer never persists; option pick re-arms.
    public void The_question_uses_the_presenter_preserves_pick_focus_and_keeps_answers_session_local() => headless.OnUiThread(() =>
    {
        using var rig = new CustomizationPageRig(seed: s => s.VoiceOrb = s.VoiceOrb with { LangMode = VoiceLangMode.Ask });
        var preview = rig.Page.Stage!.Preview;
        var focus = Assert.IsType<CzMenuButton>(rig.Page.Voice!.Section.Rows.Slots.Select(slot => slot.Row).OfType<CzRow>().Single(row => row.Title.Text == "Spoken language").Control);
        rig.Do(() => { Assert.True(focus.Focus()); Assert.True(focus.IsFocused); rig.Page.Session!.VoicePreviewTouched("langMode", TimeSpan.Zero); });
        Assert.True(preview.Question!.IsOpen);
        Assert.Single(rig.Overlay.Popups);
        Assert.DoesNotContain(preview.Question, rig.Page.Stage.Surface.GetVisualDescendants());
        Assert.True(focus.IsFocused);
        var writes = rig.Defaults.Writes.Count;
        rig.Advance(1590);
        Assert.Equal(VoiceSessionState.Asking, preview.Voice!.State);
        rig.Advance(10);
        Assert.Equal(VoiceSessionState.Listening, preview.Voice.State);
        Assert.Equal(writes, rig.Defaults.Writes.Count);
        rig.Do(() => { preview.CancelVoice(); preview.Voice.Activate(); });
        Assert.Equal(VoiceSessionState.Listening, preview.Voice.State);
        rig.Do(() => rig.Page.Session!.VoicePreviewTouched("langMode", TimeSpan.Zero));
        Assert.Equal(VoiceSessionState.Asking, preview.Voice.State);
        rig.Do(preview.CancelVoice);
        rig.Do(() => preview.Voice.Activate());
        rig.Advance(2000);
        Assert.Equal(VoiceSessionState.Asking, preview.Voice.State);
        Assert.True(preview.Question.Answers[0].IsFocused);
        rig.Do(() => preview.Question.Answers[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        Assert.Equal(VoiceSessionState.Listening, preview.Voice.State);
        Assert.Equal(writes, rig.Defaults.Writes.Count);
    });
}
