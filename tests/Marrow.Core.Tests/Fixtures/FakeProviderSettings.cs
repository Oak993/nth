using Marrow.Core.Models;

namespace Marrow.Core.Tests.Fixtures;

// Test double for the ProviderSettings interface (Core/Models/ProviderSettings.swift, B2 1fbf81e, :6-12),
// used to prove that a conforming settings object answers the four calls the registry makes; not port code.
public sealed class FakeProviderSettings : ProviderSettings
{
    public bool IsEnabled(ProviderKind provider) => provider == ProviderKind.Claude;

    public string BinaryPath(ProviderKind provider) =>
        provider == ProviderKind.Claude ? "/usr/local/bin/claude" : "";

    public string? LastModel(ProviderKind provider) =>
        provider == ProviderKind.Claude ? "claude-sonnet" : null;

    public string ApiKey(ProviderKind provider) => provider == ProviderKind.Claude ? "sk-test" : "";
}
