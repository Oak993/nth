using Marrow.Setup.Install;
using Marrow.Setup.Support;
using Marrow.Setup.Text;

namespace Marrow.Setup.Port;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3 step 8 and 8, item 1; units U3 and U5, bound at integration): the setup's own work once
/// Velopack's <c>Setup.exe</c> has exited 0, on the real shell links and the real HKCU (<see cref="Program.FinishAfterSetupExit"/>).
/// The progress engine calls it on the window's thread, as the shell links are STA COM. ED-NEW-inst-c7-2 and
/// ED-NEW-inst-merge-12: it returns only when the required steps are done (<see cref="AfterSetupExitReport.RequiredStepsDone"/>)
/// and otherwise throws with Problem's plain sentence, so the bar never reaches 100 while the Apps entry still points at
/// Update.exe; Retry runs the install again.
/// </summary>
public sealed class ProductionAfterSetupExit(Func<string, string?, AfterSetupExitReport?>? finish = null) : IAfterSetupExit
{
    private readonly Func<string, string?, AfterSetupExitReport?> finish = finish ?? Program.FinishAfterSetupExit;

    public void Run(string root, string resultPath)
    {
        if (finish(root, resultPath) is not { RequiredStepsDone: true }) throw new InvalidOperationException(SetupStrings.ProblemOther);
    }
}
