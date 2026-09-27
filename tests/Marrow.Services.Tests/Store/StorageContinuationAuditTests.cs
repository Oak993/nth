using Marrow.Core.Models;
using Marrow.Services.Store;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Store;

// Source: DroppyCode/Services/Store/Storage.swift (B3 52bc792), :118-149.
public sealed class StorageContinuationAuditTests
{
    [Fact]
    [Trait("Req", "PR-STORE-076")] // Services/Store/Storage.swift:132-139; Core/Models/Timeline.swift:353,367
    public async Task Saved_pending_continuation_reloads_and_keeps_its_attachment_during_launch_sweep()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var name = Guid.NewGuid().ToString("D") + ".txt";
        var path = Path.Combine(Storage.AttachmentsDirectory, name);
        var orphan = Path.Combine(Storage.AttachmentsDirectory, Guid.NewGuid().ToString("D") + ".txt");
        File.WriteAllBytes(path, [1]);
        File.WriteAllBytes(orphan, [2]);
        File.SetLastWriteTimeUtc(path, runtime.Time.GetUtcNow().AddDays(-2).UtcDateTime);
        File.SetLastWriteTimeUtc(orphan, runtime.Time.GetUtcNow().AddDays(-2).UtcDateTime);
        var id = Guid.NewGuid();
        var document = new ThreadDocument(id) { Continuation = new ThreadContinuation(Guid.NewGuid(), "Original", "User: pending",
            attachments: new[] { new Attachment { Name = "a.txt", Path = path, MimeType = "text/plain" } }) };
        DiskWriter.Shared.WriteSynchronously(document, Storage.ThreadURL(id));
        Assert.Equal(document, Storage.DecodeDocument(id));
        await Storage.SweepOrphanedAttachments();
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(orphan));
        Assert.Empty(Storage.DecodeDocument(id)!.Items);
    }
}
