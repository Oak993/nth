using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Input;
using Marrow.App.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// The production Windows reader (Port/KeyboardLayoutText.cs, WindowsKeyboardLayoutText) bound to a layout the test names
/// (ED-270): the layout is loaded into this process by its identifier (US 00000409, German 00000407, French 0000040C) and
/// never activated, so no test depends on the keyboard layout of the PC it runs on and none changes it. Disposing the fixture
/// unloads every layout it brought into the session's layout list (GetKeyboardLayoutList), so the list is as it was before
/// (ED-283 item 7); a layout the list already held is left loaded. Helper code, never in a test class (R-TEST-3).
/// </summary>
public sealed class WindowsLayoutReaders : IDisposable
{
    private const uint LoadWithoutTellingTheShell = 0x80; // KLF_NOTELLSHELL; no KLF_ACTIVATE, so the active layout stays

    // Shared by every instance of the process: which handles were in the list before the first live fixture loaded anything,
    // and how many fixtures are alive, so overlapping fixtures never unload a layout another one still reads.
    private static readonly object Gate = new();
    private static int live;
    private static HashSet<IntPtr> listedBefore = [];
    private static readonly HashSet<IntPtr> Loaded = [];

    private bool disposed;

    public WindowsLayoutReaders()
    {
        lock (Gate)
        {
            if (live++ == 0) listedBefore = [.. Installed()];
        }
    }

    public IKeyboardLayoutText Us => For("00000409");

    public IKeyboardLayoutText German => For("00000407");

    public IKeyboardLayoutText French => For("0000040C");

    /// <summary>The layouts in the session's layout list (GetKeyboardLayoutList), as the handles Windows gives them.</summary>
    public static IntPtr[] Installed()
    {
        var count = GetKeyboardLayoutList(0, null);
        var handles = new IntPtr[count];
        return handles[..GetKeyboardLayoutList(count, handles)];
    }

    /// <summary>The handle of the layout the calling thread has active (what the app reads), to show a test never changed it.</summary>
    public static IntPtr Active() => GetKeyboardLayout(0);

    /// <summary>The handle of the layout the fixture loaded for <paramref name="layoutId"/>.</summary>
    public IntPtr HandleOf(string layoutId) => Load(layoutId);

    private IKeyboardLayoutText For(string layoutId)
    {
        var handle = Load(layoutId);
        var type = typeof(MenuKeyEquivalents).Assembly.GetType("Marrow.App.Port.WindowsKeyboardLayoutText")!;
        return (IKeyboardLayoutText)Activator.CreateInstance(type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [(Func<IntPtr>)(() => handle)], null)!;
    }

    private IntPtr Load(string layoutId)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var handle = LoadKeyboardLayoutW(layoutId, LoadWithoutTellingTheShell);
        if (handle == IntPtr.Zero) throw new InvalidOperationException("Windows could not load the keyboard layout " + layoutId);
        lock (Gate)
        {
            if (!listedBefore.Contains(handle)) Loaded.Add(handle);
        }
        return handle;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lock (Gate)
        {
            if (--live > 0) return;
            var stuck = new List<IntPtr>();
            foreach (var handle in Loaded)
            {
                // Unloading a layout the thread has active is refused by Windows, which is why none is ever activated.
                if (!UnloadKeyboardLayout(handle) && Installed().Contains(handle)) stuck.Add(handle);
            }
            Loaded.Clear();
            if (stuck.Count > 0)
                throw new InvalidOperationException("Windows would not unload the keyboard layouts " + string.Join(", ", stuck.Select(handle => handle.ToString("X", System.Globalization.CultureInfo.InvariantCulture))));
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadKeyboardLayoutW(string layoutId, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnloadKeyboardLayout(IntPtr layout);

    [DllImport("user32.dll")]
    private static extern int GetKeyboardLayoutList(int count, [Out] IntPtr[]? handles);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint thread);
}
