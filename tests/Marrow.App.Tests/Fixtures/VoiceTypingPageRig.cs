using Avalonia.Controls;
using Marrow.App.Port;
using Marrow.App.Port.Settings;
using Marrow.App.Port.Voice;
using Marrow.Core.Port;
using Marrow.Services.Port.Voice.Capture;
using Marrow.Services.Port.Voice.Probe;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// The Voice typing page in a shown window over a throwaway model (VT-13): the microphone seam is a scripted host (or the fail-closed default), Windows' display language is
/// a fixed answer, frames are a manual source and popups stay in the window's overlay. No device, network or real language is read.
/// </summary>
internal static class VoiceTypingPageRig
{
    public const string Realtek = "{0.0.1.00000000}.{aaa}";
    public const string Usb = "{0.0.1.00000000}.{bbb}";

    public static ScriptedCaptureHost TwoMicrophones() => new((Realtek, "Microphone Array (Realtek)"), (Usb, "Headset (USB)"));

    /// <summary>
    /// VT-14 adds the Speech model section's seams: the model backend, the hardware probe and the self-test (each defaults to its fail-closed ambient: nothing installed, no hardware read, nothing
    /// timed). A scripted backend is told which model is in use (the settings' <c>voiceTyping.model</c>), as the store is.
    /// </summary>
    public static void Run(HeadlessSession headless, Action<VoiceTypingScene> check, ICaptureHost? host = null, string windowsLanguage = "en-US", Action<AppModel>? arrange = null,
        ISpeechModelBackend? backend = null, IHardwareProbe? probe = null, ISpeechSelfTest? selfTest = null) =>
        headless.OnUiThread(() =>
        {
            using var harness = new AppModelHarness();
            var model = harness.MakeModel();
            using var actor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
            var frames = new ManualFrameSource();
            using var frameScope = FrameClock.Source.Override(frames);
            using var presenter = Presentation.Presenter.Override(new ReviewOverlayPresenter(Presentation.Presenter.Current));
            using var language = WindowsDisplayLanguage.Source.Override(() => windowsLanguage);
            using var capture = host is null ? null : CaptureSeams.Host.Override(host);
            if (backend is ScriptedSpeechModelBackend scripted) scripted.InUse = () => model.Settings.VoiceTyping.Model;
            using var backendScope = backend is null ? null : SpeechModelSeams.Backend.Override(backend);
            using var probeScope = probe is null ? null : HardwareProbeSeams.Probe.Override(probe);
            using var selfTestScope = selfTest is null ? null : SpeechModelSeams.SelfTest.Override(selfTest);
            arrange?.Invoke(model);
            var page = new VoiceTypingSettingsPage();
            var holder = new Border { Child = page };
            AppEnvironment.SetAppModel(holder, model);
            var window = new Avalonia.Controls.Window { Width = 780, Height = 900, Content = holder };
            SettingsProviderPopupLayers.Install(window);
            try
            {
                window.Show();
                window.UpdateLayout();
                VoiceTypingScene.Sync();
                check(new VoiceTypingScene(page, model, CaptureSeams.Host.Current, window, frames, harness.Runtime!, SpeechModelController.For(model.Settings), SpeechModelSeams.Backend.Current));
            }
            finally
            {
                window.Close();
                VoiceTypingScene.Sync();
            }
        });
}
