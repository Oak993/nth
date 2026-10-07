using Marrow.Core.Models;

namespace Marrow.Services.Providers;

// PORT-ONLY (OD-0114, MA-6; no Swift twin). Where the secrets of several API keys for one provider live: the same Credential Manager through
// APIKeychain. The first key keeps the account older builds used ("DeepSeek API Key"), so one key looks exactly like today and an upgrade
// loses nothing; each further key is "<that account> [<key id>]". A provider with no APIKeychain account has no secrets here (reads give
// null, writes refuse): the OpenAI-compatible endpoints join when MA-1's store names their accounts.
public sealed class KeychainApiKeySecrets(ProviderKind provider, string primaryKeyId)
{
    public string? Account(string keyId) =>
        APIKeychain.PrimaryAccount(provider) is not { } account ? null : keyId == primaryKeyId ? account : $"{account} [{keyId}]";

    public string? Read(string keyId) => Account(keyId) is { } account ? APIKeychain.Read(account) : null;

    /// <summary>Stores the secret, or removes it for an empty one. False when the provider has no account or the write is refused.</summary>
    public bool Write(string keyId, string secret) => Account(keyId) is { } account && APIKeychain.Set(secret, account);

    public void Delete(string keyId)
    {
        if (Account(keyId) is { } account) APIKeychain.Delete(account);
    }
}
