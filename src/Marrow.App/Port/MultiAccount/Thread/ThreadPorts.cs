// PORT-ONLY: multi-account steps 3b-5, job 0. The ports the thread's views bind to, handed over together: the account port is made per thread (its id and service), the others serve every thread.
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Thread;

namespace Marrow.App.Port.MultiAccount.Thread;

internal sealed record ThreadPorts(Func<Guid, ProviderKind, IThreadAccountPort?> Account, ICardPort? Cards, ILinkPort? Links, IOpinionsPort? Opinions)
{
    /// <summary>Nothing bound: a view that finds this draws nothing of the multi-account surfaces.</summary>
    public static ThreadPorts None { get; } = new((_, _) => null, null, null, null);
}
