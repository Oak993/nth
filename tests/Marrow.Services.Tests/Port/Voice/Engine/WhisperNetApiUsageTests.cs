using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Marrow.Services.Port.Voice.Engine;

namespace Marrow.Services.Tests.Port.Voice.Engine;

// VT-6, V-T2: every Whisper.net member the adapter uses exists in 1.9.1 and is one VT-0 recorded, and every whisper.cpp export the P/Invoke fallback calls is one VT-0
// found in the shipped whisper.dll.
public sealed class WhisperNetApiUsageTests
{
    private static string PinsJson([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "..", "..", "src", "Marrow.Services", "Port", "Voice", "Assets", "voice-pins.json"));

    private static JsonElement WhisperNetPin()
    {
        using var json = JsonDocument.Parse(File.ReadAllBytes(PinsJson()));
        return json.RootElement.GetProperty("whisperNet").Clone();
    }

    private static Assembly WhisperNet() => typeof(Whisper.net.WhisperFactory).Assembly;

    private static Type WhisperType(string name) => WhisperNet().GetType("Whisper.net." + name, throwOnError: true)!;

    private static List<string> Signatures(Type type) => type
        .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
        .Select(m => m is MethodBase method ? m.Name + "(" + string.Join(",", method.GetParameters().Select(p => p.ParameterType.Name)) + ")" : m.Name)
        .ToList();

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T2")]
    public void Every_member_the_backend_uses_is_one_VT_0_recorded_and_exists_in_the_pinned_assembly()
    {
        var recorded = WhisperNetPin().GetProperty("apiMembers").EnumerateArray().Select(e => e.GetString()!).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(WhisperNetBackend.UsedMembers);
        foreach (var entry in WhisperNetBackend.UsedMembers)
        {
            Assert.Contains(entry, recorded);
            var split = entry.LastIndexOf('.');
            Assert.Contains(entry[(split + 1)..], Signatures(WhisperType(entry[..split])));
        }
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T2")]
    public void The_used_members_cover_what_V_7_1_asks_for_a_pass()
    {
        string[] needed =
        [
            "WhisperFactory.FromPath(String,WhisperFactoryOptions)", "WhisperFactoryOptions.UseGpu", "WhisperFactory.CreateBuilder()", "WhisperProcessorBuilder.WithLanguage(String)",
            "WhisperProcessorBuilder.WithLanguageDetection()", "WhisperProcessorBuilder.WithThreads(Int32)", "WhisperProcessorBuilder.WithGreedySamplingStrategy(Action`1)",
            "WhisperProcessorBuilder.WithBeamSearchSamplingStrategy(Action`1)", "GreedySamplingStrategyBuilder.WithBestOf(Int32)", "BeamSearchSamplingStrategyBuilder.WithBeamSize(Int32)",
            "WhisperProcessorBuilder.WithPrompt(String)", "WhisperProcessorBuilder.WithTokenTimestamps()", "WhisperProcessorBuilder.WithTemperature(Single)",
            "WhisperProcessor.ProcessAsync(ReadOnlyMemory`1,CancellationToken)", "LibraryLoader.RuntimeOptions.LoadedLibrary", "LibraryLoader.RuntimeOptions.RuntimeLibraryOrder",
            "LibraryLoader.RuntimeOptions.LibraryPath",
        ];
        foreach (var member in needed) Assert.Contains(member, WhisperNetBackend.UsedMembers);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T2")]
    public void The_exports_the_P_Invoke_fallback_calls_are_the_five_VT_0_found_in_whisper_dll()
    {
        var recorded = WhisperNetPin().GetProperty("nativeVadExports").EnumerateArray().Select(e => e.GetString()!).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(recorded, WhisperVadImports.Exports.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T2")]
    public void Each_export_is_declared_as_an_import_with_that_entry_point()
    {
        var imports = typeof(WhisperVadImports).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => (m.Attributes & MethodAttributes.PinvokeImpl) != 0)
            .Select(m => m.GetCustomAttribute<DllImportAttribute>()!.EntryPoint!).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(WhisperVadImports.Exports.Order(StringComparer.Ordinal).ToArray(), imports);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T2")]
    public void The_runtime_names_the_adapter_maps_exist_in_the_loader_enum()
    {
        var names = Enum.GetNames(WhisperType("LibraryLoader.RuntimeLibrary"));
        foreach (var name in new[] { "Cpu", "Vulkan", "Cuda12" }) Assert.Contains(name, names);
    }
}
