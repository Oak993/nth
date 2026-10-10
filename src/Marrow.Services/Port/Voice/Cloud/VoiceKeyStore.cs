// PORT-ONLY, owner-ruled addition (OD-0092; first-release voice typing, VT-16): voice-SPEC V-17.2. The OpenAI key lives in Windows
// Credential Manager through SecretStore (service "Nth", formerly "Marrow Code" and carried over once by OD-0135; account "voice-openai"; R-SEC-1). It is never written anywhere else and
// never logged: this type has no logging, and nothing it throws or returns about a failure carries the key.
namespace Marrow.Services.Port.Voice.Cloud;

/// <summary>The cloud engine's key: one item in the secret store.</summary>
public sealed class VoiceKeyStore(SecretStore? store = null)
{
    /// <summary>The item's service: the port's data identity (OD-0008 C7), "Nth" since OD-0135.</summary>
    public const string Service = Marrow.Core.Support.AppInfo.DataFolderName;

    /// <summary>The item's service before OD-0135, read only to carry the key over once.</summary>
    public const string LegacyService = Marrow.Core.Support.AppInfo.LegacyDataFolderName;

    /// <summary>The item's account.</summary>
    public const string Account = "voice-openai";

    // the store is looked up at each call, so a test's scoped store (SecretStore.Shared.Override) is the one used
    private SecretStore Store => store ?? SecretStore.Shared.Current;

    /// <summary>The stored key; null when there is none or it is blank.</summary>
    public string? Read() => Store.ReadCarryingOver(Service, LegacyService, Account) is { } key && key.Trim().Length > 0 ? key : null;

    /// <summary>Stores the key, replacing any (SetReplacing: trimmed; an empty key removes the item). False when the store refused it.</summary>
    public bool Save(string key)
    {
        Store.SupersedeLegacy(Service, LegacyService, Account); // OD-0135: a key set here is never replaced by the old one
        return Store.SetReplacing(Service, Account, key);
    }

    /// <summary>Removes the key.</summary>
    public bool Remove()
    {
        Store.SupersedeLegacy(Service, LegacyService, Account); // OD-0135: a removed key does not come back from the old name
        return Store.SetReplacing(Service, Account, "");
    }
}
