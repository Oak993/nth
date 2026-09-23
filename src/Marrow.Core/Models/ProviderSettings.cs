namespace Marrow.Core.Models;

// Source: DroppyCode/Core/Models/ProviderSettings.swift (B2 1fbf81e), :1-12.
//
// What the provider registry needs from the settings: which providers are switched on, where their
// binaries and keys are, and the model each last ran. The app's settings object conforms; the registry
// never sees the rest of it (:3-5).
//
// R-PROTO-1: a Swift protocol ports to an interface. R-ISO-1: the Swift @MainActor annotation (:6) means
// every member below is only ever called on the main actor; there is no per-call MainActor check here
// (an @MainActor class does not re-check per call), matching how MainActor.cs documents the rule.
public interface ProviderSettings // :7
{
    // :8
    bool IsEnabled(ProviderKind provider);

    // :9
    string BinaryPath(ProviderKind provider);

    // :10
    string? LastModel(ProviderKind provider);

    // :11
    string ApiKey(ProviderKind provider);
}
