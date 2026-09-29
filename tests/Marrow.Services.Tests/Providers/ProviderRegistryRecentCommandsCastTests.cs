using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// Audit fix (svc): ProviderRegistry.swift:172 casts the whole recentSlashCommands dictionary with `as? [String: [String]] ?? [:]`, so
// one entry that is not an array of strings makes the cast fail and every entry, valid ones included, is discarded.
public sealed class ProviderRegistryRecentCommandsCastTests
{
    [Trait("Req", "PR-PROVREG-216")] // Services/Providers/ProviderRegistry.swift:172
    [Fact]
    public void One_malformed_recent_command_entry_discards_the_whole_stored_dictionary()
    {
        using var runtime = new TestRuntime();
        var payload = new Dictionary<string, object[]>
        {
            ["deepseek"] = [new { id = "seed-ds", name = "Seed", efforts = Array.Empty<string>() }],
            ["zai"] = [new { id = "seed-zai", name = "Seed", efforts = Array.Empty<string>() }],
            ["commandcode"] = [new { id = "seed-cc", name = "Seed", efforts = Array.Empty<string>() }],
        };
        runtime.Defaults.Set("providerModelCatalogs",
            System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(payload)));
        runtime.Defaults.Set("recentSlashCommands", new Dictionary<string, object>
        {
            ["claude"] = new object[] { "/review" },
            ["broken"] = new object[] { 1 },
        });

        var registry = new ProviderRegistry(new FakeProviderSettings());

        Assert.Empty(registry.RecentCommands);
    }
}
