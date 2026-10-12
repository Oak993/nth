using Marrow.Setup.Install;

namespace Marrow.Setup.Port;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3 step 8 and 8, item 1; units U3 and U5, bound at integration): the setup's own work once
/// Velopack's <c>Setup.exe</c> has exited 0, on the real shell links and the real HKCU (<see cref="Program.FinishAfterSetupExit"/>).
/// The progress engine calls it on the window's thread, as the shell links are STA COM. It never throws: Setup.exe succeeded,
/// and nth's start-time self-heal repeats these steps.
/// </summary>
public sealed class ProductionAfterSetupExit : IAfterSetupExit
{
    public void Run(string root, string resultPath) => Program.FinishAfterSetupExit(root, resultPath);
}
