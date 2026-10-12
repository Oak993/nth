using System.Diagnostics;
using System.Security;
using Avalonia.Threading;
using Marrow.Core.Support;
using Marrow.Setup.Flow;
using Marrow.Setup.Platform;
using Marrow.Setup.Port;
using Marrow.Setup.Screens;
using Marrow.Setup.Shell;
using Marrow.Setup.Smoke;
using Marrow.Setup.Text;

namespace Marrow.Setup;

/// <summary>
/// PORT-ONLY (OD-0072, ED-247; nth installer PLAN.md 3 and 10, unit U1, ED-NEW-inst-u1-1): the installer the owner shares.
/// <c>--silent</c> and <c>--help</c> go to the unchanged gate (<see cref="LegacyGateHost"/>): no window. <c>--ui-smoke</c>
/// runs the window's smoke checks and exits (it cannot install). Otherwise one setup window per user session: a second
/// start brings the first to the front and exits 0. The window's first screen comes from what is installed
/// (<see cref="SetupFlow"/>), and its own signature is checked while it shows.
/// </summary>
public static class Program
{
    private static readonly TimeSpan FocusPatience = TimeSpan.FromSeconds(3);

    [STAThread]
    public static int Main(string[] args)
    {
        var started = Stopwatch.GetTimestamp();
        if (InstallerGate.IsSilent(args) || InstallerGate.IsHelp(args)) return LegacyGateHost.Run(args);

        var smoke = UiSmoke.Read(args);
        SingleInstance? instance = null;
        if (smoke is null)
        {
            instance = SingleInstance.TryAcquire();
            if (instance is null)
            {
                SingleInstance.FocusExisting(new WindowsFocuser(), FocusPatience);
                return 0;
            }
        }
        using (instance)
        {
            var (exitCode, installChosen) = RunWindow(args, instance, smoke, started);
            return installChosen ? LegacyGateHost.Run(args) : exitCode; // STUB(U3/U4): the progress engine replaces the gate's run
        }
    }

    private static (int ExitCode, bool InstallChosen) RunWindow(string[] args, SingleInstance? instance, UiSmokeOptions? smoke, long started)
    {
        SetupApp.Build().SetupWithoutStarting();
        var settings = new SystemSettings(new WindowsSystemLookSource());
        var window = new SetupWindow(settings);
        var version = LegacyGateHost.Metadata("InstallerVersion");
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string? problem = null;
        SetupRoute route;
        try
        {
            route = SetupFlow.Route(version, LegacyGateHost.Metadata("InstallerAppId"), args, AppContext.BaseDirectory, localAppData, new RegistryUninstallKeys());
        }
        catch (Exception error) when (error is FormatException or IOException or UnauthorizedAccessException or SecurityException)
        {
            route = SetupFlow.Problem(localAppData);
            problem = SetupStrings.ProblemOther;
        }
        var session = new SetupSession(window, route, new InterimScreens(), version, canInstall: smoke is null);
        if (problem is not null) session.Fail(problem);
        window.HandleReady += handle => instance?.Publish(handle, Environment.ProcessId);
        session.Start();

        using var done = new CancellationTokenSource();
        window.Closed += (_, _) => done.Cancel();
        if (smoke is not null) window.Opened += (_, _) => _ = UiSmoke.RunAsync(session, smoke, started);
        else _ = CheckSignatureAsync(session, done.Token);
        window.Show();
        Dispatcher.UIThread.MainLoop(done.Token);
        return (session.ExitCode, session.InstallChosen);
    }

    /// <summary>The self-signature check hashes the whole file, so it runs off the UI thread after the window shows.</summary>
    private static async Task CheckSignatureAsync(SetupSession session, CancellationToken closed)
    {
        try
        {
            var result = await SignatureCheck.WaitForAnswerAsync(SignatureCheck.VerifySelfAsync(), TimeProvider.System, closed).ConfigureAwait(false);
            if (closed.IsCancellationRequested) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (closed.IsCancellationRequested) return;
                if (result is null) session.SignatureCheckUnavailable();
                else session.SignatureChecked(result);
            });
        }
        catch (OperationCanceledException) when (closed.IsCancellationRequested) { }
    }
}
