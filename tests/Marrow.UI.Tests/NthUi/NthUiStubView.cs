using Avalonia.Controls;
using Marrow.Core.NthUi;
using Marrow.UI.NthUi;

namespace Marrow.UI.Tests.NthUi;

/// <summary>ED-NEW-web-w16a-1: a renderer a later unit might register for a kind (W16b's charts, W16c's map): it shows the
/// kind and counts the parts handed to it, so a test sees the registry used and the parts routed to it.</summary>
public sealed class NthUiStubView : NthUiNodeView
{
    private readonly TextBlock text;

    public NthUiStubView(NthUiContext context, NthUiNode node) : base(context, node)
    {
        text = new TextBlock { Text = "stub " + node.Kind };
    }

    public override Control Control => text;

    public List<string> Parts { get; } = [];

    public override NthUiNodeView? Add(NthUiNode child)
    {
        Parts.Add(child.Kind);
        Context.Register(child, this);
        return null;
    }
}
