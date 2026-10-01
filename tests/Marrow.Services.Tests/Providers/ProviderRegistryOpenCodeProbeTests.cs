using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/ProviderRegistry.swift (B3 52bc7926), openCodeCatalog :525-539 and loadCatalog's
// OpenCode arm and sibling delivery :492, :496-515.
//
// Test-first proofs for the shared OpenCode catalog probe (PR-PROVREG-246) and for what a delivered OpenCode load does
// to the other OpenCode provider (PR-PROVREG-248). The agent is the compiled ACP stand-in (R-TEST-8): Zen's and Go's
// binary paths name two DIFFERENT copies, so a launch count per copy says both how many probes started and whose
// executable ran. No real CLI is started and nothing touches the network (R-TEST-10). The probe launches through a
// process-wide launch gate on the flow's clock, which the rig resets, so this class runs with no other class in
// parallel (like PlanLimitsTests, ED-190).
[Collection(PlanLimitsProcessCollection.Name)]
public sealed class ProviderRegistryOpenCodeProbeTests
{
    private const string DatesKey = "providerModelCatalogDates";
    private const string BlobKey = "providerModelCatalogs";

    private static readonly string Mixed = AcpStandIns.Models(("opencode/zen-a", "Zen A"), ("opencode-go/go-b", "OpenCode Go/Go B"));
    private static readonly string ZenOnly = AcpStandIns.Models(("opencode/zen-a", "Zen A"));
    private static readonly string GoOnly = AcpStandIns.Models(("opencode-go/go-b", "OpenCode Go/Go B"));
    private const string NoModels = "{\"sessionId\":\"s1\"}";

    private static int Writes(OpenCodeProbeRig rig, string key) => rig.Runtime.Defaults.Writes.Count(written => written == key);

    private static JsonElement Stored(OpenCodeProbeRig rig, string key) => JsonDocument.Parse((byte[])rig.Runtime.Defaults.Object(key)!).RootElement;

    // ---- PR-PROVREG-246: one probe per launch, shared by Zen and Go ----------------------------------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-246")] // ProviderRegistry.swift:527-534 (the first caller starts one probe with its own executable), :492
    public async Task the_first_ask_probes_once_through_the_asking_providers_own_executable_and_a_later_ask_for_the_other_provider_spawns_nothing()
    {
        using var rig = new OpenCodeProbeRig();
        rig.Zen.SetModels(ZenOnly); // only Zen's half comes back, so Go's catalog stays open
        rig.Go.SetModels(ZenOnly);

        await rig.Ask(ProviderKind.Opencode);
        Assert.Equal(1, rig.Zen.Launches); // Zen asked: Zen's resolved executable ran
        Assert.Equal(0, rig.Go.Launches);
        Assert.Equal(["acp"], rig.Zen.Arguments(0));
        Assert.Equal(["opencode/zen-a"], rig.Ids(ProviderKind.Opencode));

        await rig.Ask(ProviderKind.OpencodeGo); // Go's own gates pass, and it awaits the stored probe instead of launching
        Assert.Equal(1, rig.Launches);
        Assert.Empty(rig.Ids(ProviderKind.OpencodeGo));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-246")] // ProviderRegistry.swift:529-534 (the probe runs with the CALLING provider's executable)
    public async Task a_first_ask_by_go_probes_through_gos_own_executable()
    {
        using var rig = new OpenCodeProbeRig();
        rig.Zen.SetModels(GoOnly);
        rig.Go.SetModels(GoOnly);

        await rig.Ask(ProviderKind.OpencodeGo);

        Assert.Equal(1, rig.Go.Launches);
        Assert.Equal(0, rig.Zen.Launches);
        Assert.Equal(["opencode-go/go-b"], rig.Ids(ProviderKind.OpencodeGo));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-246")] // ProviderRegistry.swift:525-539 (asks that overlap in time await one stored task)
    public async Task asks_for_zen_and_go_that_overlap_share_one_probe()
    {
        using var rig = new OpenCodeProbeRig();
        rig.Zen.SetModels(ZenOnly).SetDelay(700);
        rig.Go.SetModels(ZenOnly).SetDelay(700);
        AcpStandIns.ResetLaunchGate();

        TaskHandle? zen = null;
        TaskHandle? go = null;
        rig.Runtime.OnMain(() =>
        {
            zen = MainActor.Spawn(() => rig.Registry.LoadCatalog(ProviderKind.Opencode));
            go = MainActor.Spawn(() => rig.Registry.LoadCatalog(ProviderKind.OpencodeGo));
        });
        await Task.Run(() => rig.Runtime.Loop.RunUntil(() => zen!.Completion.IsCompleted && go!.Completion.IsCompleted, TimeSpan.FromSeconds(60)));

        Assert.Equal(1, rig.Launches); // one CLI process answered both providers
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-246")] // ProviderRegistry.swift:526 (`if force { openCodeCatalogTask = nil }`)
    public async Task a_forced_ask_discards_the_stored_probe_and_starts_a_new_one()
    {
        using var rig = new OpenCodeProbeRig();
        rig.Zen.SetModels(ZenOnly);

        await rig.Ask(ProviderKind.Opencode);
        Assert.Equal(1, rig.Launches);

        await rig.Ask(ProviderKind.Opencode, force: true);
        Assert.Equal(2, rig.Launches);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-246")] // ProviderRegistry.swift:535-537 (`if list == nil { openCodeCatalogTask = nil }`): a probe that threw is not remembered
    public async Task a_probe_that_throws_is_forgotten_so_the_next_ask_probes_again()
    {
        using var rig = new OpenCodeProbeRig();
        rig.Zen.SetError("agent could not start a session", launch: 0).SetModels(ZenOnly, launch: 1);

        await rig.Ask(ProviderKind.Opencode);
        Assert.Equal(1, rig.Launches);
        Assert.Empty(rig.Ids(ProviderKind.Opencode)); // nothing delivered, and the attempt stays open

        await rig.Ask(ProviderKind.Opencode); // unforced, yet it launches again
        Assert.Equal(2, rig.Launches);
        Assert.Equal(["opencode/zen-a"], rig.Ids(ProviderKind.Opencode));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-246")] // ProviderRegistry.swift:535-538 (only nil clears the store: an empty list stays stored for the rest of the launch)
    public async Task a_probe_that_returns_an_empty_list_stays_stored_so_unforced_asks_do_not_probe_again_until_forced()
    {
        using var rig = new OpenCodeProbeRig();
        rig.Zen.SetModels(NoModels);

        await rig.Ask(ProviderKind.Opencode);
        await rig.Ask(ProviderKind.Opencode);
        await rig.Ask(ProviderKind.Opencode);
        Assert.Equal(1, rig.Launches); // the empty answer is reused
        Assert.Empty(rig.Ids(ProviderKind.Opencode));

        await rig.Ask(ProviderKind.Opencode, force: true); // force discards it
        Assert.Equal(2, rig.Launches);
    }

    // ---- PR-PROVREG-248: a delivered OpenCode load also fills the other provider's catalog -----------------------

    [Fact]
    [Trait("Req", "PR-PROVREG-248")] // ProviderRegistry.swift:496-515 (the sibling's half is updated, dated, marked attempted; dates stored a second time)
    public async Task a_zen_load_of_a_mixed_probe_fills_the_go_catalog_dates_it_and_closes_its_attempt_with_no_second_probe()
    {
        using var rig = new OpenCodeProbeRig();
        rig.Zen.SetModels(Mixed);

        await rig.Ask(ProviderKind.Opencode);

        Assert.Equal(["opencode/zen-a"], rig.Ids(ProviderKind.Opencode));
        Assert.Equal(["opencode-go/go-b"], rig.Ids(ProviderKind.OpencodeGo));
        Assert.Equal("Go B", rig.Registry.Models(ProviderKind.OpencodeGo)[0].Name); // the redundant half of the name is dropped
        Assert.True(ProviderRegistryState.IsCatalogAttempted(rig.Registry, ProviderKind.Opencode));
        Assert.True(ProviderRegistryState.IsCatalogAttempted(rig.Registry, ProviderKind.OpencodeGo));
        Assert.Equal(2, Writes(rig, DatesKey)); // once for the asking provider, once more after the sibling
        var dates = Stored(rig, DatesKey);
        Assert.True(dates.TryGetProperty(SwiftEnum<ProviderKind>.RawValue(ProviderKind.Opencode), out _));
        Assert.True(dates.TryGetProperty(SwiftEnum<ProviderKind>.RawValue(ProviderKind.OpencodeGo), out _));
        var blob = Stored(rig, BlobKey);
        Assert.Equal(1, blob.GetProperty(SwiftEnum<ProviderKind>.RawValue(ProviderKind.OpencodeGo)).GetArrayLength());

        await rig.Ask(ProviderKind.OpencodeGo); // closed: nothing is launched for Go
        Assert.Equal(1, rig.Launches);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-248")] // ProviderRegistry.swift:504-515 (the loop covers each provider other than the asking one: a Go load fills Zen)
    public async Task a_go_load_of_a_mixed_probe_fills_the_zen_catalog_the_same_way()
    {
        using var rig = new OpenCodeProbeRig();
        rig.Go.SetModels(Mixed);

        await rig.Ask(ProviderKind.OpencodeGo);

        Assert.Equal(["opencode-go/go-b"], rig.Ids(ProviderKind.OpencodeGo));
        Assert.Equal(["opencode/zen-a"], rig.Ids(ProviderKind.Opencode));
        Assert.True(ProviderRegistryState.IsCatalogAttempted(rig.Registry, ProviderKind.Opencode));
        Assert.Equal(2, Writes(rig, DatesKey));
        Assert.Equal(1, rig.Launches);
        Assert.Equal(0, rig.Zen.Launches);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-248")] // ProviderRegistry.swift:509-511 (the sibling goes through updateCatalog, so the ACP merge applies to it)
    public async Task the_sibling_is_written_through_the_acp_merge_so_its_cached_effort_scale_survives()
    {
        var goKey = SwiftEnum<ProviderKind>.RawValue(ProviderKind.OpencodeGo);
        using var rig = new OpenCodeProbeRig("{\"" + goKey + "\":[{\"id\":\"opencode-go/go-b\",\"name\":\"Go B\",\"efforts\":[\"low\",\"high\"]}]}");
        rig.Zen.SetModels(Mixed); // the live Go row arrives with no scale of its own

        await rig.Ask(ProviderKind.Opencode);

        var go = Assert.Single(rig.Registry.Models(ProviderKind.OpencodeGo));
        Assert.Equal(["low", "high"], go.Efforts);
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-248")] // ProviderRegistry.swift:496 (the asking provider's own half must be non-empty: otherwise nothing is written for either)
    public async Task an_own_half_that_is_empty_writes_nothing_for_either_provider_even_when_the_other_half_is_not()
    {
        using var rig = new OpenCodeProbeRig();
        rig.Zen.SetModels(GoOnly); // Zen asks; the probe holds only Go rows

        await rig.Ask(ProviderKind.Opencode);

        Assert.Empty(rig.Ids(ProviderKind.Opencode));
        Assert.Empty(rig.Ids(ProviderKind.OpencodeGo)); // Go's half was available and is NOT used
        Assert.Equal(0, Writes(rig, DatesKey));
        Assert.Equal(0, Writes(rig, BlobKey));
        Assert.False(ProviderRegistryState.IsCatalogAttempted(rig.Registry, ProviderKind.Opencode));
        Assert.False(ProviderRegistryState.IsCatalogAttempted(rig.Registry, ProviderKind.OpencodeGo));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-248")] // ProviderRegistry.swift:511 (`guard !sibling.isEmpty else { continue }`; :515 the dates are stored after the loop regardless)
    public async Task an_empty_sibling_half_is_left_as_it_was_and_not_marked_attempted()
    {
        using var rig = new OpenCodeProbeRig();
        rig.Zen.SetModels(ZenOnly);

        await rig.Ask(ProviderKind.Opencode);

        Assert.Equal(["opencode/zen-a"], rig.Ids(ProviderKind.Opencode));
        Assert.Empty(rig.Ids(ProviderKind.OpencodeGo));
        Assert.True(ProviderRegistryState.IsCatalogAttempted(rig.Registry, ProviderKind.Opencode));
        Assert.False(ProviderRegistryState.IsCatalogAttempted(rig.Registry, ProviderKind.OpencodeGo));
        Assert.Equal(2, Writes(rig, DatesKey)); // the second store happens after the loop whether or not a sibling was written
        var dates = Stored(rig, DatesKey);
        Assert.True(dates.TryGetProperty(SwiftEnum<ProviderKind>.RawValue(ProviderKind.Opencode), out _));
        Assert.False(dates.TryGetProperty(SwiftEnum<ProviderKind>.RawValue(ProviderKind.OpencodeGo), out _));
    }
}
