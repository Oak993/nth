using Marrow.Core.Models;
using Marrow.Services.Providers;
using Marrow.Services.Store;
using Marrow.Testing;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY test rig (R-TEST-3, R-TEST-10): one ProviderRegistry whose OpenCode Zen and OpenCode Go binary paths point
// at two DIFFERENT copies of the ACP stand-in, so which provider's executable a probe ran can be told, and a launch
// count per copy says how many probes started. The rig scopes the runtime, the clock and the storage root to the
// creating test's async flow, so it must be built synchronously inside the test method.

/// <summary>A registry whose OpenCode providers launch a scripted ACP agent only.</summary>
public sealed class OpenCodeProbeRig : IDisposable
{
    private readonly IDisposable storage;

    public OpenCodeProbeRig(string? persistedCatalogs = null)
    {
        Runtime = new TestRuntime();
        var root = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException("Set MARROW_PORT_DATA_DIR to a throwaway folder before running the tests (R-TEST-9).");
        var data = Path.Combine(root, "opencode-probe", Guid.NewGuid().ToString("N")).Replace('\\', '/');
        Directory.CreateDirectory(data);
        storage = Storage.Locations.Override(StorageLocations.Create(data, data)); // Environment(Opencode) reads the MCP overrides
        Settings = new FakeProviderSettings();
        Zen = AcpStandIns.CopyFor("zen");
        Go = AcpStandIns.CopyFor("go");
        Settings.Paths[ProviderKind.Opencode] = Zen.ExecutablePath.Replace('\\', '/');
        Settings.Paths[ProviderKind.OpencodeGo] = Go.ExecutablePath.Replace('\\', '/');
        if (persistedCatalogs is not null) Runtime.Defaults.Set("providerModelCatalogs", System.Text.Encoding.UTF8.GetBytes(persistedCatalogs));
        Registry = new ProviderRegistry(Settings);
    }

    public TestRuntime Runtime { get; }

    public FakeProviderSettings Settings { get; }

    /// <summary>The agent OpenCode Zen's binary path names.</summary>
    public AcpScript Zen { get; }

    /// <summary>The agent OpenCode Go's binary path names.</summary>
    public AcpScript Go { get; }

    public ProviderRegistry Registry { get; }

    /// <summary>Total launches across both agents.</summary>
    public int Launches => Zen.Launches + Go.Launches;

    /// <summary>Asks for the provider's catalog and waits for the load to finish (every launch starts at once).</summary>
    public Task Ask(ProviderKind provider, bool force = false)
    {
        AcpStandIns.ResetLaunchGate();
        return Task.Run(() => Runtime.Loop.Run(() => Registry.LoadCatalog(provider, force), TimeSpan.FromSeconds(60)));
    }

    /// <summary>The ids of the provider's catalog rows.</summary>
    public IReadOnlyList<string> Ids(ProviderKind provider) => Registry.Models(provider).Select(option => option.Id).ToList();

    public void Dispose()
    {
        storage.Dispose();
        Runtime.Dispose();
    }
}
