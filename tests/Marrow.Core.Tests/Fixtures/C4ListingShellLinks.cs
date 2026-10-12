using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.Core.Tests.Fixtures;

// A fake-only listing that charges virtual time as each file is enumerated.
public sealed class C4ListingShellLinks(FakeShellLinks fake, ManualTimeProvider clock, TimeSpan perFile, TimeSpan? readTime = null) : IShellLinks
{
    public List<TimeSpan> ReadAllowances { get; } = [];
    public IEnumerable<ShellLinkFile> EnumerateLinks(string folder, string searchPattern = "*.lnk")
    {
        foreach (var link in fake.EnumerateLinks(folder, searchPattern))
        {
            clock.Advance(perFile);
            yield return link;
        }
    }
    public string KnownFolder(ShellKnownFolder folder) => fake.KnownFolder(folder);
    public string ExpandEnvironment(string path) => fake.ExpandEnvironment(path);
    public bool FileExists(string path) => fake.FileExists(path);
    public DateTime? FolderWriteTimeUtc(string folder) => fake.FolderWriteTimeUtc(folder);
    public ShellLink ReadLink(string path)
    {
        clock.Advance(readTime ?? TimeSpan.Zero);
        return fake.ReadLink(path);
    }
    public ShellLink ReadLink(string path, TimeSpan timeout)
    {
        ReadAllowances.Add(timeout);
        var duration = readTime ?? TimeSpan.Zero;
        clock.Advance(duration < timeout ? duration : timeout);
        if (duration > timeout) throw new TimeoutException("The fake shell read exhausted its allowance.");
        return fake.ReadLink(path);
    }
    public void WriteLink(string path, ShellLink link) => fake.WriteLink(path, link);
    public void UpdateLink(string path, ShellLinkChange change) => fake.UpdateLink(path, change);
    public void DeleteLink(string path) => fake.DeleteLink(path);
    public void MoveLink(string from, string to) => fake.MoveLink(from, to);
    public bool DeleteEmptyFolder(string folder) => fake.DeleteEmptyFolder(folder);
    public bool DesktopIconsHidden() => fake.DesktopIconsHidden();
    public void Notify(ShellChange change, string path) => fake.Notify(change, path);
    public IDisposable? TryEnterWriter(TimeSpan wait) => fake.TryEnterWriter(wait);
}
