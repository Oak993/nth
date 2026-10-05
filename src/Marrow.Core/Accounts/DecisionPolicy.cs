namespace Marrow.Core.Accounts;

/// <summary>Remember scopes and the first-time ask (NOTES "Remember scope", "Rule for every choice"). Precedence: thread, then the pill's own value (earlier account only), then Settings, then ask.</summary>
public sealed class DecisionPolicy(IDecisionStore store)
{
    public Resolution Resolve(Guid threadId, DecisionKind kind, string? pillValue = null)
    {
        if (store.GetThread(threadId, kind) is { } thread) return Of(kind, thread, ResolutionSource.Thread);
        if (kind == DecisionKind.EarlierAccount && pillValue is not null) return Of(kind, pillValue, ResolutionSource.Pill);
        if (store.GetSetting(kind) is { } setting) return Of(kind, setting, ResolutionSource.Settings);
        return new Resolution(kind, null, ResolutionSource.None, FirstTime: true);
    }

    /// <summary>One card, one Remember line, applied to every choice on it. Just this time stores nothing.</summary>
    public void Record(Guid threadId, IEnumerable<Choice> choices, RememberScope scope)
    {
        var list = choices.ToList();
        foreach (var choice in list)
            if (!DecisionValues.Allowed(choice.Kind).Contains(choice.Value, StringComparer.Ordinal))
                throw new ArgumentException($"'{choice.Value}' is not a value of {choice.Kind}.", nameof(choices));
        foreach (var choice in list)
        {
            switch (scope)
            {
                case RememberScope.ThisThread: store.SetThread(threadId, choice.Kind, choice.Value); break;
                case RememberScope.AllThreads: store.SetSetting(choice.Kind, choice.Value); break;
                case RememberScope.JustThisTime: break;
            }
        }
    }

    private static Resolution Of(DecisionKind kind, string value, ResolutionSource source) =>
        value == DecisionValues.Ask ? new Resolution(kind, null, source, FirstTime: false) : new Resolution(kind, value, source, FirstTime: false);
}
