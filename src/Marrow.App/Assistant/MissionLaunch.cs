// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.1a, 12.3 points 2 and 3, 15.4; unit AS-5; ED-NEW-dextra-as5-3, -5, -6).
// No Swift twin. The one rule every launch and every turn of a chat goes through: a chat that carries a MissionID (a
// lead, a head, a helper, a reviewer) is sealed (the ask mode, none of the owner's allow rules, hooks or unlisted
// servers) and runs in its agent's Job Object, whatever mode its record or its caller asks for. A chat with no MissionID
// launches exactly as before. ThreadRuntime calls it at its two session launches and at the account-switch launch.
using Marrow.Core.Models;
using Marrow.Services.Assistant;
using Marrow.Services.Providers;

namespace Marrow.App.Assistant;

public static class MissionLaunch
{
    /// <summary>Whether the chat is a mission's: sealed and jobbed at every launch.</summary>
    public static bool IsMissionChat(ChatThread thread) => thread.MissionID is not null;

    /// <summary>
    /// The launch, sealed and in the agent's job when the chat is a mission's. Throws (and nothing starts) for a provider
    /// nth cannot seal, when the agent's job cannot be made, or when <paramref name="refusal"/> names a reason (its mission
    /// is stopping or stopped).
    /// </summary>
    public static SessionConfiguration Apply(ChatThread thread, SessionConfiguration configuration,
        Func<ChatThread, SealedLaunch?> seal, Func<Guid, IAgentJob> job, Func<ChatThread, string?>? refusal = null)
    {
        if (thread.MissionID is not { } mission) return configuration;
        // A stopped or stopping mission starts nothing until its Resume (a retry, a relaunch or a message typed meanwhile).
        if (refusal?.Invoke(thread) is { } refused) throw new ProviderError(new ProviderError.Case.Failed(refused));
        var launch = seal(thread) is { } given && given.MissionId == mission ? given : new SealedLaunch(mission);
        var sealedLaunch = SealedProviderConfig.Seal(configuration, launch);
        IAgentJob agent;
        try { agent = job(thread.Id); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw new ProviderError(new ProviderError.Case.Failed("nth could not give this mission chat a job of its own, so it does not start."));
        }
        if (agent.Native is { IsClosed: true })
            throw new ProviderError(new ProviderError.Case.Failed("This mission chat's job has ended; it starts again on Resume."));
        return sealedLaunch with { AgentJob = agent.Native };
    }

    /// <summary>The mode a turn of the chat runs in: the ask mode for a mission's chat, its own mode otherwise.</summary>
    public static RuntimeMode TurnMode(ChatThread thread) =>
        thread.MissionID is null ? thread.RuntimeMode : SealedProviderConfig.CappedMode;
}
