// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-contract; PLAN unit U0). Where the app's Ignis parts plug into code that must
// not depend on them (the ComposerCapsuleSeams pattern). Every seam is unbound, and inert, until Program.Main binds
// the running app (unit U3), so a test of another view or provider never builds Ignis; a test overrides one per async
// flow. The seams live in Core so Services (ClaudeSession) can read HelperAgents; the UI seams therefore hand back
// `object` (an Avalonia Control in the app), since Core does not reference Avalonia.
using Marrow.Core.Models;

namespace Marrow.Core.Port.Ignis;

/// <summary>What the Claude CLI launch asks Ignis about the prompt-cache lifetimes (ED-NEW-ignis-runtime): the chat, its model,
/// the environment the CLI inherits and its working directory (for the read-only check of the user's Claude settings).</summary>
public sealed record IgnisLifetimeQuery(ProviderKind Provider, string? MainModel, Guid? ThreadID, IReadOnlyDictionary<string, string> Environment, string? WorkingDirectory);

/// <summary>What a provider session asks when it builds its helper agents (Claude --agents JSON, the scout entry only).</summary>
public sealed record IgnisHelperQuery(ProviderKind Provider, string? MainModel, Guid? ThreadID = null);

public static class IgnisSeams
{
    /// <summary>The app's controller; null (unbound): Ignis is not running and every hook does nothing.</summary>
    public static Ambient<Func<IIgnisController?>> Controller { get; } = new(new Func<IIgnisController?>(() => null));

    /// <summary>
    /// The Kindle capsule for a thread's composer (ComposerView, where the ContextMeter sits). It returns the control to
    /// host; null: the composer keeps its ContextMeter (the fallback).
    /// </summary>
    public static Ambient<Func<Guid, object?>> ComposerSlot { get; } = new(_ => null);

    /// <summary>
    /// Whether the timeline shows an item (ThreadTimeline). Bound: hides "ignis:ping:" items unless Settings › Ignis
    /// "Show keep-warm pings" is on (<see cref="ShowsItem"/>). Unbound: shows everything.
    /// </summary>
    public static Ambient<Func<TimelineItem, bool>> TimelineFilter { get; } = new(_ => true);

    /// <summary>
    /// The quiet event line for an "ignis:event:" notice item (TimelineRows, instead of the plain NoticeRow); null: the
    /// row renders as an ordinary notice.
    /// </summary>
    public static Ambient<Func<TimelineItem, object?>> EventLine { get; } = new(_ => null);

    /// <summary>
    /// The model for read-only look-up helpers and Hydra scouts when Smart helpers is on (Claude only: the scout entry
    /// of --agents gets it; writing helpers keep the main model). null: no change, every helper keeps the main model.
    /// </summary>
    public static Ambient<Func<IgnisHelperQuery, string?>> HelperAgents { get; } = new(_ => null);

    /// <summary>
    /// The cache lifetimes for one Claude CLI launch (ED-NEW-ignis-runtime): the variables it adds to the process environment
    /// so every request outside the main conversation (subagents at every depth, workflows, forks) gets them. null
    /// (unbound, or Ignis has no choice): nothing is set and every CLI default stays.
    /// </summary>
    public static Ambient<Func<IgnisLifetimeQuery, Engine.IgnisLifetimeChoice?>> CacheLifetimes { get; } = new(_ => null);

    /// <summary>
    /// Whether Ignis is on (any mode but Off) for a chat, so a session nth drives may add its request-level cache levers
    /// (stable cache ids, retention hints, cache breakpoints: never anything the model reads). Unbound: false.
    /// </summary>
    public static Ambient<Func<ProviderKind, Guid?, bool>> IsOn { get; } = new((_, _) => false);

    /// <summary>The bound filter's rule: an item shows unless it is a hidden keep-warm ping and pings are not shown.</summary>
    public static bool ShowsItem(TimelineItem item, bool showPings) => showPings || !IgnisEvent.IsPingItem(item.Id);
}
