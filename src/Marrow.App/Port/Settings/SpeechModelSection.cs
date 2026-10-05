// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092; first-release voice typing, VT-14): the Speech model section of the Voice typing page (voice-SPEC V-16.3.2, V-16.3.3).
using Avalonia;
using Avalonia.Controls;
using Marrow.App.Port.Voice;
using Marrow.UI.Chrome;

namespace Marrow.App.Port.Settings;

/// <summary>
/// The section headed `Speech model`: the hardware card and, 10 pt under it, the model list (DESIGN gen.py:638-663: `.card.hwcard` and `.card.mlist` with margin-top 10). The page folds the whole
/// section away for the cloud engine.
/// </summary>
internal sealed class SpeechModelSection : Decorator
{
    public SpeechModelSection(AppSettings settings, SpeechModelController controller)
    {
        HardwareCard = new VoiceHardwareCard(controller, settings);
        ModelList = new VoiceModelList(controller, settings) { Margin = new Thickness(0, 10, 0, 0) };
        Child = new ChromeSection(SpeechModelText.Section, () => new StackPanel { Orientation = Avalonia.Layout.Orientation.Vertical, Children = { HardwareCard, ModelList } });
    }

    public VoiceHardwareCard HardwareCard { get; }

    public VoiceModelList ModelList { get; }

    /// <summary>Shows both cards what the controller and the settings say now.</summary>
    public void Refresh()
    {
        HardwareCard.Refresh();
        ModelList.Refresh();
    }
}
