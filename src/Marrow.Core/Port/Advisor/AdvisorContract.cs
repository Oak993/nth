using Marrow.Core.Models;

namespace Marrow.Core.Port.Advisor;

/// <summary>The model a chat asks for advice (ED-NEW-ignisadvisor-1).</summary>
public sealed record AdvisorChoice(ProviderKind Provider, string Model);

/// <summary>One answer from the advisor and what it cost.</summary>
public sealed record AdvisorAdvice(string Text, AdvisorChoice By, long InputTokens, long OutputTokens, long CachedInputTokens, double? Usd);

/// <summary>Seams the Ignis side sets and the advisor host reads.</summary>
public static class AdvisorSeams
{
    /// <summary>The advisor for a chat (null: off for that chat). Set by the app.</summary>
    public static Func<Guid?, ProviderKind, string, AdvisorChoice?> Choice { get; set; } = (_, _, _) => null;

    /// <summary>Called after each piece of advice (the ledger and the chat line). Set by the app.</summary>
    public static Action<Guid?, AdvisorAdvice>? Asked { get; set; }
}
