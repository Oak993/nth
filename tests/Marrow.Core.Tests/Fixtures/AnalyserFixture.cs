using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Marrow.Core.Tests.Fixtures;

// VT-2 (voice-SPEC V-5.3, V-T18) test fixture: a signal and what headless Chrome's real AnalyserNode read from it, made locally by
// tests/fixtures/voice/analyser/generate.cjs and committed. The reads are parallel arrays indexed by read number.
public sealed class AnalyserFixture
{
    public required string Name { get; init; }

    public required int SampleRate { get; init; }

    /// <summary>The input samples (device-rate mono float32), exactly the floats Chrome was given.</summary>
    public required float[] Samples { get; init; }

    /// <summary>The frame at which each read was taken: the analyser's window is the 2048 frames before it.</summary>
    public required int[] Positions { get; init; }

    /// <summary>Chrome's getByteFrequencyData for each read (1024 bytes).</summary>
    public required byte[][] Spectrum { get; init; }

    public required double[] Rms { get; init; }

    public required double[] All { get; init; }

    public required double[] Low { get; init; }

    public required double[] Mid { get; init; }

    public required double[] High { get; init; }

    public static string[] Names { get; } = ["tone1k-48000", "sweep-44100", "voicey-48000", "mixed-16000"];

    public static AnalyserFixture Load(string name)
    {
        var dir = Path.Combine(RepositoryRoot(), "tests", "fixtures", "voice", "analyser");
        var raw = File.ReadAllBytes(Path.Combine(dir, name + ".f32"));
        var samples = new float[raw.Length / 4];
        Buffer.BlockCopy(raw, 0, samples, 0, raw.Length);
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, name + ".json")));
        var reads = doc.RootElement.GetProperty("reads").EnumerateArray().ToArray();
        return new AnalyserFixture
        {
            Name = name,
            SampleRate = doc.RootElement.GetProperty("sampleRate").GetInt32(),
            Samples = samples,
            Positions = reads.Select(r => r.GetProperty("p").GetInt32()).ToArray(),
            Spectrum = reads.Select(r => Convert.FromBase64String(r.GetProperty("spectrum").GetString()!)).ToArray(),
            Rms = reads.Select(r => r.GetProperty("rms").GetDouble()).ToArray(),
            All = reads.Select(r => r.GetProperty("all").GetDouble()).ToArray(),
            Low = reads.Select(r => r.GetProperty("low").GetDouble()).ToArray(),
            Mid = reads.Select(r => r.GetProperty("mid").GetDouble()).ToArray(),
            High = reads.Select(r => r.GetProperty("high").GetDouble()).ToArray(),
        };
    }

    private static string RepositoryRoot([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", ".."));
}
