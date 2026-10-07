// PORT-ONLY: OD-0114, OD-0116; ED-403. Contains preferences, never credentials or conversation content.
using Marrow.Core.Accounts;

namespace Marrow.Core.Port.MultiAccount;

internal sealed record AccountDecisionDocument
{
    public int Version { get; init; } = 1;
    public Dictionary<DecisionKind, string> Settings { get; init; } = [];
    public Dictionary<Guid, Dictionary<DecisionKind, string>> Threads { get; init; } = [];
}
