using Marrow.Setup.Install;
using Xunit;

namespace Marrow.Setup.Tests.Install;

[Trait("Decision", "ED-247")]
public sealed class C7U3SnapshotTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "nth-c7-snapshot-" + Guid.NewGuid().ToString("N"));
    private static readonly InstallFileIdentity Old = new(1, 1, 0);
    private static readonly InstallFileIdentity New = new(1, 2, 0);

    public C7U3SnapshotTests() => Directory.CreateDirectory(Path.Combine(root, "current"));

    [Fact]
    public void The_length_is_from_the_same_snapshot_as_the_new_identity()
    {
        var path = Path.Combine(root, "current", "nth.dll");
        File.WriteAllBytes(path, new byte[590000]);
        var baseline = true;
        var observer = new InstallObserver(root, null, 0, null, DateTime.UtcNow,
            snapshot: file =>
            {
                if (baseline) return new InstallFileSnapshot(Old, 590000);
                File.WriteAllBytes(file, [1]);
                return new InstallFileSnapshot(New, 1);
            });
        baseline = false;
        Assert.Equal(1, observer.Observe().CopiedBytes);
    }

    [Fact]
    public void An_unknown_baseline_withholds_bytes_from_later_files()
    {
        File.WriteAllBytes(Path.Combine(root, "current", "old.dll"), [1]);
        var unknown = true;
        var observer = new InstallObserver(root, null, 0, null, DateTime.UtcNow,
            snapshot: _ => unknown ? null : new InstallFileSnapshot(New, 1000));
        unknown = false;
        File.WriteAllBytes(Path.Combine(root, "current", "new.dll"), new byte[1000]);
        Assert.Equal(0, observer.Observe().CopiedBytes);
    }

    [Fact]
    public void A_current_file_without_an_identity_never_counts()
    {
        var observer = new InstallObserver(root, null, 0, null, DateTime.UtcNow, snapshot: _ => null);
        File.WriteAllBytes(Path.Combine(root, "current", "new.dll"), new byte[1000]);
        Assert.Equal(0, observer.Observe().CopiedBytes);
    }

    [Fact]
    public void Moving_an_old_root_file_into_a_child_does_not_count_it()
    {
        var path = Path.Combine(root, "Update.exe");
        File.WriteAllBytes(path, new byte[1000]);
        var observer = new InstallObserver(root, null, 0, null, DateTime.UtcNow,
            snapshot: _ => new InstallFileSnapshot(Old, 1000));
        File.Move(path, Path.Combine(root, "current", "renamed.dll"));
        Assert.Equal(0, observer.Observe().CopiedBytes);
    }

    [Fact]
    public void Two_paths_for_one_new_identity_count_its_bytes_once()
    {
        var observer = new InstallObserver(root, null, 0, null, DateTime.UtcNow,
            snapshot: _ => new InstallFileSnapshot(New, 1000));
        File.WriteAllBytes(Path.Combine(root, "current", "a.dll"), new byte[1000]);
        File.WriteAllBytes(Path.Combine(root, "current", "b.dll"), new byte[1000]);
        Assert.Equal(1000, observer.Observe().CopiedBytes);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
