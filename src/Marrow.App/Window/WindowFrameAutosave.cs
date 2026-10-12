using Marrow.Core.Port;

namespace Marrow.App.Window;

/// <summary>
/// PORT-ONLY (ED-NEW-perf-startup-structure-1). <c>NSWindow.setFrameAutosaveName</c> writes the frame on every move and resize;
/// here a move or size drag raises <c>PositionChanged</c> and <c>Resized</c> at the display rate, and every settings write
/// serializes the whole store and replaces its file (about 5 ms and 120 KB each), so an unthrottled save kept the writer thread and
/// the disk busy for the whole drag. The frame now goes through a <see cref="SettingsWriteCoalescer"/> of its own: the first change
/// after a quiet time is written at once, later ones are held and the latest is written when <see cref="Interval"/> ends, so a frame
/// is saved at most half a second late and a drag writes about twice a second. <see cref="Flush"/> writes what is held (the window is
/// hidden, the app is ending). A frame equal to the one last seen is not written again (a move that also reports a size).
/// Main-actor only, like the coalescer.
/// </summary>
internal sealed class WindowFrameAutosave
{
    /// <summary>How long the saved frame may lag a window that keeps moving.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(500);

    // The fields of the saved frame (ED-006: the port's own format).
    internal const string X = "x";
    internal const string Y = "y";
    internal const string Width = "width";
    internal const string Height = "height";

    private readonly SettingsWriteCoalescer writes;
    private readonly string name;
    private readonly Func<Frame?> read;
    private Frame? last;

    /// <summary>The window's frame as the settings store keeps it: the position in pixels, the content size in points.</summary>
    internal readonly record struct Frame(long X, long Y, double Width, double Height);

    /// <param name="store">The store the frame goes to.</param>
    /// <param name="name">The key the frame is saved under (the autosave name).</param>
    /// <param name="read">The window's frame now; <c>null</c> when it is not a frame to save (minimised, maximised).</param>
    public WindowFrameAutosave(ISettingsStore store, string name, Func<Frame?> read)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(read);
        writes = new SettingsWriteCoalescer(store, Interval);
        this.name = name;
        this.read = read;
    }

    /// <summary>Whether a frame is waiting for its interval to end.</summary>
    public bool HasPending => writes.HasPending;

    /// <summary>The window moved or was resized.</summary>
    public void Changed()
    {
        if (read() is not { } frame || last == frame) return;
        last = frame;
        writes.Write(name, new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [X] = frame.X,
            [Y] = frame.Y,
            [Width] = frame.Width,
            [Height] = frame.Height,
        });
    }

    /// <summary>Writes the held frame now.</summary>
    public void Flush() => writes.Flush();
}
