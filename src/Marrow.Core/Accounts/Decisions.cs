namespace Marrow.Core.Accounts;

/// <summary>
/// The five choices that work one way (NOTES "Rule for every choice"): a Settings value that starts unset; if unset the first in-chat card asks; every card carries one Remember line.
/// </summary>
public enum DecisionKind
{
    /// <summary>When an account reaches its limit: switch / wait.</summary>
    OnLimit,

    /// <summary>When every account of the service is out, move to another service: switch / wait. Never asked inside a pill.</summary>
    OtherService,

    /// <summary>A limit hits mid-reply: finish on the next account / stop.</summary>
    MidReply,

    /// <summary>What the next account is brought: full / Marrow's summary / AI summary.</summary>
    Bring,

    /// <summary>When an earlier account resets: go back / stay.</summary>
    EarlierAccount,
}

/// <summary>Remember line: Just this time / This thread / All threads (default Just this time).</summary>
public enum RememberScope
{
    JustThisTime,
    ThisThread,
    AllThreads,
}

public readonly record struct Choice(DecisionKind Kind, string Value);

public static class DecisionValues
{
    public const string Switch = "switch";
    public const string Wait = "wait";
    public const string Ask = "ask";
    public const string Finish = "finish";
    public const string Stop = "stop";
    public const string Full = "full";
    public const string MarrowSummary = "marrowSummary";
    public const string AiSummary = "aiSummary";
    public const string GoBack = "goBack";
    public const string Stay = "stay";

    /// <summary>The values a thread or the Settings row may hold for a kind. "Ask me" exists only where the owner asked for it (limit, other service, earlier account).</summary>
    public static IReadOnlyList<string> Allowed(DecisionKind kind) => kind switch
    {
        DecisionKind.OnLimit => [Switch, Wait, Ask],
        DecisionKind.OtherService => [Switch, Wait, Ask],
        DecisionKind.MidReply => [Finish, Stop],
        DecisionKind.Bring => [Full, MarrowSummary, AiSummary],
        DecisionKind.EarlierAccount => [GoBack, Stay, Ask],
    };

    public static Bring ToBring(string value) => value switch
    {
        Full => Bring.Full,
        MarrowSummary => Bring.MarrowSummary,
        AiSummary => Bring.AiSummary,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Not a Bring value."),
    };

    /// <summary>Used only when a card was answered without a choice for a kind the switch needs: the engine never stops to ask twice.</summary>
    public static string Fallback(DecisionKind kind) => kind switch
    {
        DecisionKind.OnLimit => Switch,
        DecisionKind.OtherService => Wait,
        DecisionKind.MidReply => Finish,
        DecisionKind.Bring => MarrowSummary,
        DecisionKind.EarlierAccount => Stay,
    };
}

public enum ResolutionSource
{
    Thread,
    Pill,
    Settings,
    None,
}

/// <summary>A resolved choice, or the need to ask. <see cref="FirstTime"/> is true when nothing was chosen yet (the first-time card), false for "Ask me".</summary>
public sealed record Resolution(DecisionKind Kind, string? Value, ResolutionSource Source, bool FirstTime)
{
    public bool MustAsk => Value is null;
}
