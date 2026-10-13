using System.Runtime.InteropServices;
using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.Core.Tests.Fixtures;

// Charges only repair preparation; every path and shell object is in-memory.
public sealed class C4cPreparationShellLinks(FakeShellLinks fake, ManualTimeProvider clock, TimeSpan? startPreparation = null) : IShellLinks
{
    private bool armed;
    public List<TimeSpan> UpdateAllowances { get; } = [];
    public List<TimeSpan> StartReadAllowances { get; } = [];
    public string KnownFolder(ShellKnownFolder folder)
    {
        if (armed && folder == ShellKnownFolder.LocalAppData) clock.Advance(TimeSpan.FromSeconds(1));
        if (folder == ShellKnownFolder.Programs && startPreparation is { } preparation) clock.Advance(preparation);
        return fake.KnownFolder(folder);
    }
    public ShellLink ReadLink(string path)
    {
        if (startPreparation is not null && path == FakeShellLinks.Programs + @"\nth.lnk") clock.Advance(TimeSpan.FromSeconds(6));
        var link = fake.ReadLink(path);
        if (path == FakeShellLinks.DesktopLink) armed = true;
        return link;
    }
    public ShellLink ReadLink(string path, TimeSpan timeout)
    {
        if (startPreparation is not null && path == FakeShellLinks.Programs + @"\nth.lnk")
        {
            StartReadAllowances.Add(timeout);
            WaitForSlowCall(timeout);
        }
        var link = fake.ReadLink(path);
        if (path == FakeShellLinks.DesktopLink) armed = true;
        return link;
    }
    public void UpdateLink(string path, ShellLinkChange change)
    {
        clock.Advance(TimeSpan.FromSeconds(6));
        fake.UpdateLink(path, change);
    }
    // This extra public overload compiles before the production interface gains its timed member.
    public void UpdateLink(string path, ShellLinkChange change, TimeSpan timeout)
    {
        UpdateAllowances.Add(timeout);
        WaitForSlowCall(timeout);
        fake.UpdateLink(path, change);
    }
    private void WaitForSlowCall(TimeSpan timeout)
    {
        var duration = TimeSpan.FromSeconds(6);
        if (timeout > TimeSpan.Zero) clock.Advance(timeout < duration ? timeout : duration);
        if (timeout < duration) throw new COMException("The synthetic slow shell call exhausted its allowance.", unchecked((int)0x800705B4));
    }
    public IEnumerable<ShellLinkFile> EnumerateLinks(string folder, string searchPattern = "*.lnk") => fake.EnumerateLinks(folder, searchPattern);
    public string ExpandEnvironment(string path) => fake.ExpandEnvironment(path);
    public bool FileExists(string path) => fake.FileExists(path);
    public DateTime? FolderWriteTimeUtc(string folder) => fake.FolderWriteTimeUtc(folder);
    public void WriteLink(string path, ShellLink link) => fake.WriteLink(path, link);
    public void DeleteLink(string path) => fake.DeleteLink(path);
    public void MoveLink(string from, string to) => fake.MoveLink(from, to);
    public bool DeleteEmptyFolder(string folder) => fake.DeleteEmptyFolder(folder);
    public bool DesktopIconsHidden() => fake.DesktopIconsHidden();
    public void Notify(ShellChange change, string path) => fake.Notify(change, path);
    public IDisposable? TryEnterWriter(TimeSpan wait) => fake.TryEnterWriter(wait);
}
