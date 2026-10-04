using System.Globalization;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Marrow.Services.Tests.Port.Voice;

// Unit VT-0 (voice typing phase V0, the pins): tests for the packages, native binaries, bundled assets and notices that
// voice typing stands on. Spec: E:\Droppy\work\design\build-specs\voice\SPEC.md V-3.1 to V-3.6, V-6.1, V-7.1, V-9.x, V-T2 to V-T5.
// Owner rulings: OD-0091, OD-0092, OD-0097 (and amendments); OD-0099 picks Q-V-9 C (Vulkan and CPU in the installer, CUDA a
// separate download) and Q-V-5 (a FLEURS CC BY 4.0 clip, decided by the lead as ED-NEW-ledger-5).
//
// Expected values here come from the sources named beside them (nuget.org registration entries, the Hugging Face file
// listing and licence cards, the dataset card, the licence files of the projects), never from running the port (R-TEST-4).
// Nothing here opens the network, a microphone, a model or a GPU: files are read from the repository and the build output,
// and the native libraries are only parsed as files, never loaded (V-2.5).
//
// The recorded facts live in src/Marrow.Services/Port/Voice/Assets/voice-pins.json; these tests hold that file to the
// artifacts it describes, so a later unit reads it without re-checking.
public sealed class VoicePinsTests
{
    private const string Feature = "VT-0";

    private static readonly string Root = RepositoryRoot();

    private static string RepositoryRoot([CallerFilePath] string path = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(path)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Directory.Packages.props was not found above " + path);
    }

    private static string AssetsDirectory => Path.Combine(Root, "src", "Marrow.Services", "Port", "Voice", "Assets");

    private static string OutputAssetsDirectory => Path.Combine(AppContext.BaseDirectory, "Port", "Voice", "Assets");

    private static string Sha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static JsonElement Pins() => JsonDocument.Parse(File.ReadAllText(Path.Combine(AssetsDirectory, "voice-pins.json"), Encoding.UTF8)).RootElement;

    private static string ReadRepositoryText(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root }.Concat(parts).ToArray()), Encoding.UTF8);

    private static Assembly WhisperNet() => Assembly.Load("Whisper.net");

    private static Type WhisperType(string name) => WhisperNet().GetType("Whisper.net." + name, throwOnError: true)!;

    private static List<string> Signatures(Type type) => type
        .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
        .Select(m => m is MethodBase method ? m.Name + "(" + string.Join(",", method.GetParameters().Select(p => p.ParameterType.Name)) + ")" : m.Name)
        .ToList();

    // The five whisper.cpp VAD functions V-6.1 names for the fallback when Whisper.net hands out no per-frame probabilities.
    private static readonly string[] NativeVadExports =
    [
        "whisper_vad_free",
        "whisper_vad_init_from_file_with_params",
        "whisper_vad_detect_speech",
        "whisper_vad_n_probs",
        "whisper_vad_probs",
    ];

    // The 9 files the win-x64 CPU and Vulkan runtime packages place, per nuget.org Whisper.net.Runtime 1.9.1 and
    // Whisper.net.Runtime.Vulkan 1.9.1 (sizes and hashes of the files inside the packages, read from a restore).
    private static readonly (string Directory, string Name, long Bytes, string Sha256)[] PinnedNatives =
    [
        ("runtimes/win-x64", "ggml-base-whisper.dll", 636928, "e6c362421b6d5a5c1b52341f88d3d72764de29f05141748cc3faad2f6348063f"),
        ("runtimes/win-x64", "ggml-cpu-whisper.dll", 782848, "480a23afc6deea301c5cda66cf462fa2f2eb7ff9bca63b8f4f933a0e8dc5d4dd"),
        ("runtimes/win-x64", "ggml-whisper.dll", 67072, "2e0ac238c2bc178a5c1f09b3bde2421904b191bce1cc1c13dc64309ca3b9cb36"),
        ("runtimes/win-x64", "whisper.dll", 484864, "07ec2fac4946005ba7380f2c2e9ccfa3ad23007592d074aa698ed5df6bfb4938"),
        ("runtimes/vulkan/win-x64", "ggml-base-whisper.dll", 636928, "0faff6ab28a1e10dfc7319dc92608259d1f68aacb897b8dbff60323dce46bcb8"),
        ("runtimes/vulkan/win-x64", "ggml-cpu-whisper.dll", 782848, "98120d5ebb99beb37a319d9de624ffdb61bfe528a9829f9a6a1a55af3dab06c5"),
        ("runtimes/vulkan/win-x64", "ggml-vulkan-whisper.dll", 58013696, "e325725c18b88bed00443a2ca4ac3d6fdb7587fb055b52c623df3b04e87fb039"),
        ("runtimes/vulkan/win-x64", "ggml-whisper.dll", 67072, "4566056b3a710ffdf89c7627aa3ab7a73fb0f78374e14f13198628dcbf27cc2d"),
        ("runtimes/vulkan/win-x64", "whisper.dll", 484864, "56fe7c2e626fc0c981e87638d3e8c8bec39de8877636ac885929212b81ddb9ed"),
    ];

    private static List<string> PeImports(string path) => PeNames(path, imports: true);

    private static List<string> PeExports(string path) => PeNames(path, imports: false);

    private static List<string> PeNames(string path, bool imports)
    {
        var data = File.ReadAllBytes(path);
        var headers = new PEHeaders(new MemoryStream(data));
        int Offset(int rva)
        {
            foreach (var section in headers.SectionHeaders)
                if (rva >= section.VirtualAddress && rva < section.VirtualAddress + Math.Max(section.VirtualSize, section.SizeOfRawData))
                    return rva - section.VirtualAddress + section.PointerToRawData;
            return -1;
        }
        string Text(int rva)
        {
            var start = Offset(rva);
            var end = Array.IndexOf(data, (byte)0, start);
            return Encoding.ASCII.GetString(data, start, end - start);
        }
        var names = new List<string>();
        if (imports)
        {
            var table = Offset(headers.PEHeader!.ImportTableDirectory.RelativeVirtualAddress);
            for (var entry = table; BitConverter.ToInt32(data, entry + 12) != 0; entry += 20) names.Add(Text(BitConverter.ToInt32(data, entry + 12)));
        }
        else
        {
            var table = Offset(headers.PEHeader!.ExportTableDirectory.RelativeVirtualAddress);
            var count = BitConverter.ToInt32(data, table + 24);
            var namesTable = Offset(BitConverter.ToInt32(data, table + 32));
            for (var index = 0; index < count; index++) names.Add(Text(BitConverter.ToInt32(data, namesTable + 4 * index)));
        }
        return names;
    }

    // ---- V-T5: package pins equal V-3.1 (reads Directory.Packages.props) ----

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T5")]
    public void Directory_Packages_props_pins_the_four_voice_packages_at_the_V_3_1_versions()
    {
        var props = ReadRepositoryText("Directory.Packages.props");
        foreach (var (id, version) in new[] { ("Whisper.net", "1.9.1"), ("Whisper.net.Runtime", "1.9.1"), ("Whisper.net.Runtime.Vulkan", "1.9.1"), ("NAudio.Wasapi", "3.1.0") })
            Assert.Contains("<PackageVersion Include=\"" + id + "\" Version=\"" + version + "\" />", props, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T5")]
    public void The_Services_project_references_the_four_voice_packages_without_a_version_and_nothing_V_3_1_leaves_out()
    {
        var project = ReadRepositoryText("src", "Marrow.Services", "Marrow.Services.csproj");
        foreach (var id in new[] { "Whisper.net", "Whisper.net.Runtime", "Whisper.net.Runtime.Vulkan", "NAudio.Wasapi" })
            Assert.Contains("<PackageReference Include=\"" + id + "\" />", project, StringComparison.Ordinal);
        // V-3.1 "Not taken": CUDA runtimes (Q-V-9 C: a separate download), the no-AVX runtime (Q-V-12), any OpenAI SDK, ONNX Runtime.
        var props = ReadRepositoryText("Directory.Packages.props");
        foreach (var text in new[] { project, props })
        {
            Assert.DoesNotContain("Whisper.net.Runtime.Cuda", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Whisper.net.Runtime.NoAvx", text, StringComparison.Ordinal);
            Assert.DoesNotContain("OpenAI", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Microsoft.ML.OnnxRuntime", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-3.1")]
    public void The_resolved_transitive_closure_is_recorded_with_licences_and_matches_what_the_build_loads()
    {
        var closure = Pins().GetProperty("transitiveClosure").EnumerateArray().Select(e => (Id: e.GetProperty("id").GetString()!, Version: e.GetProperty("version").GetString()!, Licence: e.GetProperty("licence").GetString()!)).ToList();
        Assert.Equal(
            [("Microsoft.Extensions.AI.Abstractions", "10.2.0", "MIT"), ("NAudio.Core", "3.1.0", "MIT"), ("System.Numerics.Tensors", "9.0.0", "MIT")],
            closure.OrderBy(c => c.Id, StringComparer.Ordinal).ToArray());
        // The managed assemblies of the closure sit beside the tests (copied from the Services references).
        foreach (var assembly in new[] { "Whisper.net.dll", "NAudio.Wasapi.dll", "NAudio.Core.dll", "Microsoft.Extensions.AI.Abstractions.dll", "System.Numerics.Tensors.dll" })
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, assembly)), assembly);
    }

    // ---- V-T2: Whisper.net API members the adapter uses exist (reflection; names recorded) ----

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T2")]
    public void The_Whisper_net_members_V_7_1_names_exist()
    {
        var expected = new (string Type, string[] Members)[]
        {
            ("WhisperFactory", ["FromPath(String)", "FromPath(String,WhisperFactoryOptions)", "CreateBuilder()", "GetRuntimeInfo()", "Dispose()"]),
            ("WhisperFactoryOptions", ["UseGpu", "GpuDevice", "UseFlashAttention", "DelayInitialization"]),
            ("WhisperProcessorBuilder", ["Build()", "WithLanguage(String)", "WithLanguageDetection()", "WithThreads(Int32)", "WithGreedySamplingStrategy(Action`1)", "WithBeamSearchSamplingStrategy(Action`1)", "WithPrompt(String)", "WithTokenTimestamps()", "WithTemperature(Single)", "WithTemperatureInc(Single)", "WithSegmentEventHandler(OnSegmentEventHandler)"]),
            ("GreedySamplingStrategyBuilder", ["WithBestOf(Int32)"]),
            ("BeamSearchSamplingStrategyBuilder", ["WithBeamSize(Int32)", "WithPatience(Single)"]),
            ("WhisperProcessor", ["Process(Single[])", "Process(ReadOnlySpan`1)", "ProcessAsync(Single[],CancellationToken)", "ProcessAsync(ReadOnlyMemory`1,CancellationToken)", "DetectLanguage(Single[])", "DetectLanguageWithProbability(Single[])", "ChangeLanguage(String)", "Dispose()"]),
            ("SegmentData", ["Start", "End", "Text", "Language", "Probability", "NoSpeechProbability", "Tokens"]),
            ("WhisperToken", ["Text", "Start", "End", "Probability", "DtwTimestamp"]),
            ("WhisperVadFactory", ["FromPath(String)", "FromPath(String,WhisperFactoryOptions)", "CreateBuilder()", "Dispose()"]),
            ("WhisperVadProcessorBuilder", ["Build()", "WithThreshold(Single)", "WithUseGpu(Boolean)", "WithGpuDevice(Int32)", "WithThreads(Int32)", "WithMinSpeechDuration(TimeSpan)", "WithMinSilenceDuration(TimeSpan)", "WithSpeechPadding(TimeSpan)"]),
            ("WhisperVadProcessor", ["DetectSpeech(Single[])", "DetectSpeech(ReadOnlySpan`1)", "DetectSpeechNoReset(Single[])", "ResetState()", "Dispose()"]),
            ("VadSegmentData", ["Start", "End"]),
            ("LibraryLoader.RuntimeOptions", ["LibraryPath", "LoadedLibrary", "RuntimeLibraryOrder"]),
        };
        foreach (var (typeName, members) in expected)
        {
            var signatures = Signatures(WhisperType(typeName));
            foreach (var member in members) Assert.Contains(member, signatures);
        }
        // The runtime names V-3.2 orders and Q-V-9 C adds.
        var runtimes = Enum.GetNames(WhisperType("LibraryLoader.RuntimeLibrary"));
        foreach (var name in new[] { "Vulkan", "Cpu", "Cuda", "Cuda12" }) Assert.Contains(name, runtimes);
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T2")]
    public void The_recorded_member_names_all_exist_and_the_record_names_the_static_runtime_options()
    {
        var recorded = Pins().GetProperty("whisperNet").GetProperty("apiMembers").EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.NotEmpty(recorded);
        foreach (var entry in recorded)
        {
            // "LibraryLoader.RuntimeOptions.LibraryPath": the type name may itself hold a dot, the member is the last segment.
            var split = entry.LastIndexOf('.');
            Assert.Contains(entry[(split + 1)..], Signatures(WhisperType(entry[..split])));
        }
        var options = WhisperType("LibraryLoader.RuntimeOptions");
        foreach (var name in new[] { "LibraryPath", "LoadedLibrary", "RuntimeLibraryOrder" })
            Assert.True(options.GetProperty(name, BindingFlags.Public | BindingFlags.Static) is not null, name + " is a public static property");
        Assert.Equal("1.9.1", WhisperNet().GetName().Version is { } v ? (v.Major.ToString(CultureInfo.InvariantCulture) + "." + v.Minor.ToString(CultureInfo.InvariantCulture) + "." + v.Build.ToString(CultureInfo.InvariantCulture)) : "");
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-6.1")]
    public void Whisper_net_1_9_1_exposes_no_per_frame_speech_probability_so_the_native_export_fallback_is_the_route()
    {
        // V-6.1: "the per-frame probability API is UNVERIFIED". Verified here: the VAD types hand out segments only.
        foreach (var name in new[] { "WhisperVadProcessor", "WhisperVadFactory", "WhisperVadProcessorBuilder" })
            Assert.DoesNotContain(Signatures(WhisperType(name)), s => s.Contains("Prob", StringComparison.OrdinalIgnoreCase));
        Assert.True(Pins().GetProperty("whisperNet").GetProperty("perFrameProbabilityApi").GetBoolean() == false);
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-6.1")]
    public void The_shipped_whisper_dll_exports_the_five_VAD_functions_the_P_Invoke_fallback_needs()
    {
        foreach (var directory in new[] { "runtimes/win-x64", "runtimes/vulkan/win-x64" })
        {
            var exports = PeExports(Path.Combine(AppContext.BaseDirectory, directory, "whisper.dll"));
            foreach (var name in NativeVadExports) Assert.Contains(name, exports);
        }
        var recorded = Pins().GetProperty("whisperNet").GetProperty("nativeVadExports").EnumerateArray().Select(e => e.GetString()!).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(NativeVadExports.Order(StringComparer.Ordinal).ToArray(), recorded);
    }

    // ---- V-3.2: the published native file list and sizes ----

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-3.2")]
    public void The_build_output_holds_exactly_the_win_x64_CPU_and_Vulkan_natives_and_no_other_platform()
    {
        var runtimes = Path.Combine(AppContext.BaseDirectory, "runtimes");
        var actual = Directory.EnumerateFiles(runtimes, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(AppContext.BaseDirectory, f).Replace('\\', '/'))
            .Order(StringComparer.Ordinal).ToArray();
        var expected = PinnedNatives.Select(n => n.Directory + "/" + n.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, actual);
        foreach (var (directory, name, bytes, hash) in PinnedNatives)
        {
            var path = Path.Combine(AppContext.BaseDirectory, directory, name);
            Assert.Equal(bytes, new FileInfo(path).Length);
            Assert.Equal(hash, Sha256(path));
        }
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-3.2")]
    public void The_record_lists_the_same_natives_and_totals_them_against_the_50_MB_budget_honestly()
    {
        var natives = Pins().GetProperty("natives");
        var listed = natives.GetProperty("files").EnumerateArray()
            .Select(f => (Directory: f.GetProperty("directory").GetString()!, Name: f.GetProperty("name").GetString()!, Bytes: f.GetProperty("bytes").GetInt64(), Hash: f.GetProperty("sha256").GetString()!))
            .OrderBy(f => f.Directory + "/" + f.Name, StringComparer.Ordinal).ToArray();
        Assert.Equal(PinnedNatives.OrderBy(f => f.Directory + "/" + f.Name, StringComparer.Ordinal).ToArray(), listed);
        var total = PinnedNatives.Sum(n => n.Bytes);
        Assert.Equal(61957120L, total);
        Assert.Equal(total, natives.GetProperty("totalBytes").GetInt64());
        Assert.Equal(50_000_000L, natives.GetProperty("budgetBytes").GetInt64());
        // V-3.2's budget (RESEARCH ESTIMATE, "under 50 MB unpacked") is exceeded by the measurement; the record says so.
        Assert.False(natives.GetProperty("withinBudget").GetBoolean());
        Assert.Equal(total - 50_000_000L, natives.GetProperty("overBudgetBytes").GetInt64());
        Assert.InRange(natives.GetProperty("deflateBytes").GetInt64(), 18_000_000L, 20_000_000L);
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-3.2")]
    public void The_natives_import_the_Visual_C_runtime_DLLs_the_record_lists_and_the_record_states_who_carries_them()
    {
        var recorded = Pins().GetProperty("vcRuntime");
        var listed = recorded.GetProperty("importedByNatives").EnumerateArray().Select(e => e.GetString()!).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (directory, name, _, _) in PinnedNatives)
            foreach (var import in PeImports(Path.Combine(AppContext.BaseDirectory, directory, name)))
                if (import.StartsWith("VC", StringComparison.OrdinalIgnoreCase) || import.StartsWith("MSVC", StringComparison.OrdinalIgnoreCase)) found.Add(import);
        Assert.Equal(found.ToArray(), listed);
        Assert.Contains("VCRUNTIME140.dll", found);
        Assert.Contains("VCRUNTIME140_1.dll", found);
        Assert.Contains("MSVCP140.dll", found);
        Assert.Contains("VCOMP140.DLL", found);
        // The finding is stated, not left open: either the publish carries the runtime or the owner is asked (V-3.2).
        Assert.True(recorded.TryGetProperty("carriedByPublish", out var carried));
        Assert.True(carried.ValueKind is JsonValueKind.True or JsonValueKind.False);
        Assert.False(string.IsNullOrWhiteSpace(recorded.GetProperty("finding").GetString()));
    }

    // ---- V-T3: bundled VAD model hash pin ----

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T3")]
    public void The_bundled_Silero_VAD_model_is_the_pinned_file()
    {
        // ggml-org/whisper-vad at commit 9ffd54a1e1ee413ddf265af9913beaf518d1639b: ggml-silero-v6.2.0.bin, LFS oid (sha256) and size
        // from the repository's file listing; the repository card says license: mit.
        var path = Path.Combine(AssetsDirectory, "ggml-silero-v6.2.0.bin");
        Assert.Equal(885098L, new FileInfo(path).Length);
        Assert.Equal("2aa269b785eeb53a82983a20501ddf7c1d9c48e33ab63a41391ac6c9f7fb6987", Sha256(path));
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T3")]
    public void The_VAD_model_is_copied_beside_the_app_and_the_record_pins_its_source_and_licence()
    {
        var copied = Path.Combine(OutputAssetsDirectory, "ggml-silero-v6.2.0.bin");
        Assert.Equal("2aa269b785eeb53a82983a20501ddf7c1d9c48e33ab63a41391ac6c9f7fb6987", Sha256(copied));
        var vad = Pins().GetProperty("vad");
        Assert.Equal("ggml-silero-v6.2.0.bin", vad.GetProperty("file").GetString());
        Assert.Equal("ggml-org/whisper-vad", vad.GetProperty("repository").GetString());
        Assert.Equal("9ffd54a1e1ee413ddf265af9913beaf518d1639b", vad.GetProperty("commit").GetString());
        Assert.Equal("https://huggingface.co/ggml-org/whisper-vad/resolve/9ffd54a1e1ee413ddf265af9913beaf518d1639b/ggml-silero-v6.2.0.bin", vad.GetProperty("url").GetString());
        Assert.Equal(885098L, vad.GetProperty("bytes").GetInt64());
        Assert.Equal("2aa269b785eeb53a82983a20501ddf7c1d9c48e33ab63a41391ac6c9f7fb6987", vad.GetProperty("sha256").GetString());
        Assert.Equal("MIT", vad.GetProperty("licence").GetString());
        Assert.Equal("Copyright (c) 2020-present Silero Team", vad.GetProperty("copyright").GetString());
    }

    // ---- V-3.5: the self-test clip (Q-V-5, decided by the lead: a FLEURS clip, CC BY 4.0) ----

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-3.5")]
    public void The_self_test_clip_is_the_pinned_ten_second_16_kHz_mono_16_bit_WAV()
    {
        var path = Path.Combine(AssetsDirectory, "selftest-clip.wav");
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(320684, bytes.Length);
        Assert.Equal("acd1980c18801a7ffbc3e955c4340aab1dd0410b7b12af812b0166c3f5da0d3c", Sha256(path));
        Assert.Equal("RIFF", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(bytes, 8, 4));
        Assert.Equal("fmt ", Encoding.ASCII.GetString(bytes, 12, 4));
        Assert.Equal(1, BitConverter.ToInt16(bytes, 20));
        Assert.Equal(1, BitConverter.ToInt16(bytes, 22));
        Assert.Equal(16000, BitConverter.ToInt32(bytes, 24));
        Assert.Equal(16, BitConverter.ToInt16(bytes, 34));
        Assert.Equal("data", Encoding.ASCII.GetString(bytes, 36, 4));
        var data = BitConverter.ToInt32(bytes, 40);
        Assert.Equal(bytes.Length - 44, data);
        Assert.Equal(160320, data / 2);
        Assert.Equal("10.02", ((double)(data / 2) / 16000).ToString("0.00", CultureInfo.InvariantCulture));
        // The copy beside the app is the same file.
        Assert.Equal(Sha256(path), Sha256(Path.Combine(OutputAssetsDirectory, "selftest-clip.wav")));
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-3.5")]
    public void The_clip_record_names_the_dataset_commit_the_licence_and_the_change_made()
    {
        var clip = Pins().GetProperty("selfTestClip");
        Assert.Equal("google/fleurs", clip.GetProperty("dataset").GetString());
        Assert.Equal("70bb2e84b976b7e960aa89f1c648e09c59f894dd", clip.GetProperty("datasetCommit").GetString());
        Assert.Equal("CC-BY-4.0", clip.GetProperty("licence").GetString());
        Assert.Equal("https://creativecommons.org/licenses/by/4.0/", clip.GetProperty("licenceUrl").GetString());
        Assert.Equal("data/en_us/audio/dev.tar.gz", clip.GetProperty("archive").GetString());
        Assert.Equal("dev/10290896634244679159.wav", clip.GetProperty("member").GetString());
        Assert.Equal("39097469197371ff2e4eb4450f3b7b86b65e79b3962c5e8c4eadc77786f4204d", clip.GetProperty("originalSha256").GetString());
        Assert.Equal("acd1980c18801a7ffbc3e955c4340aab1dd0410b7b12af812b0166c3f5da0d3c", clip.GetProperty("sha256").GetString());
        Assert.Equal(320684L, clip.GetProperty("bytes").GetInt64());
        // The only change: the 32-bit float samples were rounded to 16-bit PCM (rate, channel and length unchanged).
        Assert.Contains("32-bit float", clip.GetProperty("change").GetString(), StringComparison.Ordinal);
        Assert.Contains("16-bit", clip.GetProperty("change").GetString(), StringComparison.Ordinal);
    }

    // ---- Q-V-9 C: the CUDA feasibility and pin record ----

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-3.2")]
    public void The_CUDA_pack_pins_are_the_nuget_org_registration_values()
    {
        // nuget.org registration entries (packageSize, packageHash in base64 of SHA-512) for the two Windows CUDA packs at 1.9.1.
        var cuda = Pins().GetProperty("cuda");
        var packs = cuda.GetProperty("packs");
        var twelve = packs.GetProperty("Whisper.net.Runtime.Cuda12.Windows");
        Assert.Equal("1.9.1", twelve.GetProperty("version").GetString());
        Assert.Equal("https://api.nuget.org/v3-flatcontainer/whisper.net.runtime.cuda12.windows/1.9.1/whisper.net.runtime.cuda12.windows.1.9.1.nupkg", twelve.GetProperty("url").GetString());
        Assert.Equal(250058371L, twelve.GetProperty("bytes").GetInt64());
        Assert.Equal("a73be8db5ef2f271cf7196aff36cc9fee1ecb67b02169367e9da1dace8b65a3c18de3ef1c8f701061197791befa51b9989163472e4fc0444dc63d9ca1aa54e67", twelve.GetProperty("sha512").GetString());
        Assert.Equal("pzvo217y8nHPcZav82zJ/uHstnsCFpNn6dodrOi2WjwY3j7xyPcBBhGXeRvvpRuZiRY0cuT8BETcY9nKGqVOZw==", twelve.GetProperty("sha512Base64").GetString());
        Assert.Equal("runtimes/cuda12/win-x64", twelve.GetProperty("loaderFolder").GetString());
        var thirteen = packs.GetProperty("Whisper.net.Runtime.Cuda.Windows");
        Assert.Equal("1.9.1", thirteen.GetProperty("version").GetString());
        Assert.Equal("https://api.nuget.org/v3-flatcontainer/whisper.net.runtime.cuda.windows/1.9.1/whisper.net.runtime.cuda.windows.1.9.1.nupkg", thirteen.GetProperty("url").GetString());
        Assert.Equal(142586522L, thirteen.GetProperty("bytes").GetInt64());
        Assert.Equal("27b6723999a182b64264b18d28da08c5619c0d06184ecaa20d86874ea022cbf628df037fed4017a8f100f48202986c29f17d16641364703f3af77e3200b80465", thirteen.GetProperty("sha512").GetString());
        Assert.Equal("J7ZyOZmhgrZCZLGNKNoIxWGcDQYYTsqiDYaHTqAiy/Yo3wN/7UAXqPEA9IICmGwp8X0WZBNkcD86934yALgEZQ==", thirteen.GetProperty("sha512Base64").GetString());
        Assert.Equal("runtimes/cuda/win-x64", thirteen.GetProperty("loaderFolder").GetString());
        Assert.Equal("Whisper.net.Runtime.Cuda12.Windows", cuda.GetProperty("offered").GetString());
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-3.2")]
    public void The_CUDA_feasibility_finding_states_how_the_loader_finds_a_downloaded_folder_and_what_the_pack_lacks()
    {
        var finding = Pins().GetProperty("cuda").GetProperty("feasibility");
        Assert.True(finding.GetProperty("loadsFromDownloadedFolder").GetBoolean());
        // RuntimeOptions.LibraryPath is a file path: its DIRECTORY is the search root, under which the loader looks in
        // runtimes/<runtime>/win-x64 (cuda12 for the CUDA 12 pack, cuda for the CUDA 13 pack, vulkan, and win-x64 for the CPU).
        Assert.Equal("directory-of-the-path", finding.GetProperty("libraryPathSemantics").GetString());
        Assert.Equal("falls-back-to-the-next-runtime", finding.GetProperty("whenCudaCannotLoad").GetString());
        // The pack carries no NVIDIA runtime libraries: the pack's own native imports name them.
        var needs = finding.GetProperty("packDoesNotCarry").EnumerateArray().Select(e => e.GetString()!).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "cublas64_12.dll", "cublasLt64_12.dll", "cudart64_12.dll" }, needs);
        var companions = Pins().GetProperty("cuda").GetProperty("nvidiaCompanions");
        Assert.Equal("12.4.2.65", companions.GetProperty("libcublas").GetProperty("version").GetString());
        Assert.Equal("4229d2b590e4382e1b36651dde644e04c5a8664f99f53bb43dca967982a84f58", companions.GetProperty("libcublas").GetProperty("sha256").GetString());
        Assert.Equal(391101865L, companions.GetProperty("libcublas").GetProperty("bytes").GetInt64());
        Assert.Equal("12.4.99", companions.GetProperty("cuda_cudart").GetProperty("version").GetString());
        Assert.Equal("1034ca43da4afbc79f69269cc60dc081de631e1c0d229d60f045d9516409317a", companions.GetProperty("cuda_cudart").GetProperty("sha256").GetString());
        Assert.Equal(2474121L, companions.GetProperty("cuda_cudart").GetProperty("bytes").GetInt64());
    }

    // ---- V-T4: THIRD_PARTY_NOTICES has the Voice typing section ----

    private static string VoiceNoticesSection()
    {
        var text = ReadRepositoryText("THIRD_PARTY_NOTICES.md").Replace("\r\n", "\n", StringComparison.Ordinal);
        var start = text.IndexOf("\n## Voice typing\n", StringComparison.Ordinal);
        Assert.True(start >= 0, "THIRD_PARTY_NOTICES.md has a '## Voice typing' section");
        var next = text.IndexOf("\n## ", start + 5, StringComparison.Ordinal);
        return next < 0 ? text[start..] : text[start..next];
    }

    [Theory]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T4")]
    [InlineData("### OpenAI Whisper (model weights and reference code)", "https://github.com/openai/whisper", "Copyright (c) 2022 OpenAI")]
    [InlineData("### whisper.cpp and ggml", "https://github.com/ggml-org/whisper.cpp", "Copyright (c) 2023-2026 The ggml authors")]
    [InlineData("### `Whisper.net`", "https://github.com/sandrohanea/whisper.net", "Copyright (c) 2024 sandrohanea")]
    [InlineData("### NAudio", "https://github.com/naudio/NAudio", "Copyright 2020 Mark Heath")]
    [InlineData("### Silero VAD", "https://github.com/snakers4/silero-vad", "Copyright (c) 2020-present Silero Team")]
    [InlineData("### Liquid Orb Editor (LerSent001)", "https://github.com/LerSent001/orb", "Copyright (c) 2026 LerSent001")]
    [InlineData("### voice-glow 0.2.1 (Jakub Antalik)", "https://www.npmjs.com/package/voice-glow", "Copyright (c) 2026 Jakub Antalik")]
    [InlineData("### Microsoft.Extensions.AI.Abstractions and System.Numerics.Tensors", "https://dot.net/", "Copyright (c) Microsoft Corporation")]
    [InlineData("### FLEURS (the self-test clip)", "https://huggingface.co/datasets/google/fleurs", "https://creativecommons.org/licenses/by/4.0/")]
    public void The_Voice_typing_section_credits_each_component_with_its_source_licence_and_copyright(string heading, string source, string line)
    {
        var section = VoiceNoticesSection();
        var at = section.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(at >= 0, heading);
        var next = section.IndexOf("\n### ", at + heading.Length, StringComparison.Ordinal);
        var block = next < 0 ? section[at..] : section[at..next];
        Assert.Contains(source, block, StringComparison.Ordinal);
        Assert.Contains(line, block, StringComparison.Ordinal);
        Assert.Contains(heading.StartsWith("### FLEURS", StringComparison.Ordinal) ? "CC BY 4.0" : "MIT", block, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T4")]
    public void The_Voice_typing_section_opens_with_V_3_6s_paragraph_and_carries_the_model_commit_and_both_licence_texts()
    {
        var section = VoiceNoticesSection();
        Assert.Contains("runs OpenAI's Whisper on the user's computer through whisper.cpp", section, StringComparison.Ordinal);
        Assert.Contains("and `Whisper.net`, captures the microphone with NAudio, detects speech with Silero VAD", section, StringComparison.Ordinal);
        Assert.Contains("(ggerganov/whisper.cpp, commit 5359861c739e955e79d9a303bcbc70fb988958b1) and are not part of the installer.", section, StringComparison.Ordinal);
        // The MIT permission notice appears once per credited work (nine works carry the licence text) and the Apache text once.
        Assert.True(CountOf(section, "Permission is hereby granted, free of charge, to any person obtaining a copy") >= 8);
        Assert.Contains("```\nApache License\nVersion 2.0, January 2004\nhttp://www.apache.org/licenses/\n\n   TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND DISTRIBUTION\n", section, StringComparison.Ordinal);
        Assert.Contains("END OF TERMS AND CONDITIONS", section, StringComparison.Ordinal);
        Assert.Contains("the Apache License 2.0 text is included below for the large-v3 files", section, StringComparison.Ordinal);
        Assert.Contains("while the repository states MIT", section, StringComparison.Ordinal);
        Assert.Contains("Border-beam and the Uiverse button are credited above (ED-251)", section, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Spec", "V-T4")]
    public void The_Voice_typing_section_never_leaves_the_name_Whisper_net_bare_for_the_markdown_autolinker()
    {
        // The notices are shown by MarkdownView, which links a bare domain whose last label is a known top-level domain
        // (MarkdownView.swift:772-826, "net" is one): a bare Whisper.net would open https://Whisper.net, an unrelated site.
        // Inline code is never linked, so the name is written as code; dotted package names (Whisper.net.Runtime) and URLs are not domains here.
        var bare = System.Text.RegularExpressions.Regex.Matches(VoiceNoticesSection(), @"(?<![A-Za-z0-9./`])Whisper\.net(?![A-Za-z0-9`]|\.[A-Za-z])", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        Assert.Empty(bare);
    }

    private static int CountOf(string text, string needle)
    {
        var count = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal)) count++;
        return count;
    }
}
