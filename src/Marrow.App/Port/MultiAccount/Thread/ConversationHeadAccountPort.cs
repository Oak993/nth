// PORT-ONLY: RR-49 / ED-NEW-ma-gaps-5. Head-only projection over W's live primary account ports.
using Marrow.Core.Accounts;
using Marrow.Core.Models;
using Marrow.Core.Port.MultiAccount.Thread;

namespace Marrow.App.Port.MultiAccount.Thread;

internal sealed class ConversationHeadAccountPort(IThreadAccountPort source, Func<ThreadAccount, bool> offered) : IThreadAccountPort
{
    public event Action? Changed { add => source.Changed += value; remove => source.Changed -= value; }
    public ProviderKind Service => source.Service;
    public IReadOnlyList<ThreadAccount> Accounts => source.Accounts.Where(offered).ToArray();
    public Guid ActiveAccountId => source.ActiveAccountId;
    public Guid? PinnedAccountId => source.PinnedAccountId;
    public IReadOnlyList<Choice> ThreadChoices => source.ThreadChoices;
    public Task<PinResult> PinAsync(Guid account, Bring? bring = null, CancellationToken ct = default) => source.PinAsync(account, bring, ct);
    public void BackToOrder() => source.BackToOrder();
    public void UseSettings() => source.UseSettings();
}
