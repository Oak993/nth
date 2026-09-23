// Source: DroppyCode/Services/Store/Storage.swift (B2 1fbf81e), :1-390.
// R-FILE-1: every type this Swift file declares lives here (StoredContinuation, Storage, AttachmentSource,
// MimeType, DocumentPrefetch, DiskWriter).
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Services.Store;

// :6-15. StoredContinuation is used only by sweepOrphanedAttachments (:117-149), which is not a record of this
// part (no PR-STORE-01x/02x names it here); left for a later part of this unit.

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
    string WallpaperDirectory { get; }
    string WorktreesDirectory { get; }
    string PatchesDirectory { get; }
}

/// <summary>
/// The production shape of Storage.swift's seven roots (:19-48). Each of <see cref="Root"/>,
/// <see cref="ThreadsDirectory"/>, <see cref="AttachmentsDirectory"/>, <see cref="WallpaperDirectory"/>,
/// <see cref="WorktreesDirectory"/> and <see cref="PatchesDirectory"/> is created with intermediates on first
/// access and cached for the life of the instance (PR-STORE-001, -003, -004, -005, -006, -007); a creation
/// failure is swallowed (<c>try?</c>) exactly as the source swallows it, and the resolved path is still returned
/// and cached. <see cref="LibraryURL"/> (:27) is a pure derivation with no side effect.
/// </summary>
public sealed class StorageLocations : IStorageLocations
{
    private readonly Lazy<string> root;
    private readonly Lazy<string> threadsDirectory;
    private readonly Lazy<string> attachmentsDirectory;
    private readonly Lazy<string> wallpaperDirectory;
    private readonly Lazy<string> worktreesDirectory;
    private readonly Lazy<string> patchesDirectory;

    private StorageLocations(Func<string> root, Func<string> homeDirectory, bool isDevelopment)
    {
        this.root = CreateOnce(() =>
        {
            var url = root(); // :20-22
            SwiftTry.Ignore(() => Directory.CreateDirectory(url)); // :23 try? createDirectory(withIntermediateDirectories: true)
            return url;
        });
        threadsDirectory = SubDirectory("threads"); // :31, :54-58 directory(_:)
        attachmentsDirectory = SubDirectory("attachments"); // :33
        wallpaperDirectory = SubDirectory("wallpaper"); // :36

        // :43-48 stateDirectory(_:). DEVIATION OD-0012 F1: the folder name shown in Settings and handed to git is
        // ".marrow" / ".marrow-dev", not upstream AppInfo.stateDirectoryName (".droppy-code" / ".droppy-code-dev",
        // Text.swift:6): this path is a named exception to keeping AppInfo's own literal.
        var stateDirectoryName = isDevelopment ? ".marrow-dev" : ".marrow"; // DEVIATION OD-0012 F1
        worktreesDirectory = StateSubDirectory(homeDirectory, stateDirectoryName, "worktrees"); // :38
        patchesDirectory = StateSubDirectory(homeDirectory, stateDirectoryName, "patches"); // :41

        Lazy<string> SubDirectory(string name) => CreateOnce(() =>
        {
            var url = SwiftPath.AppendingPathComponent(this.root.Value, name); // :55
            SwiftTry.Ignore(() => Directory.CreateDirectory(url)); // :56
            return url;
        });
    }

    private static Lazy<string> StateSubDirectory(Func<string> homeDirectory, string stateDirectoryName, string name) =>
        CreateOnce(() =>
        {
            // :44-45: one appendingPathComponent call handed "name/subname" creates both levels at once.
            var url = SwiftPath.AppendingPathComponent(
                SwiftPath.AppendingPathComponent(homeDirectory(), stateDirectoryName), name);
            SwiftTry.Ignore(() => Directory.CreateDirectory(url)); // :46
            return url;
        });

    private static Lazy<string> CreateOnce(Func<string> factory) => new(factory, LazyThreadSafetyMode.ExecutionAndPublication);

    public string Root => root.Value; // :19-25
    public string LibraryURL => SwiftPath.AppendingPathComponent(Root, "library.json"); // :27, no side effect
    public string ThreadsDirectory => threadsDirectory.Value; // :31
    public string AttachmentsDirectory => attachmentsDirectory.Value; // :33
    public string WallpaperDirectory => wallpaperDirectory.Value; // :36
    public string WorktreesDirectory => worktreesDirectory.Value; // :38
    public string PatchesDirectory => patchesDirectory.Value; // :41

    /// <summary>
    /// DEVIATION OD-0008 C7: %LOCALAPPDATA%\Marrow Code, a fixed literal rather than AppInfo.Name (upstream
    /// appends the display name, falling back to "Droppy Code", Text.swift:4), so the port never shares a folder
    /// with the owner's separate Electron "Marrow" (%LOCALAPPDATA%\Marrow). dead_code.md #44: CaptureRun.storageRoot
    /// (:20) is reachable only via --website-captures / --tour-captures, never a normal launch, so the redirect is
    /// not reproduced here.
    /// </summary>
    public static StorageLocations CreateProduction() => new(
        root: () => SwiftPath.AppendingPathComponent(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Marrow Code"),
        homeDirectory: () => LoginEnvironment.HomeDirectory,
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
    public string WallpaperDirectory => throw new SwiftTrapException(Message);
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
    public sealed record Image(byte[] Data, string MimeType) : AttachmentSource;

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

/// <summary>:245-335. Only <see cref="Take"/> and <see cref="Forget"/> are needed by this part's records
/// (PR-STORE-030: deleteDocuments calls forget for every id); <see cref="Warm"/>'s queue and worker are not named
/// by any record in this part and are left MARROW_STUB for a later part.</summary>
public sealed class DocumentPrefetch
{
    public static DocumentPrefetch Shared { get; } = new();

    private struct State
    {
        public RecentCache<Guid, ThreadDocument> Ready;
        public Dictionary<Guid, Guid> Pending;
    }

    private readonly Mutex<State> state = new(new State
    {
        Ready = new RecentCache<Guid, ThreadDocument>(limit: 12, costLimit: 16 * 1024 * 1024), // :266-267
        Pending = new Dictionary<Guid, Guid>(),
    });

    private DocumentPrefetch() { } // :246

    // :279-308. MARROW_STUB: out of this part's scope (no PR-STORE-01x record here for warm/queue/worker).
    public TaskHandle? Warm(IReadOnlyList<Guid> ids, bool urgent = false) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Store/Storage.swift:279-308");

    // :324-330. The queue field itself is not modeled in this part (Warm's scope); nothing is queued yet, so
    // there is nothing for :327's queue removal to do.
    public ThreadDocument? Take(Guid id) => state.WithLock((ref State s) =>
    {
        s.Pending.Remove(id); // :326
        return s.Ready.RemoveValue(id, out var value) ? value : null; // :328
    });

    // :332-334
    public void Forget(Guid id) => Take(id);
}

/// <summary>:337-389.</summary>
public sealed class DiskWriter
{
    public static DiskWriter Shared { get; } = new(); // :338

    private static readonly Logger FailureLog = new(Logger.Subsystem, "storage");

    private readonly SerialQueue queue = new("droppycode.storage.write"); // :339
    private readonly Mutex<Dictionary<string, object>> pending = new(new Dictionary<string, object>(SwiftStringComparer.Instance)); // :343

    private DiskWriter() { }

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
            Write(latest, url); // :354
        });
    }

    // :358-361
    public void WriteSynchronously(object value, string url)
    {
        pending.WithLock((ref Dictionary<string, object> map) => map.Remove(url)); // :359
        queue.Sync(() => { Write(value, url); return true; }); // :360
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

    // :381-388. DEVIATION OD-0012 F1: the diagnostic text renames "Droppy Code" to "Marrow"; no record fixes its
    // exact wording (NSLog has no other caller in the tree).
    private static void Write(object value, string url)
    {
        byte[] data;
        try
        {
            var element = JsonSerializer.SerializeToElement(value, value.GetType(), Coding.StorageOptions);
            data = SwiftJson.Write(element, SwiftJsonStyle.Storage); // :382
        }
        catch (Exception e) when (SwiftTry.IsThrownError(e))
        {
            return;
        }
        try
        {
            FileOps.WriteAtomic(url, data); // :384
        }
        catch (Exception error)
        {
            FailureLog.Error($"Marrow could not save {SwiftPath.LastPathComponent(url)}: {SwiftErrors.LocalizedDescription(error)}"); // :386
        }
    }
}

/// <summary>Where the app keeps its library, thread histories and attachments (:18).</summary>
public static class Storage
{
    public static Ambient<IStorageLocations> Locations { get; } = new(new UnboundStorageLocations());

    public static string Root => Locations.Current.Root; // :19-25
    public static string LibraryURL => Locations.Current.LibraryURL; // :27
    public static string ThreadsDirectory => Locations.Current.ThreadsDirectory; // :31
    public static string AttachmentsDirectory => Locations.Current.AttachmentsDirectory; // :33
    public static string WallpaperDirectory => Locations.Current.WallpaperDirectory; // :36
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

        byte[] data;
        try
        {
            data = File.ReadAllBytes(ThreadURL(id)); // :75
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
                var jpeg = TranscodeFileToJpeg(file.Path, 0.88); // :186-187
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
