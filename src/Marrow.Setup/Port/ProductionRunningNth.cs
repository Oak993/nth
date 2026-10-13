using Marrow.Setup.Support;

namespace Marrow.Setup.Port;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3a, unit U4, ED-NEW-inst-u4-1). The running-nth handshake wired to the real thing: nth's control pipe of
/// the signed-in user, and the Windows process list. The screens and the silent path (units U2, U3) make theirs here, one per attempt
/// (Install, Retry, Update), with the install root they are about to replace files in.
/// </summary>
public static class ProductionRunningNth
{
    /// <param name="installRoot">The Velopack install root: an existing install's root, or the folder a fresh install goes to.</param>
    public static IRunningNth Create(string installRoot) =>
        new RunningNthHandshake(
            installRoot,
            PipeNthControlChannel.ForCurrentUser(installRoot: installRoot) ?? throw new PlatformNotSupportedException("nth Setup runs on Windows."),
            new WindowsRunningNthProcesses());
}
