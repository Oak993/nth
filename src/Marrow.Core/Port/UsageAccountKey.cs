using Marrow.Core.Models;

namespace Marrow.Core.Port;

// PORT-ONLY: OD-0114 / ED-406; quota and limit-event identity until the account store supplies its IDs.
// AccountId is a stable, non-secret store ID, never an email, token or path. Provider remains part of the key.
public sealed record UsageAccountKey
{
    public ProviderKind Provider { get; }
    public string AccountId { get; }
    public bool IsDefault => string.Equals(AccountId, "default", StringComparison.Ordinal);

    public UsageAccountKey(ProviderKind provider, string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        Provider = provider;
        AccountId = accountId;
    }

    public static UsageAccountKey Default(ProviderKind provider) => new(provider, "default");
}
