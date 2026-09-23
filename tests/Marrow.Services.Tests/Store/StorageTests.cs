using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Store;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Store;

// Tests for src/Marrow.Services/Store/Storage.cs, part 1 of the port of
// DroppyCode/Services/Store/Storage.swift (B2 1fbf81e), :1-390.
public sealed class StorageTests
{
    // ---- PR-LAUNCH-070 / PR-STORE-001 / PR-STORE-002 ----

    [Fact]
    [Trait("Req", "PR-STORE-001")] // Services/Store/Storage.swift:19-25
    public void Root_IsCreatedWithIntermediatesAndCachedForTheScope()
    {
        using var scratch = new StorageScratchFolder();
        Assert.False(Directory.Exists(scratch.Root));

        var first = Storage.Root;
        Assert.True(Directory.Exists(first));
        Assert.Equal(scratch.Root, first);

        Directory.Delete(first); // prove the second access is the cached value, not a fresh stat/mkdir
        Assert.Equal(first, Storage.Root);
    }

    [Fact]
    [Trait("Req", "PR-STORE-002")] // Services/Store/Storage.swift:27
    public void LibraryURL_IsLibraryJsonAtTheRoot()
    {
        using var scratch = new StorageScratchFolder();
        Assert.Equal(Path.Combine(scratch.Root, "library.json"), Storage.LibraryURL);
    }

    [Fact]
    [Trait("Req", "PR-STORE-003")] // Services/Store/Storage.swift:29-31
    public void ThreadsDirectory_IsCreatedUnderTheRoot()
    {
        using var scratch = new StorageScratchFolder();
        var directory = Storage.ThreadsDirectory;
        Assert.Equal(Path.Combine(scratch.Root, "threads"), directory);
        Assert.True(Directory.Exists(directory));
    }

    [Fact]
    [Trait("Req", "PR-STORE-004")] // Services/Store/Storage.swift:33
    public void AttachmentsDirectory_IsCreatedUnderTheRoot()
    {
        using var scratch = new StorageScratchFolder();
        var directory = Storage.AttachmentsDirectory;
        Assert.Equal(Path.Combine(scratch.Root, "attachments"), directory);
        Assert.True(Directory.Exists(directory));
    }

    [Fact]
    [Trait("Req", "PR-STORE-005")] // Services/Store/Storage.swift:35-36
    public void WallpaperDirectory_IsCreatedUnderTheRoot()
    {
        using var scratch = new StorageScratchFolder();
        var directory = Storage.WallpaperDirectory;
        Assert.Equal(Path.Combine(scratch.Root, "wallpaper"), directory);
        Assert.True(Directory.Exists(directory));
    }

    [Fact]
    [Trait("Req", "PR-STORE-006")] // Services/Store/Storage.swift:38, :43-48; DEVIATION OD-0012 F1
    public void WorktreesDirectory_LivesUnderTheHomeStateDirectory_NotUnderTheRoot()
    {
        using var scratch = new StorageScratchFolder();
        var directory = Storage.WorktreesDirectory;
        Assert.Equal(Path.Combine(scratch.Home, ".marrow", "worktrees"), directory);
        Assert.True(Directory.Exists(directory));
        Assert.DoesNotContain(scratch.Root, directory, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Req", "PR-STORE-006")] // Services/Store/Storage.swift:43-48; DEVIATION OD-0012 F1 (development suffix)
    public void WorktreesDirectory_UsesTheDevelopmentSuffixInADevelopmentBuild()
    {
        using var scratch = new StorageScratchFolder(isDevelopment: true);
        Assert.Equal(Path.Combine(scratch.Home, ".marrow-dev", "worktrees"), Storage.WorktreesDirectory);
    }

    [Fact]
    [Trait("Req", "PR-STORE-007")] // Services/Store/Storage.swift:40-41
    public void PatchesDirectory_LivesUnderTheHomeStateDirectory()
    {
        using var scratch = new StorageScratchFolder();
        var directory = Storage.PatchesDirectory;
        Assert.Equal(Path.Combine(scratch.Home, ".marrow", "patches"), directory);
        Assert.True(Directory.Exists(directory));
    }

    // ---- PR-STORE-010 ----

    [Fact]
    [Trait("Req", "PR-STORE-010")] // Services/Store/Storage.swift:50-52
    public void ThreadURL_IsTheUppercaseUuidWithDashesPlusJson()
    {
        using var scratch = new StorageScratchFolder();
        var id = Guid.NewGuid();
        var expected = Path.Combine(Storage.ThreadsDirectory, SwiftUUID.UuidString(id) + ".json");
        Assert.Equal(expected, Storage.ThreadURL(id));
        Assert.Equal(SwiftUUID.UuidString(id).ToUpperInvariant(), SwiftUUID.UuidString(id)); // uppercase, no lowering
    }

    // ---- PR-STORE-011 ----

    [Fact]
    [Trait("Req", "PR-STORE-011")] // Services/Store/Storage.swift and Core/Models/MCP.swift:244,246
    public void EveryFirstLevelStoreFile_HangsOffTheSameRoot()
    {
        using var scratch = new StorageScratchFolder();
        var root = Storage.Root;
        Assert.Equal(Path.Combine(root, "library.json"), Storage.LibraryURL);
        Assert.Equal(Path.Combine(root, "threads"), Storage.ThreadsDirectory);
        Assert.Equal(Path.Combine(root, "attachments"), Storage.AttachmentsDirectory);
        Assert.Equal(Path.Combine(root, "wallpaper"), Storage.WallpaperDirectory);
        Assert.Equal(Path.Combine(root, "mcp.json"), Marrow.Services.Models.MCPPaths.ConnectionsURL);
        Assert.Equal(Path.Combine(root, "custom-mcp.json"), Marrow.Services.Models.MCPPaths.CustomsURL);
        Assert.Equal(Path.Combine(root, "mcp"), Marrow.Services.Models.MCPPaths.Directory);
    }

    // ---- PR-STORE-012 / PR-STORE-013 ----

    [Fact]
    [Trait("Req", "PR-STORE-012")] // Services/Store/Storage.swift:60-63
    public void LoadLibrary_IsEmptyWhenLibraryJsonDoesNotExist()
    {
        using var scratch = new StorageScratchFolder();
        var result = Storage.LoadLibrary();
        var empty = Assert.IsType<LibraryLoadResult.Empty>(result);
        Assert.NotNull(empty);
    }

    [Fact]
    [Trait("Req", "PR-STORE-012")] // Services/Store/Storage.swift:60-63
    public void LoadLibrary_DecodesAnExistingWellFormedFile()
    {
        using var scratch = new StorageScratchFolder();
        var library = new Library();
        File.WriteAllBytes(Storage.LibraryURL, Coding.Encode(library));

        var result = Storage.LoadLibrary();
        var loaded = Assert.IsType<LibraryLoadResult.Loaded>(result);
        Assert.Empty(loaded.Library.Projects);
        Assert.Empty(loaded.Library.Threads);
    }

    [Fact]
    [Trait("Decision", "OD-0008")] // OD-0008 A10 / PR-STORE-013: DEVIATION from Storage.swift:60-63
    public void LoadLibrary_IsFailedWhenLibraryJsonExistsButIsNotDecodable()
    {
        using var scratch = new StorageScratchFolder();
        File.WriteAllText(Storage.LibraryURL, "not json at all");

        var result = Storage.LoadLibrary();
        Assert.IsType<LibraryLoadResult.Failed>(result);
    }

    [Fact]
    [Trait("Decision", "OD-0008")] // OD-0008 A10 / PR-STORE-013: a directory in place of the file is a read failure, not "missing"
    public void LoadLibrary_IsFailedWhenLibraryJsonCannotBeRead()
    {
        using var scratch = new StorageScratchFolder();
        Directory.CreateDirectory(Storage.LibraryURL); // a directory at the file's own path: File.Exists is false, Directory.Exists is true

        var result = Storage.LoadLibrary();
        var failed = Assert.IsType<LibraryLoadResult.Failed>(result);
        Assert.NotNull(failed.Error);
    }

    // ---- PR-STORE-019 / PR-STORE-022 / PR-STORE-023 / PR-STORE-026 / PR-STORE-027 ----

    [Fact]
    [Trait("Req", "PR-STORE-022")] // Services/Store/Storage.swift:75
    public void ReadDocument_IsNullWhenTheThreadFileDoesNotExist()
    {
        using var scratch = new StorageScratchFolder();
        Assert.Null(Storage.ReadDocument(Guid.NewGuid()));
    }

    [Fact]
    [Trait("Req", "PR-STORE-023")] // Services/Store/Storage.swift:78
    public void ReadDocument_IsNullWhenTheThreadFileIsNotDecodable()
    {
        using var scratch = new StorageScratchFolder();
        var id = Guid.NewGuid();
        File.WriteAllText(Storage.ThreadURL(id), "{ \"nope\": true }"); // no threadID
        Assert.Null(Storage.ReadDocument(id));
    }

    [Fact]
    [Trait("Req", "PR-STORE-026")] // Services/Store/Storage.swift:80
    [Trait("Req", "PR-STORE-019")] // Services/Store/Storage.swift:76-78 (Coding.StorageOptions is genuinely thread-safe: windows_path)
    public void ReadDocument_ReportsTheRawFileByteCountAsTheCost()
    {
        using var scratch = new StorageScratchFolder();
        var id = Guid.NewGuid();
        var document = new ThreadDocument(id);
        var bytes = Coding.Encode(document);
        File.WriteAllBytes(Storage.ThreadURL(id), bytes);

        var result = Storage.ReadDocument(id);
        Assert.NotNull(result);
        Assert.Equal(bytes.Length, result!.Value.Cost);
        Assert.Equal(id, result.Value.Document.ThreadID);
    }

    [Fact]
    [Trait("Req", "PR-STORE-027")] // Services/Store/Storage.swift:66-68
    public void DecodeDocument_IsReadDocumentWithTheCostDropped()
    {
        using var scratch = new StorageScratchFolder();
        var id = Guid.NewGuid();
        File.WriteAllBytes(Storage.ThreadURL(id), Coding.Encode(new ThreadDocument(id)));

        Assert.Equal(Storage.ReadDocument(id)!.Value.Document, Storage.DecodeDocument(id));
        Assert.Null(Storage.DecodeDocument(Guid.NewGuid())); // the missing-file case too
    }

    [Fact]
    [Trait("Req", "PR-STORE-024")] // Services/Store/Storage.swift:79, Core/Models/Timeline.swift:28-32
    public void ReadDocument_DropsBlankReasoningItemsButKeepsOtherBlankContent()
    {
        using var scratch = new StorageScratchFolder();
        var id = Guid.NewGuid();
        var blankReasoning = new TimelineItem(null, new TimelineItem.Content.Reasoning(new ReasoningBlock { Text = "   " }));
        var textReasoning = new TimelineItem(null, new TimelineItem.Content.Reasoning(new ReasoningBlock { Text = "kept" }));
        var blankAssistant = new TimelineItem(null, new TimelineItem.Content.Assistant(new AssistantMessage { Text = "" }));
        var document = new ThreadDocument(id) with
        {
            Items = new ValueList<TimelineItem>([blankReasoning, textReasoning, blankAssistant]),
        };
        File.WriteAllBytes(Storage.ThreadURL(id), Coding.Encode(document));

        var loaded = Storage.ReadDocument(id)!.Value.Document;
        Assert.Equal(2, loaded.Items.Count);
        Assert.IsType<TimelineItem.Content.Reasoning>(loaded.Items[0].TimelineItemContent);
        Assert.IsType<TimelineItem.Content.Assistant>(loaded.Items[1].TimelineItemContent); // the blank assistant message is kept
    }

    [Fact]
    [Trait("Req", "PR-STORE-025")] // Services/Store/Storage.swift:79, Core/Models/Timeline.swift:39-55
    public void ReadDocument_FlattensEmDashesInAssistantReasoningAndPlanTextOnlyNotUserText()
    {
        using var scratch = new StorageScratchFolder();
        var id = Guid.NewGuid();
        const string dashed = "a—b"; // U+2014 EM DASH
        const string flattened = "a-b";
        var user = new TimelineItem(null, new TimelineItem.Content.User(new UserMessage { Text = dashed }));
        var assistant = new TimelineItem(null, new TimelineItem.Content.Assistant(new AssistantMessage { Text = dashed }));
        var reasoning = new TimelineItem(null, new TimelineItem.Content.Reasoning(new ReasoningBlock { Text = dashed }));
        var plan = new TimelineItem(null, new TimelineItem.Content.Plan(new ProposedPlan { Markdown = dashed, ProposedPlanState = ProposedPlan.State.Drafting }));
        var document = new ThreadDocument(id) with
        {
            Items = new ValueList<TimelineItem>([user, assistant, reasoning, plan]),
        };
        File.WriteAllBytes(Storage.ThreadURL(id), Coding.Encode(document));

        var loaded = Storage.ReadDocument(id)!.Value.Document;
        Assert.Equal(dashed, ((TimelineItem.Content.User)loaded.Items[0].TimelineItemContent).Value.Text);
        Assert.Equal(flattened, ((TimelineItem.Content.Assistant)loaded.Items[1].TimelineItemContent).Value.Text);
        Assert.Equal(flattened, ((TimelineItem.Content.Reasoning)loaded.Items[2].TimelineItemContent).Value.Text);
        Assert.Equal(flattened, ((TimelineItem.Content.Plan)loaded.Items[3].TimelineItemContent).Value.Markdown);
    }

    // ---- PR-STORE-021 ----

    [Fact]
    [Trait("Req", "PR-STORE-021")] // Services/Store/Storage.swift:73-75, :376-379
    public void ReadDocument_WaitsForAQueuedWriteOfTheSameFileBeforeReading()
    {
        using var scratch = new StorageScratchFolder();
        var id = Guid.NewGuid();
        var document = new ThreadDocument(id);

        DiskWriter.Shared.EncodeAndWrite(document, Storage.ThreadURL(id)); // queued, not necessarily landed yet
        var result = Storage.ReadDocument(id); // must block on the writer queue first

        Assert.NotNull(result);
        Assert.Equal(id, result!.Value.Document.ThreadID);
    }

    // ---- PR-STORE-030 ----

    [Fact]
    [Trait("Req", "PR-STORE-030")] // Services/Store/Storage.swift:90-91
    public void DeleteDocuments_ReturnsImmediatelyForAnEmptyList()
    {
        using var scratch = new StorageScratchFolder();
        var exception = Record.Exception(() => Storage.DeleteDocuments([]));
        Assert.Null(exception);
    }

    [Fact]
    [Trait("Req", "PR-STORE-030")] // Services/Store/Storage.swift:87-94: a non-empty delete removes the thread file(s)
    public void DeleteDocuments_RemovesTheThreadFilesAndForgetsThePrefetchEntry()
    {
        using var scratch = new StorageScratchFolder();
        var id = Guid.NewGuid();
        File.WriteAllBytes(Storage.ThreadURL(id), Coding.Encode(new ThreadDocument(id)));
        Assert.True(File.Exists(Storage.ThreadURL(id)));

        Storage.DeleteDocuments([id]);

        Assert.False(File.Exists(Storage.ThreadURL(id)));
    }

    // ---- PR-COMPOSER-061 / PR-COMPOSER-062 (Attach and ImportSource) ----

    [Fact]
    [Trait("Req", "PR-STORE-011")] // ImportAttachment(from:) - Services/Store/Storage.swift:97-103
    public void ImportAttachment_CopiesTheFileUnderAttachmentsDirectoryNamedByAFreshUuid()
    {
        using var scratch = new StorageScratchFolder();
        var source = Path.Combine(Path.GetTempPath(), "source-" + Guid.NewGuid().ToString("N") + ".TXT");
        File.WriteAllText(source, "hello");
        try
        {
            var attachment = Storage.ImportAttachment(source);
            Assert.Equal("source-" + Path.GetFileName(source)[7..], attachment.Name); // lastPathComponent, original case
            Assert.Equal("hello", File.ReadAllText(attachment.Path));
            Assert.Equal("text/plain", attachment.MimeType); // .TXT lowercased is not in MimeType.Of's switch, falls to default
            Assert.StartsWith(Storage.AttachmentsDirectory, attachment.Path, StringComparison.Ordinal);
            Assert.EndsWith(".txt", attachment.Path, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(source);
        }
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-062")] // Services/Store/Storage.swift:189-197
    public void ImportSource_ImagePassesThroughPngDataUnchanged()
    {
        using var scratch = new StorageScratchFolder();
        var png = MinimalPngBytes();
        var attachment = Storage.ImportSource(new AttachmentSource.Image(png, AttachmentSource.PngMimeType));

        Assert.NotNull(attachment);
        Assert.Equal("Pasted image.png", attachment!.Name);
        Assert.Equal("image/png", attachment.MimeType);
        Assert.Equal(png, File.ReadAllBytes(attachment.Path));
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-062")] // Services/Store/Storage.swift:190-195, :203-219
    public void ImportSource_TranscodesANonPngPasteToPng()
    {
        using var scratch = new StorageScratchFolder();
        var bmp = MinimalBmpBytes();
        var attachment = Storage.ImportSource(new AttachmentSource.Image(bmp, "image/bmp"));

        Assert.NotNull(attachment);
        Assert.Equal("Pasted image.png", attachment!.Name);
        Assert.Equal("image/png", attachment.MimeType);
        var written = File.ReadAllBytes(attachment.Path);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, written[..4]); // PNG signature
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-062")] // Services/Store/Storage.swift:181-183
    public void ImportSource_SkipsADirectory()
    {
        using var scratch = new StorageScratchFolder();
        var directory = Path.Combine(Path.GetTempPath(), "dir-" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    [Trait("Req", "PR-COMPOSER-062")] // Services/Store/Storage.swift:184-185: anything else is copied as it is
    public void ImportSource_CopiesANonImageFileAsIs()
    {
        using var scratch = new StorageScratchFolder();
        var source = Path.Combine(Path.GetTempPath(), "note-" + Guid.NewGuid().ToString("N") + ".md");
        File.WriteAllText(source, "# hi");
        try
        {
            var attachment = Storage.ImportSource(new AttachmentSource.File(source));
            Assert.NotNull(attachment);
            Assert.Equal("text/markdown", attachment!.MimeType);
            Assert.Equal("# hi", File.ReadAllText(attachment.Path));
        }
        finally
        {
            File.Delete(source);
        }
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-062")] // Services/Store/Storage.swift:186-188: a transcode that fails is dropped with no notice
    public void ImportSource_DropsAHeicFileWhoseBytesCannotBeDecoded()
    {
        using var scratch = new StorageScratchFolder();
        var source = Path.Combine(Path.GetTempPath(), "photo-" + Guid.NewGuid().ToString("N") + ".heic");
        File.WriteAllBytes(source, [0x00, 0x01, 0x02, 0x03]); // not real image data
        try
        {
            Assert.Null(Storage.ImportSource(new AttachmentSource.File(source)));
        }
        finally
        {
            File.Delete(source);
        }
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-061")] // Services/Store/Storage.swift:161-174
    public void Attach_CountsRoomAtCallTimeAndReturnsNullWhenNoRoom()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var full = new ValueList<Attachment>(Enumerable.Range(0, Storage.AttachmentLimit)
            .Select(_ => new Attachment { Name = "a", Path = "p", MimeType = "text/plain" }));

        TaskHandle? task = null;
        runtime.Loop.Run(() =>
        {
            task = Storage.Attach([MakeTextSource("x.md")], () => full, _ => throw new InvalidOperationException("must not be called"));
            return Task.CompletedTask;
        });

        Assert.Null(task);
    }

    [Fact]
    [Trait("Req", "PR-COMPOSER-061")] // Services/Store/Storage.swift:161-174: room is recounted when the copies land, orphans removed
    public void Attach_RecountsRoomWhenCopiesLandAndDeletesOrphansPastTheLimit()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var sources = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var path = Path.Combine(Path.GetTempPath(), "attach-" + Guid.NewGuid().ToString("N") + ".md");
            File.WriteAllText(path, "content " + i);
            sources.Add(path);
        }
        try
        {
            var current = new ValueList<Attachment>([]);
            // Room shrinks to 1 by the time the copies land, simulating "more may have been attached meanwhile".
            var firstCall = true;
            TaskHandle? task = null;
            runtime.Loop.Run(() =>
            {
                task = Storage.Attach(
                    sources.Select(s => (AttachmentSource)new AttachmentSource.File(s)).ToArray(),
                    () => current,
                    value =>
                    {
                        if (firstCall)
                        {
                            firstCall = false;
                            current = new ValueList<Attachment>(Enumerable.Range(0, Storage.AttachmentLimit - 1)
                                .Select(_ => new Attachment { Name = "a", Path = "p", MimeType = "text/plain" }));
                        }
                        current = value;
                    });
                return Task.CompletedTask;
            });

            Assert.NotNull(task);
            runtime.Loop.RunUntil(() => task!.Completion.IsCompleted);
            Assert.True(task!.Completion.IsCompletedSuccessfully);

            // Only 1 of the 3 imported copies should have been kept; the other 2 files should have been deleted.
            var keptCount = current.Count - (Storage.AttachmentLimit - 1);
            Assert.True(keptCount is >= 0 and <= 1);
        }
        finally
        {
            foreach (var s in sources) SwiftTry.Ignore(() => File.Delete(s));
        }
    }

    private static AttachmentSource MakeTextSource(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), name);
        return new AttachmentSource.File(path);
    }

    private static byte[] MinimalPngBytes() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
        0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41,
        0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
        0x00, 0x03, 0x01, 0x01, 0x00, 0x18, 0xDD, 0x8D,
        0xB0, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,
        0x44, 0xAE, 0x42, 0x60, 0x82,
    ];

    // A 2x2, 24-bit uncompressed BMP: BITMAPFILEHEADER + BITMAPINFOHEADER + 2 padded 6-byte rows.
    private static byte[] MinimalBmpBytes()
    {
        var pixels = new byte[]
        {
            0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00, // row 0, padded to a multiple of 4
            0x00, 0xFF, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00, // row 1, padded
        };
        var fileHeaderSize = 14;
        var infoHeaderSize = 40;
        var pixelOffset = fileHeaderSize + infoHeaderSize;
        var fileSize = pixelOffset + pixels.Length;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)'B'); writer.Write((byte)'M');
        writer.Write(fileSize);
        writer.Write(0); // reserved
        writer.Write(pixelOffset);
        writer.Write(infoHeaderSize);
        writer.Write(2); // width
        writer.Write(2); // height
        writer.Write((short)1); // planes
        writer.Write((short)24); // bpp
        writer.Write(0); // compression
        writer.Write(pixels.Length);
        writer.Write(2835); writer.Write(2835); // ppm
        writer.Write(0); writer.Write(0);
        writer.Write(pixels);
        return stream.ToArray();
    }
}
