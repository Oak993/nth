// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.4 "Taint", "Taint follows text", "Instructions found in content"; unit AS-7;
// ED-NEW-dextra-as7-7). No Swift twin. What one Dextra chat's tool calls bring in, read from its provider events: each
// call is placed by TaintWiring when it starts (and again when the provider fills in its kind, title or detail), so the
// chat is tainted before the call's output reaches the model. A head's own calls (AgentEvent) count as the chat's. The
// output of a call that taints is kept, bounded, and read for instructions when the call ends. Main thread only, like
// ThreadRuntime.Handle that feeds it; every buffer and count is capped.
using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Providers;

namespace Marrow.App.Assistant;

/// <summary>A call's taint, and whether it may leave text in the chat's files (the workspace floor, ED-NEW-dextra-as7-7).</summary>
public sealed record TaintStep(Taint Taint, bool WritesFiles);

/// <summary>Instructions found in the output of a call that read untrusted content, with that call's source.</summary>
public sealed record InstructionsFound(InstructionFinding Finding, TaintSource Source);

/// <summary>What one provider event brought in.</summary>
public sealed record TaintObservation(IReadOnlyList<TaintStep> Steps, IReadOnlyList<InstructionsFound> Found)
{
    public static TaintObservation None { get; } = new([], []);
}

public sealed class AgentTaintTracker
{
    /// <summary>How many of a chat's calls are followed at once; past it the oldest is read and let go.</summary>
    public const int OpenCalls = 16;

    /// <summary>How deep head events nest (an AgentEvent inside an AgentEvent) before nth stops following them.</summary>
    public const int Depth = 4;

    private const int HeadChars = InstructionScanner.ScanChars;
    private const int TailChars = InstructionScanner.ScanChars;
    private const int TailSlack = 8_000;

    private sealed class Call(ToolCall tool, Taint taint, long order)
    {
        public ToolCall Tool { get; set; } = tool;
        public Taint Taint { get; set; } = taint;
        public long Order { get; } = order;
        public StringBuilder Head { get; } = new();
        public StringBuilder Tail { get; } = new();
    }

    private readonly Dictionary<Guid, Dictionary<string, Call>> chats = [];
    private long order;

    /// <summary>How many calls of the chat are followed now (for the tests of the caps).</summary>
    public int Open(Guid thread) => chats.TryGetValue(thread, out var calls) ? calls.Count : 0;

    /// <summary>
    /// Reads one provider event of a Dextra chat. <paramref name="context"/> places the chat's paths (it is asked only
    /// when a call needs placing).
    /// </summary>
    public TaintObservation Observe(Guid thread, ProviderEvent @event, Func<ClassifierContext> context, ProviderKind provider)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(context);
        var steps = new List<TaintStep>();
        var found = new List<InstructionsFound>();
        Observe(thread, "", @event, context, provider, steps, found, 0);
        return steps.Count == 0 && found.Count == 0 ? TaintObservation.None : new TaintObservation(steps, found);
    }

    /// <summary>The chat's turn ended or it is gone: what its open calls returned is read, and they are let go.</summary>
    public IReadOnlyList<InstructionsFound> Close(Guid thread)
    {
        if (!chats.Remove(thread, out var calls)) return [];
        var found = new List<InstructionsFound>();
        foreach (var call in calls.Values.OrderBy(c => c.Order)) Read(call, found);
        return found;
    }

    private void Observe(Guid thread, string prefix, ProviderEvent @event, Func<ClassifierContext> context, ProviderKind provider,
        List<TaintStep> steps, List<InstructionsFound> found, int depth)
    {
        switch (@event)
        {
            case ProviderEvent.ToolStarted started:
            {
                var key = prefix + started.Id;
                var calls = CallsOf(thread);
                if (calls.Remove(key, out var earlier)) Read(earlier, found);
                var taint = TaintWiring.Capped(TaintWiring.Of(started.Call, context(), provider));
                var call = new Call(started.Call, taint, ++order);
                steps.Add(new TaintStep(taint, TaintWiring.MayWriteFiles(started.Call)));
                Append(call, started.Call.Output);
                if (started.Call.ToolCallStatus != ToolCall.Status.Running)
                {
                    Read(call, found);
                    break;
                }
                calls[key] = call;
                Evict(calls, found);
                break;
            }
            case ProviderEvent.ToolUpdated updated:
            {
                var key = prefix + updated.Id;
                var calls = CallsOf(thread);
                var update = updated.Update;
                if (!calls.TryGetValue(key, out var call))
                {
                    // A call nth did not see start (it was let go, or nth came in late): placed from what the update says.
                    if (update.Kind is null && update.Title is null) break;
                    var tool = new ToolCall { ToolCallKind = update.Kind ?? ToolCall.Kind.Other, Title = update.Title ?? "", Detail = update.Detail };
                    call = new Call(tool, TaintWiring.Capped(TaintWiring.Of(tool, context(), provider)), ++order);
                    steps.Add(new TaintStep(call.Taint, TaintWiring.MayWriteFiles(tool)));
                    calls[key] = call;
                    Evict(calls, found);
                }
                else if (update.Kind is not null || update.Title is not null || update.Detail is not null)
                {
                    var tool = call.Tool with
                    {
                        ToolCallKind = update.Kind ?? call.Tool.ToolCallKind,
                        Title = update.Title ?? call.Tool.Title,
                        Detail = update.Detail ?? call.Tool.Detail,
                    };
                    var taint = TaintWiring.Capped(TaintWiring.Of(tool, context(), provider));
                    var writes = TaintWiring.MayWriteFiles(tool) && !TaintWiring.MayWriteFiles(call.Tool);
                    call.Tool = tool;
                    var grown = TaintWiring.Capped(call.Taint.Union(taint));
                    if (grown != call.Taint || writes)
                    {
                        call.Taint = grown;
                        steps.Add(new TaintStep(grown, TaintWiring.MayWriteFiles(tool)));
                    }
                }
                Append(call, update.Output);
                if (update.Status is { } status && status != ToolCall.Status.Running)
                {
                    calls.Remove(key);
                    Read(call, found);
                }
                break;
            }
            case ProviderEvent.ToolOutput output:
                if (chats.TryGetValue(thread, out var open) && open.TryGetValue(prefix + output.Id, out var running)) Append(running, output.Text);
                break;
            case ProviderEvent.AgentEvent inner when depth < Depth:
                Observe(thread, prefix + inner.AgentID + "/", inner.Value, context, provider, steps, found, depth + 1);
                break;
            case ProviderEvent.AgentEvent:
                // Nested deeper than nth follows: what it read is unknown, so it taints (fail closed).
                steps.Add(new TaintStep(Taint.From(ContentOrigin.ToolOutput, "a helper's helper nth could not follow"), false));
                break;
            case ProviderEvent.TurnCompleted or ProviderEvent.Exited when prefix.Length == 0:
                found.AddRange(Close(thread));
                break;
        }
    }

    private Dictionary<string, Call> CallsOf(Guid thread)
    {
        if (!chats.TryGetValue(thread, out var calls)) chats[thread] = calls = new Dictionary<string, Call>(StringComparer.Ordinal);
        return calls;
    }

    private static void Evict(Dictionary<string, Call> calls, List<InstructionsFound> found)
    {
        while (calls.Count > OpenCalls)
        {
            var oldest = calls.MinBy(p => p.Value.Order);
            calls.Remove(oldest.Key);
            Read(oldest.Value, found);
        }
    }

    // Only the output of a call that tainted is kept: the first and the last stretch the scanner reads.
    private static void Append(Call call, string? text)
    {
        if (string.IsNullOrEmpty(text) || !call.Taint.IsTainted) return;
        var room = HeadChars - call.Head.Length;
        if (room > 0)
        {
            call.Head.Append(text.AsSpan(0, Math.Min(room, text.Length)));
            if (text.Length <= room) return;
            text = text[room..];
        }
        call.Tail.Append(text.Length > TailChars ? text.AsSpan(text.Length - TailChars) : text.AsSpan());
        if (call.Tail.Length > TailChars + TailSlack) call.Tail.Remove(0, call.Tail.Length - TailChars);
    }

    private static void Read(Call call, List<InstructionsFound> found)
    {
        if (!call.Taint.IsTainted || call.Head.Length == 0) return;
        var text = call.Tail.Length == 0 ? call.Head.ToString() : call.Head + "\n" + call.Tail;
        call.Head.Clear();
        call.Tail.Clear();
        if (InstructionScanner.Find(text) is { } finding) found.Add(new InstructionsFound(finding, call.Taint.Sources[0]));
    }
}
