using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Marrow.Core.Support;
using Marrow.Setup.Flow;
using Marrow.Setup.Install;
using Marrow.Setup.Support;
using Microsoft.Win32;

namespace Marrow.Setup.Port;

/// <summary>
/// PORT-ONLY (OD-0072, ED-247; kept unchanged by nth installer PLAN.md 10, unit U1, ED-NEW-inst-u1-1). The gate the setup
/// was before it had a window: it checks what is installed (InstallerGate, unit tested in Marrow.Core.Tests) and only for a
/// new install or an upgrade stages the embedded Velopack Setup.exe where the window does (<c>%LOCALAPPDATA%\nth Setup\
/// &lt;version&gt;\nth Setup Engine.exe</c>, claimed and removed again by unit U9's guarded scope: no GUID folder in Temp,
/// PLAN.md 5), writes the install options there (the desktop shortcut is on unless <c>--no-desktop-shortcut</c>, PLAN.md
/// 8a), runs it with the same arguments, waits, and returns its exit code. The same version or a newer one installed: a message box
/// (none when silent), nothing run, nothing written. It serves <c>--silent</c> and <c>--help</c>, and an Install chosen in a
/// session built without the in-window progress engine (Problem's Retry after a problem that was not an install run).
/// </summary>
public sealed partial class LegacyGateHost : IInstallerGateHost
{
    private const string PayloadName = "VelopackSetup.exe";
    private const uint MbOk = 0x0, MbIconWarning = 0x30, MbIconInformation = 0x40, MbSetForeground = 0x10000;

    /// <summary>The gate with these arguments, its errors shown in a message box unless silent.</summary>
    public static int Run(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var host = new LegacyGateHost { DesktopShortcut = InstallOptions.DesktopShortcutChoice(args) };
        try
        {
            host.InstallRoot = RootToInstall(args);
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

    /// <summary>
    /// The folder the gate decides about and Setup.exe installs into (unit U7: a fresh install is not at Velopack's default root), where
    /// unit U5's post-exit steps run.
    /// </summary>
    private static string RootToInstall(IReadOnlyList<string> args)
    {
        var identity = Identity();
        return InstallerGate.FindInstall(identity, InstallerGate.ReadInstallTo(args, identity.LaunchDirectory)).Root;
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

    /// <summary>The root Setup.exe installs into; unit U5's post-exit steps run there.</summary>
    public string? InstallRoot { get; set; }

    /// <summary>The desktop shortcut choice of this run (on unless <c>--no-desktop-shortcut</c>), for the install hook's options file.</summary>
    public bool DesktopShortcut { get; init; } = true;

    public void ShowMessage(string caption, string text, bool warning) =>
        _ = MessageBoxW(0, text, caption, MbOk | MbSetForeground | (warning ? MbIconWarning : MbIconInformation));

    public int RunVelopackSetup(IReadOnlyList<string> arguments)
    {
        // --help prints Setup.exe's help and changes nothing: no preflight, no options, no post-exit steps (PLAN.md 3: "-h goes to Setup.exe").
        var help = InstallerGate.IsHelp(arguments);
        // nth installer PLAN.md 3a (unit U4, ED-NEW-inst-u4-2, bound at integration): a --silent setup takes the install hold and asks a running nth
        // to quit when idle before Velopack runs; busy or refusing nth exits 3, another setup on the root 4. The hold stays until Setup.exe has exited.
        using var preflight = !help && InstallerGate.IsSilent(arguments) && InstallRoot is { } holdRoot
            ? SilentPreflight.RunAsync(holdRoot, ProductionRunningNth.Create(holdRoot)).GetAwaiter().GetResult()
            : null;
        if (preflight?.ExitCode is { } refused) return refused;
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        using var scope = StagingScope.Create(localAppData, Path.GetTempPath(), Metadata("InstallerVersion"));
        var engine = Stage(scope);
        var options = Path.Combine(engine.Folder, InstallOptions.OptionsFileName);
        if (!help) WriteOptions(options, InstallRoot, DesktopShortcut);
        var start = new ProcessStartInfo(engine.EnginePath) { UseShellExecute = false, WorkingDirectory = engine.Folder };
        start.Environment[InstallOptions.EnvironmentVariable] = options;
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Velopack's Setup.exe did not start.");
        process.WaitForExit();
        if (RunsAfterExit(help, process.ExitCode) && InstallRoot is { } root) Program.FinishAfterSetupExit(root, InstallOptions.ResultPathFor(options));
        return process.ExitCode;
    }

    /// <summary>Unit U5's post-exit steps run after an install or update that exited 0, never after help.</summary>
    internal static bool RunsAfterExit(bool help, int exitCode) => !help && exitCode == 0;

    /// <summary>
    /// The engine, staged in the claimed <c>%LOCALAPPDATA%\nth Setup\&lt;version&gt;</c> (the scope removes the folder on every exit
    /// path it controls, and the next start's sweep anything a power loss leaves). Synchronous: this is the windowless gate.
    /// </summary>
    private static EngineStaging Stage(StagingScope scope)
    {
        var folder = scope.Folder ?? throw new InvalidOperationException("This installer was built without a version, so there is nowhere to stage Velopack's Setup.exe.");
        var staging = new EngineStaging(folder, () => typeof(LegacyGateHost).Assembly.GetManifestResourceStream(PayloadName), scope.Ensure);
        staging.StageAsync().GetAwaiter().GetResult(); // never throws: a failure is the staging's Error (no synchronization context here: Main's own thread)
        if (staging.Error is InvalidOperationException missing) throw missing; // built without Velopack's Setup.exe
        if (staging.Error is { } error) throw new IOException("Velopack's Setup.exe could not be written to " + folder + ": " + error.Message, error);
        return staging;
    }

    /// <summary>
    /// What the install hook does (PLAN.md 8a: <c>--silent</c> keeps the desktop shortcut on by default; <c>--no-desktop-shortcut</c>
    /// turns it off; an update never adds one): written where the hook looks, and an earlier result cleared.
    /// </summary>
    internal static void WriteOptions(string path, string? root, bool desktopShortcut)
    {
        var update = root is not null && InstallerGate.ReadInstalledVersion(root) is not null;
        InstallOptions.ForAction(update ? InstallerAction.Upgrade : InstallerAction.Install, desktopShortcut).Write(path);
        var result = InstallOptions.ResultPathFor(path);
        if (File.Exists(result)) File.Delete(result);
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(nint owner, string text, string caption, uint type);
}
