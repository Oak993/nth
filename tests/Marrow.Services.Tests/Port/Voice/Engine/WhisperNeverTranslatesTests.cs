using System.Runtime.CompilerServices;
using Marrow.Services.Port.Voice.Engine;

namespace Marrow.Services.Tests.Port.Voice.Engine;

// ED-NEW-voicefix-3: voice typing writes what was said in the language it was said in. The Whisper request never asks for translation (Whisper.net's WithTranslate,
// whisper.cpp's `translate`), Auto-detect reaches Whisper as detection (WhisperNetEngineDecodeTests pins "auto" to WithLanguageDetection), and nothing after the
// recogniser rewrites the words (WhisperTokenTextTests: the words of a Hindi or Urdu sentence come out as said).
public sealed class WhisperNeverTranslatesTests
{
    private static string Source(string file, [CallerFilePath] string here = "") =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "..", "..", "src", "Marrow.Services", "Port", "Voice", "Engine", file)));

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-NEW-voicefix-3")]
    public void The_whisper_request_has_no_translate_option_and_never_asks_for_one()
    {
        Assert.DoesNotContain(WhisperNetBackend.UsedMembers, m => m.Contains("Translate", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(BackendDecode).GetProperties(), p => p.Name.Contains("Translate", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("WithTranslate", Source("WhisperNetModel.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("Translate", Source("WhisperNetBackend.cs"), StringComparison.Ordinal);
    }
}
