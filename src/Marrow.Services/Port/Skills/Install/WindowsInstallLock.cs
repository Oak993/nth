// PORT-ONLY: SK-4 / DL-4, OD-0099 amendments; skills-ONDEMAND-PLAN 3.3-3.4 and skills-PLAN 2.1.
using System.Security.AccessControl;
using System.Security.Principal;

namespace Marrow.Services.Port.Skills.Install;

/// <summary>The exact PLAN 2.1 deny entry and read-only files. Never a Unix permission approximation.</summary>
public sealed class WindowsInstallLock : IInstallLock
{
    private const FileSystemRights Rights = FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.WriteExtendedAttributes |
        FileSystemRights.WriteAttributes | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles;
    private static SecurityIdentifier User() => WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Current Windows SID is absent.");
    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Installer ACL locking is WINDOWS-ONLY.");
    }
    public void Lock(string path)
    {
        RequireWindows();
        var files = InstallPaths.TreeFiles(path);
        foreach (var file in files) File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);
        var directory = new DirectoryInfo(path);
        var acl = directory.GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(User(), Rights, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Deny));
        directory.SetAccessControl(acl);
    }
    public bool IsLocked(string path)
    {
        RequireWindows();
        var rules = new DirectoryInfo(path).GetAccessControl().GetAccessRules(true, false, typeof(SecurityIdentifier));
        var sid = User();
        var found = rules.OfType<FileSystemAccessRule>().Any(rule => rule.IdentityReference.Equals(sid) && rule.AccessControlType == AccessControlType.Deny &&
            rule.FileSystemRights == Rights && rule.InheritanceFlags == (InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit) && rule.PropagationFlags == PropagationFlags.None);
        return found && InstallPaths.TreeFiles(path).All(f => (File.GetAttributes(f) & FileAttributes.ReadOnly) != 0);
    }
    public void Unlock(string path)
    {
        RequireWindows();
        var directory = new DirectoryInfo(path);
        var acl = directory.GetAccessControl();
        var sid = User();
        foreach (var rule in acl.GetAccessRules(true, false, typeof(SecurityIdentifier)).OfType<FileSystemAccessRule>().Where(r => r.IdentityReference.Equals(sid) && r.AccessControlType == AccessControlType.Deny && r.FileSystemRights == Rights && r.InheritanceFlags == (InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit) && r.PropagationFlags == PropagationFlags.None).ToArray())
            acl.RemoveAccessRuleSpecific(rule);
        directory.SetAccessControl(acl);
        foreach (var file in InstallPaths.TreeFiles(path)) File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
    }
}
