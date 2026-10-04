using Marrow.UI.Port.Voice.Orb;

namespace Marrow.UI.Tests.Port.Voice;

/// <summary>The headless renderer is Skia's software rasterizer, where the orb draws one still unless asked (the composer light's ED-251 rule): turns the motion on for a test.</summary>
internal sealed class VoiceOrbMotion : IDisposable
{
    public VoiceOrbMotion() => VoiceOrbHandler.AnimateWithoutGpu = true;

    public void Dispose() => VoiceOrbHandler.AnimateWithoutGpu = false;
}
