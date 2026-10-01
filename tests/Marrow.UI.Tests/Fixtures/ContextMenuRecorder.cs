using Avalonia;
using Avalonia.Controls;
using Marrow.UI.Port;

namespace Marrow.UI.Tests.Fixtures;

/// <summary>A context-menu presenter that records what it is asked to show (NativeContextMenus' test seam).</summary>
public sealed class ContextMenuRecorder : IContextMenuPresenter
{
    /// <summary>Every menu shown: the control, the point in its coordinates, and the rows.</summary>
    public List<(Control Target, Point At, IReadOnlyList<ContextMenuEntry> Entries)> Shown { get; } = [];

    /// <inheritdoc />
    public void Present(Control target, Point at, IReadOnlyList<ContextMenuEntry> entries) => Shown.Add((target, at, entries));
}
