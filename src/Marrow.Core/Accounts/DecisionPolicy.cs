namespace Marrow.Core.Accounts;

/// <summary>Remember scopes and the first-time ask (NOTES "Remember scope", "Rule for every choice"). Precedence: thread, then the pill's own value (earlier account only), then Settings, then ask.</summary>
public sealed class DecisionPolicy(IDecisionStore store)
{
    public Resolution Resolve(Guid threadId, DecisionKind kind, string? pillValue = null) => throw new NotImplementedException();

    /// <summary>One card, one Remember line, applied to every choice on it. Just this time stores nothing.</summary>
    public void Record(Guid threadId, IEnumerable<Choice> choices, RememberScope scope) => throw new NotImplementedException();
}
