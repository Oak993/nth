using System.Text.Json;

namespace Marrow.Services.Tests.Port.Voice;

// Review round 3 of VT-0.
//
// 1. Q-V-9 C asks whether Whisper.net can load the CUDA runtime from a downloaded folder. Rounds 1 and 2 left it "observed, not reproduced"
//    because the NVIDIA libraries were said to be gone from this PC and the pack too big to fetch. Both reasons were false: the NVIDIA 12.4 copies
//    sit in the benchmark folder and the Whisper.net.Runtime.Cuda12.Windows 1.9.1 pack restores from nuget.org with dotnet. The probe now runs the
//    REAL pack (cases 8 to 17, saved in porting/probes/vt-0-cuda-loader/run-output.txt) and these tests hold the record to that output.
// 2. The closure check read the Services assets file with .Single() on its targets, which a RID-specific restore (dotnet publish -r win-x64, the
//    command the package script runs) turns into two, so the test failed for a reason that had nothing to do with the closure.
public sealed class VoicePinsReview3Tests
{
    private const string Feature = "VT-0";
    private const string Segments = "VAD segments=3 [1.35-3.10] [3.65-6.78] [6.95-8.70]";

    private static string Line(int probeCase, string prefix) =>
        VoicePinsTests.ProbeCases()[probeCase].Single(l => l.StartsWith(prefix, StringComparison.Ordinal));

    private static string[] Modules(int probeCase) =>
        Line(probeCase, "Modules=")["Modules=".Length..].Split(" | ");

    // ---- finding 1: the real CUDA 12 pack, from a downloaded folder ----

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Decision", "ED-267")]
    public void REVIEW3_the_saved_probe_output_has_the_real_CUDA12_pack_load_from_a_downloaded_folder()
    {
        var cases = VoicePinsTests.ProbeCases();
        Assert.Equal(Enumerable.Range(1, 17).ToArray(), cases.Keys.Order().ToArray());
        // Case 9: the real pack with the three NVIDIA 12.4 libraries beside it, Cuda12 the only runtime listed: the CUDA runtime is the one loaded,
        // the pack's files and the NVIDIA libraries are the modules the process holds, and the VAD finds the same segments as on the CPU.
        Assert.Equal("LoadedLibrary=Cuda12", Line(9, "LoadedLibrary="));
        Assert.Equal(Segments, Line(9, "VAD segments="));
        Assert.Equal("ExitCode=0", Line(9, "ExitCode="));
        var held = Modules(9);
        foreach (var name in new[] { "ggml-cuda-whisper.dll", "whisper.dll", "cudart64_12.dll", "cublas64_12.dll", "cublasLt64_12.dll" })
            Assert.Contains(held, m => m.EndsWith("<rootnv>\\runtimes\\cuda12\\win-x64\\" + name, StringComparison.Ordinal));
        Assert.Contains(held, m => m.EndsWith("\\nvcuda.dll", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(held, m => m.Contains("\\vulkan\\", StringComparison.Ordinal));
        // Case 14: the same natives found through a folder on PATH (the pack's folder holds no NVIDIA library): Cuda12 is chosen from the ordered list.
        Assert.Equal("LoadedLibrary=Cuda12", Line(14, "LoadedLibrary="));
        Assert.Equal(Segments, Line(14, "VAD segments="));
        Assert.Equal("ExitCode=0", Line(14, "ExitCode="));
        Assert.Contains(Modules(14), m => m.EndsWith("<rootnn>\\runtimes\\cuda12\\win-x64\\ggml-cuda-whisper.dll", StringComparison.Ordinal));
        Assert.Contains(Modules(14), m => m.EndsWith("<work>\\nvpath\\cublas64_12.dll", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Decision", "ED-267")]
    public void REVIEW3_the_saved_probe_output_shows_when_the_CUDA12_runtime_is_skipped_and_what_a_half_installed_pack_does()
    {
        // Cases 8, 10 and 11: with later runtimes in the order, Cuda12 is skipped whenever Whisper.net's own CUDA check cannot find cudart by its bare
        // name, even though the pack and the NVIDIA libraries sit in runtimes\cuda12\win-x64 (case 8) and whatever the GPU option says (case 10).
        foreach (var skipped in new[] { 8, 10, 11 })
        {
            Assert.Equal("LoadedLibrary=Vulkan", Line(skipped, "LoadedLibrary="));
            Assert.Equal(Segments, Line(skipped, "VAD segments="));
            Assert.DoesNotContain(Modules(skipped), m => m.Contains("\\cuda12\\", StringComparison.Ordinal));
        }
        Assert.Equal("LoadedLibrary=Cpu", Line(12, "LoadedLibrary="));
        // Case 13: a pack with no NVIDIA libraries, Cuda12 the only runtime: an error, not a silent CPU run.
        Assert.StartsWith("FAIL Exception: Failed to load native whisper library", Line(13, "FAIL "), StringComparison.Ordinal);
        // Cases 16 and 17: Whisper.net's own debug log says why (saved by the probe run, filtered to the CUDA lines).
        Assert.Contains(VoicePinsTests.ProbeCases()[16], l => l.EndsWith("CUDA runtime Cuda12 is not available.", StringComparison.Ordinal));
        Assert.Contains(VoicePinsTests.ProbeCases()[17], l => l.EndsWith("Number of CUDA devices found for runtime Cuda12: 1", StringComparison.Ordinal));
        // Case 15: cudart is findable (so the CUDA check passes) but cuBLAS is missing: loading the CUDA runtime fails half way and the process
        // aborts when the next runtime in the order is loaded. The probe never reaches LoadedLibrary or the modules line.
        var abort = VoicePinsTests.ProbeCases()[15];
        Assert.Contains(abort, l => l.Contains("GGML_ASSERT(prev != ggml_uncaught_exception) failed", StringComparison.Ordinal));
        Assert.DoesNotContain(abort, l => l.StartsWith("LoadedLibrary=", StringComparison.Ordinal) || l.StartsWith("Modules=", StringComparison.Ordinal));
        Assert.NotEqual("ExitCode=0", Line(15, "ExitCode="));
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Decision", "ED-267")]
    public void REVIEW3_the_record_answers_Q_V_9_C_from_the_saved_output_and_no_longer_blames_missing_copies()
    {
        var finding = VoicePinsTests.Pins().GetProperty("cuda").GetProperty("feasibility");
        var cuda12 = finding.GetProperty("loadsFromDownloadedFolder").GetProperty("Cuda12");
        Assert.Equal("reproduced (probe case 9)", cuda12.GetProperty("status").GetString());
        Assert.Equal("reproduced (probe case 14)", cuda12.GetProperty("fromOrderedListWithNvidiaOnPath").GetString());
        var note = cuda12.GetProperty("note").GetString()!;
        Assert.DoesNotContain("not saved", note, StringComparison.Ordinal);
        Assert.DoesNotContain("no longer on this PC", note, StringComparison.Ordinal);
        Assert.DoesNotContain("not reproduced", note, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("observed-not-reproduced", finding.GetRawText(), StringComparison.Ordinal);
        Assert.Equal("skipped-unless-cudart-is-found-by-its-bare-name (probe cases 8, 10, 11, 16)", finding.GetProperty("cudaRuntimeSkippedWhen").GetString());
        var hazard = finding.GetProperty("halfInstalledPack");
        Assert.Equal("the-process-aborts (probe case 15)", hazard.GetProperty("result").GetString());
        Assert.Contains("cublasLt64_12.dll", hazard.GetProperty("mitigation").GetString(), StringComparison.Ordinal);
        // The pack's own imports are now read from the restored pack and saved, so the record's "pack not kept" reading is replaced by an artifact.
        Assert.Equal("read-from-the-restored-pack (porting/probes/vt-0-cuda-loader/cuda12-pack-imports.txt)", VoicePinsTests.Pins().GetProperty("cuda").GetProperty("importsEvidence").GetString());
        Assert.DoesNotContain("pack was not kept", finding.GetProperty("packDoesNotCarryNote").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Decision", "ED-267")]
    public void REVIEW3_the_saved_pack_import_listing_matches_the_pins_and_the_NVIDIA_libraries_the_pack_needs()
    {
        var lines = File.ReadAllText(Path.Combine(RepositoryFile("porting", "probes", "vt-0-cuda-loader"), "cuda12-pack-imports.txt")).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var pack = VoicePinsTests.Pins().GetProperty("cuda").GetProperty("packs").GetProperty("Whisper.net.Runtime.Cuda12.Windows");
        foreach (var file in pack.GetProperty("files").EnumerateArray())
            Assert.Contains(lines, l => l.StartsWith(file.GetProperty("bytes").GetInt64() + " ", StringComparison.Ordinal) && l.EndsWith(" " + file.GetProperty("name").GetString(), StringComparison.Ordinal));
        string[] Section(string header)
        {
            var from = Array.FindIndex(lines, l => l.StartsWith("== " + header, StringComparison.Ordinal)) + 1;
            Assert.True(from > 0, header);
            return lines.Skip(from).TakeWhile(l => l.StartsWith("   import ", StringComparison.Ordinal) || l.StartsWith("   delay ", StringComparison.Ordinal)).Select(l => l.Trim().Split(' ')[1]).ToArray();
        }
        var cudaImports = Section("ggml-cuda-whisper.dll");
        foreach (var recorded in pack.GetProperty("imports").EnumerateArray()) Assert.Contains(recorded.GetString(), cudaImports);
        // cuBLAS imports cuBLASLt, so both belong beside the pack; the pack's own files import no NVIDIA library besides the CUDA runtime ones.
        Assert.Contains("cublasLt64_12.dll", Section("cublas64_12.dll"));
        foreach (var other in new[] { "whisper.dll", "ggml-whisper.dll", "ggml-base-whisper.dll", "ggml-cpu-whisper.dll" })
            Assert.DoesNotContain(Section(other), n => n.StartsWith("cu", StringComparison.OrdinalIgnoreCase) || n.StartsWith("nv", StringComparison.OrdinalIgnoreCase));
    }

    private static string RepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props"))) directory = directory.Parent;
        return Path.Combine(new[] { directory?.FullName ?? throw new InvalidOperationException("Directory.Packages.props was not found above the test output") }.Concat(parts).ToArray());
    }

    // ---- finding 2: the closure check across the targets of an assets file ----

    private static JsonElement AssetsWith(params (string Target, string[] Packages)[] targets)
    {
        string Target((string Target, string[] Packages) t) =>
            "\"" + t.Target + "\":{" + string.Join(",", t.Packages.Select(p => "\"" + p + "\":{\"type\":\"" + (p.StartsWith("Marrow.", StringComparison.Ordinal) ? "project" : "package") + "\"}")) + "}";
        return JsonDocument.Parse("{\"targets\":{" + string.Join(",", targets.Select(Target)) + "}}").RootElement;
    }

    private static readonly string[] Direct = ["Whisper.net", "NAudio.Wasapi"];

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T5")]
    public void REVIEW3_the_closure_check_reads_a_RID_specific_restore_with_two_targets()
    {
        // dotnet publish -r win-x64 writes the plain target and "<tfm>/win-x64" into the assets file of every project it restores.
        string[] packages = ["Whisper.net/1.9.1", "NAudio.Wasapi/3.1.0", "NAudio.Core/3.1.0", "System.Numerics.Tensors/9.0.0", "Marrow.Core/1.0.0"];
        var assets = AssetsWith(("net10.0-windows", packages), ("net10.0-windows/win-x64", packages));
        Assert.Equal(["NAudio.Core 3.1.0", "System.Numerics.Tensors 9.0.0"], VoicePinsTests.ResolvedTransitivePackages(assets, Direct));
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T5")]
    public void REVIEW3_the_closure_check_still_fails_when_a_target_resolves_a_package_the_others_do_not()
    {
        string[] packages = ["Whisper.net/1.9.1", "NAudio.Core/3.1.0"];
        var assets = AssetsWith(("net10.0-windows", packages), ("net10.0-windows/win-x64", [.. packages, "runtime.win-x64.Something/1.0.0"]));
        var failure = Assert.Throws<InvalidOperationException>(() => VoicePinsTests.ResolvedTransitivePackages(assets, Direct));
        Assert.Contains("disagree", failure.Message, StringComparison.Ordinal);
        Assert.Contains("net10.0-windows/win-x64", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Feature", Feature)]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-T5")]
    public void REVIEW3_the_closure_check_reads_the_plain_assets_file_of_the_Services_project()
    {
        // The real file of this checkout, whichever way it was last restored (plain build, or a restore with a runtime identifier).
        var assets = JsonDocument.Parse(File.ReadAllText(RepositoryFile("src", "Marrow.Services", "obj", "project.assets.json"))).RootElement;
        // The Services project's direct packages: the four voice ones and Microsoft.Web.WebView2 (W7, ED-NEW-web-W7-1; pinned by WebPackagePinsTests).
        var direct = new[] { "Whisper.net", "Whisper.net.Runtime", "Whisper.net.Runtime.Vulkan", "NAudio.Wasapi", "Microsoft.Web.WebView2" };
        Assert.Equal(["Microsoft.Extensions.AI.Abstractions 10.2.0", "NAudio.Core 3.1.0", "System.Numerics.Tensors 9.0.0", "Whisper.net.Runtime.Metal 1.9.1"], VoicePinsTests.ResolvedTransitivePackages(assets, direct));
    }
}
