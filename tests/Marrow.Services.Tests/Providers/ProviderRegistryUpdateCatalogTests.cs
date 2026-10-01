using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/ProviderRegistry.swift (B2 1fbf81e), updateCatalog :717-733.
//
// Test-first proofs for PR-PROVREG-156: after the provider-specific merge, an empty list or a list equal to the
// stored one is ignored (no assignment, no write); otherwise the catalog is assigned and the WHOLE catalogs
// dictionary is encoded under "providerModelCatalogs". Settings are the in-memory store of TestRuntime, which
// records the key of every write.
public sealed class ProviderRegistryUpdateCatalogTests
{
    private const string BlobKey = "providerModelCatalogs";

    private static ProviderRegistry Registry(TestRuntime runtime) => new(new FakeProviderSettings());

    private static int BlobWrites(TestRuntime runtime) => runtime.Defaults.Writes.Count(key => key == BlobKey);

    private static ValueList<ModelOption> Rows(params ModelOption[] options) => new(options);

    private static JsonElement Blob(TestRuntime runtime) => JsonDocument.Parse((byte[])runtime.Defaults.Object(BlobKey)!).RootElement;

    [Fact]
    [Trait("Req", "PR-PROVREG-156")] // ProviderRegistry.swift:727 (the guard requires a non-empty list)
    public void an_empty_list_changes_nothing_and_writes_nothing()
    {
        using var runtime = new TestRuntime();
        var registry = Registry(runtime);
        runtime.OnMain(() => registry.UpdateCatalog(Rows(new ModelOption("a", "A")), ProviderKind.Cursor));
        var writes = BlobWrites(runtime);
        Assert.Equal(1, writes);

        runtime.OnMain(() => registry.UpdateCatalog(ValueList<ModelOption>.Empty, ProviderKind.Cursor));

        Assert.Equal(["a"], registry.Models(ProviderKind.Cursor).Select(option => option.Id));
        Assert.Equal(writes, BlobWrites(runtime));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-156")] // ProviderRegistry.swift:727 (the guard requires a list different from the stored one)
    public void a_list_equal_to_the_stored_one_writes_nothing_and_a_changed_one_writes_again()
    {
        using var runtime = new TestRuntime();
        var registry = Registry(runtime);
        runtime.OnMain(() => registry.UpdateCatalog(Rows(new ModelOption("a", "A"), new ModelOption("b", "B")), ProviderKind.Cursor));
        Assert.Equal(1, BlobWrites(runtime));

        runtime.OnMain(() => registry.UpdateCatalog(Rows(new ModelOption("a", "A"), new ModelOption("b", "B")), ProviderKind.Cursor));
        Assert.Equal(1, BlobWrites(runtime)); // equal: ignored

        runtime.OnMain(() => registry.UpdateCatalog(Rows(new ModelOption("a", "A"), new ModelOption("b", "B renamed")), ProviderKind.Cursor));
        Assert.Equal(2, BlobWrites(runtime)); // a changed row is a change
        Assert.Equal("B renamed", registry.Models(ProviderKind.Cursor)[1].Name);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-156")] // ProviderRegistry.swift:729-733 (the ENTIRE catalogs dictionary is re-encoded and written)
    public void a_write_carries_every_providers_catalog_not_only_the_changed_one()
    {
        using var runtime = new TestRuntime();
        var registry = Registry(runtime);
        Assert.True(registry.Catalogs.Count >= 4); // the constructor already holds Claude, DeepSeek, Meta, Z.ai and more

        runtime.OnMain(() => registry.UpdateCatalog(Rows(new ModelOption("only-cursor", "Cursor Row")), ProviderKind.Cursor));

        var blob = Blob(runtime);
        var providers = blob.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(registry.Catalogs.Keys.Select(SwiftEnum<ProviderKind>.RawValue).Order(StringComparer.Ordinal).ToList(), providers);
        Assert.Contains("claude", providers);
        Assert.Contains("deepseek", providers);
        Assert.Contains("cursor", providers);
        foreach (var (provider, rows) in registry.Catalogs)
        {
            var written = blob.GetProperty(SwiftEnum<ProviderKind>.RawValue(provider)).EnumerateArray()
                .Select(row => row.GetProperty("id").GetString()!).ToList();
            Assert.Equal(rows.Select(option => option.Id).ToList(), written);
        }
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-156")] // ProviderRegistry.swift:722-727 (the Claude merge runs BEFORE the unchanged guard)
    public void a_claude_list_that_merges_to_the_stored_seed_is_ignored_and_one_with_a_new_row_is_stored_merged()
    {
        using var runtime = new TestRuntime();
        var registry = Registry(runtime);
        var seed = registry.Models(ProviderKind.Claude);
        Assert.Equal(5, seed.Count);

        // The live row for "haiku" differs from nothing the seed holds: laid over the seed it changes no field,
        // so the merged list equals the stored one although the list passed in does not.
        runtime.OnMain(() => registry.UpdateCatalog(Rows(new ModelOption("haiku", "Haiku")), ProviderKind.Claude));
        Assert.Equal(0, BlobWrites(runtime));
        Assert.Equal(seed, registry.Models(ProviderKind.Claude));

        runtime.OnMain(() => registry.UpdateCatalog(Rows(new ModelOption("claude-new", "Claude New")), ProviderKind.Claude));
        Assert.Equal(1, BlobWrites(runtime));
        Assert.Equal(6, registry.Models(ProviderKind.Claude).Count); // the seed is kept and the new row appended
        Assert.Equal(6, Blob(runtime).GetProperty("claude").GetArrayLength());

        // The guard looks at the MERGED list: an empty live list merges to the seed, which is non-empty and differs from
        // the six rows now stored, so it is assigned and written (an empty list passed in is not what is guarded).
        runtime.OnMain(() => registry.UpdateCatalog(ValueList<ModelOption>.Empty, ProviderKind.Claude));
        Assert.Equal(2, BlobWrites(runtime));
        Assert.Equal(seed, registry.Models(ProviderKind.Claude));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-156")] // ProviderRegistry.swift:722-727 (the ACP merge runs BEFORE the unchanged guard)
    public void an_acp_list_that_merges_to_the_stored_rows_is_ignored()
    {
        using var runtime = new TestRuntime();
        var registry = Registry(runtime);
        var scale = new ValueList<string>(["low", "high"]);
        runtime.OnMain(() => registry.UpdateCatalog(Rows(new ModelOption("m", "M", efforts: scale)), ProviderKind.Cursor));
        Assert.Equal(1, BlobWrites(runtime));

        // A live row without a scale keeps the stored one (:112-114), so the merged list equals the stored list.
        runtime.OnMain(() => registry.UpdateCatalog(Rows(new ModelOption("m", "M")), ProviderKind.Cursor));

        Assert.Equal(1, BlobWrites(runtime));
        Assert.Equal(scale, registry.Models(ProviderKind.Cursor)[0].Efforts);
    }
}
