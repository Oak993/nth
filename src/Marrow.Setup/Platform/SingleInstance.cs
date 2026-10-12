using System.IO.MemoryMappedFiles;
using Marrow.Core.Support;

namespace Marrow.Setup.Platform;

/// <summary>Brings an existing window to the front (Windows in the app, a recorder in tests).</summary>
public interface IWindowFocuser
{
    /// <summary>Lets <paramref name="processId"/> take the foreground, restores <paramref name="window"/> if minimised and brings it forward.</summary>
    void Focus(nint window, int processId);
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 3, step 1; unit U1, ED-NEW-inst-u1-1). One setup window per session: the first start
/// creates the named lock (<c>Local\nth Setup</c>) and, once its window exists, publishes the window's handle and its
/// process id in a small named section beside it; a second start finds the lock, reads the handle, brings that window to
/// the front and exits 0. Only the lock's existence counts (no thread owns it), so it is never abandoned; it ends with the
/// first process.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    /// <summary>The lock's name (PLAN.md 3, step 1).</summary>
    public const string SetupLockName = SetupFolders.SetupLockName; // one name for the lock and for the sweeps that look for it (unit U9)

    private const int SectionSize = 16; // the window handle (8 bytes) and the process id (4), little-endian

    private readonly Mutex lockObject;
    private readonly string name;
    private MemoryMappedFile? section;

    private SingleInstance(Mutex lockObject, string name)
    {
        this.lockObject = lockObject;
        this.name = name;
    }

    /// <summary>The lock, or null when another process (or an earlier holder in this one) already has it.</summary>
    public static SingleInstance? TryAcquire(string name = SetupLockName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var created = false;
        var candidate = new Mutex(initiallyOwned: false, name, out created);
        if (created) return new SingleInstance(candidate, name);
        candidate.Dispose();
        return null;
    }

    /// <summary>Publishes the first window so a second start can bring it forward.</summary>
    public void Publish(nint window, int processId)
    {
        section ??= MemoryMappedFile.CreateNew(SectionName(name), SectionSize, MemoryMappedFileAccess.ReadWrite);
        using var view = section.CreateViewAccessor(0, SectionSize, MemoryMappedFileAccess.ReadWrite);
        view.Write(0, (long)window);
        view.Write(8, processId);
    }

    /// <summary>
    /// The second start: waits up to <paramref name="patience"/> for the first window to be published (it may still be
    /// starting), then brings it forward. True when it was found.
    /// </summary>
    public static bool FocusExisting(IWindowFocuser focuser, TimeSpan patience, string name = SetupLockName)
    {
        ArgumentNullException.ThrowIfNull(focuser);
        var deadline = DateTime.UtcNow + patience;
        while (true)
        {
            if (TryRead(name, out var window, out var processId) && window != 0)
            {
                focuser.Focus(window, processId);
                return true;
            }
            if (DateTime.UtcNow >= deadline) return false;
            Thread.Sleep(50);
        }
    }

    private static bool TryRead(string name, out nint window, out int processId)
    {
        window = 0;
        processId = 0;
        try
        {
            using var existing = MemoryMappedFile.OpenExisting(SectionName(name), MemoryMappedFileRights.Read);
            using var view = existing.CreateViewAccessor(0, SectionSize, MemoryMappedFileAccess.Read);
            window = (nint)view.ReadInt64(0);
            processId = view.ReadInt32(8);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false; // not published yet
        }
    }

    private static string SectionName(string name) => name + ".window";

    public void Dispose()
    {
        section?.Dispose();
        lockObject.Dispose();
    }
}

/// <summary>Windows' foreground rules: the second start was just launched by the user, so it may hand the foreground on.</summary>
public sealed class WindowsFocuser : IWindowFocuser
{
    public void Focus(nint window, int processId)
    {
        if (!OperatingSystem.IsWindows() || window == 0 || !Native.IsWindow(window)) return;
        if (processId > 0) _ = Native.AllowSetForegroundWindow((uint)processId);
        if (Native.IsIconic(window)) _ = Native.ShowWindow(window, Native.SW_RESTORE);
        _ = Native.SetForegroundWindow(window);
    }
}
