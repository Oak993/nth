using Marrow.Core.Support;
using Marrow.Setup.Support;
using Marrow.Setup.Tests.Fakes;
using Marrow.Testing;

namespace Marrow.Setup.Tests.Fixtures;

/// <summary>
/// A fake nth install in a temporary folder (unit U6): <c>%LOCALAPPDATA%</c> and <c>%TEMP%</c> of its own, a Velopack root at
/// <c>Programs\nth</c> with Update.exe, the stub, <c>current\</c>, <c>packages\</c> and <c>nth Uninstall.exe</c>, nth's data
/// folder, a stale nth Setup staging folder, the uninstall entry and the sign-in value in a fake registry, and desktop links on
/// a fake shell. Nothing outside the area is touched: the real Desktop, Start menu, HKCU and %LOCALAPPDATA%\nth never are.
/// </summary>
public sealed class UninstallArea : IDisposable
{
    public const string SignInCommandFormat = "\"{0}\" --at-sign-in";

    public UninstallArea()
    {
        Sweep = new SweepArea();
        Root = Path.Combine(Sweep.LocalAppData, "Programs", "nth");
        Put(Path.Combine(Root, InstallLayout.UpdateExe), "update");
        Put(Path.Combine(Root, InstallLayout.MainExe), "stub");
        Put(Path.Combine(Root, InstallLayout.RootUninstaller), "uninstaller");
        Put(InstallLayout.CurrentExe(Root), "nth");
        Put(Path.Combine(Root, InstallLayout.CurrentFolder, "sq.version"), "<package><metadata><version>1.1.0</version></metadata></package>");
        Put(Path.Combine(Root, InstallLayout.PackagesFolder, "NthApp-1.1.0-full.nupkg"), "package");
        DataFolder = Path.Combine(Sweep.LocalAppData, Uninstaller.DataFolderName);
        Put(Path.Combine(DataFolder, "library.json"), "{}");
        Put(Path.Combine(DataFolder, "threads", "a.json"), "{}");
        var log = Put(Path.Combine(DataFolder, "Logs", "nth.log"), "log");
        File.SetAttributes(log, FileAttributes.ReadOnly);
        Sweep.PutInSetup("1.0.0", InstallOptions.OptionsFileName);
        Shell.Environment["LOCALAPPDATA"] = Sweep.LocalAppData;
        Registry.WriteVelopackUninstallKey(Root);
        UninstallEntry.Repoint(Registry, Root, 420_000, uninstallerPresent: true);
        Registry.Seed(UninstallEntry.RunKeyPath, UninstallEntry.SignInValueName, string.Format(System.Globalization.CultureInfo.InvariantCulture, SignInCommandFormat, InstallLayout.CurrentExe(Root)));
        Registry.Seed(UninstallEntry.RunKeyPath, "Other", "\"C:\\Program Files\\Other\\other.exe\"");
        Runner = new FakeUninstallRunner(Registry, Shell);
    }

    public SweepArea Sweep { get; }

    public string Root { get; }

    public string DataFolder { get; }

    public string LocalAppData => Sweep.LocalAppData;

    public string Temp => Sweep.Temp;

    public FakeShellLinks Shell { get; } = new();

    public FakeUserRegistry Registry { get; } = new();

    public FakeCredentialStore Credentials { get; } = new();

    public FakeUninstallRunner Runner { get; }

    /// <summary>nth's marked desktop shortcut, stored with %LOCALAPPDATA% as nth stores it.</summary>
    public string MarkedLink => FakeShellLinks.DesktopLink;

    /// <summary>The ports over this area's fakes.</summary>
    public UninstallPorts Ports(Func<string, IRunningNth>? nth = null, Func<string, IDisposable?>? hold = null, TimeSpan? rootPatience = null) =>
        new(Runner, Shell, Registry, Credentials, LocalAppData, Temp, nth, hold ?? (_ => new FakeHold()), RootPatience: rootPatience ?? TimeSpan.FromSeconds(2));

    /// <summary>Seeds nth's desktop shortcut, a renamed unmarked one into the root, and a foreign one, on the fake desktop.</summary>
    public (string Marked, string Renamed, string Foreign) SeedLinks()
    {
        Shell.Seed(MarkedLink, new ShellLink(@"%LOCALAPPDATA%\Programs\nth\current\nth.exe", WorkingDirectory: @"%LOCALAPPDATA%\Programs\nth\current", CreatedBy: ShellLinkKeys.CreatedByValue));
        var renamed = FakeShellLinks.OneDriveDesktop + @"\my coding app.lnk";
        Shell.Seed(renamed, new ShellLink(InstallLayout.CurrentExe(Root)));
        var foreign = FakeShellLinks.OneDriveDesktop + @"\other.lnk";
        Shell.Seed(foreign, new ShellLink(@"C:\Program Files\Other\other.exe"));
        return (MarkedLink, renamed, foreign);
    }

    public static string Put(string path, string content = "x")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose() => Sweep.Dispose();
}
