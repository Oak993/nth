using System.Diagnostics;
using System.Reflection;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Window.Native;

// Reuse the process proof's job ownership/cleanup API. The native wrapper only extends the
// launcher's gate across Run and job retention so its watcher cannot close a fast-exiting
// runner's job between those steps. No process fixture or production code is changed.
internal sealed class WindowManagerNativeOwnedJob : IDisposable
{
    private readonly WindowManagerProcessOwnedJob job;

    internal WindowManagerNativeOwnedJob(ProcessLauncher launcher)
    {
        var type = typeof(ProcessLauncher);
        var gate = type.GetField("gate", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(launcher)
            ?? throw new MissingFieldException(type.FullName, "gate");
        lock (gate)
        {
            launcher.Run();
            try
            {
                job = new WindowManagerProcessOwnedJob(launcher);
            }
            catch
            {
                // The watcher still cannot release the launcher's job while this gate is held.
                launcher.KillTree();
                throw;
            }
        }
    }

    internal void RequireMember(Process candidate) => job.RequireMember(candidate);
    internal uint ActiveProcesses => job.ActiveProcesses;
    public void Dispose() => job.Dispose();
}
