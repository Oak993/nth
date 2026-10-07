// PORT-ONLY: multi-account steps 3b-5, job 0. The ports the thread's views bind to, handed over together: the account port is made per thread (its id and service), the others serve every thread.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.MultiAccount;
using Marrow.Core.Port.MultiAccount.Link;
using Marrow.Core.Port.MultiAccount.Opinions;
using Marrow.Core.Port.MultiAccount.Thread;

namespace Marrow.App.Port.MultiAccount.Thread;

internal sealed record ThreadPorts(Func<Guid, ProviderKind, IThreadAccountPort?> Account, ICardPort? Cards, ILinkPort? Links, IOpinionsPort? Opinions)
{
    /// <summary>Nothing bound: a view that finds this draws nothing of the multi-account surfaces.</summary>
    public static ThreadPorts None { get; } = new((_, _) => null, null, null, null);

    // RR-49 / ED-598: W binds raw ports for primary routing. Only these projections
    // feed private checking/opinion models; the generic models retain their supplied-head contract.
    internal AskOthersModel? CreateOpinionsModel(Guid thread, ProviderKind source, OtherServiceConsentStore consent,
        Func<ProviderKind, IReadOnlyList<ModelOption>>? models = null)
    {
        if (Opinions is null || Account(thread, source) is not { } current) return null;
        var ports = Heads(thread, current, opinions: true);
        return new(thread, ports.Single(port => port.Service == source), ports, Opinions, consent, models);
    }

    internal LinkUpModel? CreateLinkModel(Guid thread, ProviderKind source, OtherServiceConsentStore consent)
    {
        if (Links is null || Account(thread, source) is not { } current) return null;
        var ports = Heads(thread, current, opinions: false);
        return new(thread, ports.Single(port => port.Service == source), ports, Links, consent);
    }

    private IReadOnlyList<IThreadAccountPort> Heads(Guid thread, IThreadAccountPort current, bool opinions)
    {
        // E1 privately launches the primary for opinion Answer/Combine. An unverified or missing
        // primary must therefore yield no opinion targets. Checking only reads its completed answer.
        bool SourceSafe() => current.Accounts.Any(row => row.Id == current.ActiveAccountId
            && ConversationHeadCapabilities.Supports(row.Service));
        return SwiftEnum<ProviderKind>.AllCases.Select(service => Account(thread, service)).OfType<IThreadAccountPort>()
            .Append(current).Distinct().Select(port => (IThreadAccountPort)new ConversationHeadAccountPort(port, row =>
                (!opinions || SourceSafe()) && (ConversationHeadCapabilities.Supports(row.Service)
                    || !opinions && row.Id == current.ActiveAccountId))).ToArray();
    }
}
