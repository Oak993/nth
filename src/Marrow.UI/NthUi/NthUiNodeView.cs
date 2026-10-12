// PORT-ONLY: web surfaces W16a (DESIGN.md 7.4 "NthUiView (Marrow.UI) keeps a keyed tree. Each completed line adds or updates
// one node"; "State changes recompute every let and re-render only nodes that read it"; 14 W16b/W16c plug in charts, diagrams
// and the map; ED-NEW-web-w16a-1). No Swift counterpart. The pieces every component is built from: a node's view (its
// control, how its slot sizes it, the children it takes), the context a view reaches the runtime and the host through, and
// the registry later units use to draw a kind their own way (W16b charts, W16c diagrams, svg and the map).
using System.Globalization;
using Avalonia.Controls;
using Marrow.Core.NthUi;

namespace Marrow.UI.NthUi;

/// <summary>The drawn form of one node of a live view.</summary>
public abstract class NthUiNodeView
{
    protected NthUiNodeView(NthUiContext context, NthUiNode node)
    {
        Context = context;
        Node = node;
    }

    public NthUiContext Context { get; }

    public NthUiNode Node { get; }

    public NthUiRuntime Runtime => Context.Runtime;

    /// <summary>The control placed in the node's slot.</summary>
    public abstract Control Control { get; }

    /// <summary>The slot holding <see cref="Control"/>; null for a panel its owner shows itself (a tab, a step).</summary>
    public NthUiSlot? Slot { get; internal set; }

    /// <summary>Exact for a column of slotted children (they animate their own arrival), Follow for everything else.</summary>
    public virtual NthUiSlotMode SlotMode => NthUiSlotMode.Follow;

    /// <summary>The tinted surface the slot draws under the control (a card, a callout, a code well).</summary>
    public virtual NthUiSurface? Surface => null;

    /// <summary>A child line arrived: return the child's own view (it then holds the child's children), or null when this view
    /// draws the child itself (a table row, a chip, a list item) or the child is not drawn (a button's steps).</summary>
    public virtual NthUiNodeView? Add(NthUiNode child) => null;

    /// <summary>Re-reads the runtime: a state or let this node (or a part it draws) reads changed.</summary>
    public virtual void Refresh()
    {
    }

    /// <summary>A code, svg or diagram body grew by a line.</summary>
    public virtual void BodyChanged()
    {
    }

    /// <summary>The next body line while it arrives (null when there is none).</summary>
    public virtual void ShowPending(string? text)
    {
    }

    /// <summary>The stream moved past this node: no more children will arrive.</summary>
    public virtual void Close()
    {
    }

    /// <summary>Whether a text line still arriving may show at the end of this container (DESIGN 7.4).</summary>
    public virtual bool HostsPending => false;

    /// <summary>Puts a child's slot at the end of this container and starts its arrival.</summary>
    protected internal virtual void Insert(NthUiSlot slot, bool visible)
    {
    }
}

/// <summary>What a view reaches the live view through: the runtime, the motion, the host.</summary>
public sealed class NthUiContext
{
    internal NthUiContext(NthUiView view, NthUiMotion motion)
    {
        View = view;
        Motion = motion;
    }

    public NthUiView View { get; }

    public NthUiRuntime Runtime => View.Runtime;

    public CultureInfo Culture => View.Runtime.Culture;

    internal NthUiMotion Motion { get; }

    /// <summary>Builds the view of <paramref name="node"/> (a registered renderer first, then the core components).</summary>
    public NthUiNodeView Create(NthUiNode node) => NthUiRenderers.Create(this, node);

    /// <summary>Wraps <paramref name="view"/> in a slot (or the slot of the same line shown while it arrived) and places it
    /// at the end of <paramref name="host"/>.</summary>
    public NthUiSlot Place(NthUiNodeView view, NthUiNodeView host, NthUiReveal reveal = NthUiReveal.Vertical)
    {
        var adopted = View.TakePending(view, host);
        var slot = adopted ?? new NthUiSlot(Motion);
        slot.Key = view.Node.Key;
        slot.Mode = view.SlotMode;
        slot.Surface = view.Surface;
        slot.Reveal = reveal;
        slot.Child = view.Control;
        view.Slot = slot;
        var visible = Runtime.IsVisible(view.Node);
        if (adopted is null) host.Insert(slot, visible);
        else slot.SetShown(visible);
        return slot;
    }

    /// <summary>A part <paramref name="owner"/> draws itself: a change to what the part reads refreshes the owner.</summary>
    public void Register(NthUiNode part, NthUiNodeView owner) => View.Register(part.Key, owner);

    /// <summary>The reader pressed <paramref name="node"/>: run it, redraw what changed, pass effects to the host.</summary>
    public void Run(NthUiNode node) => View.Run(node);

    /// <summary>A control changed state: redraw the nodes that read it.</summary>
    public void Changed(IReadOnlySet<string> keys) => View.Refresh(keys);

    /// <summary>Asks the host to carry out an effect (copy, open, send ...).</summary>
    public void Raise(NthUiEffect effect) => View.Raise(effect);

    /// <summary>Whether motion is off for this view (Reduce Motion or a view shown whole).</summary>
    public bool IsCalm(Control control) => !Motion.CanAnimate || NthUiCalm.For(control);
}

/// <summary>Lets later units draw a kind their own way (charts, diagrams, svg, the map). Called on the UI thread.</summary>
public static class NthUiRenderers
{
    private static readonly Dictionary<string, Func<NthUiContext, NthUiNode, NthUiNodeView>> Registered = new(StringComparer.Ordinal);

    /// <summary>Draws <paramref name="kind"/> with <paramref name="factory"/> instead of the core view.</summary>
    public static void Register(string kind, Func<NthUiContext, NthUiNode, NthUiNodeView> factory) => Registered[kind] = factory;

    /// <summary>Goes back to the core view of <paramref name="kind"/>.</summary>
    public static void Unregister(string kind) => Registered.Remove(kind);

    public static bool IsRegistered(string kind) => Registered.ContainsKey(kind);

    internal static NthUiNodeView Create(NthUiContext context, NthUiNode node)
    {
        if (!node.Degraded && Registered.TryGetValue(node.Kind, out var factory)) return factory(context, node);
        return NthUiComponents.Create(context, node);
    }
}

/// <summary>An effect a live view asks its host to carry out. Set <see cref="Handled"/> when the host did it; a copy nobody
/// handled goes to the clipboard.</summary>
public sealed class NthUiEffectEventArgs(NthUiEffect effect) : EventArgs
{
    public NthUiEffect Effect { get; } = effect;

    public bool Handled { get; set; }
}
