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
    // PR-STORE-001 and -005's own creation behaviour is proved directly against Core.Support.StoragePaths
    // (Marrow.Core.Tests/Support/StoragePathsTests.cs, N2 in porting/work/b3/B3_PLAN.md section 3: root,
    // wallpaperDirectory and directory(_:) moved out of this file's B2 :19-25, :35-36 and :54-58) - but that
    // helper's own directory(_:) (StoragePaths.swift :18-22 / StoragePaths.cs Directory) is deliberately never
    // cached there (StoragePathsTests.Directory_CreatesTheNamedFolderUnderRootAndIsNeverCached proves it).
    // PR-STORE-003/-004's caching comes from Storage.swift's own `static let threadsDirectory` / `static let
    // attachmentsDirectory` (:23-27) wrapping that uncached call exactly once for the process's life ("a stat
    // plus mkdir per call was pure waste"), so StorageLocations.ThreadsDirectory/.AttachmentsDirectory must
    // cache it themselves (mirroring WorktreesDirectory/PatchesDirectory below) - proved by
    // ThreadsDirectory_IsCachedAndNotRecreatedOnSubsequentAccess and its Attachments twin further down.
    // These properties stay here proving Storage's own public properties still resolve correctly once the
    // move routes them through Core.Support.StoragePaths.

    [Fact]
    [Trait("Req", "PR-STORE-001")] // Storage.swift:19 (B3, delegates to StoragePaths.root); moved implementation StoragePaths.swift:6-12
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
    [Trait("Req", "PR-STORE-002")] // Services/Store/Storage.swift:21 (B3)
    public void LibraryURL_IsLibraryJsonAtTheRoot()
    {
        using var scratch = new StorageScratchFolder();
        Assert.Equal(Path.Combine(scratch.Root, "library.json"), Storage.LibraryURL);
    }

    [Fact]
    [Trait("Req", "PR-STORE-003")] // Storage.swift:25 (B3); moved implementation StoragePaths.swift:18-22
    public void ThreadsDirectory_IsCreatedUnderTheRoot()
    {
        using var scratch = new StorageScratchFolder();
        var directory = Storage.ThreadsDirectory;
        Assert.Equal(Path.Combine(scratch.Root, "threads"), directory);
        Assert.True(Directory.Exists(directory));
    }

    [Fact]
    [Trait("Req", "PR-STORE-003")] // Storage.swift:23-25 comment: "a stat plus mkdir per call was pure waste".
    public void ThreadsDirectory_IsCachedAndNotRecreatedOnSubsequentAccess()
    {
        using var scratch = new StorageScratchFolder();
        var first = Storage.ThreadsDirectory;
        Assert.True(Directory.Exists(first));

        Directory.Delete(first); // prove the second access returns the cached value, not a fresh stat/mkdir
        Assert.Equal(first, Storage.ThreadsDirectory);
        Assert.False(Directory.Exists(first));
    }

    [Fact]
    [Trait("Req", "PR-STORE-004")] // Storage.swift:27 (B3); moved implementation StoragePaths.swift:18-22
    public void AttachmentsDirectory_IsCreatedUnderTheRoot()
    {
        using var scratch = new StorageScratchFolder();
        var directory = Storage.AttachmentsDirectory;
        Assert.Equal(Path.Combine(scratch.Root, "attachments"), directory);
        Assert.True(Directory.Exists(directory));
    }

    [Fact]
    [Trait("Req", "PR-STORE-004")] // Storage.swift:27 static let: cached the same way as threadsDirectory (:25).
    public void AttachmentsDirectory_IsCachedAndNotRecreatedOnSubsequentAccess()
    {
        using var scratch = new StorageScratchFolder();
        var first = Storage.AttachmentsDirectory;
        Assert.True(Directory.Exists(first));

        Directory.Delete(first); // prove the second access returns the cached value, not a fresh stat/mkdir
        Assert.Equal(first, Storage.AttachmentsDirectory);
        Assert.False(Directory.Exists(first));
    }

    // PR-STORE-005 (wallpaperDirectory) left Storage.cs entirely at lane A10/ED-195: Storage no longer has a
    // WallpaperDirectory member (UI.Theme.Wallpaper reads Core.Support.StoragePaths.WallpaperDirectory directly).
    // Its test now lives on StoragePaths (Marrow.Core.Tests/Support/StoragePathsTests.cs,
    // WallpaperDirectory_IsCreatedUnderTheRootAndCached).

    [Fact]
    [Trait("Req", "PR-STORE-006")] // Services/Store/Storage.swift:29, :34-39 (B3); DEVIATION OD-0012 F1
    public void WorktreesDirectory_LivesUnderTheHomeStateDirectory_NotUnderTheRoot()
    {
        using var scratch = new StorageScratchFolder();
        var directory = Storage.WorktreesDirectory;
        Assert.Equal(Path.Combine(scratch.Home, ".nth", "worktrees"), directory);
        Assert.True(Directory.Exists(directory));
        Assert.DoesNotContain(scratch.Root, directory, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Req", "PR-STORE-006")] // Services/Store/Storage.swift:34-39 (B3); DEVIATION OD-0012 F1 (development suffix)
    public void WorktreesDirectory_UsesTheDevelopmentSuffixInADevelopmentBuild()
    {
        using var scratch = new StorageScratchFolder(isDevelopment: true);
        Assert.Equal(Path.Combine(scratch.Home, ".nth-dev", "worktrees"), Storage.WorktreesDirectory);
    }

    [Fact]
    [Trait("Req", "PR-STORE-007")] // Services/Store/Storage.swift:32 (B3)
    public void PatchesDirectory_LivesUnderTheHomeStateDirectory()
    {
        using var scratch = new StorageScratchFolder();
        var directory = Storage.PatchesDirectory;
        Assert.Equal(Path.Combine(scratch.Home, ".nth", "patches"), directory);
        Assert.True(Directory.Exists(directory));
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-070")] // Services/Store/Storage.swift:19-25,31-48: two roots, each created on first use and kept for the process
    public void The_store_root_and_the_home_state_directory_are_two_roots_each_made_on_first_use_and_then_kept()
    {
        using var scratch = new StorageScratchFolder();
        var state = Path.Combine(scratch.Home, ".nth");
        Assert.False(Directory.Exists(scratch.Root));
        Assert.False(Directory.Exists(state));

        // The home root is untouched until its first folder is asked for, and the store root likewise.
        var threads = Storage.ThreadsDirectory;
        Assert.True(Directory.Exists(threads));
        Assert.False(Directory.Exists(state));
        var worktrees = Storage.WorktreesDirectory;
        Assert.True(Directory.Exists(worktrees));

        // Library, threads and attachments under the store root; worktrees and patches under the home dot-folder.
        Assert.StartsWith(scratch.Root, threads, StringComparison.Ordinal);
        Assert.StartsWith(scratch.Root, Storage.AttachmentsDirectory, StringComparison.Ordinal);
        Assert.StartsWith(state + Path.DirectorySeparatorChar, worktrees, StringComparison.Ordinal);
        Assert.StartsWith(state + Path.DirectorySeparatorChar, Storage.PatchesDirectory, StringComparison.Ordinal);

        // Each URL is resolved once: a folder deleted later is not created again by the next read.
        Directory.Delete(worktrees);
        Assert.Equal(worktrees, Storage.WorktreesDirectory);
        Assert.False(Directory.Exists(worktrees));
    }

    // ---- PR-STORE-010 ----

    [Fact]
    [Trait("Req", "PR-STORE-010")] // Services/Store/Storage.swift:41-43 (B3)
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
        // wallpaper/ moved to Core.Support.StoragePaths at B3 (ED-195): StorageLocations builds its own private
        // StoragePathsLocations field directly from this very same root factory (Storage.cs's StorageLocations
        // constructor), never through the StoragePaths.Locations Ambient, so *that private field* hangs off
        // Storage.Root regardless of whether the Ambient is bound. It says nothing about whether
        // Marrow.Core.Support.StoragePaths.WallpaperDirectory (the Ambient UI.Theme.Wallpaper actually reads,
        // Wallpaper.cs:154, :187, :191) is wired anywhere: that is proven by
        // ProgramStartupBindingTests.Main_binds_every_Ambient_a_production_caller_reads_before_any_window_opens
        // (Marrow.App.Tests), not by this assertion.
        Assert.Equal(Path.Combine(root, "wallpaper"), StoragePathsLocations.Create(root).WallpaperDirectory);
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
    [Trait("Req", "PR-STORE-013")] // OD-0008 A10 deviation from Storage.swift:60-63
    public void LoadLibrary_IsFailedWhenLibraryJsonExistsButIsNotDecodable()
    {
        using var scratch = new StorageScratchFolder();
        File.WriteAllText(Storage.LibraryURL, "not json at all");

        var result = Storage.LoadLibrary();
        Assert.IsType<LibraryLoadResult.Failed>(result);
    }

    [Fact]
    [Trait("Req", "PR-STORE-013")] // OD-0008 A10: a non-object top-level JSON value is not a Library
    public void LoadLibrary_IsFailedWhenLibraryJsonHasANonObjectRoot()
    {
        using var scratch = new StorageScratchFolder();
        File.WriteAllText(Storage.LibraryURL, "[]");

        var result = Storage.LoadLibrary();

        Assert.IsType<LibraryLoadResult.Failed>(result);
    }

    [Fact]
    [Trait("Req", "PR-STORE-013")] // OD-0008 A10 deviation: a directory in place of the file is a read failure, not "missing"
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
    [Trait("Req", "PR-STORE-069")] // Services/Store/Storage.swift:189-197: png passes through unchanged
    [Trait("Req", "UI-STORE-004")] // Services/Store/Storage.swift:197: the literal "Pasted image.png"
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
    [Trait("Req", "PR-STORE-069")] // Services/Store/Storage.swift:190-195: a non-png paste is re-encoded to png
    [Trait("Req", "UI-STORE-004")] // Services/Store/Storage.swift:197: the literal "Pasted image.png"
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
    [Trait("Req", "PR-STORE-068")] // Services/Store/Storage.swift:184-185: every other extension is copied verbatim, no transcode
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
            TaskHandle? task = null;
            runtime.Loop.Run(() =>
            {
                task = Storage.Attach(
                    sources.Select(s => (AttachmentSource)new AttachmentSource.File(s)).ToArray(),
                    () => current,
                    value => current = value);
                return Task.CompletedTask;
            });
            Assert.NotNull(task);

            // Simulate "more may have been attached meanwhile" (:167-171): the loop is single-threaded and the
            // detached task's recount can only run once the loop is pumped below, so mutating `current` here,
            // before that pump, deterministically lands before the recount reads it.
            current = new ValueList<Attachment>(Enumerable.Range(0, Storage.AttachmentLimit - 1)
                .Select(_ => new Attachment { Name = "a", Path = "p", MimeType = "text/plain" })); // 7 of 8 slots taken

            runtime.Loop.RunUntil(() => task!.Completion.IsCompleted);
            Assert.Equal(TaskStatus.RanToCompletion, task!.Completion.Status);

            // Room was only 1 by the time the copies landed: exactly 1 of the 3 imported copies is kept, and the
            // other 2 files' copies must have been deleted (their originals in `sources` are untouched).
            Assert.Equal(Storage.AttachmentLimit - 1 + 1, current.Count);
            var keptCopyPath = current[Storage.AttachmentLimit - 1].Path;
            Assert.True(File.Exists(keptCopyPath));
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
