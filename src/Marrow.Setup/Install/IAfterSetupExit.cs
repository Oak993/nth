using Marrow.Setup.Text;

namespace Marrow.Setup.Install;

/// <summary>
/// The setup's required work after engine exit zero: re-point the uninstall entry, re-stamp Start's AppUserModelID,
/// and record the result (PLAN 3 step 8). Runs on the window thread for STA shell links. ED-NEW-inst-c7-2:
/// return only when required steps succeeded; throw with the plain failure message otherwise. Returning certifies 100%.
/// </summary>
public interface IAfterSetupExit
{
    void Run(string root, string resultPath);
}

/// <summary>STUB(U5): an unbound integration cannot certify successful post-exit work.</summary>
public sealed class NoAfterSetupExit : IAfterSetupExit
{
    public void Run(string root, string resultPath) => throw new InvalidOperationException(SetupStrings.ProblemOther);
}
