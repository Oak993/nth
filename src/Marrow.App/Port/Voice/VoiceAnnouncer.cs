// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-10, voice-SPEC V-14.5): the composer's polite live region.
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.UI.Port;

namespace Marrow.App.Port.Voice;

/// <summary>
/// The text a screen reader announces without moving focus: one pixel, drawn in nothing, read for what the voice session says (<see cref="ComposerVoiceController.Announcement"/>).
/// It takes no pointer and no focus.
/// </summary>
internal sealed class VoiceAnnouncer : TextBlock
{
    public VoiceAnnouncer(ComposerVoiceController voice)
    {
        Width = 1;
        Height = 1;
        FontSize = 1;
        MaxLines = 1;
        ClipToBounds = true;
        IsHitTestVisible = false;
        Focusable = false;
        Foreground = Brushes.Transparent;
        Text = "";
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
        this.Track(() => voice.Announcement, said => Text = said);
    }
}
