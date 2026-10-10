// Source: DroppyCode/App/AppUpdater.swift (B3 52bc792), :299-395.
// PORT-ONLY: the Windows equivalent of AppUpdater.swift's macOS install mechanism (hdiutil attach/copy/
// detach, the Developer ID signature check plus Gatekeeper, and the shell installer script). ED-188
// (orchestrator ruling, run16, recorded in porting/engineering_decisions.md and ruled to the build agent as
// work/rulings_live/App.AppUpdater.part1.md): AppUpdater.swift's install ORCHESTRATION is ported exactly in
// AppUpdater.cs over this narrow interface, whose members map one to one to the macOS steps (hdiutil
// attach/ditto copy/hdiutil detach -> Stage; the Developer ID + Gatekeeper checks, collapsed per
// PR-UPDATE-051's windows_path, plus the staged CFBundleShortVersionString read -> Verify and ReadVersion;
// the installer script launch -> LaunchDetachedInstaller). Because the Windows release asset format is not
// decided (UNK-0009; PA-0014 "The update feed is the public GitLab Releases API and is platform-neutral"),
// WindowsUpdateInstallBoundary.Stage created the real per-update staging directory and its ACL (both
// decided and tested) and then threw PortBlockedException at the step that would unpack an actual asset.
// UE-1 (OD-0062 Velopack, OD-0100 the owner's GitHub feed) CLOSES UNK-0009: the Windows release asset is
// Velopack's full package (MarrowCode-<version>-full.nupkg, a zip whose nuspec names the app and version),
// downloaded and checksum-verified by Velopack's UpdateManager into its own packages folder, and applied by
// Velopack's Update.exe. Stage, Verify and ReadVersion now work on that package (ED-319); the
// installer-script step (LaunchDetachedInstaller) stays for the pipeline that has no engine.
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Marrow.Core.Port;
using Marrow.Core.Port.SelfUpdate;
using Marrow.Core.Support;

namespace Marrow.App.Port;

/// <summary>
/// A tracked platform blocker (contract section 17): the caller sees exactly why a step cannot run yet.
/// Distinct from <see cref="UpdateInstallError"/> (a source-defined failure the app shows the user); this
/// one means the port has not been asked to solve the named unknown. Nothing throws it since UE-1 closed UNK-0009.
/// </summary>
public sealed class PortBlockedException(string message) : Exception(message);

/// <summary>ED-188. See the file header for the one-to-one mapping to AppUpdater.swift's macOS steps.</summary>
public interface IUpdateInstallBoundary
{
    /// <summary>AppUpdater.swift:299-330 (hdiutil attach, the .app lookup, ditto, hdiutil detach).</summary>
    Task<string> Stage(string downloadedArtifactPath, string expectedVersion);

    /// <summary>AppUpdater.swift:337-340, :346-368 (Gatekeeper and the Developer ID signature check).</summary>
    void Verify(string stagedPath);

    /// <summary>AppUpdater.swift:332-333 (the staged bundle's CFBundleShortVersionString).</summary>
    string? ReadVersion(string stagedPath);

    /// <summary>AppUpdater.swift:380-395 (writes the log, starts the detached installer).</summary>
    void LaunchDetachedInstaller(int processId, string stagedPath, string destinationPath, string scriptPath, string logPath);
}

/// <summary>The production boundary. See the file header for what UNK-0009 blocks and why.</summary>
public sealed class WindowsUpdateInstallBoundary : IUpdateInstallBoundary
{
    // :299-330, through Velopack (UE-1, closes UNK-0009). The downloaded artifact is Velopack's full package. It stays where
    // Velopack put it, in its own packages folder: Velopack's Update.exe applies from there, so the :322, :325 removal of the
    // downloaded image does not apply (removing it would remove the update). Velopack already unpacked nothing and verified the
    // feed's checksum when the download ended; there is nothing for this port to unpack, so the staged path is the package.
    public Task<string> Stage(string downloadedArtifactPath, string expectedVersion)
    {
        _ = expectedVersion;
        return Task.FromResult(downloadedArtifactPath);
    }

    /// <summary>
    /// :300-302. A fresh per-update folder under %TEMP% whose ACL grants only the current user: the
    /// analogue of <c>[.posixPermissions: 0o700]</c>.
    /// </summary>
    internal static string CreateStagingDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"MarrowUpdate-{SwiftUUID.UuidString(Guid.NewGuid())}");
        var info = Directory.CreateDirectory(path);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false); // no inherited rules reach this folder
        var user = WindowsIdentity.GetCurrent().User
            ?? throw new PortBlockedException("No current Windows user identity to grant the staging ACL to.");
        security.AddAccessRule(new FileSystemAccessRule(
            user,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        info.SetAccessControl(security);
        return path;
    }

    // :337-340, :346-368. Authenticode through WinVerifyTrust, collapsing the Developer ID signature
    // check and the Gatekeeper assessment into one trust gate (PR-UPDATE-051 windows_path).
    public void Verify(string stagedPath)
    {
        // UE-1: a Velopack package (.nupkg) is not an Authenticode file; Velopack checked its checksum against the feed when the
        // download ended, and this checks it is Marrow's package (ED-319). Anything else keeps the Authenticode gate.
        if (stagedPath.EndsWith(PackageExtension, StringComparison.OrdinalIgnoreCase))
        {
            if (VelopackPackage.ReadIdentity(stagedPath) is not { } identity || identity.Id != MarrowReleaseFeed.PackageId)
                throw new UpdateInstallError($"The downloaded app is not signed by Nth. Not a {MarrowReleaseFeed.PackageId} package");
            return;
        }
        if (!WinTrust.IsTrusted(stagedPath, out var reason))
            // ENGINEERING DECISION: no owner-licensed Windows wording exists for the Authenticode failure
            // text (PR-UPDATE-049 has no `deviation` field); the product-name substitution follows
            // OD-0012 F1's established pattern ("Droppy" -> "Marrow") since WinVerifyTrust collapses the
            // three macOS-specific failure messages into one trust gate (PR-UPDATE-051 windows_path).
            throw new UpdateInstallError($"The downloaded app is not signed by Nth. {reason}");
    }

    // :332-336. UE-1: the version a Velopack package's nuspec names; null for anything that is not one.
    public string? ReadVersion(string stagedPath) =>
        stagedPath.EndsWith(PackageExtension, StringComparison.OrdinalIgnoreCase) ? VelopackPackage.ReadIdentity(stagedPath)?.Version : null;

    private const string PackageExtension = ".nupkg";

    // :380-395. install.sh's own content is PR-UPDATE-056 (part 2, the file's separate work packet); this
    // launches whatever part 2 leaves at scriptPath.
    public void LaunchDetachedInstaller(int processId, string stagedPath, string destinationPath, string scriptPath, string logPath)
    {
        File.WriteAllText(logPath, string.Empty); // :383-384 FileManager.default.createFile(atPath:), empty
        var launcher = new ProcessLauncher
        {
            // ENGINEERING DECISION: the Windows analogue of /bin/sh running the installer script; the
            // script's own content and extension are PR-UPDATE-056 (part 2). A full path is required:
            // ProcessLauncher's CreateProcessW call passes ExecutableURL as lpApplicationName, which (unlike
            // lpCommandLine with a null lpApplicationName) searches only the calling process's own current
            // directory for a bare module name, never PATH.
            ExecutableURL = Path.Join(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            Arguments =
            [
                "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath,
                processId.ToString(CultureInfo.InvariantCulture), stagedPath, destinationPath, // :387-391
            ],
            StandardInput = null, // :392 FileHandle.nullDevice
            StandardOutput = new ProcessPipe(),
            UsesJobObject = false, // the installer must outlive this process, not die with its job object
        };
        launcher.StandardError = launcher.StandardOutput; // :393-394 stderr joins stdout in install.log
        var output = launcher.StandardOutput;
        launcher.Run();
        _ = Task.Run(() =>
        {
            try { File.AppendAllText(logPath, SwiftText.DecodeUtf8Repairing(output.ReadDataToEndOfFile())); }
            catch (IOException) { }
        });
    }
}

/// <summary>PORT-ONLY: WinVerifyTrust (WINTRUST_ACTION_GENERIC_VERIFY_V2), Authenticode's analogue of Gatekeeper.</summary>
internal static class WinTrust
{
    private static readonly Guid ActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WtdUiNone = 2;
    private const uint WtdRevokeNone = 0;
    private const uint WtdChoiceFile = 1;
    private const uint WtdStateActionVerify = 1;
    private const uint WtdStateActionClose = 2;
    private const uint WtdSaferFlag = 0x100;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WINTRUST_DATA pWVTData);

    public static bool IsTrusted(string filePath, out string reason)
    {
        var fileInfo = new WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = filePath,
            hFile = IntPtr.Zero,
            pgKnownSubject = IntPtr.Zero,
        };
        var fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);
            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = WtdUiNone,
                fdwRevocationChecks = WtdRevokeNone,
                dwUnionChoice = WtdChoiceFile,
                pFile = fileInfoPtr,
                dwStateAction = WtdStateActionVerify,
                dwProvFlags = WtdSaferFlag,
            };
            var action = ActionGenericVerifyV2;
            var result = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            data.dwStateAction = WtdStateActionClose;
            WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            if (result == 0)
            {
                reason = "";
                return true;
            }
            reason = $"WinVerifyTrust status 0x{result:X8}";
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(fileInfoPtr);
        }
    }
}
