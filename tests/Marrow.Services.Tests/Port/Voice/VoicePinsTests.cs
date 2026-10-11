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
// separate download) and Q-V-5 (a FLEURS CC BY 4.0 clip, ED-267).
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

    internal static JsonElement Pins() => JsonDocument.Parse(File.ReadAllText(Path.Combine(AssetsDirectory, "voice-pins.json"), Encoding.UTF8)).RootElement;

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
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T5")]
    public void Directory_Packages_props_pins_the_four_voice_packages_at_the_V_3_1_versions()
    {
        var props = ReadRepositoryText("Directory.Packages.props");
        foreach (var (id, version) in new[] { ("Whisper.net", "1.9.1"), ("Whisper.net.Runtime", "1.9.1"), ("Whisper.net.Runtime.Vulkan", "1.9.1"), ("NAudio.Wasapi", "3.1.0") })
            Assert.Contains("<PackageVersion Include=\"" + id + "\" Version=\"" + version + "\" />", props, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T5")]
    public void The_Services_project_references_the_four_voice_packages_without_a_version_and_nothing_V_3_1_leaves_out()
    {
        var project = ReadRepositoryText("src", "Marrow.Services", "Marrow.Services.csproj");
        foreach (var id in new[] { "Whisper.net", "Whisper.net.Runtime", "Whisper.net.Runtime.Vulkan", "NAudio.Wasapi" })
            Assert.Contains("<PackageReference Include=\"" + id + "\" />", project, StringComparison.Ordinal);
        // V-3.1 "Not taken": CUDA runtimes (Q-V-9 C: a separate download), the no-AVX runtime (Q-V-12), any OpenAI SDK, ONNX Runtime.
        // Every project and props file of the repository is read (the installer is built from the app project and everything it
        // references), not only the two the pins live in: a package named anywhere would ship.
        var files = new[] { Root }.Concat(new[] { "src", "tests" }.Select(d => Path.Combine(Root, d))).SelectMany((d, i) => Directory.EnumerateFiles(d, "*.*", i == 0 ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories))
            .Where(f => f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".props", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".targets", StringComparison.OrdinalIgnoreCase))
            .Where(f => !Path.GetRelativePath(Root, f).Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin" or ".git" or "node_modules"))
            .ToList();
        Assert.Contains(files, f => Path.GetFileName(f) == "Marrow.App.csproj");
        Assert.Contains(files, f => Path.GetFileName(f) == "Directory.Packages.props");
        var packageNames = new List<(string File, string Name)>();
        foreach (var file in files)
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(file, Encoding.UTF8), "<(?:PackageReference|PackageVersion|GlobalPackageReference)\\s[^>]*?(?:Include|Update)=\"([^\"]+)\"", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
                packageNames.Add((Path.GetRelativePath(Root, file), match.Groups[1].Value));
        Assert.Contains(packageNames, n => n.Name == "Whisper.net.Runtime.Vulkan");
        foreach (var (file, name) in packageNames)
        {
            Assert.False(name.StartsWith("Whisper.net.Runtime.Cuda", StringComparison.OrdinalIgnoreCase), file + " names " + name);
            Assert.False(name.StartsWith("Whisper.net.Runtime.NoAvx", StringComparison.OrdinalIgnoreCase), file + " names " + name);
            Assert.False(name.Contains("OpenAI", StringComparison.OrdinalIgnoreCase), file + " names " + name);
            Assert.False(name.StartsWith("Microsoft.ML.OnnxRuntime", StringComparison.OrdinalIgnoreCase), file + " names " + name);
        }
        // The shipped Whisper.net runtime packages are exactly the CPU and Vulkan ones, in every project that names a runtime.
        Assert.Equal(["Whisper.net.Runtime", "Whisper.net.Runtime.Vulkan"], packageNames.Select(n => n.Name).Where(n => n.StartsWith("Whisper.net.Runtime", StringComparison.Ordinal)).Distinct().Order(StringComparer.Ordinal).ToArray());
        // The two files that hold the pins are also free of the names as plain text (a comment naming a pack would mislead the next reader).
        var props = ReadRepositoryText("Directory.Packages.props");
        foreach (var text in new[] { project, props })
        {
            Assert.DoesNotContain("Whisper.net.Runtime.Cuda", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Whisper.net.Runtime.NoAvx", text, StringComparison.Ordinal);
            Assert.DoesNotContain("OpenAI", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Microsoft.ML.OnnxRuntime", text, StringComparison.Ordinal);
        }
    }

    // The transitive packages (name and version) one restore resolved besides the direct ones and the solution's own projects.
    // A plain restore writes one target ("net10.0-windows"); a RID-specific one (dotnet publish -r win-x64, which scripts/package-windows.ps1 runs)
    // also writes "net10.0-windows/win-x64". Every target must resolve the same set (a runtime package that only the RID target pulls in is
    // closure drift and fails here with the targets named), and the set is returned.
    internal static string[] ResolvedTransitivePackages(JsonElement assets, string[] direct)
    {
        var perTarget = assets.GetProperty("targets").EnumerateObject().Select(target => (
            Target: target.Name,
            Packages: target.Value.EnumerateObject().Where(p => p.Value.GetProperty("type").GetString() == "package").Select(p => p.Name)
                .Where(n => !n.StartsWith("Marrow.", StringComparison.Ordinal))
                .Where(n => !direct.Contains(n[..n.IndexOf('/', StringComparison.Ordinal)]))
                .Select(n => n[..n.IndexOf('/', StringComparison.Ordinal)] + " " + n[(n.IndexOf('/', StringComparison.Ordinal) + 1)..]).Order(StringComparer.Ordinal).ToArray())).ToList();
        var distinct = perTarget.Select(t => string.Join(", ", t.Packages)).Distinct(StringComparer.Ordinal).Count();
        if (perTarget.Count == 0 || distinct != 1)
            throw new InvalidOperationException("The targets of the assets file disagree on the transitive packages: " + string.Join("; ", perTarget.Select(t => t.Target + " = [" + string.Join(", ", t.Packages) + "]")));
        return perTarget[0].Packages;
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T5")]
    public void The_resolved_transitive_closure_is_recorded_with_licences_and_matches_what_the_build_loads()
    {
        var closure = Pins().GetProperty("transitiveClosure").EnumerateArray()
            .Select(e => (Id: e.GetProperty("id").GetString()!, Version: e.GetProperty("version").GetString()!, Licence: e.GetProperty("licence").GetString()!, Copyright: e.GetProperty("copyright").GetString()!, Shipped: e.GetProperty("shipped").GetBoolean()))
            .OrderBy(c => c.Id, StringComparer.Ordinal).ToArray();
        // The copyright holders are the ones the projects' own licence files state (never the nuspec <copyright> field, which is
        // "Microsoft Corporation" for packages whose LICENSE says ".NET Foundation"): Microsoft.Extensions.AI.Abstractions from
        // dotnet/extensions LICENSE ("Copyright (c) .NET Foundation. All rights reserved."), System.Numerics.Tensors from the LICENSE.TXT
        // inside the package (read below), NAudio from NAudio's LICENSE, Whisper.net.Runtime.Metal from the Whisper.net repository's LICENSE.
        Assert.Equal(
            [
                ("Microsoft.Extensions.AI.Abstractions", "10.2.0", "MIT", "Copyright (c) .NET Foundation. All rights reserved.", true),
                ("NAudio.Core", "3.1.0", "MIT", "Copyright 2020 Mark Heath", true),
                ("System.Numerics.Tensors", "9.0.0", "MIT", "Copyright (c) .NET Foundation and Contributors", true),
                ("Whisper.net.Runtime.Metal", "1.9.1", "MIT", "Copyright (c) 2024 sandrohanea", false),
            ],
            closure);
        // The closure is complete: it equals the packages project.assets.json resolved for the Services project besides its direct ones: the
        // four voice packages and Microsoft.Web.WebView2 (web surfaces W7, ED-NEW-web-w7-1; it declares no dependencies, and
        // WebPackagePinsTests pins the Services project's direct packages to exactly these five, so nothing else can hide here).
        var assets = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "src", "Marrow.Services", "obj", "project.assets.json"), Encoding.UTF8)).RootElement;
        var direct = new[] { "Whisper.net", "Whisper.net.Runtime", "Whisper.net.Runtime.Vulkan", "NAudio.Wasapi", "Microsoft.Web.WebView2" };
        var resolvedTransitive = ResolvedTransitivePackages(assets, direct);
        Assert.Equal(closure.Select(c => c.Id + " " + c.Version).Order(StringComparer.Ordinal).ToArray(), resolvedTransitive);
        // The managed assemblies of the closure sit beside the tests (copied from the Services references).
        foreach (var assembly in new[] { "Whisper.net.dll", "NAudio.Wasapi.dll", "NAudio.Core.dll", "Microsoft.Extensions.AI.Abstractions.dll", "System.Numerics.Tensors.dll" })
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, assembly)), assembly);
        // The Metal package ships nothing to a Windows build (its only file is ggml-metal.metal, copied for macOS target frameworks only).
        Assert.False(Directory.EnumerateFiles(AppContext.BaseDirectory, "ggml-metal*", SearchOption.AllDirectories).Any());
        // The packages' own licence files, from the restored copies: the copyright line of the closure record is the one in the file.
        var folder = assets.GetProperty("packageFolders").EnumerateObject().First().Name;
        string Licence(string id, string version, string file) => File.ReadAllText(Path.Combine(folder, id.ToLowerInvariant(), version, file), Encoding.UTF8);
        Assert.Contains("Copyright (c) .NET Foundation and Contributors", Licence("System.Numerics.Tensors", "9.0.0", "LICENSE.TXT"), StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft Corporation", Licence("System.Numerics.Tensors", "9.0.0", "LICENSE.TXT"), StringComparison.Ordinal);
        Assert.Contains("Copyright (c) 2024 sandrohanea", Licence("Whisper.net", "1.9.1", "LICENSE"), StringComparison.Ordinal);
    }

    // ---- V-T2: Whisper.net API members the adapter uses exist (reflection; names recorded) ----

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Decision", "ED-267")]
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
    [Trait("Decision", "ED-267")]
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
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T2")]
    public void Whisper_net_1_9_1_exposes_no_per_frame_speech_probability_so_the_whisper_dll_export_fallback_is_the_route()
    {
        // V-6.1: "the per-frame probability API is UNVERIFIED". Verified here: the VAD types hand out segments only.
        foreach (var name in new[] { "WhisperVadProcessor", "WhisperVadFactory", "WhisperVadProcessorBuilder" })
            Assert.DoesNotContain(Signatures(WhisperType(name)), s => s.Contains("Prob", StringComparison.OrdinalIgnoreCase));
        Assert.True(Pins().GetProperty("whisperNet").GetProperty("perFrameProbabilityApi").GetBoolean() == false);
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T2")]
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
    [Trait("Decision", "ED-267")]
    public void The_build_output_holds_exactly_the_win_x64_CPU_and_Vulkan_runtime_files_and_no_other_platform()
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
    [Trait("Decision", "ED-267")]
    public void The_record_lists_the_same_runtime_files_and_totals_them_against_the_50_MB_budget_honestly()
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
    [Trait("Decision", "ED-267")]
    public void The_runtime_DLLs_import_the_Visual_C_runtime_DLLs_the_record_lists_and_the_record_states_who_carries_them()
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
    [Trait("Decision", "ED-267")]
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
    [Trait("Decision", "ED-267")]
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

    // ---- V-3.5: the self-test clip (Q-V-5, ED-267: a FLEURS clip, CC BY 4.0) ----

    private const string ClipSha256 = "5c3db8924bb4953997de132f84000841ca0d2a9257b439b10dbc79ef400b01df";

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Decision", "ED-267")]
    public void The_self_test_clip_is_the_pinned_ten_second_16_kHz_mono_16_bit_WAV()
    {
        var path = Path.Combine(AssetsDirectory, "selftest-clip.wav");
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(320684, bytes.Length);
        Assert.Equal(ClipSha256, Sha256(path));
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
    [Trait("Decision", "ED-267")]
    public void The_self_test_clip_is_at_the_level_of_the_BENCH_clips_the_V_10_3_thresholds_came_from()
    {
        // V-10.3 compares the self-test's real-time factor with thresholds measured by BENCH on clips level-matched to an RMS of 3000
        // (16-bit units, about -20 dBFS; BENCH scripts/prep_audio.py: gain = 3000 / rms, capped so the peak stays at or under 30000),
        // whose peaks lay between -3.2 and -0.8 dBFS. The bundled clip is that very clip (BENCH en_10s_1.wav), so the timing it
        // gives is the one the thresholds were taken from; a quiet or coarsely quantised clip would not reproduce those conditions.
        var bytes = File.ReadAllBytes(Path.Combine(AssetsDirectory, "selftest-clip.wav"));
        var samples = new short[(bytes.Length - 44) / 2];
        Buffer.BlockCopy(bytes, 44, samples, 0, samples.Length * 2);
        var rms = Math.Sqrt(samples.Sum(x => (double)x * x) / samples.Length);
        var peak = samples.Max(x => Math.Abs((int)x));
        var peakDbfs = 20 * Math.Log10(peak / 32768.0);
        Assert.InRange(rms, 2970.0, 3030.0);
        Assert.InRange(peakDbfs, -3.2, -0.8);
        // Fine quantisation: the levels come from a float recording scaled once, not from an already coarse 16-bit file.
        Assert.True(samples.Distinct().Count() > 10000, "distinct 16-bit levels: " + samples.Distinct().Count());
        var clip = Pins().GetProperty("selfTestClip");
        Assert.Equal(Math.Round(rms, 1), Math.Round(clip.GetProperty("rms").GetDouble(), 1));
        Assert.Equal(peak, clip.GetProperty("peak").GetInt32());
        Assert.Equal("3000 / rms, capped so the peak stays at or under 30000", clip.GetProperty("gainRule").GetString());
        Assert.InRange(clip.GetProperty("gain").GetDouble(), 1.06, 1.07);
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Decision", "ED-267")]
    public void The_clip_record_names_the_dataset_commit_the_licence_and_the_change_made()
    {
        var clip = Pins().GetProperty("selfTestClip");
        Assert.Equal("google/fleurs", clip.GetProperty("dataset").GetString());
        Assert.Equal("70bb2e84b976b7e960aa89f1c648e09c59f894dd", clip.GetProperty("datasetCommit").GetString());
        Assert.Equal("CC-BY-4.0", clip.GetProperty("licence").GetString());
        Assert.Equal("https://creativecommons.org/licenses/by/4.0/", clip.GetProperty("licenceUrl").GetString());
        // The archive's LFS oid at the pinned commit (the Hugging Face tree listing of data/en_us/audio) equals the archive BENCH
        // downloaded, and the clip is the BENCH timing clip en_10s_1 (sentence 1935 of the English test split).
        Assert.Equal("data/en_us/audio/test.tar.gz", clip.GetProperty("archive").GetString());
        Assert.Equal("d9c2e37b41aacd41bc283554a0a82b5476b36887049774ecb2819dcaaa55a356", clip.GetProperty("archiveSha256").GetString());
        Assert.Equal(289851356L, clip.GetProperty("archiveBytes").GetInt64());
        Assert.Equal("test/1618638950277648344.wav", clip.GetProperty("member").GetString());
        Assert.Equal("6c55023cd69a460977509d43dc714f4544dc9acad2cf1f42bc266f63b2ed9e35", clip.GetProperty("originalSha256").GetString());
        Assert.Equal(641338L, clip.GetProperty("originalBytes").GetInt64());
        Assert.Equal(ClipSha256, clip.GetProperty("sha256").GetString());
        Assert.Equal(320684L, clip.GetProperty("bytes").GetInt64());
        Assert.Equal("However, that is not true. Although there is something written on the back of the document, it is not a treasure map.", clip.GetProperty("transcription").GetString());
        // The change made (CC BY 4.0 requires it to be stated): decoded to 16 kHz mono 16-bit PCM and scaled once by the stated gain.
        var change = clip.GetProperty("change").GetString()!;
        foreach (var part in new[] { "32-bit float", "16 kHz", "mono", "16-bit", "ffmpeg", "one gain", "3000", "1.062187" })
            Assert.Contains(part, change, StringComparison.Ordinal);
        Assert.StartsWith("1.062187", clip.GetProperty("gain").GetDouble().ToString("R", CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    // ---- Q-V-9 C: the CUDA feasibility and pin record ----

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Decision", "ED-267")]
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

    // The saved console output of the loader probe (porting\probest-0-cuda-loader): every claim of the feasibility record that rests on
    // the probe is held to a line of that file, so the record is not asserted against itself.
    internal static Dictionary<int, List<string>> ProbeCases()
    {
        var cases = new Dictionary<int, List<string>>();
        List<string>? current = null;
        foreach (var line in ReadRepositoryText("porting", "probes", "vt-0-cuda-loader", "run-output.txt").Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var header = System.Text.RegularExpressions.Regex.Match(line, @"^== case ([0-9]+):");
            if (header.Success) cases[int.Parse(header.Groups[1].Value, CultureInfo.InvariantCulture)] = current = [];
            else if (current is not null && line.Length > 0) current.Add(line);
        }
        return cases;
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Decision", "ED-267")]
    public void The_CUDA_loader_probe_output_backs_each_claim_the_feasibility_record_makes_about_RuntimeOptions_LibraryPath()
    {
        var cases = ProbeCases();
        Assert.Equal(Enumerable.Range(1, 17).ToArray(), cases.Keys.Order().ToArray());
        const string Segments = "VAD segments=3 [1.35-3.10] [3.65-6.78] [6.95-8.70]";
        // Each case prints: the options line, then LoadedLibrary and the VAD segments (or the failure), the modules the process holds, and the exit code.
        string[] Outcome(int probeCase) => cases[probeCase].Skip(1).Where(l => !l.StartsWith("Modules=", StringComparison.Ordinal)).ToArray();
        // Cases 1 to 3: the path is read through its directory, so a file path inside the root works and the folder or a file two levels down does not.
        Assert.Equal(["LoadedLibrary=Cpu", Segments, "ExitCode=0"], Outcome(1));
        Assert.StartsWith("FAIL FileNotFoundException: Native Library not found", Outcome(2)[0], StringComparison.Ordinal);
        Assert.StartsWith("FAIL FileNotFoundException: Native Library not found", Outcome(3)[0], StringComparison.Ordinal);
        // Case 4: the Vulkan runtime loads from the downloaded folder too, and finds the same segments as the CPU one.
        Assert.Equal(["LoadedLibrary=Vulkan", Segments, "ExitCode=0"], Outcome(4));
        // Cases 5 to 7: a CUDA runtime that cannot be loaded is skipped when a later one is listed, and is an error when it is the only one.
        Assert.Equal(["LoadedLibrary=Vulkan", Segments, "ExitCode=0"], Outcome(5));
        Assert.Equal(["LoadedLibrary=Cpu", Segments, "ExitCode=0"], Outcome(6));
        Assert.StartsWith("FAIL Exception: Failed to load native whisper library", Outcome(7)[0], StringComparison.Ordinal);

        var finding = Pins().GetProperty("cuda").GetProperty("feasibility");
        Assert.Equal("porting/probes/vt-0-cuda-loader/run-output.txt", finding.GetProperty("probeOutput").GetString());
        Assert.Equal("directory-of-the-path", finding.GetProperty("libraryPathSemantics").GetString());
        Assert.Equal("falls-back-to-the-next-runtime-unless-cudart-is-findable-and-cublas-is-missing (probe cases 5, 6, 11, 12; case 15 aborts the process)", finding.GetProperty("whenCudaCannotLoad").GetString());
        Assert.Equal(Segments, finding.GetProperty("vadCheck").GetString());
        var loads = finding.GetProperty("loadsFromDownloadedFolder");
        Assert.Equal("reproduced (probe case 1)", loads.GetProperty("Cpu").GetString());
        Assert.Equal("reproduced (probe case 4)", loads.GetProperty("Vulkan").GetString());
        // The real CUDA 12 pack is loaded from a downloaded folder in the saved probe output (cases 9 and 14, held by VoicePinsReview3Tests); the
        // fall-back above is shown with an unloadable stand-in for the pack (cases 5 to 7) and with the real pack missing its NVIDIA libraries (11 to 13).
        var cuda12 = loads.GetProperty("Cuda12");
        Assert.Equal("reproduced (probe case 9)", cuda12.GetProperty("status").GetString());
        Assert.Contains("stand-in", finding.GetProperty("fallbackEvidence").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Decision", "ED-267")]
    public void The_CUDA_feasibility_finding_states_what_the_pack_lacks_and_pins_the_NVIDIA_companions()
    {
        var finding = Pins().GetProperty("cuda").GetProperty("feasibility");
        // The pack carries no NVIDIA runtime libraries: the pack's own imports name them (the PE import table of ggml-cuda-whisper.dll in the
        // restored pack, saved in porting/probes/vt-0-cuda-loader/cuda12-pack-imports.txt and held to the record by VoicePinsReview3Tests).
        var needs = finding.GetProperty("packDoesNotCarry").EnumerateArray().Select(e => e.GetString()!).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "cublas64_12.dll", "cublasLt64_12.dll", "cudart64_12.dll" }, needs);
        var imports = Pins().GetProperty("cuda").GetProperty("packs").GetProperty("Whisper.net.Runtime.Cuda12.Windows").GetProperty("imports").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Contains("cudart64_12.dll", imports);
        Assert.Contains("cublas64_12.dll", imports);
        Assert.Equal("read-from-the-restored-pack (porting/probes/vt-0-cuda-loader/cuda12-pack-imports.txt)", Pins().GetProperty("cuda").GetProperty("importsEvidence").GetString());
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

    // One "### " block of the section: its text up to the next heading.
    private static string NoticesBlock(string heading)
    {
        var section = VoiceNoticesSection();
        var at = section.IndexOf("\n" + heading + "\n", StringComparison.Ordinal);
        Assert.True(at >= 0, heading);
        var next = section.IndexOf("\n### ", at + heading.Length + 2, StringComparison.Ordinal);
        return next < 0 ? section[(at + 1)..] : section[(at + 1)..next];
    }

    // The text above a block's first code fence (the bullets) and the text inside it (the licence).
    private static (string Bullets, string Licence) Split(string block)
    {
        var open = block.IndexOf("\n```\n", StringComparison.Ordinal);
        if (open < 0) return (block, "");
        var close = block.IndexOf("\n```", open + 5, StringComparison.Ordinal);
        return (block[..open], block[(open + 5)..close]);
    }

    // The component blocks of the V-3.6 section: heading, source, and the copyright line that appears in the licence text.
    // V-3.6 gives one block for Microsoft.Extensions.AI.Abstractions and System.Numerics.Tensors; they are two here because their
    // licence files name different holders (see the closure record and ED-287).
    [Theory]
    [Trait("Feature", Feature)]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T4")]
    [InlineData("### OpenAI Whisper (model weights and reference code)", "https://github.com/openai/whisper", "Copyright (c) 2022 OpenAI")]
    [InlineData("### whisper.cpp and ggml (native engine inside `Whisper.net`'s runtimes; the ggml model files)", "https://github.com/ggml-org/whisper.cpp", "Copyright (c) 2023-2026 The ggml authors")]
    [InlineData("### `Whisper.net`", "https://github.com/sandrohanea/whisper.net", "Copyright (c) 2024 sandrohanea")]
    [InlineData("### NAudio", "https://github.com/naudio/NAudio", "Copyright 2020 Mark Heath")]
    [InlineData("### Silero VAD", "https://github.com/snakers4/silero-vad", "Copyright (c) 2020-present Silero Team")]
    [InlineData("### Liquid Orb Editor (LerSent001): the voice orb", "https://github.com/LerSent001/orb", "Copyright (c) 2026 LerSent001")]
    [InlineData("### voice-glow 0.2.1 (Jakub Antalik): the composer's voice glow", "https://www.npmjs.com/package/voice-glow", "Copyright (c) 2026 Jakub Antalik")]
    [InlineData("### Microsoft.Extensions.AI.Abstractions (dependency of `Whisper.net`)", "https://dot.net/", "Copyright (c) .NET Foundation. All rights reserved.")]
    [InlineData("### System.Numerics.Tensors (dependency of Microsoft.Extensions.AI.Abstractions)", "https://dot.net/", "Copyright (c) .NET Foundation and Contributors")]
    public void The_Voice_typing_section_credits_each_component_with_its_source_licence_and_copyright(string heading, string source, string copyright)
    {
        var (bullets, licence) = Split(NoticesBlock(heading));
        Assert.Contains(source, bullets, StringComparison.Ordinal);
        // The component's own bullet states the licence and the holder (not merely some line somewhere in the block) ...
        Assert.Contains("Licence: MIT, \"" + copyright + "\"", bullets, StringComparison.Ordinal);
        // ... and the licence text carried under it is the MIT text with the same copyright line.
        Assert.Contains("\n" + copyright + "\n", "\n" + licence + "\n", StringComparison.Ordinal);
        Assert.Contains("Permission is hereby granted, free of charge, to any person obtaining a copy", licence, StringComparison.Ordinal);
        Assert.Contains("THE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND", licence, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T4")]
    public void The_MIT_texts_of_the_two_dotnet_packages_are_their_own_licence_files()
    {
        // System.Numerics.Tensors: LICENSE.TXT inside the package (restored copy). Microsoft.Extensions.AI.Abstractions: the LICENSE of
        // dotnet/extensions (its package carries no licence file; the nuspec only names the MIT expression): the same text with the
        // holder line "Copyright (c) .NET Foundation. All rights reserved.".
        var assets = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "src", "Marrow.Services", "obj", "project.assets.json"), Encoding.UTF8)).RootElement;
        var folder = assets.GetProperty("packageFolders").EnumerateObject().First().Name;
        var tensors = File.ReadAllText(Path.Combine(folder, "system.numerics.tensors", "9.0.0", "LICENSE.TXT"), Encoding.UTF8).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
        var (_, tensorsText) = Split(NoticesBlock("### System.Numerics.Tensors (dependency of Microsoft.Extensions.AI.Abstractions)"));
        Assert.Equal(tensors, tensorsText.TrimEnd());
        var extensions = "The MIT License (MIT)\n\nCopyright (c) .NET Foundation. All rights reserved.\n\nAll rights reserved.\n\n" + tensors[tensors.IndexOf("Permission is hereby granted", StringComparison.Ordinal)..];
        var (_, extensionsText) = Split(NoticesBlock("### Microsoft.Extensions.AI.Abstractions (dependency of `Whisper.net`)"));
        Assert.Equal(extensions, extensionsText.TrimEnd());
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T4")]
    public void The_FLEURS_block_carries_the_creator_attribution_the_licence_requires_and_states_the_change()
    {
        var block = NoticesBlock("### FLEURS (the self-test clip)");
        Assert.Contains("https://huggingface.co/datasets/google/fleurs", block, StringComparison.Ordinal);
        Assert.Contains("Licence: CC BY 4.0,", block, StringComparison.Ordinal);
        Assert.Contains("https://creativecommons.org/licenses/by/4.0/", block, StringComparison.Ordinal);
        // CC BY 4.0 section 3(a)(1): the creators, the title, the licence and the change notice.
        foreach (var author in new[] { "Alexis Conneau", "Min Ma", "Simran Khanuja", "Yu Zhang", "Vera Axelrod", "Siddharth Dalmia", "Jason Riesa", "Clara Rivera", "Ankur Bapna" })
            Assert.Contains(author, block, StringComparison.Ordinal);
        Assert.Contains("\"FLEURS: Few-shot Learning Evaluation of Universal Representations of Speech\"", block, StringComparison.Ordinal);
        Assert.Contains("https://arxiv.org/abs/2205.12446", block, StringComparison.Ordinal);
        Assert.Contains("test/1618638950277648344.wav of data/en_us/audio/test.tar.gz", block, StringComparison.Ordinal);
        // The change made is the one the record states: a gain of 1.062187 (3000 / rms) after the conversion to 16 kHz mono 16-bit.
        Assert.Contains("Changes made:", block, StringComparison.Ordinal);
        foreach (var part in new[] { "32-bit float", "16 kHz", "mono", "16-bit", "one gain of 1.062187" })
            Assert.Contains(part, block, StringComparison.Ordinal);
    }

    // V-3.6's opening paragraph, as the spec words it. Two replacements are made on purpose and named in ED-287: "ED-nnn" is the
    // spec's placeholder for the engineering decision that records the section, filled with ED-267 (the voice decision that settles
    // the clip credited in it), and the name Whisper.net is written as inline code so MarkdownView does not turn it into a link (ED 10).
    private const string SpecParagraph =
        "Voice typing (owner decisions OD-0091, OD-0092; ED-nnn) runs OpenAI's Whisper on the user's computer through whisper.cpp\n" +
        "and Whisper.net, captures the microphone with NAudio, detects speech with Silero VAD, and draws the voice orb and the\n" +
        "composer's voice glow from the two works credited last. Model files are downloaded on request from Hugging Face\n" +
        "(ggerganov/whisper.cpp, commit 5359861c739e955e79d9a303bcbc70fb988958b1) and are not part of the installer.";

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T4")]
    public void The_Voice_typing_section_opens_with_V_3_6s_paragraph_word_for_word()
    {
        var section = VoiceNoticesSection();
        const string Opening = "\n## Voice typing\n\n";
        Assert.StartsWith(Opening, section, StringComparison.Ordinal);
        var firstHeading = section.IndexOf("\n\n### ", StringComparison.Ordinal);
        var paragraph = section[Opening.Length..firstHeading];
        var expected = SpecParagraph.Replace("ED-nnn", "ED-267", StringComparison.Ordinal).Replace("Whisper.net", "`Whisper.net`", StringComparison.Ordinal);
        Assert.Equal(expected, paragraph);
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T4")]
    public void The_Voice_typing_section_has_the_V_3_6_headings_in_the_order_the_paragraph_implies()
    {
        // V-3.6's list, with the orb and the glow last as its opening paragraph says ("the two works credited last"), the two .NET
        // packages in one block each, FLEURS (Q-V-5) before them and the Apache text after (V-3.6's closing bracket).
        string[] expected =
        [
            "### OpenAI Whisper (model weights and reference code)",
            "### whisper.cpp and ggml (native engine inside `Whisper.net`'s runtimes; the ggml model files)",
            "### `Whisper.net`",
            "### NAudio",
            "### Silero VAD",
            "### Microsoft.Extensions.AI.Abstractions (dependency of `Whisper.net`)",
            "### System.Numerics.Tensors (dependency of Microsoft.Extensions.AI.Abstractions)",
            "### FLEURS (the self-test clip)",
            "### Liquid Orb Editor (LerSent001): the voice orb",
            "### voice-glow 0.2.1 (Jakub Antalik): the composer's voice glow",
            "### Apache License 2.0 (OpenAI Whisper large-v3 model files)",
        ];
        var headings = new List<string>();
        var inFence = false;
        foreach (var line in VoiceNoticesSection().Split('\n'))
        {
            if (line.StartsWith("```", StringComparison.Ordinal)) inFence = !inFence;
            else if (!inFence && line.StartsWith("### ", StringComparison.Ordinal)) headings.Add(line);
        }
        Assert.Equal(expected, headings);
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T4")]
    public void The_Voice_typing_section_carries_the_model_commit_and_both_licence_texts()
    {
        var section = VoiceNoticesSection();
        // The MIT permission notice appears once per credited work that carries its text (nine works) and the Apache text once.
        Assert.Equal(9, CountOf(section, "Permission is hereby granted, free of charge, to any person obtaining a copy"));
        Assert.Equal(1, CountOf(section, "TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND DISTRIBUTION"));
        Assert.Contains("```\nApache License\nVersion 2.0, January 2004\nhttp://www.apache.org/licenses/\n\n   TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND DISTRIBUTION\n", section, StringComparison.Ordinal);
        Assert.Contains("END OF TERMS AND CONDITIONS", section, StringComparison.Ordinal);
        Assert.Contains("the Apache License 2.0 text is included below for the large-v3 files", section, StringComparison.Ordinal);
        Assert.Contains("while the repository states MIT", section, StringComparison.Ordinal);
        // Border-beam and the Uiverse button are credited above (ED-251): the Voice typing section does not repeat them.
        Assert.DoesNotContain("border-beam", section, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T4")]
    public void The_Voice_typing_section_never_leaves_the_name_Whisper_net_bare_for_the_markdown_autolinker()
    {
        // The notices are shown by MarkdownView, which links a bare domain whose last label is a known top-level domain
        // (MarkdownView.swift:772-826, "net" is one): a bare Whisper.net would open https://Whisper.net, an unrelated site.
        // Inline code is never linked, so the name is written as code; dotted package names (Whisper.net.Runtime) and URLs are not domains here.
        var bare = System.Text.RegularExpressions.Regex.Matches(VoiceNoticesSection(), @"(?<![A-Za-z0-9./`])Whisper\.net(?![A-Za-z0-9`]|\.[A-Za-z])", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        Assert.Empty(bare);
    }

    // ---- Review round 2: the VC++ finding is backed by a saved publish listing; the pins record does not ship; the notices carry no more than their sources ----

    // porting/probes/vt-0-publish/publish-listing.txt: the file list of `dotnet publish src/Marrow.App -c Release -r win-x64 --self-contained`
    // taken once (header lines start with '#', then one "path<TAB>bytes<TAB>sha256" line per file, paths relative to the publish folder).
    private const string PublishListingPath = "porting/probes/vt-0-publish/publish-listing.txt";

    private static List<(string Path, long Bytes, string Sha256)> PublishListing()
    {
        var text = ReadRepositoryText("porting", "probes", "vt-0-publish", "publish-listing.txt");
        return text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0 && l[0] != '#')
            .Select(l => l.Split('\t')).Select(c => (c[0], long.Parse(c[1], CultureInfo.InvariantCulture), c[2])).ToList();
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Decision", "ED-267")]
    public void The_saved_publish_listing_holds_exactly_the_nine_runtime_files_the_record_pins_and_the_two_bundled_assets()
    {
        var header = ReadRepositoryText("porting", "probes", "vt-0-publish", "publish-listing.txt");
        Assert.Contains("# command: dotnet publish src/Marrow.App -c Release -r win-x64 --self-contained", header, StringComparison.Ordinal);
        var listing = PublishListing();
        Assert.True(listing.Count > 100, "a self-contained publish lists its whole folder: " + listing.Count);
        var runtimes = listing.Where(f => f.Path.StartsWith("runtimes/", StringComparison.Ordinal)).OrderBy(f => f.Path, StringComparer.Ordinal).ToArray();
        var expected = PinnedNatives.Select(n => (Path: n.Directory + "/" + n.Name, n.Bytes, n.Sha256)).OrderBy(f => f.Path, StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, runtimes);
        var assets = listing.Where(f => f.Path.StartsWith("Port/Voice/Assets/", StringComparison.Ordinal)).OrderBy(f => f.Path, StringComparer.Ordinal).ToArray();
        Assert.Equal(
            new[] { "Port/Voice/Assets/ggml-silero-v6.2.0.bin", "Port/Voice/Assets/selftest-clip.wav" },
            assets.Select(f => f.Path).ToArray());
        foreach (var asset in assets) Assert.Equal(Sha256(Path.Combine(AssetsDirectory, Path.GetFileName(asset.Path))), asset.Sha256);
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Decision", "ED-267")]
    public void The_record_says_the_publish_carries_the_Visual_C_runtime_only_if_the_saved_publish_listing_does()
    {
        var recorded = Pins().GetProperty("vcRuntime");
        Assert.Equal(PublishListingPath, recorded.GetProperty("evidence").GetString());
        var carriedInListing = PublishListing()
            .Select(f => System.IO.Path.GetFileName(f.Path))
            .Any(n => System.Text.RegularExpressions.Regex.IsMatch(n, @"^(msvcp|vcruntime|vcomp|concrt|vccorlib|vcamp)[0-9_]*\.dll$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant));
        Assert.Equal(carriedInListing, recorded.GetProperty("carriedByPublish").GetBoolean());
        // The four DLLs the whisper libraries import (the previous test) are the ones the listing is searched for by name as well.
        foreach (var dll in new[] { "msvcp140.dll", "vcruntime140.dll", "vcruntime140_1.dll", "vcomp140.dll" })
            Assert.DoesNotContain(PublishListing(), f => string.Equals(System.IO.Path.GetFileName(f.Path), dll, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Decision", "ED-267")]
    public void The_recorded_pins_file_is_a_developer_record_and_is_not_copied_to_the_app_or_the_publish_folder()
    {
        // V-3.4 and V-3.5 bundle the VAD model and the clip only. voice-pins.json holds the owner PC's paths and hardware and review notes.
        var project = System.Xml.Linq.XDocument.Load(Path.Combine(Root, "src", "Marrow.Services", "Marrow.Services.csproj"));
        var copied = project.Descendants("None")
            .Where(e => e.Attribute("CopyToOutputDirectory") is { Value: not "Never" })
            .Select(e => (e.Attribute("Update") ?? e.Attribute("Include"))?.Value.Replace('\\', '/') ?? "")
            .Where(v => v.StartsWith("Port/Voice/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "Port/Voice/Assets/ggml-silero-v6.2.0.bin", "Port/Voice/Assets/selftest-clip.wav" }, copied);
        Assert.DoesNotContain(PublishListing(), f => f.Path.EndsWith("voice-pins.json", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(AssetsDirectory, "voice-pins.json")), "the pins file stays in the repository for later units");
    }

    private static string Flat(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ", System.Text.RegularExpressions.RegexOptions.CultureInvariant).Trim();

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T4")]
    public void The_orb_and_glow_scope_statements_say_what_the_design_NOTICES_and_V_3_6_say_and_no_more()
    {
        // Sources: E:\Droppy\work\voice\design\NOTICES.md (the orb and voice-glow sections) and SPEC.md V-3.6 ("translated to SkSL").
        var orb = Flat(Split(NoticesBlock("### Liquid Orb Editor (LerSent001): the voice orb")).Bullets);
        Assert.Contains("and the shader (effect.wgsl and shader-source.ts) translated to SkSL. The Toolcraft UI code of the same project (MIT, Pixel Point) is not used.", orb, StringComparison.Ordinal);
        // No SkSL code exists yet, so the notice names no SkSL function or its naming.
        Assert.DoesNotContain("function by function", orb, StringComparison.Ordinal);
        Assert.DoesNotContain("same names", orb, StringComparison.Ordinal);
        Assert.DoesNotContain("glsSiriBand", orb, StringComparison.Ordinal);
        Assert.Contains("Marrow's own (not covered by this licence): the button framing through the source's radius and edge softness, the state layer's speed, the glow amount (through the source's Outer Glow), the lift shadow and the error desaturation, and the 2x render below 128 device px.", orb, StringComparison.Ordinal);
        Assert.DoesNotContain("framing of the ball", orb, StringComparison.Ordinal);

        var glow = Flat(Split(NoticesBlock("### voice-glow 0.2.1 (Jakub Antalik): the composer's voice glow")).Bullets);
        Assert.Contains("Marrow's own: mountVoiceGlow, the stand-in for the package's React component.", glow, StringComparison.Ordinal);
        Assert.DoesNotContain("drawing of the layers", glow, StringComparison.Ordinal);

        var whisper = Flat(Split(NoticesBlock("### `Whisper.net`")).Bullets);
        Assert.Contains("(NuGet `Whisper.net`, Whisper.net.Runtime, Whisper.net.Runtime.Vulkan 1.9.1)", whisper, StringComparison.Ordinal);
    }

    private static int CountOf(string text, string needle)
    {
        var count = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal)) count++;
        return count;
    }
}
