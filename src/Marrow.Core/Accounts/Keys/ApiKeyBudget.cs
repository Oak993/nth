// PORT-ONLY: RR-50 / ED-461. A request reserves a known ceiling before dispatch; uncertain usage keeps its durable hold.
namespace Marrow.Core.Accounts.Keys;

public interface IApiKeyBudget
{
    bool RequiresPrice(string keyId);
    IApiKeyReservation? Reserve(string keyId, double? maximumUsd);
}

public interface IApiKeyReservation
{
    bool CanDispatch { get; }
    void Complete(double? actualUsd);
    void CancelBeforeDispatch();
}

public interface IApiKeyReservedSpend : IApiKeySpend
{
    // Includes uncertainty, unlike displayed/known spend. An unknown receipt blocks every finite USD cap.
    double BudgetSince(string keyId, DateTimeOffset since);
    void Hold(Guid request, string keyId, double? maximumUsd, DateTimeOffset at);
    bool HasUnsettled(string keyId);
    void Settle(Guid request, double actualUsd);
    void Cancel(Guid request);
}
