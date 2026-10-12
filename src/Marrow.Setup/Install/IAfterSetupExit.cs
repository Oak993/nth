namespace Marrow.Setup.Install;

/// <summary>
/// The setup's own work once Velopack's <c>Setup.exe</c> has exited 0 (nth installer PLAN.md 3, step 8): re-point the
/// uninstall entry to <c>&lt;root&gt;\nth Uninstall.exe</c>, re-stamp the Start shortcut's AppUserModelID if Velopack wrote a
/// different one, and record that in <c>install-result.json</c>. Unit U5 builds it (<c>Support/AfterSetupExit.Run</c> with the
/// real shell links and HKCU); the progress engine only calls it, on the window's thread (the shell links are STA COM),
/// and lets the bar reach 100 only after it has returned. It must not throw.
/// </summary>
public interface IAfterSetupExit
{
    void Run(string root, string resultPath);
}
