using Marrow.Services.Store;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Store;

// Audit fix (svc): Storage.swift:222-227 `case image(Data, type: UTType)` holds a Data value, so a source built from a buffer keeps the
// bytes it was built with even when the caller overwrites the buffer before ImportSource runs.
public sealed class StorageImageSnapshotTests
{
    [Trait("Req", "PR-STORE-069")] // Services/Store/Storage.swift:189-197,222-227
    [Fact]
    public void An_image_source_keeps_the_bytes_it_was_built_with()
    {
        using var scratch = new StorageScratchFolder();
        var png = new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
        };
        var original = (byte[])png.Clone();
        var source = new AttachmentSource.Image(png, AttachmentSource.PngMimeType);
        png[0] = 0x00;

        var attachment = Storage.ImportSource(source);

        Assert.NotNull(attachment);
        Assert.Equal(original, File.ReadAllBytes(attachment!.Path));
    }
}
