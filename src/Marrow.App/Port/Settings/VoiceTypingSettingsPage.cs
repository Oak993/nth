// PORT-ONLY, first-release VT-13 and VT-14 (voice-SPEC V-16.3.1, V-16.3.5; OD-0099 Q-V-19 B, Q-CZ-1 A; no chrome accessory): the Voice typing Settings page, page A: the Voice typing section
// (Engine, the OpenAI key row, Language, Microphone, the Customization link) and the privacy line. Page B (the Speech model section) is VT-14's and goes into <see cref="Sections"/>
// between the two; the key row's field and storing are VT-16's. The page is hidden from the sidebar, from search and from SettingsNavigation until IsReady is true (SA-1's pattern,
// voice-SPEC V-2.6's VoiceFeature.Enabled); VT-13 sets it. Tracked as incomplete in porting/STATE.md.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Port.Voice;
using Marrow.App.Views.Settings;
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Capture;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

/// <summary>
/// The Voice typing page (voice-SPEC V-16.3). Built from the port's <see cref="ChromeSection"/>, <see cref="ChromeCard"/> and <see cref="ChromeRowDivider"/> and CZ-1's controls
/// (ED-312). Every row follows its setting (<c>voiceTyping</c>, and <c>voiceOrb.langMode</c> for the Language row's default) and writes it back at once; every change is
/// transitioned: segmented pill, folds, crossfading details and values (OD-0076).
/// </summary>
internal sealed class VoiceTypingSettingsPage : Decorator, ISettingsPageChrome
{
    /// <summary>False until the unit that builds this page sets it; while false the page is not listed or searchable.</summary>
    internal const bool IsReady = true;

    private const string CloudId = "cloud";
    private const string LocalId = "local";

    // The sections sit Chrome.SectionSpacing apart, but each spacing belongs to the section it precedes (the Speech model fold carries its own, and the privacy line its margin), so that a folded-away
    // section leaves no gap behind it (VT-14).
    private readonly StackPanel root = new() { Spacing = 0 };
    private AppSettings? settings;
    private ICaptureHost? captureHost;
    private IDisposable? deviceWatch;
    private SpeechModelController? speech;
    private bool speechWatched;
    private bool built;
    private bool refreshing;
    private VoiceKeyEditor? keyEditor;

    public VoiceTypingSettingsPage()
    {
        Child = root;
        this.Track(() => AppEnvironment.GetAppModel(this), model => Build(model.Settings));
        AttachedToVisualTree += (_, _) => { WatchDevices(); WatchSpeech(); };
        DetachedFromVisualTree += (_, _) => { StopWatching(); StopWatchingSpeech(); keyEditor?.Close(); };
        WindowsDisplayLanguage.Watch(this, Refresh); // the Language row names the display language under language default C
    }

    /// <inheritdoc />
    public bool HasChromeControls => false;

    /// <inheritdoc />
    public IReadOnlyList<Control> ChromeAccessories { get; } = [];

    /// <summary>The page's sections, top to bottom: the Voice typing section, the Speech model section (folded away for the cloud engine), then the privacy line. Each section after the first carries its own top spacing.</summary>
    internal StackPanel Sections => root;

    /// <summary>The Speech model section (VT-14): the hardware card and the model list.</summary>
    internal SpeechModelSection SpeechModel { get; private set; } = null!;

    /// <summary>The fold that takes the Speech model section away for the cloud engine (V-16.3.2).</summary>
    internal CzFoldRow SpeechFold { get; private set; } = null!;

    internal VoiceSettingsRow EngineRow { get; private set; } = null!;
    internal CzSegmentedControl EngineControl { get; private set; } = null!;
    internal VoiceSettingsRow KeyRow { get; private set; } = null!;
    internal CzFoldRow KeyFold { get; private set; } = null!;
    internal CzCapsuleButton KeyButton { get; private set; } = null!;
    internal VoiceSettingsRow LanguageRow { get; private set; } = null!;
    internal CzMenuButton LanguageMenu { get; private set; } = null!;
    internal VoiceSettingsRow MicrophoneRow { get; private set; } = null!;
    internal CzMenuButton MicrophoneMenu { get; private set; } = null!;
    internal VoiceSettingsRow OrbRow { get; private set; } = null!;
    internal CzCapsuleButton OrbButton { get; private set; } = null!;
    internal CzCrossfadeText PrivacyLine { get; private set; } = null!;

    private void Build(AppSettings store)
    {
        if (built) return;
        built = true;
        settings = store;
        captureHost = CaptureSeams.Host.Current;
        var typing = store.VoiceTyping;
        var cloud = typing.Engine == VoiceEngine.Cloud;
        var language = VoiceLanguageRow.For(typing.Lang, store.VoiceOrb.LangMode, WindowsDisplayLanguage.Current());
        var microphone = MicrophoneRowState.Of(captureHost, typing.Mic);

        // Engine (V-16.3.1): segmented sm; the detail follows the engine.
        EngineControl = new CzSegmentedControl(VoiceTypingText.EngineTitle, [(LocalId, VoiceTypingText.EngineLocal), (CloudId, VoiceTypingText.EngineCloud)], cloud ? CloudId : LocalId, small: true);
        EngineControl.SelectionChanged += id => Write(current => current with { Engine = id == CloudId ? VoiceEngine.Cloud : VoiceEngine.Local });
        EngineRow = new VoiceSettingsRow(VoiceTypingText.EngineTitle, EngineControl, cloud ? VoiceTypingText.EngineCloudDetail : VoiceTypingText.EngineLocalDetail);

        // OpenAI key (V-16.3.1): the Providers protected field pattern, with its own Add key / Cancel fold. Merely picking cloud stores no key and makes no request.
        keyEditor = new VoiceKeyEditor();
        KeyButton = keyEditor.Button;
        KeyButton.IsEnabled = KeyButton.Focusable = cloud;
        KeyRow = new VoiceSettingsRow(VoiceTypingText.KeyTitle, KeyButton, VoiceTypingText.KeyDetail);
        KeyFold = new CzFoldRow(new StackPanel { Children = { new ChromeRowDivider(), KeyRow, keyEditor.Fold } }, cloud);

        // Language (V-16.3.1, V-7.14): the menu names the language default's value while nothing is picked.
        LanguageMenu = new CzMenuButton(VoiceTypingText.LanguageTitle, VoiceLanguageRow.MenuItems, language.CheckedCode, valueText: language.ButtonText);
        LanguageMenu.SelectionChanged += code => Write(current => VoiceLanguage.WithPagePick(current, code));
        LanguageRow = new VoiceSettingsRow(VoiceTypingText.LanguageTitle, LanguageMenu, language.Detail);

        // Microphone (OD-0099 Q-V-19 B): "Windows default" first, then the active endpoints (VT-5's seam); the pick is stored as voiceTyping.mic.
        MicrophoneMenu = new CzMenuButton(VoiceTypingText.MicrophoneTitle, microphone.MenuOptions, microphone.SelectedId);
        MicrophoneMenu.SelectionChanged += id => Write(current => current with { Mic = MicrophoneRowState.ValueOf(id) });
        MicrophoneRow = new VoiceSettingsRow(VoiceTypingText.MicrophoneTitle, MicrophoneMenu);

        // Orb and after voice input (V-16.3.1): opens Customization.
        OrbButton = new CzCapsuleButton(VoiceTypingText.OrbButton, link: true);
        OrbButton.Invoked += () => SettingsNavigation.Shared.RequestedPage = SettingsPage.Customization;
        OrbRow = new VoiceSettingsRow(VoiceTypingText.OrbTitle, OrbButton, VoiceTypingText.OrbDetail);

        var rows = new StackPanel();
        rows.Children.Add(EngineRow);
        rows.Children.Add(KeyFold);
        rows.Children.Add(new ChromeRowDivider());
        rows.Children.Add(LanguageRow);
        rows.Children.Add(new ChromeRowDivider());
        rows.Children.Add(MicrophoneRow);
        rows.Children.Add(new ChromeRowDivider());
        rows.Children.Add(OrbRow);
        var section = new ChromeSection(VoiceTypingText.Section, () => new ChromeCard(() => rows));
        // The rows follow the settings, wherever they change (Customization's language default resets voiceTyping.lang).
        section.Track(() => (store.VoiceTyping, store.VoiceOrb.LangMode), _ => Refresh());
        root.Children.Add(section);

        // Speech model (V-16.3.2): folds away for the cloud engine; its own top spacing folds with it.
        speech = SpeechModelController.For(store);
        SpeechModel = new SpeechModelSection(store, speech);
        SpeechFold = new CzFoldRow(new Border { Padding = new Thickness(0, Chrome.SectionSpacing, 0, 0), Child = SpeechModel }, !cloud);
        root.Children.Add(SpeechFold);
        SpeechFold.Track(() => store.VoiceHardware, _ => SpeechModel.Refresh());

        // Privacy line (V-16.3.5): 12 / 17, secondary; the design's .czpriv padding 4 4 0.
        PrivacyLine = new CzCrossfadeText(cloud ? VoiceTypingText.PrivacyCloud : VoiceTypingText.PrivacyLocal) { Wrap = true, Secondary = true, FontSize = 12, Margin = new Thickness(4, Chrome.SectionSpacing + 4, 4, 0) };
        PrivacyLine.SetValue(SwiftFont.LineBoxProperty, 17.0);
        root.Children.Add(PrivacyLine);
        WatchDevices();
        WatchSpeech();
    }

    // The Speech model section follows its controller (the probe, the downloads and the answers tell it from any thread, on the UI thread).
    private void WatchSpeech()
    {
        if (!built || speechWatched || speech is null) return;
        speechWatched = true;
        speech.Changed += SpeechModel.Refresh;
        SpeechModel.Refresh();
    }

    private void StopWatchingSpeech()
    {
        if (!speechWatched || speech is null) return;
        speechWatched = false;
        speech.Changed -= SpeechModel.Refresh;
    }

    // Stores a change of voiceTyping and shows it at once (the tracked read would follow on the next turn of the loop).
    private void Write(Func<VoiceTypingSettings, VoiceTypingSettings> change)
    {
        if (settings is null || refreshing) return;
        var next = change(settings.VoiceTyping);
        if (next != settings.VoiceTyping) settings.VoiceTyping = next;
        Refresh();
    }

    // Shows what the settings say: the engine, the key row's fold, the privacy line, the Language and Microphone rows. Idempotent.
    private void Refresh()
    {
        if (settings is null || refreshing) return;
        refreshing = true;
        try
        {
            var typing = settings.VoiceTyping;
            var cloud = typing.Engine == VoiceEngine.Cloud;
            EngineControl.Select(cloud ? CloudId : LocalId);
            EngineRow.SetDetail(cloud ? VoiceTypingText.EngineCloudDetail : VoiceTypingText.EngineLocalDetail);
            KeyFold.Open = cloud;
            // Lead VT-16 ruling: the collapsed key row leaves no enabled/focusable button, even during its exit transition.
            KeyButton.IsEnabled = KeyButton.Focusable = cloud;
            if (!cloud) keyEditor?.Close();
            SpeechFold.Open = !cloud;
            SpeechModel.Refresh();
            PrivacyLine.SetText(cloud ? VoiceTypingText.PrivacyCloud : VoiceTypingText.PrivacyLocal);
            var language = VoiceLanguageRow.For(typing.Lang, settings.VoiceOrb.LangMode, WindowsDisplayLanguage.Current());
            LanguageMenu.SetOptions(VoiceLanguageRow.MenuItems, language.CheckedCode, language.ButtonText);
            LanguageRow.SetDetail(language.Detail);
            RefreshMicrophone();
        }
        finally { refreshing = false; }
    }

    private void RefreshMicrophone()
    {
        if (settings is null || captureHost is null) return;
        // A listing that fails (the audio service stopped) keeps the list the page has, and throws nothing into the device-watch callback (review of VT-13).
        if (!MicrophoneRowState.TryOf(captureHost, settings.VoiceTyping.Mic, out var state)) return;
        MicrophoneMenu.SetOptions(state.MenuOptions, state.SelectedId);
    }

    // The list follows the devices (VT-5's Watch; any thread): plugging in lists a device, unplugging one that is picked shows Windows default until it returns.
    private void WatchDevices()
    {
        if (!built || deviceWatch is not null || captureHost is null) return;
        try { deviceWatch = captureHost.Watch(_ => Dispatcher.UIThread.Post(RefreshMicrophone)); }
        catch (Exception) { deviceWatch = null; } // the seam is not composed (V-2.5) or the system cannot watch: the list stays as listed
    }

    private void StopWatching()
    {
        deviceWatch?.Dispose();
        deviceWatch = null;
    }
}
