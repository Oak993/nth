using Avalonia.Automation;
using Avalonia.Input;
using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models.ComposerLights;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice;
using Marrow.UI.Port.Settings;
using Marrow.UI.Port.Voice.Orb;

namespace Marrow.App.Tests.Port.Settings;

[Collection("Headless")]
public sealed class CustomizationVoiceRowsTests(HeadlessSession headless)
{
    private static CzSection Section(CustomizationPageRig rig) => Assert.Single(rig.Page.Sticky.Children.OfType<CzSection>(), s => s.Title == "Voice orb");
    private static CzRow Row(CustomizationPageRig rig, string title) => Section(rig).Rows.Slots.Select(s => s.Row).OfType<CzRow>().Single(r => r.Title.Text == title);
    private static CzSegmentedControl Segment(CustomizationPageRig rig, string title) => Assert.IsType<CzSegmentedControl>(Row(rig, title).Control);
    private static CzMenuButton Menu(CustomizationPageRig rig, string title) => Assert.IsType<CzMenuButton>(Row(rig, title).Control);

    [Fact]
    [Trait("Decision", "ED-251")]
    public void CZ_T70_T90_Exact_row_order_options_details_and_note() => headless.OnUiThread(() =>
    {
        using var context = new VoiceRowsContext();
        using var rig = new CustomizationPageRig(reduceMotion: true);
        var section = Section(rig);
        Assert.Equal(new[] { "Orb", "Listening style", "Pill words", "Orb size", "Attachment button", "Composer light while you speak", "Spoken language", "After voice input" }, section.Rows.Slots.Select(s => s.Row).OfType<CzRow>().Select(r => r.Title.Text));
        Assert.Equal("Click the orb in the preview to try it: it listens until you click it again (the cross or Esc cancels), then the words arrive.", Assert.IsType<CzNoteRow>(section.Rows.Slots[^1].Row).Text.Text);
        foreach (var row in VoiceRowsOracle.Options)
        {
            var control = Row(rig, row.Key).Control;
            Assert.Equal(row.Value.Select(x => x.Text), control is CzRadioGroup group ? group.Buttons.Select(AutomationProperties.GetName) : ((CzMenuButton)control).Items.Select(i => i.Label.Text));
            foreach (var option in row.Value)
            {
                rig.Do(() => { if (control is CzRadioGroup radio) radio.Select(option.Id); else ((CzMenuButton)control).Select(option.Id); });
                Assert.Equal(option.Detail, Row(rig, row.Key).Detail.Text);
            }
        }
    });

    [Fact]
    [Trait("Decision", "ED-251")]
    public void CZ_T71_All_picks_write_the_documented_voiceOrb_JSON() => headless.OnUiThread(() =>
    {
        using var context = new VoiceRowsContext();
        using var rig = new CustomizationPageRig(reduceMotion: true);
        Section(rig);
        var requests = new List<(string Key, TimeSpan? Delay)>();
        rig.Page.Session!.VoicePreviewChangeRequested += (key, delay) => requests.Add((key, delay));
        Assert.Equal("{\"orb\":\"siri\",\"lstyle\":\"pill\",\"orbSize\":32,\"attach\":\"in\",\"afterVoice\":\"leave\",\"labels\":\"L1\",\"lightVoice\":\"away\",\"langMode\":\"auto\"}", System.Text.Encoding.UTF8.GetString(rig.App.Settings.VoiceOrb.ToJson()));
        rig.Do(() => { ((CzOrbTiles)Row(rig, "Orb").Control).Select("membrane"); Segment(rig, "Listening style").Select("float"); Segment(rig, "Orb size").Select("26"); Segment(rig, "Attachment button").Select("out"); Segment(rig, "After voice input").Select("send"); Menu(rig, "Pill words").Select("L6"); Segment(rig, "Composer light while you speak").Select("stay"); Menu(rig, "Spoken language").Select("ask"); rig.App.Settings.FlushPendingWrites(); });
        Assert.Equal("{\"orb\":\"membrane\",\"lstyle\":\"float\",\"orbSize\":26,\"attach\":\"out\",\"afterVoice\":\"send\",\"labels\":\"L6\",\"lightVoice\":\"stay\",\"langMode\":\"ask\"}", System.Text.Encoding.UTF8.GetString(rig.Defaults.Data("voiceOrb")!));
        Assert.Equal(new[] { "orb", "lstyle", "orbSize", "attach", "afterVoice", "labels", "lightVoice", "langMode" }, requests.Select(r => r.Key));
        Assert.Equal(new TimeSpan?[] { null, TimeSpan.Zero, null, null, null, TimeSpan.FromMilliseconds(200), TimeSpan.Zero, TimeSpan.Zero }, requests.Select(r => r.Delay));
    });

    [Fact]
    [Trait("Decision", "ED-251")]
    public void CZ_T72_Tiles_use_real_52px_views_30px_balls_selected_glow_and_wrapping_arrows() => headless.OnUiThread(() =>
    {
        using var context = new VoiceRowsContext();
        using var rig = new CustomizationPageRig(reduceMotion: true);
        var tiles = Assert.IsType<CzOrbTiles>(Row(rig, "Orb").Control);
        foreach (var button in tiles.Buttons)
        {
            var line = Assert.IsType<Avalonia.Controls.StackPanel>(button.Child);
            var tile = Assert.IsType<Avalonia.Controls.Border>(line.Children[0]);
            Assert.Equal((96d, 56d), (tile.Width, tile.Height));
            var orb = Assert.IsType<VoiceOrbView>(tile.Child);
            Assert.Equal((52d, 52d, 30d), (orb.Width, orb.Height, orb.SlotDiameter));
            Assert.Equal(button.Picked ? .25 : 0, (double)orb.GetType().GetProperty("TileGlow")!.GetValue(orb)!);
            var scene = new OrbScene(orb.Kind, 30, true);
            scene.Advance(0, new OrbSceneInput(orb.Kind, Marrow.Core.Port.Voice.Orb.OrbMotionMode.Rest, false, false, default, 30, true, true, 52, orb.TileGlow), 1);
            var runtime = scene.Runtime(orb.Kind)!;
            Assert.Equal(52, runtime.DevicePx);
            Assert.Equal((float)(30d / 52), runtime.Values[4]);
            Assert.Equal(button.Picked ? .25f : 0f, runtime.Values[17]);
        }
        rig.Do(() => tiles.RaiseEvent(new KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Key.Left }));
        Assert.Equal("membrane", tiles.SelectedId);
        rig.Do(() => tiles.RaiseEvent(new KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Key.Right }));
        Assert.Equal("siri", tiles.SelectedId);
    });

    [Fact]
    [Trait("Decision", "ED-251")]
    public void CZ_T91_Every_language_pick_including_current_clears_override_and_keeps_other_settings() => headless.OnUiThread(() =>
    {
        using var context = new VoiceRowsContext();
        using var rig = new CustomizationPageRig(reduceMotion: true);
        var menu = Menu(rig, "Spoken language");
        foreach (var id in new[] { "auto", "show", "windows", "ask", "ask" })
        {
            rig.Do(() => rig.App.Settings.VoiceTyping = VoiceTypingSettings.Default with { Lang = "hi", Mic = "test-mic" });
            rig.Advance(300);
            var before = rig.Defaults.Writes.Count;
            rig.Do(() => menu.Select(id));
            Assert.Null(rig.App.Settings.VoiceTyping.Lang);
            Assert.Equal("test-mic", rig.App.Settings.VoiceTyping.Mic);
            Assert.Equal(id, menu.SelectedId);
            Assert.InRange(rig.Defaults.Writes.Count - before, 1, 2);
            Assert.Null(VoiceTypingSettings.FromJson(rig.Defaults.Data("voiceTyping")!).Lang);
        }
    });

    [Fact]
    [Trait("Decision", "ED-251")]
    public void CZ_T93_Dimmed_rows_are_inert_unfocusable_keep_values_and_return() => headless.OnUiThread(() =>
    {
        using var context = new VoiceRowsContext();
        using var rig = new CustomizationPageRig(reduceMotion: true);
        var words = Menu(rig, "Pill words");
        rig.Do(() => Segment(rig, "Listening style").Select("cross"));
        Assert.Equal("Cross beside the orb has no pill, so no words are shown", Row(rig, "Pill words").Detail.Text);
        Assert.False(words.Focusable);
        rig.Do(() => words.Select("L3"));
        Assert.Equal(VoicePillWords.L1, rig.App.Settings.VoiceOrb.Labels);
        rig.SetLight(rig.Light with { Style = ComposerLightStyle.Off });
        var light = Segment(rig, "Composer light while you speak");
        Assert.Equal("No effect while the composer light is Off", Row(rig, "Composer light while you speak").Detail.Text);
        Assert.All(light.Buttons, b => Assert.False(b.Focusable));
        rig.Do(() => light.Select("stay"));
        Assert.Equal(VoiceLightVoice.Away, rig.App.Settings.VoiceOrb.LightVoice);
        rig.Do(() => Segment(rig, "Listening style").Select("pill"));
        rig.SetLight(rig.Light with { Style = ComposerLightStyle.Beam });
        Assert.True(words.Focusable);
        Assert.True(light.Buttons[0].Focusable);
    });

    [Fact]
    [Trait("Decision", "ED-251")]
    public void CZ_T97_Listed_unlisted_Windows_language_and_explicit_override_details() => headless.OnUiThread(() =>
    {
        using var context = new VoiceRowsContext();
        using var rig = new CustomizationPageRig(reduceMotion: true);
        var row = Row(rig, "Spoken language");
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("hi-IN");
            rig.Do(() => Menu(rig, "Spoken language").Select("windows"));
            Assert.Equal("Hindi, Windows’ display language, from the start; Auto-detect stays one click away on the Voice typing page", row.Detail.Text);
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("fi-FI");
            rig.Do(() => Menu(rig, "Spoken language").Select("windows"));
            Assert.Equal("Windows’ display language is not in the list, so Whisper detects the language", row.Detail.Text);
            foreach (var code in new[] { "hi", "auto" })
            {
                rig.Do(() => rig.App.Settings.VoiceTyping = VoiceTypingSettings.Default with { Lang = code });
                Assert.Equal((code == "hi" ? "Hindi" : "Auto-detect") + " is set on the Voice typing page and comes first; picking here clears it", row.Detail.Text);
            }
        }
        finally { System.Globalization.CultureInfo.CurrentUICulture = previous; }
    });

    [Fact]
    [Trait("Decision", "ED-237")]
    public void Row_detail_and_dimming_crossfade_on_the_frame_clock() => headless.OnUiThread(() =>
    {
        using var context = new VoiceRowsContext();
        using var rig = new CustomizationPageRig();
        var row = Row(rig, "Pill words");
        rig.Do(() => Segment(rig, "Listening style").Select("cross"));
        Assert.InRange(row.TextOpacity, .51, 1);
        Assert.Equal(0, row.Detail.IncomingOpacity);
        rig.Advance(150);
        Assert.InRange(row.TextOpacity, .5, .99);
        rig.Advance(300);
        Assert.Equal(.5, row.TextOpacity);
        Assert.Equal(1, row.Detail.IncomingOpacity);
    });
}
