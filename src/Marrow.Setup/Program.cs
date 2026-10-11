using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Marrow.App.Port;
using Marrow.Core.Support;
using Marrow.Setup.Support;
using Microsoft.Win32;

namespace Marrow.Setup;

/// <summary>
/// PORT-ONLY (OD-0072, ED-247): the installer the owner shares. It checks what is installed (InstallerGate, unit tested
/// in Marrow.Core.Tests) and only for a new install or an upgrade copies the embedded Velopack Setup.exe to a temporary
/// folder, runs it with the same arguments, waits, deletes the copy and returns its exit code. The same version or a
/// newer one installed: a message box, nothing run, nothing written.
/// </summary>
public static class Program
{
    private const string PayloadName = "VelopackSetup.exe";

    [STAThread]
    public static int Main(string[] args)
    {
        var host = new Win32InstallerHost();
        try
        {
            host.InstallRoot = Identity().DefaultInstallRoot;
            return InstallerGate.Run(host, Identity(), args);
        }
        catch (Exception error)
        {
            if (!InstallerGate.IsSilent(args)) host.ShowMessage(InstallerGate.Caption(Metadata("InstallerTitle")), error.Message, warning: true);
            return 1;
        }
    }

    private static InstallerIdentity Identity()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Metadata("InstallerAppId"));
        return new InstallerIdentity(Metadata("InstallerTitle"), Metadata("InstallerVersion"), root, AppContext.BaseDirectory);
    }

    private static string Metadata(string key) =>
        typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value ?? "";

    private sealed class Win32InstallerHost : IInstallerGateHost
    {
        private const uint MbOk = 0x0, MbIconWarning = 0x30, MbIconInformation = 0x40, MbSetForeground = 0x10000;

        /// <summary>The root Setup.exe installs into; the post-exit steps run there.</summary>
        public string? InstallRoot { get; set; }

        public void ShowMessage(string caption, string text, bool warning) =>
            _ = MessageBoxW(IntPtr.Zero, text, caption, MbOk | MbSetForeground | (warning ? MbIconWarning : MbIconInformation));

        public int RunVelopackSetup(IReadOnlyList<string> arguments)
        {
            using var payload = typeof(Program).Assembly.GetManifestResourceStream(PayloadName)
                ?? throw new InvalidOperationException("This installer was built without Velopack's Setup.exe.");
            var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "MarrowSetup-" + Guid.NewGuid().ToString("N"))).FullName;
            try
            {
                var setup = Path.Combine(folder, "Setup.exe");
                using (var file = File.Create(setup)) payload.CopyTo(file);
                var start = new ProcessStartInfo(setup) { UseShellExecute = false, WorkingDirectory = folder };
                foreach (var argument in arguments) start.ArgumentList.Add(argument);
                using var process = Process.Start(start) ?? throw new InvalidOperationException("Velopack's Setup.exe did not start.");
                process.WaitForExit();
                // Unit U5 (PLAN.md 8, item 1; 8a): Start's AppUserModelID, the desktop shortcut's alignment and the uninstall
                // entry, which Velopack writes after the install hook. The real shell and HKCU are bound here only.
                if (process.ExitCode == 0 && InstallRoot is { } root)
                {
                    try
                    {
                        using var shell = new ShellLinks();
                        AfterSetupExit.Run(shell, new RegistryUserHive(Registry.CurrentUser), root, resultPath: null);
                    }
                    catch (Exception)
                    {
                        // Best effort: Setup.exe succeeded, and nth's start-time self-heal repeats these steps.
                    }
                }
                return process.ExitCode;
            }
            finally
            {
                try { Directory.Delete(folder, recursive: true); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { } // %TEMP% is cleaned by Windows
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);
    }
}
