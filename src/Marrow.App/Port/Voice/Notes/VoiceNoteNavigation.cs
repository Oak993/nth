// PORT-ONLY: OD-0106's picked links target actual Windows Settings pages or the named row of Marrow's Voice typing page.
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Port.Settings;
using Marrow.App.Views.Settings;
using Marrow.App.Window;
using Avalonia.LogicalTree;
namespace Marrow.App.Port.Voice.Notes;
internal static class VoiceNoteNavigation
{
    internal static void Open(string target)
    {
        var manager = WindowManager.Shared;
        manager.ShowSettings(SettingsPage.VoiceTyping);
        Dispatcher.UIThread.Post(() =>
        {
            if (manager.SettingsOverlay is not { IsOpen: true } settings) return; // OD-0151: the Settings panel in the main window
            var page = settings.Content.GetLogicalDescendants().OfType<VoiceTypingSettingsPage>().FirstOrDefault();
            if (page is null) return;
            Control row = target switch
            {
                "voice:microphone" => page.MicrophoneRow,
                "voice:speechModel" => page.SpeechModel,
                "voice:engine" => page.EngineRow,
                "voice:openAiKey" => page.KeyRow,
                _ => page,
            };
            row.BringIntoView();
            var control = target switch
            {
                "voice:microphone" => (Control)page.MicrophoneMenu,
                "voice:speechModel" => page.SpeechModel.HardwareCard.CheckButton,
                "voice:engine" => page.EngineControl,
                "voice:openAiKey" => page.KeyButton,
                _ => null,
            };
            control?.Focus();
        }, DispatcherPriority.Loaded);
    }
}
