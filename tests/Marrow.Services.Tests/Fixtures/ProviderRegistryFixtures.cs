using Marrow.Core.Models;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>A minimal in-memory ProviderSettings for ProviderRegistryTests (R-TEST-3: helpers live in Fixtures/).</summary>
public sealed class FakeProviderSettings : ProviderSettings
{
    public Dictionary<ProviderKind, string> Keys { get; } = new();
    public Dictionary<ProviderKind, string> Paths { get; } = new();
    public HashSet<ProviderKind> Enabled { get; } = new();

    public bool IsEnabled(ProviderKind provider) => Enabled.Contains(provider);
    public string BinaryPath(ProviderKind provider) => Paths.TryGetValue(provider, out var p) ? p : "";
    public string? LastModel(ProviderKind provider) => null;
    public string ApiKey(ProviderKind provider) => Keys.TryGetValue(provider, out var k) ? k : "";
}
