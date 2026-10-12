using Marrow.Setup.Install;

namespace Marrow.Setup.Tests.Install;

public sealed class C7U3AttemptIdentityTests : IDisposable
{
    private readonly string area = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "nth-c7-attempt-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose() => Directory.Delete(area, recursive: true);

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Replacing_the_root_counts_new_files_even_with_the_same_path_size_and_timestamps()
    {
        var root = Path.Combine(area, "nth");
        var old = Path.Combine(root, "current", "nth.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(old)!);
        File.WriteAllBytes(old, new byte[4096]);
        var created = File.GetCreationTimeUtc(old);
        var written = File.GetLastWriteTimeUtc(old);
        var observer = new InstallObserver(root, null, 0, null, DateTime.UtcNow);
        Assert.Equal(0, observer.Observe().CopiedBytes);

        Directory.Move(root, root + ".old");
        Directory.CreateDirectory(Path.GetDirectoryName(old)!);
        File.WriteAllBytes(old, new byte[4096]);
        File.SetCreationTimeUtc(old, created);
        File.SetLastWriteTimeUtc(old, written);
        Assert.Equal(4096, observer.Observe().CopiedBytes);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Renaming_a_leftover_inside_the_root_never_makes_it_new_attempt_bytes()
    {
        var root = Path.Combine(area, "nth");
        var old = Path.Combine(root, "current", "old.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(old)!);
        File.WriteAllBytes(old, new byte[4096]);
        var observer = new InstallObserver(root, null, 0, null, DateTime.UtcNow);
        File.Move(old, Path.Combine(root, "current", "renamed.dll"));
        Assert.Equal(0, observer.Observe().CopiedBytes);
    }

}
