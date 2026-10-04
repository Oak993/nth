using Marrow.Core.Port.Voice;

namespace Marrow.Core.Tests.Port.Voice;

// VT-1 (OD-0092; voice-SPEC V-10.2, from the benchmark's draft recommendation table): the candidate for scripted hardware.
// Expected ids are the table's rows. The scripted machines stand in for the hardware probe, which the unit does not build.
public sealed class RecommenderTests
{
    private const long Gb = 1L << 30;

    private static HardwareProfile Gpu(string name, GpuVendor vendor, long dedicatedGb, double freeGb, GpuBackend backend = GpuBackend.Vulkan, int cores = 8) =>
        new("Some Processor", cores, 32, name, vendor, dedicatedGb * Gb, (long)(freeGb * Gb), "31.0.15.5222", backend);

    private static HardwareProfile Cpu(int cores) =>
        new("Some Processor", cores, 16, "Microsoft Basic Render", GpuVendor.Other, 0, 0, "", GpuBackend.None);

    // The owner's desktop: Ryzen 7 9800X3D, RTX 4080, 31 GB (OD-0092).
    private static HardwareProfile Desktop() => Gpu("NVIDIA GeForce RTX 4080", GpuVendor.Nvidia, 16, 14);

    [Theory]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T14")]
    [Trait("Decision", "ED-267")] // V-10.2 row 1: a working GPU backend on an NVIDIA GPU with >= 3 GB free -> large-v3-turbo-q8_0, for any language
    [InlineData("en")]
    [InlineData("auto")]
    [InlineData("hi")]
    public void The_owners_desktop_gets_large_v3_turbo_q8_0(string language)
    {
        var r = Recommender.Recommend(Desktop(), language);
        Assert.Equal("large-v3-turbo-q8_0", r.ModelId);
        Assert.Equal(RecommendationBasis.Gpu, r.Basis);
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T14")]
    [Trait("Decision", "ED-267")] // V-10.2 row 1 / row 2: the 3 GB free boundary, counted in 2^30 bytes
    public void NVIDIA_needs_three_gigabytes_free_for_q8_and_otherwise_gets_q5()
    {
        Assert.Equal("large-v3-turbo-q8_0", Recommender.Recommend(Gpu("NVIDIA GeForce GTX 1060", GpuVendor.Nvidia, 6, 3.0), "auto").ModelId);
        Assert.Equal("large-v3-turbo-q5_0", Recommender.Recommend(Gpu("NVIDIA GeForce GTX 1060", GpuVendor.Nvidia, 6, 2.99), "auto").ModelId);
        var twoGb = Gpu("NVIDIA GeForce GT 1030", GpuVendor.Nvidia, 2, 1.7);
        Assert.Equal("large-v3-turbo-q5_0", Recommender.Recommend(twoGb, "en").ModelId);
        Assert.Equal(RecommendationBasis.Gpu, Recommender.Recommend(twoGb, "en").Basis);
    }

    [Theory]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T14")]
    [Trait("Decision", "ED-267")] // V-10.2 row 2: a working GPU backend otherwise (less VRAM, AMD, Intel) -> large-v3-turbo-q5_0, with plenty of free memory too
    [InlineData("AMD Radeon RX 7900 XTX", GpuVendor.Amd, 24L, 22.0)]
    [InlineData("Intel(R) Arc(TM) A770", GpuVendor.Intel, 16L, 15.0)]
    [InlineData("Intel(R) UHD Graphics", GpuVendor.Intel, 0L, 0.0)]
    [InlineData("Some Other Adapter", GpuVendor.Other, 8L, 7.0)]
    public void Any_other_working_GPU_gets_large_v3_turbo_q5_0(string name, GpuVendor vendor, long dedicatedGb, double freeGb)
    {
        foreach (var language in new[] { "en", "auto", "ur" })
            Assert.Equal("large-v3-turbo-q5_0", Recommender.Recommend(Gpu(name, vendor, dedicatedGb, freeGb), language).ModelId);
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T14")]
    [Trait("Decision", "ED-267")] // V-10.2: CUDA counts as a working GPU backend as Vulkan does
    public void A_working_CUDA_backend_counts_as_a_working_GPU_backend() =>
        Assert.Equal("large-v3-turbo-q8_0", Recommender.Recommend(Gpu("NVIDIA GeForce RTX 4080", GpuVendor.Nvidia, 16, 14, GpuBackend.Cuda), "en").ModelId);

    [Theory]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T14")]
    [Trait("Decision", "ED-267")] // V-10.2 rows 3 to 6: CPU only, by physical cores (>= 8) and language (English only when forced to English)
    [InlineData(16, "en", "small.en-q8_0")]
    [InlineData(8, "en", "small.en-q8_0")]
    [InlineData(16, "auto", "large-v3-turbo-q5_0")]
    [InlineData(8, "auto", "large-v3-turbo-q5_0")]
    [InlineData(16, "hi", "large-v3-turbo-q5_0")]
    [InlineData(7, "en", "base.en")]
    [InlineData(4, "en", "base.en")]
    [InlineData(1, "en", "base.en")]
    [InlineData(7, "auto", "small-q5_1")]
    [InlineData(4, "auto", "small-q5_1")]
    [InlineData(4, "ur", "small-q5_1")]
    public void CPU_only_machines_follow_the_cores_and_language_rows(int cores, string language, string expected)
    {
        var r = Recommender.Recommend(Cpu(cores), language);
        Assert.Equal(expected, r.ModelId);
        Assert.Equal(RecommendationBasis.Cpu, r.Basis);
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T14")]
    [Trait("Decision", "ED-267")] // V-10.1 / V-10.2: "a working GPU backend" -- a GPU that is present but with no working backend runs on the processor
    public void A_GPU_with_no_working_backend_is_a_CPU_only_machine()
    {
        var r = Recommender.Recommend(Gpu("NVIDIA GeForce RTX 4080", GpuVendor.Nvidia, 16, 14, GpuBackend.None, cores: 16), "en");
        Assert.Equal("small.en-q8_0", r.ModelId);
        Assert.Equal(RecommendationBasis.Cpu, r.Basis);
        Assert.Equal("base.en", Recommender.Recommend(Gpu("NVIDIA GeForce RTX 4080", GpuVendor.Nvidia, 16, 14, GpuBackend.None, cores: 4), "en").ModelId);
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T14")]
    [Trait("Decision", "ED-267")] // V-10.2: the language is English when forced to English, otherwise multilingual (the GPU rows ignore it)
    public void Only_a_forced_English_picks_an_english_only_model()
    {
        Assert.True(Recommender.IsEnglish("en"));
        foreach (var other in new[] { "auto", "hi", "ur", "", "EN" }) Assert.False(Recommender.IsEnglish(other));
        Assert.False(Recommender.IsEnglish(null));
        Assert.Equal("small-q5_1", Recommender.Recommend(Cpu(4), null).ModelId);
    }

    private static readonly string[] NeverRecommended =
    [
        "large-v3-q5_0", "medium.en-q5_0", "medium.en-q8_0", "medium.en", "tiny-q5_1", "tiny-q8_0", "tiny", "base-q5_1", "base-q8_0", "base",
        "small", "small.en", "medium", "large-v3-turbo", "large-v3",
    ];

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T14")]
    [Trait("Decision", "ED-267")] // V-10.2 "Never recommended (BENCH 'Dominated')": the list, exactly
    public void The_never_recommended_list_is_the_dominated_models_and_nothing_else()
    {
        foreach (var id in NeverRecommended) Assert.True(Recommender.IsNeverRecommended(id), id);
        var rest = ModelCatalog.Files.Select(f => f.Id).Except(NeverRecommended).ToList();
        Assert.Equal(14, rest.Count);
        foreach (var id in rest) Assert.False(Recommender.IsNeverRecommended(id), id);
        Assert.False(Recommender.IsNeverRecommended("nope"));
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T14")]
    [Trait("Decision", "ED-267")] // V-10.2: no scripted machine is ever given a dominated model, and every answer is in the catalogue
    public void No_machine_is_recommended_a_dominated_model()
    {
        var vendors = Enum.GetValues<GpuVendor>();
        var backends = Enum.GetValues<GpuBackend>();
        var count = 0;
        foreach (var vendor in vendors)
        foreach (var backend in backends)
        foreach (var freeGb in new[] { 0.0, 1.0, 2.99, 3.0, 8.0 })
        foreach (var cores in new[] { 1, 2, 4, 7, 8, 12, 24 })
        foreach (var language in new string?[] { "en", "auto", "hi", "ur", null })
        {
            var profile = Gpu("Adapter", vendor, 8, freeGb, backend, cores);
            var id = Recommender.Recommend(profile, language).ModelId;
            Assert.NotNull(ModelCatalog.Find(id));
            Assert.DoesNotContain(id, NeverRecommended);
            count++;
        }
        Assert.Equal(4 * 3 * 5 * 7 * 5, count);
    }

    [Fact]
    [Trait("Feature", "VT-1")]
    [Trait("Spec", "V-T14")]
    [Trait("Decision", "ED-267")] // V-10.2: an English-only recommendation exists only for a forced English
    public void Non_English_runs_never_get_an_english_only_model()
    {
        foreach (var cores in new[] { 1, 4, 8, 16 })
        foreach (var language in new string?[] { "auto", "hi", "ur", null })
            Assert.False(ModelCatalog.Find(Recommender.Recommend(Cpu(cores), language).ModelId)!.EnglishOnly);
    }
}
