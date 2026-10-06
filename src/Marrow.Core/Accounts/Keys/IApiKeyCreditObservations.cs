namespace Marrow.Core.Accounts.Keys;

// PORT-ONLY OD-0114: a production credit report has an account identity and a time. Unknown stays unknown.
public interface IApiKeyCreditObservations : IApiKeyCredits
{
    DateTimeOffset? ObservedAt(string keyId);
    bool? IsUsable(string keyId);
}
