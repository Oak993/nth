using Marrow.App.Port;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// The taskbar overlay seam (Port/TaskbarBadge.cs, ED-219) as a recorder: every overlay the app asked for is kept
/// (count, accessible description and rendered pixels), a null entry is a clear. It never reaches ITaskbarList3 or any
/// window, so no test touches the real taskbar (R-TEST-10). The small-icon size is whatever the test sets, standing for
/// the system metric at the window's DPI.
/// </summary>
public sealed class RecordingTaskbarOverlay : ITaskbarOverlay
{
    public List<TaskbarOverlayIcon?> Calls { get; } = [];

    public int SmallIconSize { get; set; } = 16;

    /// <summary>The overlay the taskbar button shows now, or null when it shows none.</summary>
    public TaskbarOverlayIcon? Current => Calls.Count == 0 ? null : Calls[^1];

    public void SetOverlay(TaskbarOverlayIcon? icon) => Calls.Add(icon);
}
