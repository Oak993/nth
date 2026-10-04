#:project ../../src/Marrow.Services/Marrow.Services.csproj
#:property PublishAot=false
#:property TargetFramework=net10.0-windows
// Developer gate tool of voice typing (unit VT-6; voice-SPEC test list row "voice-evidence"; BUILD-PLAN VT-6). Run by hand on the owner's Windows PC with a Whisper model that is
// already on disk and the bundled fixture WAV. No test runs it; it downloads nothing and reads no user data folder:
//
//   dotnet run tools/voice-evidence/VoiceEvidence.cs -- --model <ggml model file> [--clip <16 kHz mono 16-bit WAV>] [--out <folder>] [--data-root <folder with runtimes\cuda12>]
//
// It runs the real pieces and records what the specification marks UNVERIFIED:
//   1. the hardware probe (DXGI, Win32, registry) and the runtime Whisper.net loads for the VAD;
//   2. the Silero VAD per-frame probabilities through the whisper.cpp exports, against the same audio in ONE native call (it checks the rolling-window decision of SileroVad) and
//      against Whisper.net's own VAD segments;
//   3. WhisperNetEngine: a live pass over the first seconds and a final pass over the whole clip: the words and their times (the token time unit), the loaded runtime, the
//      real-time factor.
// Output: a text report and a JSON file in the output folder (default work\voice\evidence). Exit code 1 when a step fails.
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Port.Voice.Engine;
using Marrow.Services.Port.Voice.Probe;
using Whisper.net;

var options = ParseArguments(args);
if (!OperatingSystem.IsWindows()) return Fail("voice-evidence needs Windows: it loads the win-x64 natives and reads DXGI and the registry.");
if (options.Model is null || !File.Exists(options.Model)) return Fail("--model <ggml model file> is required and must exist (nothing is downloaded).");

var appBase = AppContext.BaseDirectory;
var assets = FindAssets();
var vadModel = Path.Combine(assets, "ggml-silero-v6.2.0.bin");
var clipPath = options.Clip ?? Path.Combine(assets, "selftest-clip.wav");
var clip = ReadWav(clipPath);
var seconds = clip.Length / 16000.0;
var report = new StringBuilder();
var json = new Dictionary<string, object?> { ["when"] = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture), ["clip"] = clipPath, ["clipSeconds"] = seconds, ["model"] = options.Model };
var failed = false;

void Say(string line)
{
    Console.WriteLine(line);
    report.AppendLine(line);
}

T? Step<T>(string name, Func<T> run)
{
    Say("== " + name);
    try
    {
        var value = run();
        return value;
    }
    catch (Exception error)
    {
        failed = true;
        Say("FAILED: " + error);
        json[name + ".error"] = error.ToString();
        return default;
    }
}

var inventory = new FileRuntimeInventory(appBase, options.DataRoot ?? Path.Combine(Path.GetTempPath(), "voice-evidence-no-cuda"), () => true);
var facts = inventory.Current();
Say("runtimes installed: " + string.Join(", ", facts.Installed) + "; library root " + facts.LibraryRoot);
json["runtimesInstalled"] = facts.Installed.Select(r => r.ToString()).ToArray();

// 1. the hardware probe
var profile = Step("hardware probe", () =>
{
    var rows = new List<ProbeRow>();
    var probe = new HardwareProbe(new WindowsHardwareSource(), new GpuBackendCheck(inventory.Current, new WhisperNetVadLoadTest(vadModel)));
    var result = probe.RunAsync(row => { rows.Add(row); Say("  " + row.Name + ": " + row.Text); }, CancellationToken.None).GetAwaiter().GetResult();
    json["probeRows"] = rows.Select(r => new { r.Name, r.Text }).ToArray();
    json["profile"] = result;
    return result;
});

// 2. the VAD
Step("silero vad", () =>
{
    var order = RuntimeOrder.For(facts with { NvidiaPresent = profile?.GpuVendor == GpuVendor.Nvidia }, true);
    var loaded = new WhisperNetVadLoadTest(vadModel).Run(order, facts.LibraryRoot);
    Say("  runtime that loaded the VAD: " + loaded);
    var dll = FileRuntimeInventory.AppRuntimeLibrary(appBase, loaded == RuntimeKind.Vulkan ? RuntimeKind.Vulkan : RuntimeKind.Cpu);
    var frames = clip.Length / 512;

    using var vad = SileroVad.Open(vadModel, dll, useGpu: false, threads: 2);
    var stopwatch = Stopwatch.StartNew();
    var perFrame = new float[frames];
    for (var i = 0; i < frames; i++) perFrame[i] = vad.Probability(clip.AsSpan(i * 512, 512));
    var perFrameMs = stopwatch.Elapsed.TotalMilliseconds / frames;

    using var whole = WhisperVadNative.Open(vadModel, dll, useGpu: false, threads: 2);
    var wholeBuffer = whole.Detect(clip.AsSpan(0, frames * 512));
    Say("  frames " + frames + ", native probabilities in one call " + wholeBuffer.Length);
    var common = Math.Min(frames, wholeBuffer.Length);
    var maxDifference = Enumerable.Range(0, common).Max(i => Math.Abs(perFrame[i] - wholeBuffer[i]));
    var sameSide = Enumerable.Range(0, common).Count(i => (perFrame[i] >= 0.5f) == (wholeBuffer[i] >= 0.5f)) / (double)common;
    Say(string.Create(CultureInfo.InvariantCulture, $"  rolling window ({SileroVad.DefaultWindowFrames} frames) against one call: max |difference| {maxDifference:F3}, same side of 0.5 for {sameSide:P1} of frames; {perFrameMs:F2} ms per frame"));

    using var factory = WhisperVadFactory.FromPath(vadModel, new WhisperFactoryOptions { UseGpu = false });
    using var processor = factory.CreateBuilder().WithUseGpu(false).WithThreads(2).Build();
    var segments = processor.DetectSpeech(clip).Select(s => (s.Start.TotalSeconds, s.End.TotalSeconds)).ToArray();
    Say("  Whisper.net VAD segments: " + string.Join(" ", segments.Select(s => string.Create(CultureInfo.InvariantCulture, $"[{s.Item1:F2}-{s.Item2:F2}]"))));
    var speechFrames = Enumerable.Range(0, common).Where(i => wholeBuffer[i] >= 0.5f).Select(i => i * 0.032).ToArray();
    Say(string.Create(CultureInfo.InvariantCulture, $"  frames at or above 0.5 in one call: {speechFrames.Length}, first at {(speechFrames.Length > 0 ? speechFrames[0] : double.NaN):F2} s"));
    json["vad"] = new { runtime = loaded?.ToString(), frames, nativeCount = wholeBuffer.Length, maxDifference, sameSide, perFrameMs, segments };
    return 0;
});

// 3. the engine
Step("engine", () =>
{
    using var engine = new WhisperNetEngine(new WhisperNetBackend(), inventory.Current, () => profile?.PhysicalCores ?? 4);
    var load = Stopwatch.StartNew();
    engine.LoadAsync(new SpeechModel(Path.GetFileNameWithoutExtension(options.Model), options.Model), new EngineLoadOptions(), CancellationToken.None).GetAwaiter().GetResult();
    Say(string.Create(CultureInfo.InvariantCulture, $"  loaded in {load.Elapsed.TotalSeconds:F2} s on {engine.LoadedRuntime} (GPU {engine.GpuActive})"));
    var results = new List<object>();
    foreach (var (name, mode, audio) in new[] { ("live, first 5 s", DecodeMode.Live, clip.AsMemory(0, Math.Min(clip.Length, 80000))), ("final, whole clip", DecodeMode.Final, clip.AsMemory()) })
    {
        var watch = Stopwatch.StartNew();
        var hypothesis = engine.TranscribeAsync(audio, new DecodeRequest("auto", "", mode, TimeSpan.Zero), CancellationToken.None).GetAwaiter().GetResult();
        var took = watch.Elapsed.TotalSeconds;
        var length = audio.Length / 16000.0;
        Say(string.Create(CultureInfo.InvariantCulture, $"  {name}: {hypothesis.Words.Count} words in {took:F2} s for {length:F1} s of audio (RTF {took / length:F3}); detected language {hypothesis.Language ?? "-"}"));
        foreach (var word in hypothesis.Words) Say(string.Create(CultureInfo.InvariantCulture, $"    {word.Start,6:F2} {word.End,6:F2}  {word.Text}"));
        results.Add(new { name, took, audioSeconds = length, rtf = took / length, words = hypothesis.Words.Select(w => new { w.Text, w.Start, w.End }).ToArray(), language = hypothesis.Language });
    }
    json["engine"] = new { runtime = engine.LoadedRuntime?.ToString(), gpu = engine.GpuActive, loadSeconds = load.Elapsed.TotalSeconds, passes = results };
    return 0;
});

Directory.CreateDirectory(options.Out);
var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
File.WriteAllText(Path.Combine(options.Out, "voice-evidence-" + stamp + ".txt"), report.ToString(), new UTF8Encoding(false));
File.WriteAllText(Path.Combine(options.Out, "voice-evidence-" + stamp + ".json"), JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
Say("saved to " + options.Out);
return failed ? 1 : 0;

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static (string? Model, string? Clip, string Out, string? DataRoot) ParseArguments(string[] args)
{
    string? model = null, clip = null, dataRoot = null;
    var output = Path.Combine("work", "voice", "evidence");
    for (var i = 0; i + 1 < args.Length; i += 2)
    {
        switch (args[i])
        {
            case "--model": model = args[i + 1]; break;
            case "--clip": clip = args[i + 1]; break;
            case "--out": output = args[i + 1]; break;
            case "--data-root": dataRoot = args[i + 1]; break;
        }
    }
    return (model, clip, output, dataRoot);
}

static string FindAssets()
{
    foreach (var candidate in new[] { Path.Combine(AppContext.BaseDirectory, "Port", "Voice", "Assets"), Path.Combine(Directory.GetCurrentDirectory(), "src", "Marrow.Services", "Port", "Voice", "Assets") })
        if (File.Exists(Path.Combine(candidate, "ggml-silero-v6.2.0.bin"))) return candidate;
    throw new FileNotFoundException("The bundled VAD model was not found beside the tool or under src\\Marrow.Services\\Port\\Voice\\Assets (run from the repository root).");
}

static float[] ReadWav(string path)
{
    var bytes = File.ReadAllBytes(path);
    if (bytes.Length < 44 || Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF" || Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE") throw new InvalidDataException(path + " is not a WAV file.");
    int channels = 0, rate = 0, bits = 0;
    for (var offset = 12; offset + 8 <= bytes.Length;)
    {
        var id = Encoding.ASCII.GetString(bytes, offset, 4);
        var size = BitConverter.ToInt32(bytes, offset + 4);
        if (id == "fmt ")
        {
            channels = BitConverter.ToInt16(bytes, offset + 10);
            rate = BitConverter.ToInt32(bytes, offset + 12);
            bits = BitConverter.ToInt16(bytes, offset + 22);
        }
        else if (id == "data")
        {
            if (channels != 1 || rate != 16000 || bits != 16) throw new InvalidDataException("The clip must be 16 kHz mono 16-bit PCM.");
            var length = Math.Min(size, bytes.Length - offset - 8) / 2;
            var samples = new float[length];
            for (var i = 0; i < length; i++) samples[i] = BitConverter.ToInt16(bytes, offset + 8 + i * 2) / 32768f;
            return samples;
        }
        offset += 8 + size + (size & 1);
    }
    throw new InvalidDataException("No data chunk in " + path);
}
