using System.Text;
using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/ProviderRegistry.swift (B2 1fbf81e), loadAPICatalog :567-593.
//
// Test-first proofs for the API-key catalog load (PR-PROVREG-155), driven through DeepSeek's models endpoint on a
// loopback server (R-TEST-10). The order the record names is what is observed: seed first, loading flag, key check,
// attempt mark BEFORE the fetch, update only on a non-empty list.
public sealed class ProviderRegistryApiCatalogTests
{
    private const ProviderKind DeepSeek = ProviderKind.Deepseek;

    private static string ModelsBody(params string[] ids) =>
        "{\"data\":[" + string.Join(",", ids.Select(id => "{\"id\":\"" + id + "\"}")) + "]}";

    private static IReadOnlyList<string> Ids(ProviderApiRig rig) => rig.Registry.Models(DeepSeek).Select(option => option.Id).ToList();

    private static byte[]? Stored(ProviderApiRig rig, string key) => rig.Runtime.Defaults.Object(key) as byte[];

    [Fact]
    [Trait("Req", "PR-PROVREG-155")] // ProviderRegistry.swift:576 (seed installed when the list is empty), :580 (empty key returns before any fetch), :581 (attempt marked only after the key check)
    public async Task an_empty_catalog_gets_the_seed_first_and_a_missing_key_fetches_nothing_and_leaves_the_attempt_open()
    {
        await using var rig = new ProviderApiRig();
        rig.ClearCatalog(DeepSeek);
        Assert.Empty(rig.Registry.Models(DeepSeek));
        rig.Loopback.Route(ProviderApiRig.DeepSeekModels, 200, ModelsBody("deepseek-v4-pro"));

        await rig.Run(() => rig.Registry.LoadCatalog(DeepSeek)); // no key in Settings

        Assert.Equal(ProviderRegistry.DeepseekSeed, rig.Registry.Models(DeepSeek)); // the seed is installed before the key is even read
        Assert.Equal(0, rig.Loopback.Total); // an empty key returns before any fetch
        Assert.Empty(rig.Registry.LoadingCatalogs); // the defer cleared the flag on the early return
        Assert.Null(Stored(rig, "providerModelCatalogDates")); // nothing was delivered, so no date

        // The early return preceded the attempt mark: once a key exists, an unforced ask still fetches.
        rig.Settings.Keys[DeepSeek] = "ds-key";
        await rig.Run(() => rig.Registry.LoadCatalog(DeepSeek));
        Assert.Equal(1, rig.Loopback.Count(ProviderApiRig.DeepSeekModels));
        Assert.Equal(["deepseek-v4-pro"], Ids(rig));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-155")] // ProviderRegistry.swift:576 (the seed is installed only when the current list is empty)
    public async Task a_catalog_that_already_has_rows_keeps_them_when_the_key_is_missing()
    {
        await using var rig = new ProviderApiRig(); // the rig persists one "seed-ds" row
        await rig.Run(() => rig.Registry.LoadCatalog(DeepSeek));
        Assert.Equal(["seed-ds"], Ids(rig));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-155")] // ProviderRegistry.swift:581-587 (attempt marked BEFORE the fetch, so a failed fetch is not retried this launch)
    public async Task a_failed_fetch_is_not_retried_by_an_unforced_ask_but_a_forced_ask_fetches_again()
    {
        await using var rig = new ProviderApiRig();
        rig.Settings.Keys[DeepSeek] = "ds-key";
        rig.Loopback.Route(ProviderApiRig.DeepSeekModels, 500, "{}");

        await rig.Run(() => rig.Registry.LoadCatalog(DeepSeek));
        Assert.Equal(1, rig.Loopback.Count(ProviderApiRig.DeepSeekModels));
        Assert.Equal(["seed-ds"], Ids(rig)); // nothing was updated

        await rig.Run(() => rig.Registry.LoadCatalog(DeepSeek)); // the attempt was already marked: no second request
        Assert.Equal(1, rig.Loopback.Count(ProviderApiRig.DeepSeekModels));

        await rig.Run(() => rig.Registry.LoadCatalog(DeepSeek, force: true)); // force bypasses the gate
        Assert.Equal(2, rig.Loopback.Count(ProviderApiRig.DeepSeekModels));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-155")] // ProviderRegistry.swift:588-592 (only a non-empty list updates the catalog and stores the date)
    public async Task an_empty_fetch_updates_nothing_and_stores_no_date()
    {
        await using var rig = new ProviderApiRig();
        rig.Settings.Keys[DeepSeek] = "ds-key";
        rig.Loopback.Route(ProviderApiRig.DeepSeekModels, 200, "{\"data\":[]}");
        var blobWrites = rig.Runtime.Defaults.Writes.Count(key => key == "providerModelCatalogs");

        await rig.Run(() => rig.Registry.LoadCatalog(DeepSeek));

        Assert.Equal(1, rig.Loopback.Count(ProviderApiRig.DeepSeekModels));
        Assert.Equal(["seed-ds"], Ids(rig));
        Assert.Null(Stored(rig, "providerModelCatalogDates"));
        Assert.Equal(blobWrites, rig.Runtime.Defaults.Writes.Count(key => key == "providerModelCatalogs")); // no cache write either
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-155")] // ProviderRegistry.swift:577-578 (loading flag set around the fetch), :589-591 (update, date stored); persistence "both catalog keys"
    public async Task a_non_empty_fetch_marks_loading_while_it_is_out_then_updates_the_catalog_and_stores_the_date()
    {
        await using var rig = new ProviderApiRig();
        rig.Settings.Keys[DeepSeek] = "ds-key";
        rig.Loopback.Intercept(ProviderApiRig.DeepSeekModels);
        rig.Runtime.Advance(TimeSpan.FromSeconds(100));

        TaskHandle? load = null;
        rig.OnMain(() => load = MainActor.Spawn(() => rig.Registry.LoadCatalog(DeepSeek)));
        var pump = rig.Until(() => rig.Loopback.Count(ProviderApiRig.DeepSeekModels) >= 1);
        var held = await rig.Loopback.NextIntercepted(ProviderApiRig.DeepSeekModels);
        await pump;
        Assert.Contains(DeepSeek, rig.Registry.LoadingCatalogs);

        await held.Respond(200, "OK", Encoding.UTF8.GetBytes(ModelsBody("deepseek-v4-pro", "deepseek-flash")));
        await rig.Until(() => load!.Completion.IsCompleted);

        Assert.Empty(rig.Registry.LoadingCatalogs);
        Assert.Equal(["deepseek-flash", "deepseek-v4-pro"], Ids(rig).Order(StringComparer.Ordinal).ToList());
        var blob = JsonDocument.Parse(Stored(rig, "providerModelCatalogs")!).RootElement;
        Assert.Equal(2, blob.GetProperty("deepseek").GetArrayLength());
        var dates = JsonDocument.Parse(Stored(rig, "providerModelCatalogDates")!).RootElement;
        Assert.Equal(SwiftDate.Seconds1970(SwiftDate.Now), dates.GetProperty("deepseek").GetDouble());
        Assert.Equal(1, rig.Loopback.Count(ProviderApiRig.DeepSeekModels));
    }
}
