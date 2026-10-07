// PORT-ONLY: MA-3b-E1 / OD-0115. Checking/opinion heads can return text only; injection/warm-start capability does not grant tool-safe model turns.
using Marrow.Core.Models;
using Marrow.Services.Providers;

namespace Marrow.Services.Port.MultiAccount;

public static class TextOnlyAccountHead
{
    public static bool Supports(ProviderKind service) => service is ProviderKind.Claude or ProviderKind.Deepseek
        or ProviderKind.Meta or ProviderKind.Zai or ProviderKind.OpenRouter or ProviderKind.Local;

    public static async Task<string> RunAsync(ProviderSession session, SessionConfiguration configuration, string prompt,
        Action<string> text, Func<bool> permitted, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!Supports(configuration.Provider)) throw new NotSupportedException("This service has no verified text-only checking or opinion turn.");
        if (!permitted()) throw new InvalidOperationException("Permission to use this account changed.");
        if (configuration.ResumeID is not null || configuration.ResumeAt is not null || configuration.ResumePath is not null
            || configuration.Transcript.Count != 0 || configuration.Hydra is not null || configuration.IsHydraHead
            || configuration.SkillGeneration is not null || configuration.SkillMcpExecutable is not null)
            throw new ArgumentException("A checking or opinion head requires a fresh private configuration.", nameof(configuration));
        // The existing boundary verifies Claude's restricted launch, and disables all tools in API/Local payloads.
        // It is always disposed here; this session is never adopted as the thread's working session.
        await using var standby = await HandshakeStandbySession.StartAsync(session, configuration, ct, permitted);
        ct.ThrowIfCancellationRequested();
        if (!permitted()) throw new InvalidOperationException("Permission to use this account changed.");
        var answer = await standby.TextTurnAsync(prompt, ct, text);
        ct.ThrowIfCancellationRequested();
        if (!permitted() || string.IsNullOrWhiteSpace(answer)) throw new InvalidOperationException("The account did not return an authorized text answer.");
        return answer;
    }
}
