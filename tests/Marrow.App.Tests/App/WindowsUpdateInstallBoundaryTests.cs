using System.Security.AccessControl;
using System.Security.Principal;
using Marrow.App.Port;
using Xunit;

namespace Marrow.App.Tests.App;

// Tests for the Windows side of App/AppUpdater.swift (B2 1fbf81e) :299-368, over the ED-188 boundary
// (Marrow.App.Port.WindowsUpdateInstallBoundary). ED-188 ruling point 3: "Verify against real files on
// this machine (%WINDIR%\System32\notepad.exe is Authenticode-signed; a freshly written unsigned temp
// file is not); the ACL of the staging folder read back with DirectorySecurity."
public sealed class WindowsUpdateInstallBoundaryTests
{
    // WinTrust.IsTrusted checks only an embedded Authenticode signature (WTD_CHOICE_FILE), not the OS's
    // catalog-signed system files (notepad.exe and friends are catalog-signed, not embedded, on Windows 10+
    // and fail this file-only check with TRUST_E_NOSIGNATURE even though Get-AuthenticodeSignature, which
    // also consults the catalog database, reports them valid). The running host process (dotnet.exe or
    // testhost.exe under the .NET SDK) carries its own embedded Microsoft signature and always exists.
    private static readonly string SignedSystemFile = Environment.ProcessPath!;

    // ---- PR-UPDATE-047: the per-install staging directory and its ACL ---------------------------------

    [Fact]
    [Trait("Req", "PR-UPDATE-047")] // App/AppUpdater.swift:300-302, the analogue of [.posixPermissions: 0o700]
    public void The_staging_directory_grants_full_control_to_the_current_user_only_and_no_inherited_rule()
    {
        var path = WindowsUpdateInstallBoundary.CreateStagingDirectory();
        try
        {
            Assert.True(Directory.Exists(path));
            Assert.Matches(@"^MarrowUpdate-[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}$", Path.GetFileName(path)); // :301 UUID().uuidString; R-UUID-1
            var security = new DirectoryInfo(path).GetAccessControl();
            Assert.True(security.AreAccessRulesProtected, "inherited rules must not reach the staging folder");
            var user = WindowsIdentity.GetCurrent().User!;
            var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier));
            var grantsFullControlToUser = rules.Cast<FileSystemAccessRule>().Any(r =>
                r.IdentityReference.Equals(user)
                && r.AccessControlType == AccessControlType.Allow
                && (r.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl);
            Assert.True(grantsFullControlToUser, "the current user must have FullControl on the staging folder");
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-047")] // App/AppUpdater.swift:322, :325 removeItem(at: image) on both paths
    public async Task Stage_deletes_the_downloaded_artifact_even_though_the_unpack_step_is_blocked()
    {
        var downloaded = Path.Combine(Path.GetTempPath(), $"aut-artifact-{Guid.NewGuid():N}");
        await File.WriteAllBytesAsync(downloaded, [1, 2, 3]);
        var boundary = new WindowsUpdateInstallBoundary();
        await Assert.ThrowsAsync<PortBlockedException>(() => boundary.Stage(downloaded, "1.0.0"));
        Assert.False(File.Exists(downloaded), "the downloaded artifact must be removed even when staging is blocked");
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-047")] // UNK-0009, resolved by OD-0008 C6: the tracked blocker itself
    public async Task Stage_names_UNK_0009_as_the_reason_the_unpack_step_cannot_run()
    {
        var downloaded = Path.Combine(Path.GetTempPath(), $"aut-artifact-{Guid.NewGuid():N}");
        await File.WriteAllBytesAsync(downloaded, [1]);
        var boundary = new WindowsUpdateInstallBoundary();
        var error = await Assert.ThrowsAsync<PortBlockedException>(() => boundary.Stage(downloaded, "1.0.0"));
        Assert.Contains("UNK-0009", error.Message, StringComparison.Ordinal);
    }

    // ---- PR-UPDATE-049 / PR-UPDATE-051 (collapsed): the Authenticode trust gate -------------------------

    [Fact]
    [Trait("Req", "PR-UPDATE-049")] // App/AppUpdater.swift:346-368, collapsed with :337-340 per the windows_path
    public void A_Windows_signed_binary_passes_verification()
    {
        Assert.True(File.Exists(SignedSystemFile), "this test needs a Windows install with notepad.exe present");
        new WindowsUpdateInstallBoundary().Verify(SignedSystemFile); // must not throw
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-049")] // App/AppUpdater.swift:346-368
    public async Task An_unsigned_file_fails_verification_with_the_ports_own_verbatim_prefix()
    {
        var path = Path.Combine(Path.GetTempPath(), $"aut-unsigned-{Guid.NewGuid():N}.exe");
        await File.WriteAllBytesAsync(path, [0x4D, 0x5A, 1, 2, 3]); // not a real, signed PE
        try
        {
            var error = Assert.Throws<UpdateInstallError>(() => new WindowsUpdateInstallBoundary().Verify(path));
            Assert.StartsWith("The downloaded app is not signed by Marrow. ", error.ErrorDescription, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
