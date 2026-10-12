using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Marrow.Core.Support;
using Microsoft.Win32;

namespace Marrow.Setup.Port;

/// <summary>
/// PORT-ONLY (OD-0072, ED-247; kept unchanged by nth installer PLAN.md 10, unit U1, ED-NEW-inst-u1-1). The gate the setup
/// was before it had a window: it checks what is installed (InstallerGate, unit tested in Marrow.Core.Tests) and only for a
/// new install or an upgrade copies the embedded Velopack Setup.exe to a temporary folder, runs it with the same
/// arguments, waits, deletes the copy and returns its exit code. The same version or a newer one installed: a message box
/// (none when silent), nothing run, nothing written. It still serves <c>--silent</c> and <c>--help</c>, and the interim
/// screens' Install (STUB(U3/U4): the progress engine and the silent Setup.exe run replace it).
/// </summary>
public sealed partial class LegacyGateHost : IInstallerGateHost
{
    private const string PayloadName = "VelopackSetup.exe";
    private const uint MbOk = 0x0, MbIconWarning = 0x30, MbIconInformation = 0x40, MbSetForeground = 0x10000;

    /// <summary>The gate with these arguments, its errors shown in a message box unless silent.</summary>
    public static int Run(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var host = new LegacyGateHost();
        try
        {
            return InstallerGate.Run(host, Identity(), args);
        }
        catch (Exception error)
        {
            if (!InstallerGate.IsSilent(args)) host.ShowMessage(InstallerGate.Caption(Metadata("InstallerTitle")), error.Message, warning: true);
            return 1;
        }
    }

    public static InstallerIdentity Identity()
    {
        // nth installer PLAN.md override 10 (unit U7, ED-NEW-inst-u7-8): Velopack's default root (%LOCALAPPDATA%\<id>, where 1.0.0 lives) is where an
        // existing install is looked for last; a fresh install goes to the default root the package script stamps (%LOCALAPPDATA%\Programs\nth).
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appId = Metadata("InstallerAppId");
        return new InstallerIdentity(Metadata("InstallerTitle"), Metadata("InstallerVersion"), Path.Combine(local, appId), AppContext.BaseDirectory,
            FreshRoot(local), () => RegisteredRoot(appId));
    }

    private static string FreshRoot(string local)
    {
        var configured = Environment.ExpandEnvironmentVariables(Metadata("InstallerDefaultRoot"));
        return configured.Length > 0 && Path.IsPathRooted(configured) ? configured : Path.Combine(local, "Programs", "nth");
    }

    // The folder Velopack records in the uninstall entry (HKCU\...\Uninstall\<package id>, InstallLocation).
    private static string? RegisteredRoot(string appId)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + appId);
        return key?.GetValue("InstallLocation") as string;
    }

    public static string Metadata(string key) =>
        typeof(LegacyGateHost).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value ?? "";

    public void ShowMessage(string caption, string text, bool warning) =>
        _ = MessageBoxW(0, text, caption, MbOk | MbSetForeground | (warning ? MbIconWarning : MbIconInformation));

    public int RunVelopackSetup(IReadOnlyList<string> arguments)
    {
        using var payload = typeof(LegacyGateHost).Assembly.GetManifestResourceStream(PayloadName)
            ?? throw new InvalidOperationException("This installer was built without Velopack's Setup.exe.");
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "nth Setup-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var setup = Path.Combine(folder, "Setup.exe");
            using (var file = File.Create(setup)) payload.CopyTo(file);
            var start = new ProcessStartInfo(setup) { UseShellExecute = false, WorkingDirectory = folder };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Velopack's Setup.exe did not start.");
            process.WaitForExit();
            return process.ExitCode;
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { } // %TEMP% is cleaned by Windows
        }
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(nint owner, string text, string caption, uint type);
}
