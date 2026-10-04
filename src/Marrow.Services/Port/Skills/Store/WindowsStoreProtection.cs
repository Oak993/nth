// PORT-ONLY: SK-5 real Windows implementation. PLAN 2.1 exact (OI)(CI)(WD,AD,WEA,WA,DE,DC), no shell/elevation.
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Marrow.Services.Port.Skills.Store;

public sealed class WindowsStoreProtection : IStoreProtection
{
    private const FileSystemRights Denied = FileSystemRights.WriteData | FileSystemRights.AppendData |
        FileSystemRights.WriteExtendedAttributes | FileSystemRights.WriteAttributes | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles;
    private const InheritanceFlags Children = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

    private static SecurityIdentifier CurrentUser()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("SK-5 protection requires Windows ACLs and named-stream inspection.");
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User ?? throw new InvalidOperationException("The current Windows user has no SID.");
    }

    private static FileSystemSecurity Read(string path) => Directory.Exists(path)
        ? new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access)
        : new FileInfo(path).GetAccessControl(AccessControlSections.Access);

    private static void Write(string path, FileSystemSecurity security)
    {
        if (security is DirectorySecurity directory) new DirectoryInfo(path).SetAccessControl(directory);
        else new FileInfo(path).SetAccessControl((FileSecurity)security);
    }

    public void Lock(string folder)
    {
        var sid = CurrentUser();
        StorePaths.EnsureAncestors(folder);
        var security = new DirectoryInfo(folder).GetAccessControl(AccessControlSections.Access);
        var rule = new FileSystemAccessRule(sid, Denied, Children, PropagationFlags.None, AccessControlType.Deny);
        var exists = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>().Any(r => r.IdentityReference.Equals(sid) && r.AccessControlType == AccessControlType.Deny &&
                r.FileSystemRights == Denied && r.InheritanceFlags == Children && r.PropagationFlags == PropagationFlags.None);
        if (!exists)
        {
            security.AddAccessRule(rule);
            new DirectoryInfo(folder).SetAccessControl(security);
        }
    }

    public bool IsLocked(string path)
    {
        var sid = CurrentUser();
        StorePaths.EnsureAncestors(path);
        var directory = Directory.Exists(path);
        return Read(path).GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>().Any(r => r.IdentityReference.Equals(sid) && r.AccessControlType == AccessControlType.Deny &&
                r.FileSystemRights == Denied && (!directory || r.InheritanceFlags == Children) && r.PropagationFlags == PropagationFlags.None);
    }

    public void Unlock(string path)
    {
        var sid = CurrentUser();
        StorePaths.EnsureAncestors(path);
        var security = Read(path);
        var changed = false;
        foreach (var rule in security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>())
        {
            if (!rule.IdentityReference.Equals(sid) || rule.AccessControlType != AccessControlType.Deny || rule.FileSystemRights != Denied) continue;
            security.RemoveAccessRuleSpecific(rule);
            changed = true;
        }
        if (changed) Write(path, security);
    }

    public bool HasNamedStreams(string path)
    {
        _ = CurrentUser();
        StorePaths.EnsureAncestors(path);
        var handle = FindFirstStreamW(path, 0, out var data, 0);
        if (handle == new IntPtr(-1))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 38) return false; // ERROR_HANDLE_EOF: no streams (normal for directories).
            throw new IOException("Cannot enumerate NTFS streams.", new Win32Exception(error));
        }
        try
        {
            do
            {
                if (!data.Name.Equals("::$DATA", StringComparison.Ordinal)) return true;
            } while (FindNextStreamW(handle, out data));
            var error = Marshal.GetLastWin32Error();
            if (error != 38) throw new IOException("Cannot complete NTFS stream enumeration.", new Win32Exception(error));
            return false;
        }
        finally { FindClose(handle); }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr FindFirstStreamW(string fileName, int infoLevel, out StoreStreamData data, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindNextStreamW(IntPtr handle, out StoreStreamData data);
    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindClose(IntPtr handle);
}
