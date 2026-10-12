using E = Marrow.App.Tests.Fixtures.WindowManagerRestartExpectations;
using P = Marrow.App.Tests.Fixtures.WindowManagerProcessProtocol;
using R = Marrow.App.Tests.Fixtures.WindowManagerRestartProtocol;

namespace Marrow.App.Tests.Fixtures;

// The parent initializes no Avalonia platform. Each newly launched testhost selects this exact
// existing-DLL Fact, runs its internally permitted phase and returns normally to the test runner.
internal static class WindowManagerRestartProof
{
    internal static void Execute(string factName)
    {
        var token = Environment.GetEnvironmentVariable(P.ChildVariable);
        var phase = Environment.GetEnvironmentVariable(R.PhaseVariable);
        if (!string.IsNullOrEmpty(token) || !string.IsNullOrEmpty(phase))
        {
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(phase))
                throw new InvalidOperationException("A restart child needs the internally paired nonce and phase.");
            WindowManagerRestartChild.Run(token, factName);
            return;
        }
        var root = R.CreateRoot(factName);
        Console.WriteLine("WindowManager restart evidence: " + root.Directory);
        WindowManagerRestartCompletion? firstResult = null;
        WindowManagerRestartCompletion? secondResult = null;
        try
        {
            var firstRequest = R.CreatePhase(root, 1);
            using var first = new WindowManagerRestartPhase(firstRequest);
            firstResult = first.Run();
            Assert.True(first.ChildHasExited);
            var persisted = File.ReadAllBytes(R.StorePath(root));
            E.AssertSerializedFrame(persisted);
            Assert.Equal(firstResult.Result.OutputHash, R.Hash(persisted));
            // Created only after phase one's child, runner and complete owned job have ended
            // naturally, and its exact Fact/TRX and disk evidence have all been verified.
            P.Write(root.Directory, "phase-1-complete.json", firstResult);

            var secondRequest = R.CreatePhase(root, 2, firstResult.Result.Identity.Process.ChildProcessId,
                firstResult.Result.OutputHash);
            using var second = new WindowManagerRestartPhase(secondRequest);
            secondResult = second.Run();
            Assert.True(first.ChildHasExited);
            Assert.True(second.ChildHasExited);
            Assert.NotEqual(firstResult.Result.Identity.Process.ChildProcessId, secondResult.Result.Identity.Process.ChildProcessId);
            Assert.NotEqual(firstRequest.Token, secondRequest.Token);
            Assert.Equal(firstResult.Result.StorePath, secondResult.Result.StorePath);
            Assert.Equal(firstResult.Result.OutputHash, secondResult.Result.InputHash);
            Assert.Equal(firstResult.Result.OutputHash, secondResult.Result.OutputHash);
            Assert.Equal(persisted, File.ReadAllBytes(R.StorePath(root)));
            Assert.Equal(E.SavedMain, firstResult.Result.Main);
            Assert.Equal(E.SavedMain, secondResult.Result.Main);
            // OD-0151: Settings opens as the panel inside the main window in both processes, so it is where the restored main frame is.
            Assert.Equal(E.SavedMain, firstResult.Result.Settings);
            Assert.Equal(E.SavedMain, secondResult.Result.Settings);
            P.Write(root.Directory, "restart-result.json", new { Root = root, First = firstResult, Second = secondResult });
        }
        catch (Exception error)
        {
            P.Write(root.Directory, "restart-failure.json", new
            {
                Root = root, First = firstResult, Second = secondResult, Error = error.ToString(),
            });
            throw new InvalidOperationException("WindowManager restart proof failed; evidence: " + root.Directory, error);
        }
    }
}
