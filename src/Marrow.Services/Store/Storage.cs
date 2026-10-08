// Source: DroppyCode/Services/Store/Storage.swift (B3 52bc7926), :1-387.
// R-FILE-1: every type this Swift file declares lives here (StoredContinuation, Storage, AttachmentSource,
// MimeType, DocumentPrefetch, DiskWriter). root, wallpaperDirectory and directory(_:) moved out to
// Core/Support/StoragePaths.cs at B3 (N2, ED-195); wallpaperDirectory's own forwarding member left this file at
// lane A10/ED-195, once UI.Theme.Wallpaper moved to reading Core.Support.StoragePaths.WallpaperDirectory directly.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Services.Store;

/// <summary>:6-15. Just enough of a stored thread to read the files a forwarded continuation points at
/// (PR-STORE-076): sweepOrphanedAttachments scans whole thread files as text, so this is the only key it ever
/// decodes. Synthesized Decodable in Swift, both members optional -> DecodeIfPresent (R-CODE-1).</summary>
file sealed record StoredContinuation
{
    public ContinuationPayload? Continuation { get; init; } // :14

    public static StoredContinuation Decode(JsonElement element) => new()
    {
        Continuation = SwiftDecoding.DecodeIfPresent(element, "continuation", ContinuationPayload.Decode),
    };

    // :10-12
    public sealed record ContinuationPayload
    {
        public ValueList<Attachment>? Attachments { get; init; } // :11

        public static ContinuationPayload Decode(JsonElement element) => new()
        {
            Attachments = SwiftDecoding.DecodeIfPresent(element, "attachments",
                e => SwiftDecoding.ReadArray(e, item => item.Deserialize<Attachment>(Coding.StorageOptions)!)),
        };
    }
}

/// <summary>
/// PORT-ONLY seam for Storage.swift's side-effecting <c>static let</c> roots (R-LIFE-2), styled on
/// <c>UserDefaults.Standard</c> (Port/UserDefaults.cs). Unbound by default so a test can never create real files
/// under %LOCALAPPDATA%\Marrow Code or %USERPROFILE%\.marrow: Marrow.App binds
/// <see cref="StorageLocations.CreateProduction"/> at start-up, and a test binds
/// <see cref="StorageLocations.Create"/> under its own throwaway folder (R-TEST-9).
/// </summary>
public interface IStorageLocations
{
    string Root { get; }
    string LibraryURL { get; }
    string ThreadsDirectory { get; }
    string AttachmentsDirectory { get; }
    string WorktreesDirectory { get; }
    string PatchesDirectory { get; }
}

/// <summary>
/// The production shape of Storage.swift's four remaining roots (:19-39; two more, root and wallpaperDirectory,
/// moved to Core.Support.StoragePaths in B3 - see <see cref="paths"/>; wallpaperDirectory's own forwarding member
/// left this type at lane A10/ED-195, once UI.Theme.Wallpaper moved to reading
/// Core.Support.StoragePaths.WallpaperDirectory directly). Each of <see cref="Root"/>,
/// <see cref="ThreadsDirectory"/>, <see cref="AttachmentsDirectory"/>, <see cref="WorktreesDirectory"/> and
/// <see cref="PatchesDirectory"/> is created with intermediates on first access and cached for the life of the
/// instance (PR-STORE-001, -003, -004, -006, -007); a creation failure is swallowed (<c>try?</c>) exactly as the
/// source swallows it, and the resolved path is still returned and cached. <see cref="LibraryURL"/> (:21) is a
/// pure derivation with no side effect.
/// </summary>
public sealed class StorageLocations : IStorageLocations
{
    // N2 (porting/work/b3/B3_PLAN.md section 3): root, wallpaperDirectory and directory(_:) moved out of this
    // file into Core/Support/StoragePaths.cs. This instance is constructed directly from the same `root`
    // factory this type already receives (never through the StoragePaths.Locations Ambient), so
    // StorageLocations.Create/.CreateProduction and every existing test fixture that calls them keep their
    // exact signature; ENGINEERING DECISION, reported for the ledger to record.
    private readonly StoragePathsLocations paths;
    private readonly Lazy<string> threadsDirectory;
    private readonly Lazy<string> attachmentsDirectory;
    private readonly Lazy<string> worktreesDirectory;
    private readonly Lazy<string> patchesDirectory;

    private StorageLocations(Func<string> root, Func<string> homeDirectory, bool isDevelopment)
    {
        paths = StoragePathsLocations.Create(root); // :19 static let root: URL = StoragePaths.root

        // :23-27. static let threadsDirectory / attachmentsDirectory: the underlying StoragePaths.Directory(_:)
        // helper is deliberately uncached (Core/Support/StoragePaths.cs), but Storage's own `static let` around
        // it in the Swift source runs that call exactly once and keeps the URL for the process's life ("a stat
        // plus mkdir per call was pure waste", :24). CreateOnce reproduces that one-time evaluation here.
        threadsDirectory = CreateOnce(() => paths.Directory("threads")); // :25
        attachmentsDirectory = CreateOnce(() => paths.Directory("attachments")); // :27

        // :34-39 stateDirectory(_:) under AppInfo.stateDirectoryName (Text.swift:6), which DEVIATION OD-0012 F1 makes
        // ".marrow" / ".marrow-dev" (PR-STORE-008): the folder name shown in Settings and handed to git.
        var stateDirectoryName = AppInfo.StateDirectoryNameFor(isDevelopment);
        worktreesDirectory = StateSubDirectory(homeDirectory, stateDirectoryName, "worktrees"); // :29
        patchesDirectory = StateSubDirectory(homeDirectory, stateDirectoryName, "patches"); // :32
    }

    private static Lazy<string> StateSubDirectory(Func<string> homeDirectory, string stateDirectoryName, string name) =>
        CreateOnce(() =>
        {
            // :35-36: one appendingPathComponent call handed "name/subname" creates both levels at once.
            var url = SwiftPath.AppendingPathComponent(
                SwiftPath.AppendingPathComponent(homeDirectory(), stateDirectoryName), name);
            SwiftTry.Ignore(() => Directory.CreateDirectory(url)); // :36
            return url;
        });

    private static Lazy<string> CreateOnce(Func<string> factory) => new(factory, LazyThreadSafetyMode.ExecutionAndPublication);

    public string Root => paths.Root; // :19
    public string LibraryURL => SwiftPath.AppendingPathComponent(Root, "library.json"); // :21, no side effect
    public string ThreadsDirectory => threadsDirectory.Value; // :25, via directory(_:) (now Core.Support.StoragePaths)
    public string AttachmentsDirectory => attachmentsDirectory.Value; // :27
    public string WorktreesDirectory => worktreesDirectory.Value; // :29
    public string PatchesDirectory => patchesDirectory.Value; // :32

    /// <summary>
    /// DEVIATION OD-0008 C7: %LOCALAPPDATA%\Marrow Code, a fixed literal rather than AppInfo.Name (upstream
    /// appends the display name, falling back to "Droppy Code", Text.swift:4), so the port never shares a folder
    /// with the owner's separate Electron "Marrow" (%LOCALAPPDATA%\Marrow). dead_code.md #44: CaptureRun.storageRoot
    /// (now StoragePaths.swift:7, moved from this file's B2 :20) is reachable only via --website-captures /
    /// --tour-captures, never a normal launch, so the redirect is not reproduced here, matching
    /// Core.Support.StoragePathsLocations.CreateProduction.
    /// </summary>
    public static StorageLocations CreateProduction() => new(
        // ED-212: a Debug launch honours MARROW_DEV_DATA_ROOT when it names an existing directory; Release and an
        // unset/absent variable resolve exactly the special folder, unchanged.
        root: () => SwiftPath.AppendingPathComponent(DevDataRoot.LocalApplicationData(), "Marrow Code"),
        // ED-212: the redirect also moves the .marrow-dev/.marrow state directory (worktrees, patches) under the
        // dev root instead of the real %USERPROFILE%, so a dev launch never touches the user's own home folder.
        homeDirectory: () => DevDataRoot.IsRedirected ? DevDataRoot.LocalApplicationData() : LoginEnvironment.HomeDirectory,
        isDevelopment: AppInfo.Current.IsDevelopment);

    /// <summary>A test's own throwaway roots (R-TEST-9): real folders under MARROW_PORT_DATA_DIR, never production.</summary>
    public static StorageLocations Create(string root, string homeDirectory, bool isDevelopment = false) =>
        new(() => root, () => homeDirectory, isDevelopment);
}

file sealed class UnboundStorageLocations : IStorageLocations
{
    private const string Message = "Storage.Locations is not bound: Marrow.App sets it at start-up; tests override it.";
    public string Root => throw new SwiftTrapException(Message);
    public string LibraryURL => throw new SwiftTrapException(Message);
    public string ThreadsDirectory => throw new SwiftTrapException(Message);
    public string AttachmentsDirectory => throw new SwiftTrapException(Message);
    public string WorktreesDirectory => throw new SwiftTrapException(Message);
    public string PatchesDirectory => throw new SwiftTrapException(Message);
}

/// <summary>Where a load of library.json landed (:60-63). DEVIATION OD-0008 A10 (PR-STORE-012, PR-STORE-013):
/// upstream collapses a missing file and any read or decode error into the same empty Library
/// (<c>guard let data = try? Data(contentsOf: libraryURL) else { return Library() }; return (try? decode) ?? Library()</c>).
/// The port tells a missing file (<see cref="Empty"/>, ordinary empty start) apart from a file that exists but
/// cannot be read or decoded (<see cref="Failed"/>: surfaced to the user, never overwritten by the next save;
/// that policy is AppModel's, out of this file).</summary>
public abstract record LibraryLoadResult
{
    private LibraryLoadResult() { }

    public sealed record Empty : LibraryLoadResult; // :61 guard branch, PR-STORE-012

    public sealed record Loaded(Library Library) : LibraryLoadResult;

    public sealed record Failed(Exception Error) : LibraryLoadResult; // PR-STORE-013
}

// :222-227. Sendable enum, R-ENUM-2.
public abstract record AttachmentSource
{
    private AttachmentSource() { }

    // :224
    public sealed record File(string Path) : AttachmentSource;

    // :225-226. ENGINEERING DECISION: Swift's payload type is UTType, a system type with no port twin anywhere in
    // the tree; Storage.swift reads only one fact from it (:191 type == .png), so it is ported as the MIME-style
    // identifier MimeType (below) already uses for the same distinction.
    // Swift Data is a value: the source keeps a private copy of the bytes it was built with.
    public sealed record Image : AttachmentSource
    {
        public Image(byte[] data, string mimeType)
        {
            Data = (byte[])data.Clone();
            MimeType = mimeType;
        }

        public byte[] Data { get; }
        public string MimeType { get; }
    }

    public const string PngMimeType = "image/png";
}

// :229-243
public static class MimeType
{
    // :230-242
    public static string Of(string fileExtension) => SwiftString.Lowercased(fileExtension) switch
    {
        "png" => "image/png",
        "jpg" or "jpeg" => "image/jpeg",
        "gif" => "image/gif",
        "webp" => "image/webp",
        "heic" => "image/heic",
        "pdf" => "application/pdf",
        "json" => "application/json",
        "md" or "markdown" => "text/markdown",
        _ => "text/plain",
    };
}

/// <summary>:245-335. <see cref="Take"/> and <see cref="Forget"/> serve PR-STORE-049/050 (deleteDocuments calls
/// forget for every id, :92; ThreadRuntime calls take, :324-330); <see cref="Warm"/> and its queue and background
/// worker are PR-STORE-042 to PR-STORE-048 (:275-322).</summary>
public sealed class DocumentPrefetch
{
    public static DocumentPrefetch Shared { get; } = new(); // :246

    // :248-251. Each request carries a fresh token so a superseded decode (take/forget/a second warm) is
    // discarded when it lands (PR-STORE-047).
    private readonly struct Request
    {
        public Request(Guid id)
        {
            Id = id;
            Token = Guid.NewGuid(); // :250
        }

        public Guid Id { get; } // :249
        public Guid Token { get; } // :250
    }

    // :253-258
    private struct State
    {
        public RecentCache<Guid, ThreadDocument> Ready;
        public Dictionary<Guid, Guid> Pending;
        public List<Request> Queue;
        public TaskHandle? Worker;
    }

    private readonly Mutex<State> state;
    private readonly int pendingLimit; // :261, :271 max(1, pendingLimit)
    private readonly Func<Guid, (ThreadDocument Document, int Cost)?> load; // :262-263

    // :264-273. Shared (the only production instance, :246) always uses the defaults below.
    private DocumentPrefetch() : this(limit: 12, costLimit: 16 * 1024 * 1024, pendingLimit: 24, load: Storage.ReadDocument) { }

    // :264-273. Test-only construction (PR-STORE-042 windows_path: "exist only so tests can substitute a loader
    // and smaller limits"); internal per R-NAME-6, visible to Marrow.Services.Tests (InternalsVisibleTo declared
    // in Git/WorkingTreeWatch.cs).
    internal DocumentPrefetch(int limit, long costLimit, int pendingLimit, Func<Guid, (ThreadDocument Document, int Cost)?> load)
    {
        state = new Mutex<State>(new State
        {
            Ready = new RecentCache<Guid, ThreadDocument>(limit: limit, costLimit: costLimit), // :270
            Pending = new Dictionary<Guid, Guid>(),
            Queue = new List<Request>(),
            Worker = null,
        });
        this.pendingLimit = Math.Max(1, pendingLimit); // :271
        this.load = load; // :272
    }

    // :275-308. urgent ids move to (or are inserted at) the front (PR-STORE-045); the queue stops growing at
    // pendingLimit, dropping the rest of this call's ids (PR-STORE-044); already-ready or already-pending ids are
    // skipped (PR-STORE-043); at most one background worker drains the queue (PR-STORE-046).
    public TaskHandle? Warm(IReadOnlyList<Guid> ids, bool urgent = false) => state.WithLock((ref State s) =>
    {
        if (urgent)
        {
            for (var i = ids.Count - 1; i >= 0; i--) // :281 ids.reversed()
            {
                var id = ids[i];
                if (s.Ready.Contains(id)) continue; // :282
                var index = s.Queue.FindIndex(r => r.Id == id);
                if (index < 0) continue; // :282
                var moved = s.Queue[index];
                s.Queue.RemoveAt(index);
                s.Queue.Insert(0, moved); // :283
            }
        }
        foreach (var id in ids) // :285
        {
            if (s.Ready.Contains(id) || s.Pending.ContainsKey(id)) continue; // :286
            if (s.Queue.Count >= pendingLimit) break; // :287, PR-STORE-044: break, not continue
            var request = new Request(id); // :288
            if (urgent) s.Queue.Insert(0, request); else s.Queue.Add(request); // :289
            s.Pending[id] = request.Token; // :290
        }
        if (s.Worker is not null || s.Queue.Count == 0) return s.Worker; // :292
        var worker = TaskHandle.Detached(async () =>
        {
            while (Next() is { } request) // :294
            {
                var result = load(request.Id); // :295
                state.WithLock((ref State inner) =>
                {
                    if (!inner.Pending.TryGetValue(request.Id, out var token) || token != request.Token) return; // :297
                    inner.Pending.Remove(request.Id); // :298 state.pending[request.id] = nil
                    if (result is { } r && !SwiftTask.IsCancelled) // :299
                        inner.Ready.Insert(r.Document, request.Id, r.Cost); // :300
                });
            }
        }); // :293-304
        s.Worker = worker; // :305
        return worker; // :306
    });

    // :310-322. Cancellation flushes the whole queue and every pending token, keeping the ready cache untouched
    // (PR-STORE-048); an empty queue clears the worker so the next warm() starts a fresh one (PR-STORE-046).
    private Request? Next() => state.WithLock((ref State s) =>
    {
        if (SwiftTask.IsCancelled) // :312
        {
            s.Queue.Clear(); // :313
            s.Pending.Clear(); // :314
        }
        if (s.Queue.Count == 0) // :316
        {
            s.Worker = null; // :317
            return (Request?)null; // :318
        }
        var request = s.Queue[0];
        s.Queue.RemoveAt(0); // :320 removeFirst()
        return (Request?)request;
    });

    // :324-330. A consuming read: also cancels any queued or in-flight work for the id (PR-STORE-049).
    public ThreadDocument? Take(Guid id) => state.WithLock((ref State s) =>
    {
        s.Pending.Remove(id); // :326
        s.Queue.RemoveAll(r => r.Id == id); // :327
        return s.Ready.RemoveValue(id, out var value) ? value : null; // :328
    });

    // :332-334
    public void Forget(Guid id) => Take(id);

    // Test-only observation hooks (R-NAME-6, internal, visible to Marrow.Services.Tests): none of these are
    // called from production code, which only ever reads DocumentPrefetch through Warm/Take/Forget.
    internal IReadOnlyList<Guid> QueuedIds => state.WithLock((ref State s) => s.Queue.Select(r => r.Id).ToArray());
    internal bool IsPending(Guid id) => state.WithLock((ref State s) => s.Pending.ContainsKey(id));
    internal bool IsReady(Guid id) => state.WithLock((ref State s) => s.Ready.Contains(id));
    internal bool HasWorker => state.WithLock((ref State s) => s.Worker is not null);
}

/// <summary>:337-389.</summary>
public sealed class DiskWriter
{
    public static DiskWriter Shared { get; } = new(); // :338

    private static readonly Logger FailureLog = new(Logger.Subsystem, "storage");

    private readonly SerialQueue queue = new("droppycode.storage.write"); // :339
    private readonly Mutex<Dictionary<string, object>> pending = new(new Dictionary<string, object>(SwiftStringComparer.Instance)); // :343
    // :333-334. The size of the last write per file, so a caller can space its saves by it (PR-STORE-174). Only
    // encodeAndWrite's queued hop records a size (:349-350 below); writeSynchronously and removeSynchronously
    // never touch this map, even though they write or remove the same files.
    private readonly Mutex<Dictionary<string, int>> sizes = new(new Dictionary<string, int>(SwiftStringComparer.Instance));

    private DiskWriter() { }

    // Test-only (PR-STORE-035); internal per R-NAME-6, visible to Marrow.Services.Tests.
    internal string QueueLabel => queue.Label; // :339

    // :336-338 lastWrittenSize(of:)
    public int? LastWrittenSize(string url) =>
        sizes.WithLock((ref Dictionary<string, int> map) => map.TryGetValue(url, out var size) ? size : (int?)null);

    // :345-356
    public void EncodeAndWrite(object value, string url)
    {
        var queued = pending.WithLock((ref Dictionary<string, object> map) =>
        {
            var alreadyQueued = map.ContainsKey(url); // :347
            map[url] = value; // :348
            return alreadyQueued;
        });
        if (queued) return; // :351
        queue.Async(() =>
        {
            var latest = pending.WithLock((ref Dictionary<string, object> map) =>
            {
                if (!map.TryGetValue(url, out var v)) return null;
                map.Remove(url);
                return v;
            });
            if (latest is null) return; // :353
            // :349 if let size = Self.write(value, to: url) { sizes.withLock { $0[url] = size } }
            if (Write(latest, url) is { } size) sizes.WithLock((ref Dictionary<string, int> map) => map[url] = size);
        });
    }

    // :358-361
    public void WriteSynchronously(object value, string url)
    {
        pending.WithLock((ref Dictionary<string, object> map) => map.Remove(url)); // :359
        queue.Sync(() => Write(value, url)); // :360. The returned size is discarded: only encodeAndWrite records one.
    }

    // :363-365
    public void RemoveSynchronously(string url) => RemoveSynchronously([url]);

    // :367-374
    public void RemoveSynchronously(IReadOnlyList<string> urls)
    {
        pending.WithLock((ref Dictionary<string, object> map) =>
        {
            foreach (var u in urls) map.Remove(u); // :369
            return true;
        });
        queue.Sync(() =>
        {
            foreach (var u in urls) SwiftTry.Ignore(() => FileOps.RemoveItem(u)); // :372
            return true;
        });
    }

    // :377-379
    public void WaitForQueuedWrites() => queue.Sync(() => true);

    // :376-386. DEVIATION OD-0012 F1: the diagnostic text renames "Droppy Code" to "Marrow"; no record fixes its
    // exact wording (NSLog has no other caller in the tree). Returns the written byte count on success (:381,
    // data.count), or null on an encode failure (:378) or a write failure (:383-385) - PR-STORE-174.
    private static int? Write(object value, string url)
    {
        byte[] data;
        try
        {
            var element = JsonSerializer.SerializeToElement(value, value.GetType(), Coding.StorageOptions);
            data = SwiftJson.Write(element, SwiftJsonStyle.Storage); // :382
        }
        catch (Exception e) when (SwiftTry.IsThrownError(e))
        {
            return null;
        }
        try
        {
            FileOps.WriteAtomic(url, data); // :384
            return data.Length; // :381
        }
        catch (Exception error)
        {
            var description = error switch
            {
                UnauthorizedAccessException => error.Message, // GD-001
                IOException => error.Message, // GD-002: keep the OS description for this log-only filesystem failure.
                _ => SwiftErrors.LocalizedDescription(error),
            };
            FailureLog.Error($"Marrow could not save {SwiftPath.LastPathComponent(url)}: {description}"); // :386
            return null;
        }
    }
}

/// <summary>Where the app keeps its library, thread histories and attachments (:18).</summary>
public static partial class Storage
{
    public static Ambient<IStorageLocations> Locations { get; } = new(new UnboundStorageLocations());

    public static string Root => Locations.Current.Root; // :19-25
    public static string LibraryURL => Locations.Current.LibraryURL; // :27
    public static string ThreadsDirectory => Locations.Current.ThreadsDirectory; // :31
    public static string AttachmentsDirectory => Locations.Current.AttachmentsDirectory; // :33
    public static string WorktreesDirectory => Locations.Current.WorktreesDirectory; // :38
    public static string PatchesDirectory => Locations.Current.PatchesDirectory; // :41

    // :50-52
    public static string ThreadURL(Guid id) => SwiftPath.AppendingPathComponent(ThreadsDirectory, $"{SwiftUUID.UuidString(id)}.json");

    // :60-63. See LibraryLoadResult for the OD-0008 A10 deviation.
    public static LibraryLoadResult LoadLibrary()
    {
        // A directory occupying library.json's path is not "missing" (PR-STORE-013: it exists but cannot be
        // read as a file); only the true absence of anything at that path takes the empty-Library path.
        if (!File.Exists(LibraryURL) && !Directory.Exists(LibraryURL)) return new LibraryLoadResult.Empty(); // PR-STORE-012

        byte[] data;
        try
        {
            data = File.ReadAllBytes(LibraryURL);
        }
        catch (Exception e) when (SwiftTry.IsThrownError(e))
        {
            return new LibraryLoadResult.Failed(e); // PR-STORE-013
        }

        Library? library;
        try
        {
            using var document = JsonDocument.Parse(data, SwiftJson.DocumentOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return new LibraryLoadResult.Failed(new InvalidDataException("library.json could not be decoded."));

            library = Coding.Decode<Library>(data);
        }
        catch (Exception e) when (SwiftTry.IsThrownError(e))
        {
            return new LibraryLoadResult.Failed(e); // PR-STORE-013
        }
        return library is not null
            ? new LibraryLoadResult.Loaded(library)
            : new LibraryLoadResult.Failed(new InvalidDataException("library.json could not be decoded.")); // PR-STORE-013
    }

    // :66-68
    public static ThreadDocument? DecodeDocument(Guid id) => ReadDocument(id)?.Document;

    // :73-81. PR-STORE-019: Coding.StorageOptions is an immutable, thread-safe JsonSerializerOptions (unlike a
    // Swift JSONDecoder instance), the "genuinely thread-safe shared one" the record's windows_path allows in
    // place of a fresh decoder per call; the observable decode is identical either way.
    public static (ThreadDocument Document, int Cost)? ReadDocument(Guid id)
    {
        DiskWriter.Shared.WaitForQueuedWrites(); // :74, PR-STORE-021

        // T44: a missing document still returns nil, without allocating an exception on every idle probe.
        var path = ThreadURL(id);
        if (!File.Exists(path)) return null;

        byte[] data;
        try
        {
            data = File.ReadAllBytes(path); // :75; a deletion after the existence check still takes the same error path.
        }
        catch (Exception e) when (SwiftTry.IsThrownError(e))
        {
            return null; // PR-STORE-022
        }

        ThreadDocument? document;
        try
        {
            document = Coding.Decode<ThreadDocument>(data); // :76-78
        }
        catch (Exception e) when (SwiftTry.IsThrownError(e))
        {
            return null; // PR-STORE-023
        }
        if (document is null) return null; // PR-STORE-023

        document = document with
        {
            Items = new ValueList<TimelineItem>(
                document.Items.Where(item => !item.IsEmptyReasoning).Select(item => item.CleanedOfEmDashes)), // :79, PR-STORE-024, PR-STORE-025
        };
        return (document, data.Length); // :80, PR-STORE-026
    }

    // :83-85
    public static void DeleteDocument(Guid id) => DeleteDocuments([id]);

    // :87-94
    public static void DeleteDocuments(IReadOnlyList<Guid> ids)
    {
        if (ids.Count == 0) return; // :91, PR-STORE-030
        foreach (var id in ids) DocumentPrefetch.Shared.Forget(id); // :92
        DiskWriter.Shared.RemoveSynchronously(ids.Select(ThreadURL).ToArray()); // :93
    }

    // :97-103
    public static Attachment ImportAttachment(string source)
    {
        var id = Guid.NewGuid(); // :98
        var extension = SwiftPath.PathExtension(source);
        extension = extension.Length == 0 ? "bin" : SwiftString.Lowercased(extension); // :99
        var destination = SwiftPath.AppendingPathComponent(AttachmentsDirectory, $"{SwiftUUID.UuidString(id)}.{extension}"); // :100
        File.Copy(source, destination); // :101, throwing
        return new Attachment
        {
            Id = id,
            Name = SwiftPath.LastPathComponent(source),
            Path = destination,
            MimeType = MimeType.Of(extension),
        }; // :102
    }

    // :105-110
    public static Attachment ImportAttachment(byte[] data, string name, string fileExtension)
    {
        var id = Guid.NewGuid(); // :106
        var destination = SwiftPath.AppendingPathComponent(AttachmentsDirectory, $"{SwiftUUID.UuidString(id)}.{fileExtension}"); // :107
        FileOps.WriteAtomic(destination, data); // :108, throwing
        return new Attachment { Id = id, Name = name, Path = destination, MimeType = MimeType.Of(fileExtension) }; // :109
    }

    /// <summary>:112-149. Removes attachment files no thread refers to any more, once they are a day old
    /// (PR-STORE-078), so a draft still open at relaunch keeps its files (PR-STORE-074 empty-attachments
    /// guard) and only files named the way ImportAttachment names them ever go (PR-STORE-077). Directory
    /// URLs are captured here, on the calling actor (PR-STORE-073), before the scan runs on a detached
    /// utility task whose completion is awaited (:117-120, :149 .value); the scan itself never decodes a
    /// thread document (PR-STORE-073), scanning each file as raw, repair-decoded text (PR-STORE-073).</summary>
    public static async Task SweepOrphanedAttachments()
    {
        var attachments = AttachmentsDirectory; // :118
        var threads = ThreadsDirectory; // :119
        await TaskHandle.Detached(() =>
        {
            SweepOrphanedAttachmentsCore(attachments, threads); // :120-149
            return Task.CompletedTask;
        }).Completion;
    }

    // :121-148, the body of the detached task.
    private static void SweepOrphanedAttachmentsCore(string attachmentsDirectory, string threadsDirectory)
    {
        // :122-123. PR-STORE-074: a listing failure for EITHER directory aborts the whole sweep; an empty
        // attachments/ also stops here (nothing to sweep). Only a threads/ listing that truly succeeds (even
        // as an empty array) lets the scan continue.
        string[] names;
        try
        {
            names = Directory.GetFileSystemEntries(attachmentsDirectory).Select(SwiftPath.LastPathComponent).ToArray();
        }
        catch (Exception e) when (SwiftTry.IsThrownError(e))
        {
            return;
        }
        if (names.Length == 0) return; // :122 !names.isEmpty

        string[] documents;
        try
        {
            documents = Directory.GetFileSystemEntries(threadsDirectory).Select(SwiftPath.LastPathComponent).ToArray();
        }
        catch (Exception e) when (SwiftTry.IsThrownError(e))
        {
            return; // PR-STORE-074: a threads/ listing failure never treats every attachment as unreferenced.
        }

        var referenced = new HashSet<string>(SwiftStringComparer.Instance); // :124
        foreach (var document in documents)
        {
            if (!SwiftString.HasSuffix(document, ".json")) continue; // :125

            byte[] data;
            try
            {
                data = File.ReadAllBytes(SwiftPath.AppendingPathComponent(threadsDirectory, document)); // :126
            }
            catch (Exception e) when (SwiftTry.IsThrownError(e))
            {
                continue; // PR-STORE-075: an unreadable thread file is skipped; its attachments become candidates.
            }
            var text = SwiftText.DecodeUtf8Repairing(data); // :127, R-STR-8

            // :128-131. DEVIATION windows_path (PR-STORE-075): the source pattern looks for the macOS encoder's
            // "attachments\/UUID.ext" (an escaped forward slash). The port's encoder writes Windows separators,
            // so a persisted path reads "attachments\\UUID.ext" in the raw JSON text (one backslash character,
            // JSON-escaped to two); the pattern is adjusted to that separator, or the sweep matches nothing and
            // every attachment is deleted a day after it is written (the record's own highest-risk warning).
            foreach (Match match in AttachmentPathPattern().Matches(text))
                referenced.Add(match.Groups[1].Value); // :130

            // :132-139. A forwarded continuation keeps its own copy of the metadata, and the files it names stay
            // referenced while the thread holding it is on disk.
            if (text.Contains("\"continuation\"", StringComparison.Ordinal)) // :134
            {
                StoredContinuation? stored = null;
                try
                {
                    using var document2 = JsonDocument.Parse(data, SwiftJson.DocumentOptions);
                    stored = StoredContinuation.Decode(document2.RootElement); // :135
                }
                catch (Exception e) when (SwiftTry.IsThrownError(e))
                {
                    // PR-STORE-076: a continuation decode failure contributes nothing; its files can be swept later.
                }
                foreach (var attachment in stored?.Continuation?.Attachments ?? ValueList<Attachment>.Empty) // :136
                    referenced.Add(SwiftPath.LastPathComponent(attachment.Path)); // :137, Swift: attachment.url.lastPathComponent
            }
        }

        var cutoff = SwiftDate.AddingTimeInterval(SwiftDate.Now, -24 * 60 * 60); // :141
        foreach (var name in names) // :142
        {
            if (referenced.Contains(name)) continue; // :142
            if (!OrphanFileNamePattern().IsMatch(name)) continue; // :143, wholeMatch
            var url = SwiftPath.AppendingPathComponent(attachmentsDirectory, name); // :144
            if (!File.Exists(url) && !Directory.Exists(url)) continue; // :145 a modification date that cannot be read is skipped and kept; a folder has one too.
            DateTimeOffset modified;
            try
            {
                modified = new DateTimeOffset(File.GetLastWriteTimeUtc(url), TimeSpan.Zero); // :145, contentModificationDateKey (File.GetLastWriteTimeUtc reads a folder's too)
            }
            catch (Exception e) when (SwiftTry.IsThrownError(e))
            {
                continue;
            }
            if (!(modified < cutoff)) continue; // :146
            SwiftTry.Ignore(() => FileOps.RemoveItem(url)); // :147
        }
    }

    // :129. Swift #/attachments\\?\/([0-9A-Fa-f-]{36}\.[A-Za-z0-9]+)/#, adapted per the windows_path DEVIATION
    // above: one or two raw backslash characters (a JSON-escaped Windows path separator) in place of the
    // optional-backslash-then-forward-slash the macOS encoder produces.
    [GeneratedRegex(@"attachments\\{1,2}([0-9A-Fa-f-]{36}\.[A-Za-z0-9]+)", RegexOptions.CultureInvariant)]
    private static partial Regex AttachmentPathPattern();

    // :143. Swift #/[0-9A-Fa-f-]{36}\.[A-Za-z0-9]+/#.wholeMatch -> anchored \A...\z (R-RE-1).
    [GeneratedRegex(@"\A[0-9A-Fa-f-]{36}\.[A-Za-z0-9]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex OrphanFileNamePattern();

    // :153
    public const int AttachmentLimit = 8;

    // :161-174. @MainActor. DEVIATION (layering, IMPLEMENTING.md 2.1): Swift's Binding<[Attachment]> is a UI type
    // (Marrow.UI.Port.Binding<T>) that Services cannot reference; ported as the get/set pair a Binding wraps
    // (identical read-modify-write semantics), with the UI call site adapting a real Binding to these delegates.
    public static TaskHandle? Attach(
        IReadOnlyList<AttachmentSource> sources,
        Func<ValueList<Attachment>> getAttachments,
        Action<ValueList<Attachment>> setAttachments)
    {
        MainActor.AssumeIsolated();
        var batch = sources.Take((int)Math.Max(0, AttachmentLimit - getAttachments().Count)).ToArray(); // :163
        if (batch.Length == 0) return null; // :164
        return TaskHandle.Detached(async () =>
        {
            var imported = batch.Select(ImportSource).OfType<Attachment>().ToArray(); // :166
            var kept = await MainActor.Run(() =>
            {
                var current = getAttachments();
                var room = (int)Math.Max(0, AttachmentLimit - current.Count); // :168
                setAttachments(new ValueList<Attachment>(current.Concat(imported.Take(room)))); // :169
                return room; // :170
            });
            foreach (var orphan in imported.Skip(kept)) SwiftTry.Ignore(() => FileOps.RemoveItem(orphan.Path)); // :172
        });
    }

    // :179-199
    public static Attachment? ImportSource(AttachmentSource source)
    {
        switch (source)
        {
            case AttachmentSource.File file:
            {
                var isDirectory = Directory.Exists(file.Path);
                var exists = isDirectory || File.Exists(file.Path);
                if (!exists || isDirectory) return null; // :183
                var fileExtension = SwiftString.Lowercased(SwiftPath.PathExtension(file.Path)); // :184
                if (fileExtension != "heic" && fileExtension != "heif")
                    return SwiftTry.Optional(() => ImportAttachment(file.Path)); // :185
                var jpeg = TranscodeFileToJpeg(file.Path, HeicTranscodeQuality); // :186-187
                if (jpeg is null) return null;
                var last = SwiftPath.LastPathComponent(file.Path);
                var dot = last.LastIndexOf('.');
                var baseName = dot > 0 ? last[..dot] : last; // url.deletingPathExtension().lastPathComponent
                return SwiftTry.Optional(() => ImportAttachment(jpeg, baseName + ".jpg", "jpg")); // :188
            }
            case AttachmentSource.Image image:
            {
                var png = image.MimeType == AttachmentSource.PngMimeType ? image.Data : TranscodeBytesToPng(image.Data); // :190-195
                if (png is null) return null; // :196
                return SwiftTry.Optional(() => ImportAttachment(png, "Pasted image.png", "png")); // :197
            }
            default:
                throw new UnreachableException();
        }
    }

    // :215 kCGImageDestinationLossyCompressionQuality: 0.88, PR-STORE-071. Named so a test can assert the literal
    // without re-deriving it from encoded JPEG bytes.
    internal const double HeicTranscodeQuality = 0.88;

    // :201-219 transcode(_:to:), private to Storage.swift. PORT-ONLY WIC machinery styled on the interop pattern
    // in Core/Port/WicImageDecoder.cs, but with no downscale step: the requested pixel size is always
    // max(width, height) of the (post-orientation) source itself, matching
    // ":208 kCGImageSourceThumbnailMaxPixelSize: max(width, height)".

    private static readonly Guid WicFactoryClass = new("cacaf262-9370-4615-a13b-9f5539da4c0a");
    private static readonly Guid WicFactoryInterface = new("ec5ec8a9-c395-4314-9c77-54d7a935ff70");
    private static readonly Guid WicContainerFormatJpeg = new("19e4a5aa-5662-4fc5-a0c0-1758028e1057");
    private static readonly Guid WicContainerFormatPng = new("1b7cfaf4-713f-473c-bbcd-6137425faeaf");
    private static readonly Guid WicPixelFormatBgr24 = new("6fddc324-4e03-4bfe-b185-3d77768dc90c");
    private static readonly Guid WicPixelFormatPbgra32 = new("6fddc324-4e03-4bfe-b185-3d77768dc910");

    private static byte[]? TranscodeFileToJpeg(string path, double quality) =>
        TranscodeCore(factory => Method<WicCreateDecoderFromFilename>(factory, 3)(factory, path, IntPtr.Zero, 0x80000000, 0, out var decoder) < 0
                ? (IntPtr.Zero, -1) : (decoder, 0),
            WicContainerFormatJpeg, WicPixelFormatBgr24, quality);

    private static byte[]? TranscodeBytesToPng(byte[] data) =>
        TranscodeCore(factory =>
            {
                var stream = WicSHCreateMemStream(data, (uint)data.Length);
                if (stream == IntPtr.Zero) return (IntPtr.Zero, -1);
                var hr = Method<WicCreateDecoderFromStream>(factory, 4)(factory, stream, IntPtr.Zero, 0, out var decoder);
                Release(ref stream);
                return hr < 0 ? (IntPtr.Zero, -1) : (decoder, 0);
            },
            WicContainerFormatPng, WicPixelFormatPbgra32, quality: null);

    private static byte[]? TranscodeCore(Func<IntPtr, (IntPtr Decoder, int Hr)> makeDecoder, Guid containerFormat, Guid pixelFormat, double? quality)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var initialized = WicCoInitializeEx(IntPtr.Zero, 0);
        if (initialized < 0 && initialized != unchecked((int)0x80010106)) return null;
        IntPtr factory = IntPtr.Zero, decoder = IntPtr.Zero, frame = IntPtr.Zero, reader = IntPtr.Zero, oriented = IntPtr.Zero, converter = IntPtr.Zero;
        IntPtr outputStream = IntPtr.Zero, encoder = IntPtr.Zero, frameEncode = IntPtr.Zero, propertyBag = IntPtr.Zero;
        var namePtr = IntPtr.Zero;
        try
        {
            var clsid = WicFactoryClass;
            var iid = WicFactoryInterface;
            if (WicCoCreateInstance(ref clsid, IntPtr.Zero, 1, ref iid, out factory) < 0) return null;
            var (madeDecoder, hr) = makeDecoder(factory);
            decoder = madeDecoder;
            if (hr < 0 || decoder == IntPtr.Zero) return null;
            if (Method<WicGetFrame>(decoder, 13)(decoder, 0, out frame) < 0) return null;
            if (Method<WicGetSize>(frame, 3)(frame, out var width, out var height) < 0 || width == 0 || height == 0) return null;

            var orientation = ReadOrientation(frame, ref reader);
            IntPtr source = frame;
            var transform = orientation switch
            {
                2 => 0x8u, 3 => 0x2u, 4 => 0x10u, 5 => 0x9u,
                6 => 0x1u, 7 => 0xBu, 8 => 0x3u, _ => 0u,
            }; // :202 orientation applied
            if (transform != 0)
            {
                if (Method<WicCreateComponent>(factory, 13)(factory, out oriented) < 0) return null;
                if (Method<WicInitializeFlip>(oriented, 8)(oriented, source, transform) < 0) return null;
                source = oriented;
                if (Method<WicGetSize>(source, 3)(source, out width, out height) < 0) return null;
            }

            if (Method<WicCreateComponent>(factory, 10)(factory, out converter) < 0) return null;
            var format = pixelFormat;
            if (Method<WicInitializeConverter>(converter, 8)(converter, source, ref format, 0, IntPtr.Zero, 0, 0) < 0) return null;
            source = converter;

            outputStream = WicSHCreateMemStream(null!, 0);
            if (outputStream == IntPtr.Zero) return null;
            var formatId = containerFormat;
            if (Method<WicCreateEncoder>(factory, 8)(factory, ref formatId, IntPtr.Zero, out encoder) < 0) return null;
            if (Method<WicEncoderInitialize>(encoder, 3)(encoder, outputStream, 2 /* WICBitmapEncoderNoCache */) < 0) return null;
            if (Method<WicCreateNewFrame>(encoder, 10)(encoder, out frameEncode, out propertyBag) < 0) return null;

            if (quality is { } q && propertyBag != IntPtr.Zero)
            {
                namePtr = Marshal.StringToCoTaskMemUni("ImageQuality"); // :215 kCGImageDestinationLossyCompressionQuality
                var option = new WicPropBag2 { VariantType = 4 /* VT_R4 */, Name = namePtr };
                var value = new WicVariant { VariantType = 4 /* VT_R4 */, Float = (float)q };
                Method<WicPropertyBagWrite>(propertyBag, 4)(propertyBag, 1, ref option, ref value);
            }

            if (Method<WicFrameInitialize>(frameEncode, 3)(frameEncode, propertyBag) < 0) return null;
            if (Method<WicFrameSetSize>(frameEncode, 4)(frameEncode, width, height) < 0) return null;
            var framePixelFormat = pixelFormat;
            if (Method<WicFrameSetPixelFormat>(frameEncode, 6)(frameEncode, ref framePixelFormat) < 0) return null;
            if (Method<WicWriteSource>(frameEncode, 11)(frameEncode, source, IntPtr.Zero) < 0) return null;
            if (Method<WicSimpleCall>(frameEncode, 12)(frameEncode) < 0) return null;
            if (Method<WicSimpleCall>(encoder, 11)(encoder) < 0) return null;

            if (Method<WicStreamSeek>(outputStream, 5)(outputStream, 0, 0, out _) < 0) return null;
            using var result = new MemoryStream();
            var buffer = new byte[64 * 1024];
            while (true)
            {
                if (Method<WicStreamRead>(outputStream, 3)(outputStream, buffer, (uint)buffer.Length, out var read) < 0) return null;
                if (read == 0) break;
                result.Write(buffer, 0, (int)read);
            }
            return result.ToArray();
        }
        catch (Exception error) when (error is COMException or OverflowException or OutOfMemoryException)
        {
            return null;
        }
        finally
        {
            if (namePtr != IntPtr.Zero) Marshal.FreeCoTaskMem(namePtr);
            Release(ref propertyBag); Release(ref frameEncode); Release(ref encoder); Release(ref outputStream);
            Release(ref converter); Release(ref oriented); Release(ref reader); Release(ref frame);
            Release(ref decoder); Release(ref factory);
            if (initialized >= 0) WicCoUninitialize();
        }
    }

    private static ushort ReadOrientation(IntPtr frame, ref IntPtr reader)
    {
        if (Method<WicGetMetadataReader>(frame, 8)(frame, out reader) < 0) return 1;
        foreach (var query in new[] { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" })
        {
            var value = new WicPropVariant();
            try
            {
                if (Method<WicGetMetadataByName>(reader, 5)(reader, query, ref value) >= 0 && value.VariantType == 18)
                    return value.UnsignedShort;
            }
            finally
            {
                WicPropVariantClear(ref value);
            }
        }
        return 1;
    }

    private static T Method<T>(IntPtr instance, int index) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), index * IntPtr.Size));

    private static void Release(ref IntPtr instance)
    {
        if (instance == IntPtr.Zero) return;
        Method<WicReleaseCom>(instance, 2)(instance);
        instance = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct WicPropVariant
    {
        [FieldOffset(0)] public ushort VariantType;
        [FieldOffset(8)] public ushort UnsignedShort;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WicPropBag2
    {
        public uint Type;
        public ushort VariantType;
        public uint ClipFormat;
        public uint Hint;
        public IntPtr Name;
        public Guid Clsid;
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct WicVariant
    {
        [FieldOffset(0)] public ushort VariantType;
        [FieldOffset(8)] public float Float;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint WicReleaseCom(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)] private delegate int WicCreateDecoderFromFilename(IntPtr self, string filename, IntPtr vendor, uint access, uint options, out IntPtr decoder);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int WicCreateDecoderFromStream(IntPtr self, IntPtr stream, IntPtr vendor, uint metadataOptions, out IntPtr decoder);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int WicGetFrame(IntPtr self, uint index, out IntPtr frame);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int WicGetSize(IntPtr self, out uint width, out uint height);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int WicCreateComponent(IntPtr self, out IntPtr component);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int WicInitializeFlip(IntPtr self, IntPtr source, uint transform);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int WicInitializeConverter(IntPtr self, IntPtr source, ref Guid format, uint dither, IntPtr palette, double alphaThreshold, uint paletteType);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int WicGetMetadataReader(IntPtr self, out IntPtr reader);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)] private delegate int WicGetMetadataByName(IntPtr self, string query, ref WicPropVariant value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int WicCreateEncoder(IntPtr self, ref Guid containerFormat, IntPtr vendor, out IntPtr encoder);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int WicEncoderInitialize(IntPtr self, IntPtr stream, uint cacheOption);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int WicCreateNewFrame(IntPtr self, out IntPtr frameEncode, out IntPtr propertyBag);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int WicSimpleCall(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int WicFrameInitialize(IntPtr self, IntPtr propertyBag);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int WicFrameSetSize(IntPtr self, uint width, uint height);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int WicFrameSetPixelFormat(IntPtr self, ref Guid pixelFormat);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int WicWriteSource(IntPtr self, IntPtr source, IntPtr rectangle);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int WicPropertyBagWrite(IntPtr self, uint count, ref WicPropBag2 propBag, ref WicVariant value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int WicStreamSeek(IntPtr self, long move, uint origin, out ulong newPosition);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int WicStreamRead(IntPtr self, [Out] byte[] buffer, uint count, out uint read);

    [DllImport("ole32.dll", EntryPoint = "CoInitializeEx")] private static extern int WicCoInitializeEx(IntPtr reserved, uint mode);
    [DllImport("ole32.dll", EntryPoint = "CoUninitialize")] private static extern void WicCoUninitialize();
    [DllImport("ole32.dll", EntryPoint = "CoCreateInstance")] private static extern int WicCoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, out IntPtr instance);
    [DllImport("ole32.dll", EntryPoint = "PropVariantClear")] private static extern int WicPropVariantClear(ref WicPropVariant value);
    [DllImport("shlwapi.dll", EntryPoint = "SHCreateMemStream")] private static extern IntPtr WicSHCreateMemStream(byte[]? init, uint size);
}
