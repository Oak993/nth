// PORT-ONLY, owner-ruled addition (OD-0092; first-release voice typing, VT-15, voice-SPEC V-3.5, V-10.3): the bundled 10 s clip.
namespace Marrow.App.Port.Voice;

/// <summary>Reads selftest-clip.wav (16 kHz, mono, 16-bit PCM, 44-byte header) as the engine takes audio.</summary>
public static class SelfTestClip
{
    public static string DefaultPath => throw new NotImplementedException();

    public static ReadOnlyMemory<float> Load(string path) => throw new NotImplementedException();
}
