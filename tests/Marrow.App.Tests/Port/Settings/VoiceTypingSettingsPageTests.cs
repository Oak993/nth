using Avalonia.Automation;
using Avalonia.Controls;
using Marrow.App.Port.Settings;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Capture;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Tests.Port.Settings;

// VT-13, Voice typing page A (voice-SPEC V-16.3.1, V-16.3.5, V-T90 section 1, V-T91, V-T92, V-T130; OD-0099 Q-V-19 B): the Voice typing section, the Language and Microphone rows, the
// Customization link and the privacy line, built from CZ-1's controls. The microphone list comes from VT-5's scripted enumerator; no device is read or opened.
[Collection("Headless")]
public sealed class VoiceTypingSettingsPageTests(HeadlessSession headless)
{
    private const string KeyDetail = "Cloud voice typing uses your own key, kept in Windows’ credential store";
    private const string LocalPrivacy = "Voice typing runs on this computer. Your voice is not sent anywhere, and recordings are never kept.";
    private const string CloudPrivacy = "Your recording is sent to OpenAI to be turned into text, using your key. OpenAI states it does not train on API data. Nothing is kept on this PC.";

    private static string[] Texts(Control root) => LogicalQuery.SelfAndDescendants<TextBlock>(root).Select(block => block.Text ?? "").Where(text => text.Length > 0).ToArray();

    private static string[] Shown(CzMenuButton menu) => menu.Items.Where(item => item.Checked).Select(item => item.Label.Text!).ToArray();

    [Fact]
    [Trait("Spec", "V-T90")]
    [Trait("Decision", "ED-267")] // V-16.3: the page is ready, listed, and has no chrome accessory
    public void The_page_is_ready_and_asks_for_no_chrome_row()
    {
        Assert.True(VoiceTypingSettingsPage.IsReady);
        Assert.True(SettingsPage.VoiceTyping.IsReady);
        Assert.Equal([SettingsPage.VoiceTyping], SettingsSidebar.Pages("microphone"));
        Assert.Contains(SettingsPage.VoiceTyping, SettingsSidebar.Pages(""));
    }

    [Fact]
    [Trait("Spec", "V-T90")]
    [Trait("Decision", "ED-267")] // V-16.3.1: section, rows, details and buttons, byte for byte
    public void The_Voice_typing_section_has_the_words_of_V_16_3_1() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var texts = Texts(scene.Page);
        Assert.False(scene.Page.HasChromeControls);
        Assert.Empty(scene.Page.ChromeAccessories);
        Assert.Equal("Voice typing", texts[0]);
        Assert.Contains("Engine", texts);
        Assert.Equal(["On this PC", "OpenAI cloud"], scene.Page.EngineControl.Buttons.Select(b => AutomationProperties.GetName(b)));
        Assert.Equal("Your voice never leaves this computer", scene.Page.EngineRow.Detail!.Text);
        Assert.Equal("OpenAI key", scene.Page.KeyRow.TitleText);
        Assert.Equal(KeyDetail, scene.Page.KeyRow.Detail!.Text);
        Assert.Contains("Add key", texts);
        Assert.Contains("Language", texts);
        Assert.Contains("Microphone", texts);
        Assert.Contains("Orb and after voice input", texts);
        Assert.Equal("How the orb looks, and whether the words are sent", scene.Page.OrbRow.Detail!.Text);
        Assert.Contains("Customization", texts);
        Assert.Equal(LocalPrivacy, scene.Page.PrivacyLine.Text);
        Assert.Equal("Engine", AutomationProperties.GetName(scene.Page.EngineControl));
        Assert.Equal("Language", AutomationProperties.GetName(scene.Page.LanguageMenu));
        Assert.Equal("Microphone", AutomationProperties.GetName(scene.Page.MicrophoneMenu));
        Assert.Equal("Orb and after voice input", AutomationProperties.GetName(scene.Page.OrbButton));
    }, VoiceTypingPageRig.TwoMicrophones());

    [Fact]
    [Trait("Spec", "V-T90")]
    [Trait("Decision", "ED-267")] // V-16.3.1: the rows are in the design's order, the Microphone row (Q-V-19 B) after Language and before the Customization link
    public void The_rows_are_in_order_with_the_Microphone_row_after_Language() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var order = LogicalQuery.SelfAndDescendants<VoiceSettingsRow>(scene.Page.Sections.Children[0]).Select(row => row.TitleText).ToArray(); // the Voice typing section; VT-14's Speech model section follows it
        Assert.Equal(["Engine", "OpenAI key", "Language", "Microphone", "Orb and after voice input"], order);
    }, VoiceTypingPageRig.TwoMicrophones());

    [Fact]
    [Trait("Spec", "V-T91")]
    [Trait("Decision", "ED-267")] // V-16.3.1: the key row folds open for the cloud engine only; it is inert until VT-16
    public void The_cloud_engine_opens_the_key_row_which_stays_inert_until_VT_16() => VoiceTypingPageRig.Run(headless, scene =>
    {
        Assert.Equal(0, scene.Page.KeyFold.Fraction);
        Assert.False(scene.Page.KeyFold.Open);
        Assert.False(scene.Page.KeyButton.IsEnabled);
        Assert.False(scene.Page.KeyButton.Focusable);
        scene.Page.EngineControl.Select("cloud");
        Assert.Equal(VoiceEngine.Cloud, scene.Model.Settings.VoiceTyping.Engine);
        Assert.True(scene.Page.KeyFold.Open);
        scene.Advance(80);
        Assert.InRange(scene.Page.KeyFold.Fraction, .01, .99);
        scene.Advance(500);
        Assert.Equal(1, scene.Page.KeyFold.Fraction);
        scene.Page.KeyButton.Activate();
        Assert.Equal("Add key", scene.Page.KeyButton.Child is TextBlock text ? text.Text : "");
        scene.Page.EngineControl.Select("local");
        Assert.Equal(VoiceEngine.Local, scene.Model.Settings.VoiceTyping.Engine);
        Assert.False(scene.Page.KeyFold.Open);
        scene.Advance(600);
        Assert.Equal(0, scene.Page.KeyFold.Fraction);
    });

    [Fact]
    [Trait("Spec", "V-T91")]
    [Trait("Decision", "ED-267")] // OD-0076: the Engine detail and the privacy line crossfade; nothing cuts
    public void Switching_the_engine_crossfades_the_detail_and_the_privacy_line() => VoiceTypingPageRig.Run(headless, scene =>
    {
        scene.Page.EngineControl.Select("cloud");
        Assert.Equal(0, scene.Page.EngineRow.Detail!.IncomingOpacity);
        Assert.Equal(1, scene.Page.EngineRow.Detail!.OutgoingOpacity);
        Assert.Equal(0, scene.Page.PrivacyLine.IncomingOpacity);
        scene.Advance(500);
        Assert.Equal("Sent to OpenAI with your own key, about half a US cent a minute", scene.Page.EngineRow.Detail!.Text);
        Assert.Equal(CloudPrivacy, scene.Page.PrivacyLine.Text);
        Assert.Equal(1, scene.Page.PrivacyLine.IncomingOpacity);
        scene.Page.EngineControl.Select("local");
        scene.Advance(500);
        Assert.Equal("Your voice never leaves this computer", scene.Page.EngineRow.Detail!.Text);
        Assert.Equal(LocalPrivacy, scene.Page.PrivacyLine.Text);
    });

    [Fact]
    [Trait("Spec", "V-T91")]
    [Trait("Decision", "ED-267")] // a stored cloud engine opens the page with the key row already open and the cloud words, with no transition on arrival
    public void A_stored_cloud_engine_opens_the_page_already_in_cloud_form() => VoiceTypingPageRig.Run(headless, scene =>
    {
        Assert.Equal("cloud", scene.Page.EngineControl.SelectedId);
        Assert.True(scene.Page.KeyFold.Open);
        Assert.Equal(1, scene.Page.KeyFold.Fraction);
        Assert.Equal(CloudPrivacy, scene.Page.PrivacyLine.Text);
        Assert.Equal(1, scene.Page.PrivacyLine.IncomingOpacity);
    }, arrange: model => model.Settings.VoiceTyping = model.Settings.VoiceTyping with { Engine = VoiceEngine.Cloud });

    [Fact]
    [Trait("Spec", "V-T91")]
    [Trait("Decision", "ED-267")] // the engine can also change elsewhere (a settings write): the control follows
    public void The_engine_control_follows_the_stored_engine() => VoiceTypingPageRig.Run(headless, scene =>
    {
        scene.Model.Settings.VoiceTyping = scene.Model.Settings.VoiceTyping with { Engine = VoiceEngine.Cloud };
        VoiceTypingScene.Sync();
        Assert.Equal("cloud", scene.Page.EngineControl.SelectedId);
        Assert.True(scene.Page.KeyFold.Open);
    });

    [Fact]
    [Trait("Spec", "V-T92")]
    [Trait("Decision", "ED-267")] // V-16.3.1: the Language menu lists the 21 languages in the design's order with a check on the current item
    public void The_Language_menu_lists_the_languages_in_order() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var menu = scene.Page.LanguageMenu;
        Assert.Equal(VoiceLanguage.Languages.Select(l => l.Name), menu.Items.Select(i => i.Label.Text));
        Assert.Equal(["Auto-detect"], Shown(menu));
        Assert.Equal("Auto-detect", menu.ValueLabel.Text);
        Assert.Equal(VoiceLanguage.AutoDetail, scene.Page.LanguageRow.Detail!.Text);
    });

    [Fact]
    [Trait("Spec", "V-T92")]
    [Trait("Decision", "ED-267")] // V-16.3.1 / V-7.14: picking any item stores it explicitly (the code, or auto), checks it and changes the detail
    public void Picking_a_language_stores_it_explicitly_and_changes_the_detail() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var menu = scene.Page.LanguageMenu;
        menu.Select("hi");
        Assert.Equal("hi", scene.Model.Settings.VoiceTyping.Lang);
        VoiceTypingScene.Sync();
        scene.Advance(600);
        Assert.Equal(["Hindi"], Shown(scene.Page.LanguageMenu));
        Assert.Equal("Hindi", scene.Page.LanguageMenu.ValueLabel.Text);
        Assert.Equal("Written in Devanagari", scene.Page.LanguageRow.Detail!.Text);
        scene.Page.LanguageMenu.Select("ur");
        scene.Advance(600);
        Assert.Equal("Written in Urdu script, right to left", scene.Page.LanguageRow.Detail!.Text);
        scene.Page.LanguageMenu.Select("fr");
        scene.Advance(600);
        Assert.Equal("Every dictation is read as French", scene.Page.LanguageRow.Detail!.Text);
        scene.Page.LanguageMenu.Select("auto");
        Assert.Equal("auto", scene.Model.Settings.VoiceTyping.Lang);
        scene.Advance(600);
        Assert.Equal(VoiceLanguage.AutoDetail, scene.Page.LanguageRow.Detail!.Text);
    });

    [Fact]
    [Trait("Spec", "V-T92")]
    [Trait("Decision", "ED-267")] // OD-0076: the detail crossfades when the language changes
    public void The_Language_detail_crossfades_when_the_language_changes() => VoiceTypingPageRig.Run(headless, scene =>
    {
        scene.Page.LanguageMenu.Select("hi");
        VoiceTypingScene.Sync();
        Assert.Equal(0, scene.Page.LanguageRow.Detail!.IncomingOpacity);
        Assert.Equal(1, scene.Page.LanguageRow.Detail!.OutgoingOpacity);
        scene.Advance(500);
        Assert.Equal(1, scene.Page.LanguageRow.Detail!.IncomingOpacity);
    });

    [Fact]
    [Trait("Spec", "V-T130")]
    [Trait("Decision", "ED-267")] // V-16.3.1: nothing picked and language default B, C, D: the button, the check and the detail
    public void With_nothing_picked_the_row_shows_the_language_default() => VoiceTypingPageRig.Run(headless, scene =>
    {
        void Mode(VoiceLangMode mode)
        {
            scene.Model.Settings.VoiceOrb = scene.Model.Settings.VoiceOrb with { LangMode = mode };
            VoiceTypingScene.Sync();
            scene.Advance(600);
        }
        Mode(VoiceLangMode.Show);
        Assert.Equal("Auto-detect · shown while you speak", scene.Page.LanguageMenu.ValueLabel.Text);
        Assert.Equal(["Auto-detect"], Shown(scene.Page.LanguageMenu));
        Mode(VoiceLangMode.Windows);
        Assert.Equal("Hindi (from Windows)", scene.Page.LanguageMenu.ValueLabel.Text);
        Assert.Equal(["Hindi"], Shown(scene.Page.LanguageMenu));
        Assert.Equal("Written in Devanagari", scene.Page.LanguageRow.Detail!.Text);
        Mode(VoiceLangMode.Ask);
        Assert.Equal("Asked on first use", scene.Page.LanguageMenu.ValueLabel.Text);
        Assert.Empty(Shown(scene.Page.LanguageMenu));
        Assert.Equal("You will be asked the first time you click the orb", scene.Page.LanguageRow.Detail!.Text);
        Assert.Null(scene.Model.Settings.VoiceTyping.Lang);
        Mode(VoiceLangMode.Auto);
        Assert.Equal("Auto-detect", scene.Page.LanguageMenu.ValueLabel.Text);
        scene.Page.LanguageMenu.Select("de");
        Mode(VoiceLangMode.Ask);
        Assert.Equal("German", scene.Page.LanguageMenu.ValueLabel.Text);
        Assert.Equal("de", scene.Model.Settings.VoiceTyping.Lang);
    }, windowsLanguage: "hi-IN");

    [Fact]
    [Trait("Spec", "V-T130")]
    [Trait("Decision", "ED-267")] // V-16.3.1 C: a Windows language outside the list is Auto-detect; and the reset rule (the Customization row sets lang back to null) shows at once
    public void A_Windows_language_outside_the_list_and_the_reset_rule_show_on_the_row() => VoiceTypingPageRig.Run(headless, scene =>
    {
        Assert.Equal("Auto-detect", scene.Page.LanguageMenu.ValueLabel.Text);
        scene.Model.Settings.VoiceTyping = scene.Model.Settings.VoiceTyping with { Lang = "ur" };
        VoiceTypingScene.Sync();
        scene.Advance(600);
        Assert.Equal("Urdu", scene.Page.LanguageMenu.ValueLabel.Text);
        var changed = VoiceLanguage.WithLanguageDefault(scene.Model.Settings.VoiceOrb, scene.Model.Settings.VoiceTyping, VoiceLangMode.Windows);
        scene.Model.Settings.VoiceOrb = changed.Orb;
        scene.Model.Settings.VoiceTyping = changed.Typing;
        VoiceTypingScene.Sync();
        scene.Advance(600);
        Assert.Equal("Auto-detect", scene.Page.LanguageMenu.ValueLabel.Text);
        Assert.Equal(["Auto-detect"], Shown(scene.Page.LanguageMenu));
    }, windowsLanguage: "sw-KE");

    [Fact]
    [Trait("Spec", "V-T42")]
    [Trait("Decision", "ED-267")] // Q-V-19 B: the Microphone row lists "Windows default" and the scripted endpoints, opens no device, and Windows default is checked
    public void The_Microphone_row_lists_the_scripted_endpoints() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var menu = scene.Page.MicrophoneMenu;
        Assert.Equal(["Windows default", "Microphone Array (Realtek)", "Headset (USB)"], menu.Items.Select(i => i.Label.Text));
        Assert.Equal(["", VoiceTypingPageRig.Realtek, VoiceTypingPageRig.Usb], menu.Items.Select(i => i.Id));
        Assert.Equal(["Windows default"], Shown(menu));
        Assert.Equal("Windows default", menu.ValueLabel.Text);
        Assert.Null(scene.Model.Settings.VoiceTyping.Mic);
        Assert.Empty(((ScriptedCaptureHost)scene.Host).OpenRequests);
    }, VoiceTypingPageRig.TwoMicrophones());

    [Fact]
    [Trait("Spec", "V-T42")]
    [Trait("Decision", "ED-267")] // Q-V-19 B: the pick is stored as voiceTyping.mic (null for Windows default) and shown
    public void Picking_a_microphone_stores_its_id_and_Windows_default_stores_none() => VoiceTypingPageRig.Run(headless, scene =>
    {
        scene.Page.MicrophoneMenu.Select(VoiceTypingPageRig.Usb);
        Assert.Equal(VoiceTypingPageRig.Usb, scene.Model.Settings.VoiceTyping.Mic);
        VoiceTypingScene.Sync();
        scene.Advance(600);
        Assert.Equal(["Headset (USB)"], Shown(scene.Page.MicrophoneMenu));
        Assert.Equal("Headset (USB)", scene.Page.MicrophoneMenu.ValueLabel.Text);
        scene.Page.MicrophoneMenu.Select("");
        Assert.Null(scene.Model.Settings.VoiceTyping.Mic);
        scene.Advance(600);
        Assert.Equal("Windows default", scene.Page.MicrophoneMenu.ValueLabel.Text);
        Assert.Empty(((ScriptedCaptureHost)scene.Host).OpenRequests);
    }, VoiceTypingPageRig.TwoMicrophones());

    [Fact]
    [Trait("Spec", "V-T42")]
    [Trait("Decision", "ED-267")] // a stored pick is the checked item when the page opens
    public void A_stored_microphone_is_the_checked_item_on_arrival() => VoiceTypingPageRig.Run(headless, scene =>
    {
        Assert.Equal(["Headset (USB)"], Shown(scene.Page.MicrophoneMenu));
        Assert.Equal("Headset (USB)", scene.Page.MicrophoneMenu.ValueLabel.Text);
        Assert.Equal(1, scene.Page.MicrophoneMenu.ValueLabel.IncomingOpacity);
    }, VoiceTypingPageRig.TwoMicrophones(), arrange: model => model.Settings.VoiceTyping = model.Settings.VoiceTyping with { Mic = VoiceTypingPageRig.Usb });

    [Fact]
    [Trait("Spec", "V-T42")]
    [Trait("Decision", "ED-267")] // the list follows the devices live: a new one is listed; the pick unplugged falls back to Windows default without touching the stored id; it comes back with the device
    public void The_Microphone_list_follows_plugging_and_unplugging() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var host = (ScriptedCaptureHost)scene.Host;
        host.Add("{ccc}", "Webcam");
        VoiceTypingScene.Sync();
        scene.Advance(600);
        Assert.Equal(["Windows default", "Microphone Array (Realtek)", "Headset (USB)", "Webcam"], scene.Page.MicrophoneMenu.Items.Select(i => i.Label.Text));
        scene.Page.MicrophoneMenu.Select(VoiceTypingPageRig.Usb);
        scene.Advance(600);
        host.Remove(VoiceTypingPageRig.Usb);
        VoiceTypingScene.Sync();
        scene.Advance(600);
        Assert.Equal(VoiceTypingPageRig.Usb, scene.Model.Settings.VoiceTyping.Mic);
        Assert.Equal("Windows default", scene.Page.MicrophoneMenu.ValueLabel.Text);
        Assert.Equal(["Windows default"], Shown(scene.Page.MicrophoneMenu));
        Assert.Equal(3, scene.Page.MicrophoneMenu.Items.Count);
        host.Add(VoiceTypingPageRig.Usb, "Headset (USB)");
        VoiceTypingScene.Sync();
        scene.Advance(600);
        Assert.Equal("Headset (USB)", scene.Page.MicrophoneMenu.ValueLabel.Text);
        Assert.Empty(host.OpenRequests);
    }, VoiceTypingPageRig.TwoMicrophones());

    [Fact]
    [Trait("Spec", "V-T42")]
    [Trait("Decision", "ED-267")] // review of VT-13: an enumerator that lists an id twice, or lists an empty one, must not fail the page build (CzMenuButton needs unique ids)
    public void An_enumerator_that_repeats_or_blanks_an_id_does_not_break_the_page() => VoiceTypingPageRig.Run(headless, scene =>
    {
        Assert.Equal(["Windows default", "Headset (USB)"], scene.Page.MicrophoneMenu.Items.Select(i => i.Label.Text));
        ((ScriptedCaptureHost)scene.Host).Add(VoiceTypingPageRig.Usb, "Headset (USB) again");
        VoiceTypingScene.Sync();
        scene.Advance(600);
        Assert.Equal(["Windows default", "Headset (USB)"], scene.Page.MicrophoneMenu.Items.Select(i => i.Label.Text));
    }, new ScriptedCaptureHost((VoiceTypingPageRig.Usb, "Headset (USB)"), (VoiceTypingPageRig.Usb, "Headset (USB) again"), ("", "Nameless")));

    [Fact]
    [Trait("Spec", "V-T42")]
    [Trait("Decision", "ED-267")] // review of VT-13: a device-watch refresh whose listing throws keeps the list the page has and throws nothing
    public void A_refresh_that_cannot_list_keeps_the_last_list() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var host = (FlakyCaptureHost)scene.Host;
        Assert.Equal(3, scene.Page.MicrophoneMenu.Items.Count);
        host.Failing = true;
        host.Inner.Add("{ccc}", "Webcam");
        VoiceTypingScene.Sync();
        scene.Advance(600);
        Assert.Equal(["Windows default", "Microphone Array (Realtek)", "Headset (USB)"], scene.Page.MicrophoneMenu.Items.Select(i => i.Label.Text));
        host.Failing = false;
        host.Inner.Add("{ddd}", "Line in");
        VoiceTypingScene.Sync();
        scene.Advance(600);
        Assert.Equal(5, scene.Page.MicrophoneMenu.Items.Count);
    }, new FlakyCaptureHost(VoiceTypingPageRig.TwoMicrophones()));

    [Fact]
    [Trait("Spec", "V-T42")]
    [Trait("Decision", "ED-267")] // OD-0076: a changed Microphone value crossfades
    public void The_Microphone_value_crossfades_when_the_device_goes() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var host = (ScriptedCaptureHost)scene.Host;
        host.Remove(VoiceTypingPageRig.Usb);
        VoiceTypingScene.Sync();
        Assert.Equal(0, scene.Page.MicrophoneMenu.ValueLabel.IncomingOpacity);
        Assert.Equal(1, scene.Page.MicrophoneMenu.ValueLabel.OutgoingOpacity);
    }, VoiceTypingPageRig.TwoMicrophones(), arrange: model => model.Settings.VoiceTyping = model.Settings.VoiceTyping with { Mic = VoiceTypingPageRig.Usb });

    [Fact]
    [Trait("Spec", "V-T42")]
    [Trait("Decision", "ED-267")] // V-2.5: the seam not composed yet (VT-14 binds it) lists Windows default only, and the page builds
    public void With_the_microphone_seam_unbound_the_row_has_Windows_default_only() => VoiceTypingPageRig.Run(headless, scene =>
    {
        Assert.Equal(["Windows default"], scene.Page.MicrophoneMenu.Items.Select(i => i.Label.Text));
        Assert.Equal("Windows default", scene.Page.MicrophoneMenu.ValueLabel.Text);
    });

    [Fact]
    [Trait("Spec", "V-T42")]
    [Trait("Decision", "ED-267")] // the page stops listening to the devices when it leaves the window
    public void The_page_stops_watching_the_devices_when_it_is_detached() => VoiceTypingPageRig.Run(headless, scene =>
    {
        var host = (ScriptedCaptureHost)scene.Host;
        Assert.Equal(1, host.WatcherCount);
        scene.Window.Content = null;
        VoiceTypingScene.Sync();
        Assert.Equal(0, host.WatcherCount);
    }, VoiceTypingPageRig.TwoMicrophones());

    [Fact]
    [Trait("Spec", "V-T90")]
    [Trait("Decision", "ED-267")] // V-16.3.1: the Customization capsule opens the Customization page (a request SettingsView consumes)
    public void The_Customization_capsule_asks_for_the_Customization_page() => VoiceTypingPageRig.Run(headless, scene =>
    {
        SettingsNavigation.Shared.RequestedPage = null;
        try
        {
            scene.Page.OrbButton.Activate();
            Assert.Equal(SettingsPage.Customization, SettingsNavigation.Shared.RequestedPage);
        }
        finally { SettingsNavigation.Shared.RequestedPage = null; }
    });
}
