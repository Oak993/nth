// PORT-ONLY, owner-ruled addition (OD-0092; first-release voice typing, VT-16): voice-SPEC V-17.2. The OpenAI key lives in Windows
// Credential Manager through SecretStore (service "Marrow Code", account "voice-openai"; R-SEC-1). It is never written anywhere else and
// never logged: this type has no logging, and nothing it throws or returns about a failure carries the key.
namespace Marrow.Services.Port.Voice.Cloud;

/// <summary>The cloud engine's key: one item in the secret store.</summary>
public sealed class VoiceKeyStore(SecretStore? store = null)
{
    /// <summary>The item's service: the port's data identity (OD-0008 C7).</summary>
    public const string Service = "Marrow Code";

    /// <summary>The item's account.</summary>
    public const string Account = "voice-openai";

    // the store is looked up at each call, so a test's scoped store (SecretStore.Shared.Override) is the one used
    private SecretStore Store => store ?? SecretStore.Shared.Current;

    /// <summary>The stored key; null when there is none or it is blank.</summary>
    public string? Read() => Store.Read(Service, Account) is { } key && key.Trim().Length > 0 ? key : null;

    /// <summary>Stores the key, replacing any (SetReplacing: trimmed; an empty key removes the item). False when the store refused it.</summary>
    public bool Save(string key) => Store.SetReplacing(Service, Account, key);

    /// <summary>Removes the key.</summary>
    public bool Remove() => Store.SetReplacing(Service, Account, "");
}
