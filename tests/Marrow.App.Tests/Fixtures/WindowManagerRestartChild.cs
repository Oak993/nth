using System.Diagnostics;
using P = Marrow.App.Tests.Fixtures.WindowManagerProcessProtocol;
using R = Marrow.App.Tests.Fixtures.WindowManagerRestartProtocol;

namespace Marrow.App.Tests.Fixtures;

// Unlike the separate shell-exit proof, successful restart phases return normally to xUnit.
// The parent owns the external process deadline and the job cleanup even if this UI body hangs.
internal static class WindowManagerRestartChild
{
    internal static void Run(string token, string factName)
    {
        var identity = R.ValidateChild(token, factName, out var directory);
        try
        {
            using var parent = Process.GetProcessById(identity.Request.Root.ParentProcessId);
            _ = parent.SafeHandle;
            Assert.Equal(identity.Request.Root.ParentStartUtcTicks, parent.StartTime.ToUniversalTime().Ticks);
            P.Write(directory, "ready.json", identity);
            P.Until(() =>
            {
                if (parent.HasExited) throw new InvalidOperationException("The restart parent exited before granting this phase.");
                return File.Exists(Path.Combine(directory, "permit.json"));
            }, P.StartupBound, "The restart parent did not permit this identified phase.");
            var permit = P.Read<WindowManagerRestartPermit>(directory, "permit.json");
            Assert.Equal(identity, permit.Identity);
            P.Log(directory, "phase-authorized");
            WindowManagerRestartResult result = null!;
            using (var headless = new HeadlessSession())
                headless.OnUiThread(() => result = WindowManagerRestartScenario.Run(identity, directory));
            P.Log(directory, "headless-disposed");
            Assert.Equal(result.OutputHash, R.Hash(File.ReadAllBytes(result.StorePath)));
            P.Log(directory, "phase-returning");
            Assert.Equal(WindowManagerRestartExpectations.Events(identity.Request.Phase),
                File.ReadAllLines(Path.Combine(directory, "child.log")));
            P.Write(directory, "phase-result.json", result);
        }
        catch (Exception error)
        {
            try { P.Write(directory, "failure.json", new { Identity = identity, Error = error.ToString() }); }
            catch (Exception evidenceError) { throw new AggregateException(error, evidenceError); }
            throw; // the exact child Fact must fail normally and produce its real failing TRX
        }
    }
}
