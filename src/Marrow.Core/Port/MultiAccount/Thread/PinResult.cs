// PORT-ONLY: multi-account steps 3b-5, job 0. What pinning a thread to an account came to; a refusal says why and changes nothing (AccountStore.Choose: a disabled pin is refused, never silently replaced).
namespace Marrow.Core.Port.MultiAccount.Thread;

public enum PinResult
{
    Pinned,

    /// <summary>Not an account of this thread's service, or not signed in.</summary>
    UnknownAccount,

    /// <summary>The account is switched off in Settings.</summary>
    Disabled,

    /// <summary>The service reported the account's window used up.</summary>
    OutOfService,
}
