namespace Marrow.Core.Accounts;

/// <summary>The in-memory <see cref="IDecisionStore"/> the units use until Settings and the thread store are joined.</summary>
public sealed class InMemoryDecisionStore : IDecisionStore
{
    private readonly Dictionary<DecisionKind, string> settings = [];
    private readonly Dictionary<(Guid Thread, DecisionKind Kind), string> threads = [];

    public string? GetSetting(DecisionKind kind) => settings.GetValueOrDefault(kind);

    public void SetSetting(DecisionKind kind, string? value)
    {
        if (value is null) settings.Remove(kind);
        else settings[kind] = value;
    }

    public string? GetThread(Guid threadId, DecisionKind kind) => threads.GetValueOrDefault((threadId, kind));

    public void SetThread(Guid threadId, DecisionKind kind, string? value)
    {
        if (value is null) threads.Remove((threadId, kind));
        else threads[(threadId, kind)] = value;
    }

    public void ClearThread(Guid threadId)
    {
        foreach (var key in threads.Keys.Where(k => k.Thread == threadId).ToList()) threads.Remove(key);
    }
}
