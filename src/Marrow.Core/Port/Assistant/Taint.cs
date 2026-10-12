// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.4; unit AS-1; ED-NEW-dextracore-4). No Swift twin.
// Taint is a property of content, not only of the agent that read it. Text from the web, email, messages, PDFs,
// files outside the access folders, tool output and connectors is untrusted; anything made from it (a summary, a
// merge, a report, a digest paragraph) carries its taint, and a context that has taken in tainted text is tainted
// until the owner's own message starts a fresh turn or the mission ends. Pure: the wiring is AS-7's.
using Marrow.Core.Port;

namespace Marrow.Core.Port.Assistant;

/// <summary>Where a piece of text came from.</summary>
public enum ContentOrigin
{
    /// <summary>The owner's own message (typed, spoken or a card he filled).</summary>
    Owner,
    /// <summary>A file inside the project's access folders or the mission worktree.</summary>
    AccessFolder,
    /// <summary>The assistant's own folder and its kept memory.</summary>
    AssistantFolder,
    Web,
    Email,
    Message,
    Pdf,
    /// <summary>A file outside the access folders.</summary>
    OutsideFile,
    /// <summary>The output of a command or tool that read any of the above.</summary>
    ToolOutput,
    /// <summary>What a connector (an MCP server) returned.</summary>
    Connector,
}

/// <summary>One untrusted source a piece of text was made from: its origin and what it was ("https://x.example/a").</summary>
public sealed record TaintSource(ContentOrigin Origin, string Detail);

/// <summary>The untrusted sources a piece of text or a context was made from. Clean when there are none.</summary>
public sealed record Taint(ValueList<TaintSource> Sources)
{
    public static Taint Clean { get; } = new(ValueList<TaintSource>.Empty);

    public bool IsTainted => Sources.Count > 0;

    /// <summary>Whether text from an origin is untrusted (12.4).</summary>
    public static bool IsUntrusted(ContentOrigin origin) =>
        origin is not (ContentOrigin.Owner or ContentOrigin.AccessFolder or ContentOrigin.AssistantFolder);

    /// <summary>The taint of text read from one origin: clean for a trusted origin.</summary>
    public static Taint From(ContentOrigin origin, string detail) =>
        IsUntrusted(origin) ? new Taint(new ValueList<TaintSource>([new TaintSource(origin, detail)])) : Clean;

    /// <summary>Every source of this and the other, each once, in the order first seen.</summary>
    public Taint Union(Taint other)
    {
        if (!other.IsTainted) return this;
        if (!IsTainted) return other;
        var all = Sources.ToList();
        foreach (var s in other.Sources)
            if (!all.Contains(s)) all.Add(s);
        return new Taint(new ValueList<TaintSource>(all));
    }

    public static Taint UnionOf(IEnumerable<Taint> taints) => taints.Aggregate(Clean, (a, b) => a.Union(b));
}

/// <summary>A piece of text with the taint of everything it was made from.</summary>
public sealed record TaintedText(string Text, Taint Taint)
{
    /// <summary>The owner's own words: clean.</summary>
    public static TaintedText FromOwner(string text) => new(text, Taint.Clean);

    /// <summary>Text read from one origin.</summary>
    public static TaintedText Read(ContentOrigin origin, string detail, string text) => new(text, Taint.From(origin, detail));
}

/// <summary>How taint moves through what agents make (12.4, "Taint follows text").</summary>
public static class TaintRules
{
    /// <summary>A reader's summary of some inputs is tainted by every input. A summary never cleans.</summary>
    public static TaintedText Summarize(string summary, IEnumerable<TaintedText> inputs) =>
        new(summary, Taint.UnionOf(inputs.Select(i => i.Taint)));

    /// <summary>A merge is tainted if any of its inputs is.</summary>
    public static TaintedText Merge(string merged, IEnumerable<TaintedText> inputs) =>
        new(merged, Taint.UnionOf(inputs.Select(i => i.Taint)));

    /// <summary>
    /// The origin a classified read takes text from, or null when it reads nothing untrusted. A web read is Web, a
    /// read outside the access folders is OutsideFile, a connector read is Connector.
    /// </summary>
    public static ContentOrigin? OriginOf(Classification leaf)
    {
        if (!leaf.ReadsUntrusted) return null;
        if (leaf.GrantTarget?.Kind == GrantTargetKind.Connector) return ContentOrigin.Connector;
        return leaf.Class switch
        {
            ActionClass.ReadLocal => ContentOrigin.OutsideFile,
            ActionClass.ReadWeb => ContentOrigin.Web,
            _ => ContentOrigin.ToolOutput,
        };
    }
}

/// <summary>
/// The taint of one turn context: an agent's, a mission head's, or the Assistant's own brain for one wake. Immutable:
/// each step returns the context after it.
/// </summary>
public sealed record TaintContext(Taint Taint)
{
    public static TaintContext Clean { get; } = new(Taint.Clean);

    public bool IsTainted => Taint.IsTainted;

    /// <summary>The context after it takes in a piece of text (a tool result, a summary, a report).</summary>
    public TaintContext Absorb(TaintedText text) => text.Taint.IsTainted ? new TaintContext(Taint.Union(text.Taint)) : this;

    /// <summary>The context after it ran an action that reads untrusted content (12.4: "once an agent has read...").</summary>
    public TaintContext AfterAction(Classification classification)
    {
        var sources = classification.Leaves
            .Select(l => (Leaf: l, Origin: TaintRules.OriginOf(l)))
            .Where(x => x.Origin is not null)
            .Select(x => Taint.From(x.Origin!.Value, x.Leaf.Target ?? x.Leaf.Reason));
        return new TaintContext(Taint.Union(Taint.UnionOf(sources)));
    }

    /// <summary>The owner's own message starts a fresh, untainted turn context.</summary>
    public static TaintContext OwnerMessage() => Clean;

    /// <summary>The mission ending clears its taint.</summary>
    public static TaintContext MissionEnded() => Clean;

    /// <summary>
    /// The Assistant's own brain for one wake: tainted for the rest of the wake when the wake's batch holds any
    /// tainted result.
    /// </summary>
    public static TaintContext ForWake(IEnumerable<TaintedText> batch) => new(Taint.UnionOf(batch.Select(b => b.Taint)));

    /// <summary>
    /// The context a delegated child mission starts in: tainted when the delegating context is (the material goes as
    /// data only), clean otherwise.
    /// </summary>
    public TaintContext ChildContext() => this;

    /// <summary>A report this context sends up: its taint goes with it to the parent.</summary>
    public TaintedText Report(string text) => new(text, Taint);
}
