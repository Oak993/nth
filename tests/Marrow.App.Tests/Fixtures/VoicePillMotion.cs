using Marrow.UI.Port.Voice.Orb;
using Marrow.Core.Port;
using Avalonia.Styling;

namespace Marrow.App.Tests.Fixtures;

// V-T132: exercise the real pop and breathing on the headless software compositor's manual clock.
internal sealed class VoicePillMotion : IDisposable
{
    private readonly bool previous = VoiceOrbHandler.AnimateWithoutGpu;

    public VoicePillMotion() => VoiceOrbHandler.AnimateWithoutGpu = true;

    // Replay the compositor's driver at every scripted frame without painting the whole window between pixel samples.
    public static void Step(VoiceOrbView orb, double scale)
    {
        var clock = SwiftClock.Provider.Current;
        orb.Handler!.Scene!.Advance(clock.GetTimestamp() / (double)clock.TimestampFrequency,
            new OrbSceneInput(orb.Kind, orb.Mode, orb.Hovered, orb.Pressed, orb.Bands, orb.SlotDiameter,
                orb.ActualThemeVariant == ThemeVariant.Dark, false), scale);
    }

    public void Dispose() => VoiceOrbHandler.AnimateWithoutGpu = previous;
}
