using Marrow.Services.Store;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Store;

// Audit fix (svc): Storage.swift:122-147 lists every entry of attachments/, and removeItem takes a folder with everything in it, so an old
// unreferenced entry whose name matches the orphan pattern goes even when it is a directory.
public sealed class StorageOrphanDirectoryTests
{
    [Trait("Req", "PR-STORE-078")] // Services/Store/Storage.swift:141-147
    [Fact]
    public async Task An_old_unreferenced_directory_with_an_orphan_name_is_removed()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var directory = Path.Combine(Storage.AttachmentsDirectory, Guid.NewGuid().ToString("D") + ".png");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "inner.bin"), [1]);
        Directory.SetLastWriteTimeUtc(directory, runtime.Time.GetUtcNow().AddDays(-2).UtcDateTime);

        await Storage.SweepOrphanedAttachments();

        Assert.False(Directory.Exists(directory));
    }
}
