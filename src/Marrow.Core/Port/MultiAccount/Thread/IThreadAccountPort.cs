// PORT-ONLY: multi-account steps 3b-5, job 0. What the thread's account popover and the composer bind to for ONE thread, instead of the not-yet-ready engine types. The app's real binding (job W) reads AccountStore, the usage the service reported, the thread's
// pin (AppModel.TryPinThreadAccountAsync) and the thread's decision values; tests bind FakeThreadAccountPort.
using Marrow.Core.Accounts;
using Marrow.Core.Models;

namespace Marrow.Core.Port.MultiAccount.Thread;

public interface IThreadAccountPort
{
    /// <summary>Raised after anything below changed (the order, the usage, the pin, what the thread remembers).</summary>
    event Action? Changed;

    ProviderKind Service { get; }

    /// <summary>The service's signed-in accounts in the order Marrow uses them, row 1 first.</summary>
    IReadOnlyList<ThreadAccount> Accounts { get; }

    /// <summary>The account the thread runs on now.</summary>
    Guid ActiveAccountId { get; }

    /// <summary>The account the user pinned the thread to by hand, or null: Marrow follows the order.</summary>
    Guid? PinnedAccountId { get; }

    /// <summary>What this thread remembers (its own Remember choices); empty when it follows Settings.</summary>
    IReadOnlyList<Choice> ThreadChoices { get; }

    /// <summary>Moves the thread to the account and keeps it there. <paramref name="bring"/> is what the account is brought; null takes the thread's or Settings' choice.</summary>
    Task<PinResult> PinAsync(Guid accountId, Bring? bring = null, CancellationToken ct = default);

    /// <summary>"Back to order": drops the pin, so Marrow follows the order again.</summary>
    void BackToOrder();

    /// <summary>"Use Settings": the thread forgets what it remembers.</summary>
    void UseSettings();
}
