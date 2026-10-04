using System.Text.Json;
using System.Runtime.CompilerServices;
using Marrow.Services.Port.Voice.Engine;
using Marrow.Services.Port.Voice.Models;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Voice.Engine;

// VT-6, V-3.2, Q-V-9 C, VT-0 halfInstalledPack: which runtimes are really on disk. The CUDA runtime counts only when the pack's five files AND the three NVIDIA
// libraries are all in <data root>\runtimes\cuda12\win-x64, because a half-installed CUDA folder aborts the whole process (probe case 15).
public sealed class FileRuntimeInventoryTests : IDisposable
{
    private readonly string root;
    private readonly string appBase;
    private readonly string dataRoot;

    public FileRuntimeInventoryTests()
    {
        var data = Environment.GetEnvironmentVariable(TestDataDirectory.VariableName) ?? throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR (R-TEST-9).");
        root = Path.Combine(OperatingSystem.IsWindows() ? TestDataDirectory.Read()! : data, "VoiceEngineTests", Guid.NewGuid().ToString("N"));
        appBase = Path.Combine(root, "app");
        dataRoot = Path.Combine(root, "data");
        Directory.CreateDirectory(appBase);
        Directory.CreateDirectory(dataRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(root, true); }
        catch (IOException) { /* the data folder is swept as a whole by the test host */ }
    }

    private static string PinsJson([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "..", "..", "src", "Marrow.Services", "Port", "Voice", "Assets", "voice-pins.json"));

    private void Touch(string folder, params string[] names)
    {
        Directory.CreateDirectory(folder);
        foreach (var name in names) File.WriteAllBytes(Path.Combine(folder, name), [1]);
    }

    private string CudaFolder => Path.Combine(dataRoot, "runtimes", "cuda12", "win-x64");

    private void ShipApp()
    {
        Touch(Path.Combine(appBase, "runtimes", "win-x64"), "whisper.dll");
        Touch(Path.Combine(appBase, "runtimes", "vulkan", "win-x64"), "whisper.dll");
    }

    private void FullCuda() => Touch(CudaFolder, [.. CudaRuntimePack.Cuda12.Files.Select(f => f.Name), .. FileRuntimeInventory.CudaCompanions]);

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void The_shipped_CPU_and_Vulkan_runtimes_are_found_in_the_app_folder_and_the_data_root_is_the_library_root()
    {
        ShipApp();
        var facts = new FileRuntimeInventory(appBase, dataRoot, () => false).Current();
        Assert.Equivalent(new[] { RuntimeKind.Cpu, RuntimeKind.Vulkan }, facts.Installed);
        Assert.False(facts.NvidiaPresent);
        Assert.Equal(dataRoot, facts.LibraryRoot);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void A_complete_CUDA_folder_adds_Cuda12_and_the_NVIDIA_flag_comes_from_the_caller()
    {
        ShipApp();
        FullCuda();
        var facts = new FileRuntimeInventory(appBase, dataRoot, () => true).Current();
        Assert.Contains(RuntimeKind.Cuda12, facts.Installed);
        Assert.True(facts.NvidiaPresent);
    }

    [Theory]
    [InlineData("cublasLt64_12.dll")]
    [InlineData("cudart64_12.dll")]
    [InlineData("cublas64_12.dll")]
    [InlineData("ggml-cuda-whisper.dll")]
    [InlineData("whisper.dll")]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void A_half_installed_CUDA_folder_is_not_offered_because_it_would_abort_the_process(string missing)
    {
        ShipApp();
        FullCuda();
        File.Delete(Path.Combine(CudaFolder, missing));
        Assert.DoesNotContain(RuntimeKind.Cuda12, new FileRuntimeInventory(appBase, dataRoot, () => true).Current().Installed);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void A_missing_app_runtime_is_not_listed()
    {
        Touch(Path.Combine(appBase, "runtimes", "win-x64"), "whisper.dll");
        Assert.Equal([RuntimeKind.Cpu], new FileRuntimeInventory(appBase, dataRoot, () => false).Current().Installed);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void The_three_NVIDIA_libraries_are_the_ones_VT_0_recorded_the_pack_does_not_carry()
    {
        using var json = JsonDocument.Parse(File.ReadAllBytes(PinsJson()));
        var recorded = json.RootElement.GetProperty("cuda").GetProperty("feasibility").GetProperty("packDoesNotCarry").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(recorded.Order(StringComparer.Ordinal), FileRuntimeInventory.CudaCompanions.Order(StringComparer.Ordinal));
    }
}
