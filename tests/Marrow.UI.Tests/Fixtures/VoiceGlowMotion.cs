using Marrow.UI.Port.Voice.Glow;

namespace Marrow.UI.Tests.Fixtures;

/// <summary>VT-9: the headless renderer is Skia's software rasterizer, where the glow is one still unless asked (ED-336, as ED-251 for the composer light).</summary>
internal sealed class VoiceGlowMotion : IDisposable
{
    public VoiceGlowMotion() => VoiceGlowHandler.AnimateWithoutGpu = true;

    public void Dispose() => VoiceGlowHandler.AnimateWithoutGpu = false;
}
