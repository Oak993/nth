using Marrow.UI.Panes;

namespace Marrow.UI.Tests.Fixtures;

/// <summary>The order of a pane header's drag events ("lift", "move", "drop", "cancel").</summary>
public sealed class HeaderDragLog
{
    public HeaderDragLog(PaneHeader header)
    {
        header.DragLifted += _ => Log.Add("lift");
        header.DragMoved += _ => Log.Add("move");
        header.DragDropped += _ => Log.Add("drop");
        header.DragCanceled += () => Log.Add("cancel");
    }

    public List<string> Log { get; } = [];
}
