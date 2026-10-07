// PORT-ONLY: OD-0114, OD-0116; ED-403. Selecting a pill consents to that sequence only.
using Marrow.Core.Accounts;

namespace Marrow.Core.Port.MultiAccount;

public sealed class PillDecisionStore(IDecisionStore stored, Guid threadId, Func<Pill?> pill) : IDecisionStore
{
    public string? GetSetting(DecisionKind kind) => stored.GetSetting(kind);
    public void SetSetting(DecisionKind kind, string? value) => stored.SetSetting(kind, value);
    public string? GetThread(Guid id, DecisionKind kind)
    {
        if (stored.GetThread(id, kind) is { } chosen) return chosen;
        if (id != threadId || pill() is null) return null;
        if (kind == DecisionKind.OnLimit) return DecisionValues.Switch;
        if (stored.GetSetting(kind) is not null) return null;
        return kind switch
        {
            DecisionKind.Bring => DecisionValues.MarrowSummary,
            DecisionKind.MidReply => DecisionValues.Finish,
            _ => null,
        };
    }
    public void SetThread(Guid id, DecisionKind kind, string? value) => stored.SetThread(id, kind, value);
    public void ClearThread(Guid id) => stored.ClearThread(id);
}
