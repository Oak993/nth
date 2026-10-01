// PORT-ONLY. SwiftUI's HStack lays its children on one line and, unless told otherwise, centres each of them vertically
// on that line (HStack(alignment: .center) is the default). Avalonia's horizontal StackPanel instead stretches every
// child to the row's height, so a TextBlock beside a taller item draws at the top of its slot: the turn file card's title
// stood 6.5 points above its badge's centre (TimelineRows.swift:2741-2757 beside 2-chat-use.mov at 32 s). An HStack here is
// a horizontal StackPanel that gives each child the stack's alignment unless the child set its own.
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Marrow.UI.Port;

/// <summary><c>HStack(alignment:spacing:)</c>: a horizontal stack whose children sit on its alignment line (see the note).</summary>
public class HStack : StackPanel
{
    /// <param name="alignment">The HStack's alignment: <see cref="VerticalAlignment.Center"/> by default (SwiftUI's .center);
    /// .top and .bottom map to Top and Bottom.</param>
    public HStack(VerticalAlignment alignment = VerticalAlignment.Center)
    {
        Alignment = alignment;
        Orientation = Orientation.Horizontal;
        Children.CollectionChanged += OnChildrenChanged;
    }

    /// <summary>The line the children sit on.</summary>
    public VerticalAlignment Alignment { get; }

    private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is null) return;
        foreach (var item in e.NewItems)
            if (item is Control child && !child.IsSet(VerticalAlignmentProperty))
                child.VerticalAlignment = Alignment;
    }
}
