using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Marrow.Core.Support;

namespace Marrow.App.Port;

/// <summary>
/// PORT-ONLY (no Swift twin; nth installer PLAN.md 8a, unit U5, ED-NEW-inst-u5-1). The production
/// <see cref="IShellLinks"/>: real shell links through <c>IShellLinkW</c>, <c>IPersistFile</c>, <c>IPropertyStore</c> and
/// <c>IShellLinkDataList</c>, declared with <c>[GeneratedComInterface]</c> and created through <see cref="ComWrappers"/>,
/// because built-in COM interop is not AOT-safe and nth Setup (Native AOT) compiles this file too. <c>PROPVARIANT</c> is
/// marshalled by hand (<c>VT_LPWSTR</c> and <c>VT_CLSID</c> in CoTaskMem, freed with <c>PropVariantClear</c>).
/// <para>Every COM call runs on an STA thread this object owns, because hook processes and pool threads are MTA; a call
/// that does not answer within <see cref="CallTimeout"/> fails with <c>ERROR_TIMEOUT</c>. A read timeout is unknown,
/// so a later call gets its own bounded opportunity to answer. <c>IShellLink::Resolve</c> is never called.</para>
/// <para>Constructed only in <c>src/Marrow.App/Program.cs</c> and <c>src/Marrow.Setup/Program.cs</c> (ShellLinksBindingTests);
/// tests construct it only over a temporary folder.</para>
/// </summary>
public sealed class ShellLinks : IShellLinks, IDisposable
{
    /// <summary>The maximum wait for one shell call; a timeout does not prove the shell is broken.</summary>
    public static readonly TimeSpan CallTimeout = ShellLinkKeys.CallTimeout;

    private const int TimeoutHResult = unchecked((int)0x800705B4);
    private const uint ClsctxInprocServer = 0x1;
    private const uint KfFlagDontVerify = 0x00004000;
    private const uint StgmRead = 0x0, StgmReadWrite = 0x2;
    private const uint SlgpRawPath = 0x4;
    private const uint SldfHasExpSz = 0x00000200;
    private const uint ExpSzLinkSignature = 0xA0000001;
    private const int ExpSzWideOffset = 8 + 260; // EXP_SZ_LINK: cbSize, dwSignature, szTarget[MAX_PATH], swzTarget[MAX_PATH]
    private const ushort VtLpwstr = 31, VtClsid = 72;
    private const uint SsfHideIcons = 0x00004000;
    private const int ShellStateHideIconsBit = 1 << 12; // SHELLSTATE's first DWORD: fHideIcons is its 13th bit field
    private const int ShcneCreate = 0x2, ShcneDelete = 0x4, ShcneUpdateItem = 0x2000;
    private const uint ShcnfPathW = 0x5;
    private const int TextCapacity = 32 * 1024;

    private static readonly Guid ShellLinkClsid = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid UnknownIid = new("00000000-0000-0000-C000-000000000046");
    private static readonly StrategyBasedComWrappers Wrappers = new();
    private static readonly PropertyKey AppUserModelIdKey = new(ShellLinkKeys.AppUserModelFormat, ShellLinkKeys.AppUserModelIdPid);
    private static readonly PropertyKey ToastActivatorKey = new(ShellLinkKeys.AppUserModelFormat, ShellLinkKeys.ToastActivatorClsidPid);
    private static readonly PropertyKey CreatedByKey = new(ShellLinkKeys.NthFormat, ShellLinkKeys.CreatedByPid);

    private readonly IReadOnlyDictionary<ShellKnownFolder, string>? folders;
    private readonly BlockingCollection<Action> queue = new();
    private readonly Thread worker;
    private volatile bool timeoutOccurred;
    private volatile bool disposed;

    /// <param name="knownFolders">Folders to use instead of the user's (tests); any folder not listed is the real one.</param>
    public ShellLinks(IReadOnlyDictionary<ShellKnownFolder, string>? knownFolders = null)
    {
        folders = knownFolders;
        worker = new Thread(Work) { IsBackground = true, Name = "nth shell links (STA)" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
    }

    /// <summary>The apartment the shell calls run in (STA), for the round-trip test.</summary>
    public ApartmentState CallApartment => Invoke(() => Thread.CurrentThread.GetApartmentState());

    public string KnownFolder(ShellKnownFolder folder)
    {
        if (folders is not null && folders.TryGetValue(folder, out var path)) return path;
        var id = folder switch
        {
            ShellKnownFolder.Desktop => new Guid("B4BFCC3A-DB2C-424C-B029-7FE99A87C641"),
            ShellKnownFolder.Programs => new Guid("A77F5D77-2E2B-44C3-A6A2-ABA601054A51"),
            ShellKnownFolder.LocalAppData => new Guid("F1B32785-6FBA-4FCF-9D55-7B8E7F157091"),
            ShellKnownFolder.RoamingAppData => new Guid("3EB685DB-65F9-4CF6-A03A-E3EF65729F3D"),
            ShellKnownFolder.Profile => new Guid("5E6C858F-0E22-4760-9AFE-EA3317B67173"),
        };
        var hr = SHGetKnownFolderPath(ref id, KfFlagDontVerify, 0, out var buffer);
        try
        {
            Marshal.ThrowExceptionForHR(hr);
            return Marshal.PtrToStringUni(buffer) ?? throw new COMException("The shell returned no folder.", unchecked((int)0x80004005));
        }
        finally
        {
            Marshal.FreeCoTaskMem(buffer);
        }
    }

    public string ExpandEnvironment(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Environment.ExpandEnvironmentVariables(path);
    }

    public bool FileExists(string path) => File.Exists(path);

    public DateTime? FolderWriteTimeUtc(string folder) => Directory.Exists(folder) ? Directory.GetLastWriteTimeUtc(folder) : null;

    public IEnumerable<ShellLinkFile> EnumerateLinks(string folder, string searchPattern = "*.lnk")
    {
        var directory = new DirectoryInfo(folder);
        if (!directory.Exists) yield break;
        var options = new EnumerationOptions { MatchType = MatchType.Win32, AttributesToSkip = 0, IgnoreInaccessible = true, RecurseSubdirectories = false };
        // The listing's own data (FindFirstFile): no link is opened, so a cloud placeholder stays one.
        foreach (var file in directory.EnumerateFiles(searchPattern, options))
            if (file.Name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                yield return new ShellLinkFile(file.FullName, file.Attributes, file.LastWriteTimeUtc);
    }

    public ShellLink ReadLink(string path) => ReadLink(path, CallTimeout);

    public ShellLink ReadLink(string path, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Invoke(() => WithLink(path, StgmRead, Read), timeout);
    }

    public void WriteLink(string path, ShellLink link)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(link);
        Invoke(() =>
        {
            var instance = CreateLink();
            try
            {
                var shellLink = (IShellLinkW)instance;
                shellLink.SetPath(link.Target);
                shellLink.SetArguments(link.Arguments);
                shellLink.SetWorkingDirectory(link.WorkingDirectory);
                if (link.IconPath.Length > 0) shellLink.SetIconLocation(link.IconPath, link.IconIndex);
                shellLink.SetDescription(link.Description);
                shellLink.SetShowCmd(link.ShowCommand);
                var store = (IPropertyStore)instance;
                if (link.AppUserModelId is not null) SetString(store, AppUserModelIdKey, link.AppUserModelId);
                if (link.ToastActivatorClsid is { } clsid) SetGuid(store, ToastActivatorKey, clsid);
                if (link.CreatedBy is not null) SetString(store, CreatedByKey, link.CreatedBy);
                store.Commit();
                ((IPersistFile)instance).Save(path, 1);
            }
            finally
            {
                Release(instance);
            }
            return 0;
        });
    }

    public void UpdateLink(string path, ShellLinkChange change) => UpdateLink(path, change, CallTimeout);

    public void UpdateLink(string path, ShellLinkChange change, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(change);
        if (change.IsEmpty) return;
        Invoke(() => WithLink(path, StgmReadWrite, instance =>
        {
            var shellLink = (IShellLinkW)instance;
            if (change.Target is not null) shellLink.SetPath(change.Target);
            if (change.WorkingDirectory is not null) shellLink.SetWorkingDirectory(change.WorkingDirectory);
            if (change.IconPath is not null || change.IconIndex is not null)
            {
                var (iconPath, iconIndex) = ReadIcon(instance);
                shellLink.SetIconLocation(change.IconPath ?? iconPath, change.IconIndex ?? iconIndex);
            }
            if (change.AppUserModelId is not null)
            {
                var store = (IPropertyStore)instance;
                SetString(store, AppUserModelIdKey, change.AppUserModelId);
                store.Commit();
            }
            ((IPersistFile)instance).Save(path, 1);
            return 0;
        }), timeout);
    }

    public void DeleteLink(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    public void MoveLink(string from, string to) => File.Move(from, to, overwrite: false);

    public bool DeleteEmptyFolder(string folder)
    {
        if (!Directory.Exists(folder) || Directory.EnumerateFileSystemEntries(folder).Any()) return false;
        try
        {
            Directory.Delete(folder, recursive: false);
            return true;
        }
        catch (IOException)
        {
            return false; // something appeared in it meanwhile
        }
    }

    public bool DesktopIconsHidden() => Invoke(() =>
    {
        var state = Marshal.AllocCoTaskMem(64);
        try
        {
            Marshal.Copy(new byte[64], 0, state, 64);
            SHGetSetSettings(state, SsfHideIcons, 0);
            return (Marshal.ReadInt32(state) & ShellStateHideIconsBit) != 0;
        }
        finally
        {
            Marshal.FreeCoTaskMem(state);
        }
    });

    public void Notify(ShellChange change, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var eventId = change switch
        {
            ShellChange.Created => ShcneCreate,
            ShellChange.Deleted => ShcneDelete,
            ShellChange.Updated => ShcneUpdateItem,
        };
        SHChangeNotify(eventId, ShcnfPathW, path, 0);
    }

    public IDisposable? TryEnterWriter(TimeSpan wait)
    {
        var mutex = new Mutex(initiallyOwned: false, ShellLinkKeys.WriterMutexName);
        bool owned;
        try
        {
            owned = mutex.WaitOne(wait);
        }
        catch (AbandonedMutexException)
        {
            owned = true; // a writer that died mid-step: the next one repairs what it left
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
        if (owned) return new Hold(mutex);
        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        queue.CompleteAdding();
        // A worker stuck in a call that never answered keeps the queue: disposing it would throw on that background thread.
        if (worker.Join(timeoutOccurred ? TimeSpan.Zero : CallTimeout)) queue.Dispose();
    }

    private T Invoke<T>(Func<T> work) => Invoke(work, CallTimeout);

    private T Invoke<T>(Func<T> work, TimeSpan timeout)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (timeout <= TimeSpan.Zero) throw new COMException("The shell call has no time left.", TimeoutHResult);
        if (timeout > CallTimeout) timeout = CallTimeout;
        if (Thread.CurrentThread == worker) return work();
        var call = new Call<T>(work);
        queue.Add(call.Run);
        if (!call.Done.Wait(timeout))
        {
            timeoutOccurred = true; // Teardown must not spend a second wait after an unanswered call.
            throw new COMException("The shell did not answer in time.", TimeoutHResult);
        }
        return call.Result();
    }

    private void Work()
    {
        foreach (var work in queue.GetConsumingEnumerable()) work();
    }

    private static T WithLink<T>(string path, uint mode, Func<object, T> use)
    {
        var instance = CreateLink();
        try
        {
            ((IPersistFile)instance).Load(path, mode);
            return use(instance);
        }
        finally
        {
            Release(instance);
        }
    }

    private static ShellLink Read(object instance)
    {
        var shellLink = (IShellLinkW)instance;
        var data = (IShellLinkDataList)instance;
        data.GetFlags(out var flags);
        var target = (flags & SldfHasExpSz) != 0 ? ExpandableString(data, ExpSzLinkSignature) : null;
        target ??= Text((buffer, capacity) => shellLink.GetPath(buffer, capacity, 0, SlgpRawPath));
        // The icon as it was set: GetIconLocation returns a %VAR% path raw. Not EXP_SZ_ICON: for an absolute icon the shell
        // writes its own %USERPROFILE% form there (SLDF_HAS_EXP_ICON_SZ), which nothing ever set.
        var (iconPath, iconIndex) = ReadIcon(instance);
        shellLink.GetShowCmd(out var show);
        var store = (IPropertyStore)instance;
        return new ShellLink(
            Target: target,
            Arguments: Text((buffer, capacity) => shellLink.GetArguments(buffer, capacity)),
            WorkingDirectory: Text((buffer, capacity) => shellLink.GetWorkingDirectory(buffer, capacity)),
            IconPath: iconPath,
            IconIndex: iconIndex,
            Description: Text((buffer, capacity) => shellLink.GetDescription(buffer, capacity)),
            ShowCommand: show,
            AppUserModelId: GetString(store, AppUserModelIdKey),
            ToastActivatorClsid: GetGuid(store, ToastActivatorKey),
            CreatedBy: GetString(store, CreatedByKey));
    }

    private static (string Path, int Index) ReadIcon(object instance)
    {
        var index = 0;
        var path = Text((buffer, capacity) => ((IShellLinkW)instance).GetIconLocation(buffer, capacity, out index));
        return (path, index);
    }

    private static string? ExpandableString(IShellLinkDataList data, uint signature)
    {
        data.CopyDataBlock(signature, out var block);
        if (block == 0) return null;
        try
        {
            return Marshal.PtrToStringUni(block + ExpSzWideOffset);
        }
        finally
        {
            _ = LocalFree(block);
        }
    }

    private static string Text(Action<nint, int> read)
    {
        var buffer = Marshal.AllocCoTaskMem(TextCapacity * sizeof(char));
        try
        {
            Marshal.WriteInt16(buffer, 0);
            read(buffer, TextCapacity);
            return Marshal.PtrToStringUni(buffer) ?? "";
        }
        finally
        {
            Marshal.FreeCoTaskMem(buffer);
        }
    }

    private static string? GetString(IPropertyStore store, PropertyKey key)
    {
        store.GetValue(in key, out var value);
        try
        {
            return value.Type == VtLpwstr ? Marshal.PtrToStringUni(value.Value) : null;
        }
        finally
        {
            _ = PropVariantClear(ref value);
        }
    }

    private static Guid? GetGuid(IPropertyStore store, PropertyKey key)
    {
        store.GetValue(in key, out var value);
        try
        {
            if (value.Type != VtClsid || value.Value == 0) return null;
            var bytes = new byte[16];
            Marshal.Copy(value.Value, bytes, 0, 16);
            return new Guid(bytes);
        }
        finally
        {
            _ = PropVariantClear(ref value);
        }
    }

    private static void SetString(IPropertyStore store, PropertyKey key, string text)
    {
        // InitPropVariantFromString is an inline in propvarutil.h: VT_LPWSTR over a CoTaskMem copy, which PropVariantClear frees.
        var value = new PropVariant { Type = VtLpwstr, Value = Marshal.StringToCoTaskMemUni(text) };
        try { store.SetValue(in key, in value); }
        finally { _ = PropVariantClear(ref value); }
    }

    private static void SetGuid(IPropertyStore store, PropertyKey key, Guid guid)
    {
        var memory = Marshal.AllocCoTaskMem(16);
        Marshal.Copy(guid.ToByteArray(), 0, memory, 16);
        var value = new PropVariant { Type = VtClsid, Value = memory };
        try { store.SetValue(in key, in value); }
        finally { _ = PropVariantClear(ref value); }
    }

    private static object CreateLink()
    {
        var clsid = ShellLinkClsid;
        var iid = UnknownIid;
        Marshal.ThrowExceptionForHR(CoCreateInstance(ref clsid, 0, ClsctxInprocServer, ref iid, out var unknown));
        try
        {
            return Wrappers.GetOrCreateObjectForComInstance(unknown, CreateObjectFlags.UniqueInstance);
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    private static void Release(object instance)
    {
        if (instance is ComObject com) com.FinalRelease(); // closes the file at once, before a rename or delete
    }

    private sealed class Call<T>(Func<T> work)
    {
        private T value = default!;
        private ExceptionDispatchInfo? error;

        public ManualResetEventSlim Done { get; } = new();

        public void Run()
        {
            try { value = work(); }
            catch (Exception failure) { error = ExceptionDispatchInfo.Capture(failure); }
            finally { Done.Set(); }
        }

        public T Result()
        {
            error?.Throw();
            return value;
        }
    }

    private sealed class Hold(Mutex mutex) : IDisposable
    {
        private int released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) != 0) return;
            mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CoCreateInstance(ref Guid clsid, nint outer, uint context, ref Guid iid, out nint instance);

    [DllImport("ole32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int PropVariantClear(ref PropVariant value);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint LocalFree(nint memory);

    [DllImport("shell32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int SHGetKnownFolderPath(ref Guid folder, uint flags, nint token, out nint path);

    [DllImport("shell32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void SHGetSetSettings(nint state, uint mask, int set);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void SHChangeNotify(int eventId, uint flags, string item1, nint item2);
}

/// <summary><c>PROPERTYKEY</c>: a format id and a property id.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey(Guid format, int id)
{
    public Guid Format = format;
    public uint Id = (uint)id;
}

/// <summary><c>PROPVARIANT</c> on x64: the type, three reserved words, and a 16-byte union (only its first pointer is used here).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    public ushort Type;
    public ushort Reserved1;
    public ushort Reserved2;
    public ushort Reserved3;
    public nint Value;
    public nint Value2;
}

[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("000214F9-0000-0000-C000-000000000046")]
internal partial interface IShellLinkW
{
    void GetPath(nint file, int capacity, nint findData, uint flags);
    void GetIDList(out nint idList);
    void SetIDList(nint idList);
    void GetDescription(nint name, int capacity);
    void SetDescription(string name);
    void GetWorkingDirectory(nint directory, int capacity);
    void SetWorkingDirectory(string directory);
    void GetArguments(nint arguments, int capacity);
    void SetArguments(string arguments);
    void GetHotkey(out ushort hotkey);
    void SetHotkey(ushort hotkey);
    void GetShowCmd(out int showCommand);
    void SetShowCmd(int showCommand);
    void GetIconLocation(nint iconPath, int capacity, out int iconIndex);
    void SetIconLocation(string iconPath, int iconIndex);
    void SetRelativePath(string relativePath, uint reserved);
    void Resolve(nint window, uint flags); // declared for the v-table only; never called
    void SetPath(string file);
}

/// <summary><c>IPersistFile</c> with <c>IPersist::GetClassID</c> first, as in its v-table.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("0000010B-0000-0000-C000-000000000046")]
internal partial interface IPersistFile
{
    void GetClassID(out Guid classId);
    [PreserveSig] int IsDirty();
    void Load(string fileName, uint mode);
    void Save(string? fileName, int remember);
    void SaveCompleted(string? fileName);
    void GetCurFile(out nint fileName);
}

[GeneratedComInterface]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
internal partial interface IPropertyStore
{
    void GetCount(out uint count);
    void GetAt(uint index, out PropertyKey key);
    void GetValue(in PropertyKey key, out PropVariant value);
    void SetValue(in PropertyKey key, in PropVariant value);
    void Commit();
}

[GeneratedComInterface]
[Guid("45E2B4AE-B1C3-11D0-B92F-00A0C90312E1")]
internal partial interface IShellLinkDataList
{
    void AddDataBlock(nint dataBlock);
    void CopyDataBlock(uint signature, out nint dataBlock);
    void RemoveDataBlock(uint signature);
    void GetFlags(out uint flags);
    void SetFlags(uint flags);
}
