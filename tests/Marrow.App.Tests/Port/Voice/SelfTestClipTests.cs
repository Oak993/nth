using System.Runtime.CompilerServices;
using Marrow.App.Port.Voice;

namespace Marrow.App.Tests.Port.Voice;

// VT-15 (V-3.5, V-10.3): the bundled 10 s clip reads as 16 kHz mono floats (the audio the timed decode gets).
public sealed class SelfTestClipTests
{
    private static string Asset([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", "..", "..", "src", "Marrow.Services", "Port", "Voice", "Assets", "selftest-clip.wav"));

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Decision", "ED-289")]
    public void The_bundled_clip_is_160320_samples_in_range_and_not_silent()
    {
        var samples = SelfTestClip.Load(Asset()).ToArray();
        Assert.Equal(160320, samples.Length);
        Assert.All(samples, s => Assert.InRange(s, -1.0f, 1.0f));
        Assert.Contains(samples, s => Math.Abs(s) > 0.01f);
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Decision", "ED-289")]
    public void The_default_path_is_beside_the_app_under_the_voice_assets()
    {
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "Port", "Voice", "Assets", "selftest-clip.wav"), SelfTestClip.DefaultPath);
    }

    [Fact]
    [Trait("Feature", "VT-15")]
    [Trait("Decision", "ED-289")]
    public void A_file_that_is_not_a_16k_mono_16_bit_wav_is_refused()
    {
        var path = Path.Combine(Path.GetTempPath(), "marrow-vt15-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            File.WriteAllBytes(path, new byte[100]);
            Assert.Throws<InvalidDataException>(() => SelfTestClip.Load(path));
        }
        finally { File.Delete(path); }
    }
}
