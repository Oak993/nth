using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/ProviderRegistry.swift (B2 1fbf81e), loadClaudeCatalog :561-565 and
// ClaudeSession.swift handshake :203-205.
//
// Test-first proofs for PR-PROVREG-154. The probe is the Claude handshake spawned in the system temporary folder,
// of which only the models are used; the handshake needs a commands array, so a reply without one throws and the
// models are lost with it, the throw swallowed by try? and the attempt left open. The CLI is the compiled Claude
// stand-in (R-TEST-8), reached only through a custom binary path inside this test run's folders (R-TEST-10).
public sealed class ProviderRegistryClaudeCatalogTests
{
    private const string WithCommands =
        "{\"type\":\"control_response\",\"response\":{\"request_id\":\"__REQUEST_ID__\",\"response\":{" +
        "\"models\":[{\"value\":\"default\",\"displayName\":\"Default\"},{\"value\":\"claude-probe-1\",\"displayName\":\"Probe One\"}]," +
        "\"commands\":[\"deploy\"]}}}";

    private const string WithoutCommands =
        "{\"type\":\"control_response\",\"response\":{\"request_id\":\"__REQUEST_ID__\",\"response\":{" +
        "\"models\":[{\"value\":\"default\",\"displayName\":\"Default\"},{\"value\":\"claude-probe-1\",\"displayName\":\"Probe One\"}]}}}";

    private static SwiftURL FileUrl(string windowsPath) => SwiftURL.Create("marrow-native-path:" + windowsPath.Replace('\\', '/'))!;

    private static string Normalised(string folder) => Path.GetFullPath(folder).TrimEnd('\\', '/').ToUpperInvariant();

    [Fact]
    [Trait("Req", "PR-PROVREG-154")] // ProviderRegistry.swift:561-565 (the handshake runs in FileManager.default.temporaryDirectory; only the models are read)
    public async Task the_catalog_probe_runs_the_handshake_in_the_system_temporary_folder_and_returns_only_the_models()
    {
        using var runtime = new TestRuntime();
        var script = new ClaudeScript("catalog-probe");
        script.SetResponse(WithCommands);
        var environment = new ValueMap<string, string>(new Dictionary<string, string> { [script.EnvironmentEntry.Key] = script.EnvironmentEntry.Value });

        var models = await ProviderRegistry.LoadClaudeCatalog(FileUrl(script.ExecutablePath), environment);

        Assert.NotNull(models);
        Assert.Equal(["default", "claude-probe-1"], models.Select(option => option.Id));
        Assert.Equal(["Default", "Probe One"], models.Select(option => option.Name));
        var launch = Assert.Single(script.Calls());
        Assert.Equal("-p", launch.Arguments[0]); // the handshake's own argument vector
        Assert.Equal(Normalised(Path.GetTempPath()), Normalised(script.WorkingDirectory(0)));
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-154")] // ClaudeSession.swift:203-205 via ProviderRegistry.swift:565 (a reply without commands throws; try? makes it nil, so the models are lost too)
    public async Task a_reply_without_a_commands_array_loses_the_models_as_well()
    {
        using var runtime = new TestRuntime();
        var script = new ClaudeScript("catalog-probe-no-commands");
        script.SetResponse(WithoutCommands); // the models are there; the commands array is not
        var environment = new ValueMap<string, string>(new Dictionary<string, string> { [script.EnvironmentEntry.Key] = script.EnvironmentEntry.Value });

        var models = await ProviderRegistry.LoadClaudeCatalog(FileUrl(script.ExecutablePath), environment);

        Assert.Null(models);
        Assert.Single(script.Calls()); // one probe, one guard: nothing splits the models from the commands
    }

    [Fact]
    [Trait("Req", "PR-PROVREG-154")] // ProviderRegistry.swift:484-503 (a nil probe leaves attemptedCatalogs unmarked: retried on the next ask; a delivered list closes the attempt)
    public async Task a_lost_catalog_is_retried_on_the_next_ask_and_a_delivered_one_closes_the_attempt()
    {
        using var runtime = new TestRuntime();
        var script = new ClaudeScript("catalog-probe-retry");
        var settings = new FakeProviderSettings();
        settings.Paths[ProviderKind.Claude] = script.ExecutablePath.Replace('\\', '/');
        var registry = new ProviderRegistry(settings);
        var seed = registry.Models(ProviderKind.Claude);

        script.SetResponse(WithoutCommands);
        await Task.Run(() => runtime.Loop.Run(() => registry.LoadCatalog(ProviderKind.Claude)));
        Assert.Single(script.Calls());
        Assert.Equal(seed, registry.Models(ProviderKind.Claude)); // no models, no message

        script.SetResponse(WithCommands);
        await Task.Run(() => runtime.Loop.Run(() => registry.LoadCatalog(ProviderKind.Claude))); // unforced, yet it probes again
        Assert.Equal(2, script.Calls().Count);
        Assert.Contains("claude-probe-1", registry.Models(ProviderKind.Claude).Select(option => option.Id));

        await Task.Run(() => runtime.Loop.Run(() => registry.LoadCatalog(ProviderKind.Claude))); // delivered: the attempt is closed
        Assert.Equal(2, script.Calls().Count);
    }
}
