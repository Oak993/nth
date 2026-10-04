using Marrow.Core.Models;
using Marrow.Testing;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.Skills.Providers;

// PORT-ONLY: SK-11d / DL-11 hard rule: with nothing installed, the provider sees exactly today's environment.
public sealed class OpenCodeRegistryByteIdentityTests
{
    private static ProviderRegistry Make(TestRuntime runtime)
    {
        // The constructor seeds three providers' catalogs only when empty; pre-seed so the registry touches nothing else (as ProviderRegistryTests).
        var seed = new Dictionary<string, object[]>
        {
            ["deepseek"] = [new { id = "s", name = "S", efforts = Array.Empty<string>() }],
            ["zai"] = [new { id = "s", name = "S", efforts = Array.Empty<string>() }],
            ["commandcode"] = [new { id = "s", name = "S", efforts = Array.Empty<string>() }],
        };
        runtime.Defaults.Set("providerModelCatalogs", System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(seed)));
        return new ProviderRegistry(new FakeProviderSettings());
    }

    private static IEnumerable<KeyValuePair<string, string>> Sorted(IReadOnlyDictionary<string, string> e) => e.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).ToArray();

    [Fact, Trait("Feature", "SK-11d"), Trait("Rule", "R-TEST-4")]
    public void Without_a_generation_the_environment_of_every_provider_is_byte_identical_to_the_existing_code()
    {
        using var f = new OpenCodeFixture();
        using var storage = Storage.Locations.Override(StorageLocations.Create(f.Root, f.Root));
        using var claude = new ClaudeConfigOverride(f.Write(Path.Combine(f.Root, "claude.json"), "{\"mcpServers\":{\"local\":{\"command\":\"exe\"}}}"));
        using var runtime = new TestRuntime();
        var registry = Make(runtime);
        foreach (var provider in Enum.GetValues<ProviderKind>())
        {
            var today = registry.Environment(provider);
            var with = registry.Environment(provider, null);
            Assert.Equal(Sorted(today), Sorted(with));
        }
    }

    [Fact, Trait("Feature", "SK-11d"), Trait("Rule", "R-TEST-4")]
    public void A_generation_changes_only_OpenCode_and_only_PATH_and_the_config_variable()
    {
        using var f = new OpenCodeFixture();
        using var storage = Storage.Locations.Override(StorageLocations.Create(f.Root, f.Root));
        using var claude = new ClaudeConfigOverride(f.Write(Path.Combine(f.Root, "claude.json"), "{\"mcpServers\":{\"local\":{\"command\":\"exe\"}}}"));
        using var runtime = new TestRuntime();
        var registry = Make(runtime);
        foreach (var provider in Enum.GetValues<ProviderKind>())
        {
            var today = registry.Environment(provider);
            var with = registry.Environment(provider, f.Launch);
            if (provider is not (ProviderKind.Opencode or ProviderKind.OpencodeGo)) { Assert.Equal(Sorted(today), Sorted(with)); continue; }
            var rest = new[] { "PATH", "OPENCODE_CONFIG_CONTENT" };
            Assert.Equal(Sorted(today).Where(p => !rest.Contains(p.Key)), Sorted(with).Where(p => !rest.Contains(p.Key)));
            var mcp = today["OPENCODE_CONFIG_CONTENT"];
            Assert.StartsWith(mcp[..^1] + ",\"skills\":{\"paths\":[", with["OPENCODE_CONFIG_CONTENT"], StringComparison.Ordinal);
        }
    }
}
