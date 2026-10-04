using System.Text.Json;
using Marrow.Services.Port.Skills.Shims;

namespace Marrow.Services.Tests.Port.Skills.Shims;

// PORT-ONLY: SK-6 integration review. ED-309 2 and the launcher README say a sidecar needs only "Target" and that absent overrides and prefixes are identity operations. The unit's
// tests always wrote the sidecar with ShimDefinition.Write, which emits every field, so a sidecar that leaves fields out was never read.
public sealed class ShimDefinitionTests
{
    [Fact, Trait("Feature", "SK-6"), Trait("Rule", "R-TEST-4")]
    public async Task A_sidecar_with_only_a_target_reads_with_empty_overrides_and_runs_the_target_unchanged()
    {
        using var fixture = new ShimFixtures();
        var sidecar = Path.Combine(fixture.Root, "only-target.shim");
        File.WriteAllText(sidecar, JsonSerializer.Serialize(new Dictionary<string, string> { ["Target"] = fixture.Target }));

        var definition = ShimDefinition.Read(sidecar);

        Assert.Empty(definition.Environment);
        Assert.Empty(definition.PrefixArguments);
        Assert.Empty(definition.PackagePaths);
        Assert.Equal(0, await ShimRunner.RunAsync(definition, ["args", "one"], new RecordingShimLifetime()));
    }

    [Fact, Trait("Feature", "SK-6"), Trait("Rule", "R-TEST-4")]
    public void An_explicit_null_for_a_list_or_map_is_also_empty()
    {
        using var fixture = new ShimFixtures();
        var sidecar = Path.Combine(fixture.Root, "nulls.shim");
        File.WriteAllText(sidecar, JsonSerializer.Serialize(new Dictionary<string, string?> { ["Target"] = fixture.Target, ["Environment"] = null, ["PrefixArguments"] = null, ["PackagePaths"] = null }));

        var definition = ShimDefinition.Read(sidecar);

        Assert.Empty(definition.Environment);
        Assert.Empty(definition.PrefixArguments);
        Assert.Empty(definition.PackagePaths);
    }
}
