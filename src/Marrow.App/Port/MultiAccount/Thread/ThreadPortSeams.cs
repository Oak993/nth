// PORT-ONLY: multi-account steps 3b-5, job 0. Where the popover, the cards, link up and "Ask others" get their ports: unbound (none) until the wiring job (W) binds the running app, so a test of another view never builds the app's accounts
// (the pattern of ComposerCapsuleSeams). A test overrides it with the fakes of Marrow.Testing.
using Marrow.Core.Port;

namespace Marrow.App.Port.MultiAccount.Thread;

internal static class ThreadPortSeams
{
    public static Ambient<ThreadPorts> Offer { get; } = new(ThreadPorts.None);
}
