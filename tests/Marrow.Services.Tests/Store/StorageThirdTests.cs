using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Store;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Store;

// Tests for src/Marrow.Services/Store/Storage.cs, part 3 of the port of
// DroppyCode/Services/Store/Storage.swift (B2 1fbf81e). See StorageTests.cs / StorageSecondTests.cs for parts 1
// and 2, R-TEST-3. Covers PR-STORE-064 to PR-STORE-071 (attachment import, MIME table, HEIC/paste transcode) and
// PR-STORE-073 to PR-STORE-078 plus UI-STORE-004 (sweepOrphanedAttachments).
public sealed class StorageThirdTests
{
    // ---- PR-STORE-064 (importAttachment(from:)) ----

    [Fact]
    [Trait("Req", "PR-STORE-064")] // Services/Store/Storage.swift:96-103
    public void ImportAttachment_FromFile_UsesDotBinWhenTheSourceHasNoExtension()
    {
        using var scratch = new StorageScratchFolder();
        var source = Path.Combine(Path.GetTempPath(), "noext-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(source, "data");
        try
        {
            var attachment = Storage.ImportAttachment(source);
            Assert.Equal(Path.GetFileName(source), attachment.Name); // :102 source.lastPathComponent, original casing
            Assert.EndsWith(".bin", attachment.Path, StringComparison.Ordinal); // :99 ext.isEmpty ? "bin"
            Assert.Equal("text/plain", attachment.MimeType);
            Assert.True(File.Exists(source), "the original file must still exist: it was copied, not moved"); // :101
        }
        finally
        {
            File.Delete(source);
        }
    }

    [Fact]
    [Trait("Req", "PR-STORE-064")] // Services/Store/Storage.swift:99: the extension is lowercased for the destination
    public void ImportAttachment_FromFile_LowercasesTheExtensionOnTheDestinationName()
    {
        using var scratch = new StorageScratchFolder();
        var source = Path.Combine(Path.GetTempPath(), "upper-" + Guid.NewGuid().ToString("N") + ".JSON");
        File.WriteAllText(source, "{}");
        try
        {
            var attachment = Storage.ImportAttachment(source);
            Assert.EndsWith(".json", attachment.Path, StringComparison.Ordinal); // :99 ext.lowercased()
            Assert.Equal("application/json", attachment.MimeType);
            Assert.Equal(Path.GetFileName(source), attachment.Name); // :102 name keeps original casing (.JSON)
        }
        finally
        {
            File.Delete(source);
        }
    }

    [Fact]
    [Trait("Req", "PR-STORE-064")] // Services/Store/Storage.swift:100-101: fresh uppercase UUID under attachments/
    public void ImportAttachment_FromFile_NamesTheDestinationWithAFreshUppercaseUuid()
    {
        using var scratch = new StorageScratchFolder();
        var source = Path.Combine(Path.GetTempPath(), "src-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(source, "x");
        try
        {
            var attachment = Storage.ImportAttachment(source);
            var fileName = Path.GetFileNameWithoutExtension(attachment.Path);
            Assert.Equal(fileName, fileName.ToUpperInvariant()); // R-UUID-1: uppercase uuidString
            Assert.True(Guid.TryParse(fileName, out var parsed));
            Assert.Equal(attachment.Id, parsed);
            Assert.StartsWith(Storage.AttachmentsDirectory, attachment.Path, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(source);
        }
    }

    // ---- PR-STORE-065 (importAttachment(data:name:fileExtension:)) ----

    [Fact]
    [Trait("Req", "PR-STORE-065")] // Services/Store/Storage.swift:105-110
    public void ImportAttachment_FromBytes_WritesAtomicallyUnderAFreshUuidUsingTheExtensionExactlyAsPassed()
    {
        using var scratch = new StorageScratchFolder();
        var bytes = new byte[] { 1, 2, 3, 4, 5 };

        // The extension is used exactly as passed: no lowercasing, unlike importAttachment(from:). Both real
        // callers pass a lowercase literal ("jpg"/"png"); this proves the port does not additionally lowercase.
        var attachment = Storage.ImportAttachment(bytes, "custom-name.bin", "JpG");

        Assert.Equal("custom-name.bin", attachment.Name); // :109 name is whatever the caller passed, unchanged
        Assert.EndsWith(".JpG", attachment.Path, StringComparison.Ordinal); // :107 no .lowercased() call here
        Assert.Equal(bytes, File.ReadAllBytes(attachment.Path)); // :108 .atomic write, unchanged bytes
        Assert.Equal("image/jpeg", attachment.MimeType); // MimeType.Of lowercases before its own switch (PR-STORE-066)
    }

    [Fact]
    [Trait("Req", "PR-STORE-065")] // Services/Store/Storage.swift:108: options: .atomic
    public void ImportAttachment_FromBytes_TheDestinationIsUnderAttachmentsDirectory()
    {
        using var scratch = new StorageScratchFolder();
        var attachment = Storage.ImportAttachment([9, 9], "n", "png");
        Assert.StartsWith(Storage.AttachmentsDirectory, attachment.Path, StringComparison.Ordinal);
        Assert.True(File.Exists(attachment.Path));
    }

    // ---- PR-STORE-066 (MimeType.of) ----

    [Theory]
    [Trait("Req", "PR-STORE-066")] // Services/Store/Storage.swift:229-243
    [InlineData("png", "image/png")]
    [InlineData("PNG", "image/png")] // .lowercased() inside MimeType.of
    [InlineData("jpg", "image/jpeg")]
    [InlineData("jpeg", "image/jpeg")]
    [InlineData("gif", "image/gif")]
    [InlineData("webp", "image/webp")]
    [InlineData("heic", "image/heic")]
    [InlineData("pdf", "application/pdf")]
    [InlineData("json", "application/json")]
    [InlineData("md", "text/markdown")]
    [InlineData("markdown", "text/markdown")]
    [InlineData("txt", "text/plain")] // default branch
    [InlineData("heif", "text/plain")] // :66-67 .heif never reaches this table with "heif" (importSource transcodes first); the table itself has no heif case
    [InlineData("", "text/plain")] // default branch, no extension at all
    [InlineData("bin", "text/plain")]
    public void MimeType_Of_MapsTheTenBranchTableExactly(string extension, string expected)
    {
        Assert.Equal(expected, MimeType.Of(extension));
    }

    // ---- PR-STORE-067 (folder / missing source skipped) ----

    [Fact]
    [Trait("Req", "PR-STORE-067")] // Services/Store/Storage.swift:181-183: a source that does not exist is omitted
    public void ImportSource_SkipsASourceThatDoesNotExistAtAll()
    {
        using var scratch = new StorageScratchFolder();
        var missing = Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N") + ".txt");
        Assert.False(File.Exists(missing));
        Assert.Null(Storage.ImportSource(new AttachmentSource.File(missing)));
    }

    [Fact]
    [Trait("Req", "PR-STORE-067")] // Services/Store/Storage.swift:181-183: a folder is skipped rather than imported
    public void ImportSource_SkipsAFolderEvenWhenNamedLikeAnImage()
    {
        using var scratch = new StorageScratchFolder();
        var directory = Path.Combine(Path.GetTempPath(), "dir-" + Guid.NewGuid().ToString("N") + ".png");
        Directory.CreateDirectory(directory);
        try
        {
            Assert.Null(Storage.ImportSource(new AttachmentSource.File(directory)));
        }
        finally
        {
            Directory.Delete(directory);
        }
    }

    // ---- PR-STORE-068 / PR-STORE-070 / PR-STORE-071 (HEIC/HEIF transcode) ----

    [Theory]
    [Trait("Req", "PR-STORE-068")] // Services/Store/Storage.swift:176-188: heic and heif both transcode to jpg
    [InlineData("heic")]
    [InlineData("HEIC")] // :184 .lowercased() before the heic/heif compare
    [InlineData("heif")]
    public void ImportSource_TranscodesHeicAndHeifExtensionsToJpegAndRenames(string extension)
    {
        using var scratch = new StorageScratchFolder();
        // WIC's decoder factory (CreateDecoderFromFilename, :742) sniffs the container from the file's bytes, not
        // its extension, so a real BMP saved under a .heic/.heif name decodes exactly as a real HEIC photo would;
        // this exercises the same transcode path the record describes without needing a HEIC codec fixture.
        var source = Path.Combine(Path.GetTempPath(), "IMG_0042-" + Guid.NewGuid().ToString("N") + "." + extension);
        File.WriteAllBytes(source, MinimalBmpBytes(width: 6, height: 2));
        try
        {
            var attachment = Storage.ImportSource(new AttachmentSource.File(source));
            Assert.NotNull(attachment);
            var expectedBaseName = Path.GetFileNameWithoutExtension(source);
            Assert.Equal(expectedBaseName + ".jpg", attachment!.Name); // :188 deletingPathExtension().lastPathComponent + ".jpg"
            Assert.Equal("image/jpeg", attachment.MimeType);
            Assert.EndsWith(".jpg", attachment.Path, StringComparison.Ordinal);
            var bytes = File.ReadAllBytes(attachment.Path);
            Assert.Equal(0xFF, bytes[0]); Assert.Equal(0xD8, bytes[1]); // JPEG SOI marker
        }
        finally
        {
            File.Delete(source);
        }
    }

    [Fact]
    [Trait("Req", "PR-STORE-070")] // Services/Store/Storage.swift:201-212: full size, never upscaled
    public void ImportSource_TranscodedHeicIsReEncodedAtTheSourceSPixelSize()
    {
        using var scratch = new StorageScratchFolder();
        var source = Path.Combine(Path.GetTempPath(), "wide-" + Guid.NewGuid().ToString("N") + ".heic");
        File.WriteAllBytes(source, MinimalBmpBytes(width: 6, height: 2)); // non-square, so width != height is observable
        try
        {
            var attachment = Storage.ImportSource(new AttachmentSource.File(source));
            Assert.NotNull(attachment);
            // :208 kCGImageSourceThumbnailMaxPixelSize: max(width, height) never downscales or upscales a source
            // whose longest side is already <= the requested size, so the decoded output keeps the exact source
            // pixel dimensions. requestedPixels is set far above 6 (the source's longest side) to prove this.
            var decoded = WicImageDecoder.Decode(attachment!.Path, requestedPixels: 4096, fillingSquare: false);
            Assert.NotNull(decoded);
            Assert.Equal(6, decoded!.Width);
            Assert.Equal(2, decoded.Height);
        }
        finally
        {
            File.Delete(source);
        }
    }

    [Fact]
    [Trait("Req", "PR-STORE-070")] // Services/Store/Storage.swift:204-206: missing pixel dimensions -> nil, attachment dropped
    public void ImportSource_DropsAHeicSourceWhoseBytesAreNotADecodableImage()
    {
        using var scratch = new StorageScratchFolder();
        var source = Path.Combine(Path.GetTempPath(), "bad-" + Guid.NewGuid().ToString("N") + ".heic");
        File.WriteAllBytes(source, [0x00, 0x01, 0x02, 0x03]); // no codec can find pixel dimensions in this
        try
        {
            Assert.Null(Storage.ImportSource(new AttachmentSource.File(source))); // :188 try? drops the attachment
            Assert.False(Directory.EnumerateFileSystemEntries(Storage.AttachmentsDirectory).Any()); // nothing copied
        }
        finally
        {
            File.Delete(source);
        }
    }

    [Fact]
    [Trait("Req", "PR-STORE-071")] // Services/Store/Storage.swift:215: kCGImageDestinationLossyCompressionQuality: 0.88
    public void HeicTranscodeQuality_IsExactlyPointEightEight()
    {
        Assert.Equal(0.88, Storage.HeicTranscodeQuality);
    }

    // ---- PR-STORE-069 / UI-STORE-004 (paste) are proven by StorageTests.ImportSource_ImagePassesThroughPngDataUnchanged
    // and ImportSource_TranscodesANonPngPasteToPng (Services/Store/Storage.swift:189-197); see that file. ----

    // ---- PR-STORE-073 / PR-STORE-074 (sweepOrphanedAttachments: guards, and never decoding a thread document) ----

    [Fact]
    [Trait("Req", "PR-STORE-074")] // Services/Store/Storage.swift:122: !names.isEmpty short-circuits an empty attachments/
    public async Task SweepOrphanedAttachments_DoesNothingWhenAttachmentsDirectoryIsEmpty()
    {
        using var scratch = new StorageScratchFolder();
        _ = Storage.AttachmentsDirectory; // create it, empty
        await Storage.SweepOrphanedAttachments();
        Assert.False(Directory.EnumerateFileSystemEntries(Storage.AttachmentsDirectory).Any());
    }

    [Fact]
    [Trait("Req", "PR-STORE-074")] // Services/Store/Storage.swift:122-123: a threads/ listing failure aborts, never deletes
    public async Task SweepOrphanedAttachments_AbortsAndKeepsEverythingWhenThreadsDirectoryCannotBeListed()
    {
        using var scratch = new StorageScratchFolder();
        var orphanName = MakeOrphanFileName("png");
        var attachmentPath = Path.Combine(Storage.AttachmentsDirectory, orphanName);
        File.WriteAllBytes(attachmentPath, [1]);
        SetAge(attachmentPath, TimeSpan.FromHours(48)); // old enough to qualify for deletion, if the sweep ran

        _ = Storage.ThreadsDirectory; // create it once (StorageLocations caches the path after this)
        Directory.Delete(Storage.ThreadsDirectory); // now the cached path names a directory that does not exist

        await Storage.SweepOrphanedAttachments();

        // PR-STORE-074: a listing failure for threads/ aborts the WHOLE sweep, so even an old, unreferenced,
        // correctly-named attachment survives; a transient failure must never be read as "no threads at all".
        Assert.True(File.Exists(attachmentPath));
    }

    [Fact]
    [Trait("Req", "PR-STORE-074")] // Services/Store/Storage.swift:122-123: an empty (but listable) threads/ still proceeds
    [Trait("Req", "PR-STORE-078")] // Services/Store/Storage.swift:141, :144-147: >24h old, unreferenced, deleted
    public async Task SweepOrphanedAttachments_ProceedsWithAnEmptyReferencedSetWhenThreadsDirectoryIsEmpty()
    {
        using var scratch = new StorageScratchFolder();
        var orphanName = MakeOrphanFileName("png");
        var attachmentPath = Path.Combine(Storage.AttachmentsDirectory, orphanName);
        File.WriteAllBytes(attachmentPath, [1]);
        SetAge(attachmentPath, TimeSpan.FromHours(48));
        _ = Storage.ThreadsDirectory; // exists and lists successfully, but has nothing in it

        await Storage.SweepOrphanedAttachments();

        Assert.False(File.Exists(attachmentPath)); // an install with no threads left sweeps every old attachment
    }

    [Fact]
    [Trait("Req", "PR-STORE-073")] // Services/Store/Storage.swift:125-131: the scan matches raw text, never JSON-decodes a thread
    public async Task SweepOrphanedAttachments_MatchesTheRawTextEvenWhenTheThreadFileIsNotValidJson()
    {
        using var scratch = new StorageScratchFolder();
        var referencedName = MakeOrphanFileName("png");
        var referencedPath = Path.Combine(Storage.AttachmentsDirectory, referencedName);
        File.WriteAllBytes(referencedPath, [1]);
        SetAge(referencedPath, TimeSpan.FromHours(48));

        // Not valid JSON at all (would fail any JSONDecoder.decode(ThreadDocument.self, ...)), but the sweep never
        // decodes a thread document (PR-STORE-073): it only regex-scans the raw bytes for the attachments pattern.
        var garbage = "{ this is not json, but it mentions " + WindowsAttachmentsPathText(referencedName) + " anyway";
        File.WriteAllText(Path.Combine(Storage.ThreadsDirectory, "garbled.json"), garbage);

        await Storage.SweepOrphanedAttachments();

        Assert.True(File.Exists(referencedPath)); // referenced by the raw text match, so it is not swept
    }

    // ---- PR-STORE-075 (matching the escaped path; .json suffix filter; unreadable file skipped) ----

    [Fact]
    [Trait("Req", "PR-STORE-075")] // Services/Store/Storage.swift:125: for document in documents where document.hasSuffix(".json")
    public async Task SweepOrphanedAttachments_OnlyScansFilesNamedWithADotJsonSuffix()
    {
        using var scratch = new StorageScratchFolder();
        var referencedName = MakeOrphanFileName("png");
        var referencedPath = Path.Combine(Storage.AttachmentsDirectory, referencedName);
        File.WriteAllBytes(referencedPath, [1]);
        SetAge(referencedPath, TimeSpan.FromHours(48));

        // The reference lives in a file that does NOT end in ".json"; it must be ignored, so the attachment it
        // "mentions" is still swept away as unreferenced.
        File.WriteAllText(Path.Combine(Storage.ThreadsDirectory, "notes.txt"), WindowsAttachmentsPathText(referencedName));

        await Storage.SweepOrphanedAttachments();

        Assert.False(File.Exists(referencedPath));
    }

    [Fact]
    [Trait("Req", "PR-STORE-075")] // Services/Store/Storage.swift:126: guard let data = try? Data(contentsOf:) else { continue }
    public async Task SweepOrphanedAttachments_SkipsAThreadFileThatCannotBeReadAndItsAttachmentsBecomeCandidates()
    {
        using var scratch = new StorageScratchFolder();
        var referencedName = MakeOrphanFileName("png");
        var referencedPath = Path.Combine(Storage.AttachmentsDirectory, referencedName);
        File.WriteAllBytes(referencedPath, [1]);
        SetAge(referencedPath, TimeSpan.FromHours(48));

        var threadPath = Path.Combine(Storage.ThreadsDirectory, "locked.json");
        File.WriteAllText(threadPath, WindowsAttachmentsPathText(referencedName));

        // Hold an exclusive handle open (R-TEST-9: file locks are tested with a real held-open handle) so
        // File.ReadAllBytes throws IOException inside the sweep's try? and this file is skipped with continue.
        using (var locked = new FileStream(threadPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Storage.SweepOrphanedAttachments();
        }

        Assert.False(File.Exists(referencedPath)); // its would-be reference was never read, so it is a candidate
    }

    [Fact]
    [Trait("Req", "PR-STORE-075")] // windows_path DEVIATION: the encoder's backslash separator, not macOS's escaped forward slash
    public async Task SweepOrphanedAttachments_MatchesAWindowsBackslashSeparatedPersistedPath()
    {
        using var scratch = new StorageScratchFolder();
        var referencedName = MakeOrphanFileName("png");
        var referencedPath = Path.Combine(Storage.AttachmentsDirectory, referencedName);
        File.WriteAllBytes(referencedPath, [1]);
        SetAge(referencedPath, TimeSpan.FromHours(48));
        // Exactly the shape SwiftJson writes a Windows path: one backslash separator, JSON-escaped to two raw
        // backslash characters in the file's bytes.
        var text = "{\"path\":\"C:" + new string('\\', 2) + "root" + new string('\\', 2) + "attachments" +
                   new string('\\', 2) + referencedName + "\"}";
        File.WriteAllText(Path.Combine(Storage.ThreadsDirectory, "thread.json"), text);

        await Storage.SweepOrphanedAttachments();

        Assert.True(File.Exists(referencedPath));
    }

    // ---- PR-STORE-076 (continuation) ----

    [Fact]
    [Trait("Req", "PR-STORE-076")] // Services/Store/Storage.swift:132-139: a continuation's own attachments stay referenced
    public async Task SweepOrphanedAttachments_KeepsAContinuationSAttachmentsReferenced()
    {
        using var scratch = new StorageScratchFolder();
        var referencedName = MakeOrphanFileName("jpg");
        var referencedPath = Path.Combine(Storage.AttachmentsDirectory, referencedName);
        File.WriteAllBytes(referencedPath, [1]);
        SetAge(referencedPath, TimeSpan.FromHours(48));

        // A real path value (backslash-escaped once by System.Text.Json, as the storage encoder would write it),
        // reachable only via the "continuation" JSON key, decoded with StoredContinuation, never the plain-text
        // regex (this file mentions no bare "attachments\\..." text at all).
        var path = Path.Combine(Storage.AttachmentsDirectory, referencedName).Replace("\\", "\\\\", StringComparison.Ordinal);
        var text = "{\"continuation\":{\"attachments\":[{\"id\":\"" + Guid.NewGuid().ToString().ToUpperInvariant() +
                   "\",\"name\":\"a.jpg\",\"path\":\"" + path + "\",\"mimeType\":\"image/jpeg\"}]}}";
        File.WriteAllText(Path.Combine(Storage.ThreadsDirectory, "continuation.json"), text);

        await Storage.SweepOrphanedAttachments();

        Assert.True(File.Exists(referencedPath));
    }

    [Fact]
    [Trait("Req", "PR-STORE-076")] // Services/Store/Storage.swift:135: try? JSONDecoder.storage.decode(...) - a decode failure contributes nothing
    public async Task SweepOrphanedAttachments_AContinuationThatFailsToDecodeContributesNothing()
    {
        using var scratch = new StorageScratchFolder();
        var referencedName = MakeOrphanFileName("jpg");
        var referencedPath = Path.Combine(Storage.AttachmentsDirectory, referencedName);
        File.WriteAllBytes(referencedPath, [1]);
        SetAge(referencedPath, TimeSpan.FromHours(48));

        // Contains the "continuation" substring (so the pre-test at :134 is true) but is not valid JSON at all, so
        // JsonDocument.Parse/StoredContinuation.Decode throws and try? swallows it (PR-STORE-076 failure case).
        var text = "{\"continuation\": not valid json, mentions " + referencedName + "}";
        File.WriteAllText(Path.Combine(Storage.ThreadsDirectory, "bad-continuation.json"), text);

        await Storage.SweepOrphanedAttachments();

        Assert.False(File.Exists(referencedPath)); // the decode failure contributed nothing; the file is swept
    }

    // ---- PR-STORE-077 (whole-match filename pattern) ----

    [Theory]
    [Trait("Req", "PR-STORE-077")] // Services/Store/Storage.swift:142-143: name.wholeMatch(of:) over the whole string
    [InlineData("not-a-uuid-at-all.png")] // too short / wrong shape
    [InlineData("A1B2C3D4-E5F6-7890-ABCD-EF1234567890.tar.gz")] // a two-part extension: [A-Za-z0-9]+ never matches the dot
    [InlineData(".A1B2C3D4-E5F6-7890-ABCD-EF1234567890.png")] // a leading dot / hidden file, one char too many before the ext
    [InlineData("A1B2C3D4_E5F6_7890_ABCD_EF1234567890.png")] // underscores are not in [0-9A-Fa-f-]
    public async Task SweepOrphanedAttachments_NeverDeletesAFileWhoseNameDoesNotWhollyMatchThePattern(string name)
    {
        using var scratch = new StorageScratchFolder();
        var path = Path.Combine(Storage.AttachmentsDirectory, name);
        File.WriteAllBytes(path, [1]);
        SetAge(path, TimeSpan.FromHours(48)); // old and unreferenced, so only the name pattern can save it
        _ = Storage.ThreadsDirectory;

        await Storage.SweepOrphanedAttachments();

        Assert.True(File.Exists(path));
    }

    // ---- PR-STORE-078 (24-hour cutoff) ----

    [Fact]
    [Trait("Req", "PR-STORE-078")] // Services/Store/Storage.swift:114-115, :141: a draft still open at relaunch keeps its files
    public async Task SweepOrphanedAttachments_KeepsAnUnreferencedFileThatIsLessThanADayOld()
    {
        using var scratch = new StorageScratchFolder();
        var name = MakeOrphanFileName("png");
        var path = Path.Combine(Storage.AttachmentsDirectory, name);
        File.WriteAllBytes(path, [1]);
        SetAge(path, TimeSpan.FromHours(1)); // well inside the 24h window
        _ = Storage.ThreadsDirectory;

        await Storage.SweepOrphanedAttachments();

        Assert.True(File.Exists(path));
    }

    [Fact]
    [Trait("Req", "PR-STORE-078")] // Services/Store/Storage.swift:141-147: strictly older than the 24h cutoff is removed
    public async Task SweepOrphanedAttachments_RemovesAnUnreferencedFileJustPastTheDayCutoff()
    {
        using var scratch = new StorageScratchFolder();
        var name = MakeOrphanFileName("png");
        var path = Path.Combine(Storage.AttachmentsDirectory, name);
        File.WriteAllBytes(path, [1]);
        SetAge(path, TimeSpan.FromHours(24) + TimeSpan.FromMinutes(5)); // strictly older than 24h
        _ = Storage.ThreadsDirectory;

        await Storage.SweepOrphanedAttachments();

        Assert.False(File.Exists(path));
    }

    // ---- helpers ----

    private static string MakeOrphanFileName(string extension) => Guid.NewGuid().ToString().ToUpperInvariant() + "." + extension;

    private static void SetAge(string path, TimeSpan age) =>
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);

    // The raw JSON text an encoded Attachment.path produces for a file directly under Storage.AttachmentsDirectory
    // on Windows: one backslash path separator, escaped to two raw backslash characters in the file's bytes.
    private static string WindowsAttachmentsPathText(string attachmentFileName) =>
        "\"path\":\"C:" + new string('\\', 2) + "root" + new string('\\', 2) + "attachments" +
        new string('\\', 2) + attachmentFileName + "\"";

    private static byte[] MinimalBmpBytes(int width, int height)
    {
        var rowBytes = width * 3;
        var padding = (4 - rowBytes % 4) % 4;
        var pixels = new byte[(rowBytes + padding) * height];
        for (var i = 0; i < width * height; i++) pixels[i * 3] = 0x80; // arbitrary content, blue channel
        var fileHeaderSize = 14;
        var infoHeaderSize = 40;
        var pixelOffset = fileHeaderSize + infoHeaderSize;
        var fileSize = pixelOffset + pixels.Length;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)'B'); writer.Write((byte)'M');
        writer.Write(fileSize);
        writer.Write(0);
        writer.Write(pixelOffset);
        writer.Write(infoHeaderSize);
        writer.Write(width);
        writer.Write(height);
        writer.Write((short)1);
        writer.Write((short)24);
        writer.Write(0);
        writer.Write(pixels.Length);
        writer.Write(2835); writer.Write(2835);
        writer.Write(0); writer.Write(0);
        writer.Write(pixels);
        return stream.ToArray();
    }
}
