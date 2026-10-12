// PORT-ONLY: Dextra (DESIGN.md rev 5, 5.1 Backpressure; unit AS-2; ED-NEW-dextra-as2-5, -6). No Swift twin.
// The bounded queue behind the event bus. Enqueue is O(1) under one short lock and runs no subscriber code. Token and
// progress drafts of one thread and stream fold into one for 2 seconds. When the queue is over its limit the oldest
// quiet draft goes first, then the oldest info draft; a needs-you or error draft is never dropped. Order is kept per
// thread: a draft that is held back for coalescing is released the moment any other draft of its thread arrives, so it
// is always delivered before that draft. Pure: the caller passes the time in.
namespace Marrow.Core.Port.Assistant;

/// <summary>The queue between the taps (main thread) and the subscribers (a background thread).</summary>
public sealed class FeedQueue(int limit = FeedQueue.DefaultLimit)
{
    /// <summary>"Quiet events drop oldest-first when the queue is over 10,000" (5.1).</summary>
    public const int DefaultLimit = 10_000;

    /// <summary>"Token spend (coalesced to 1 per 2 s per thread)" (5.1).</summary>
    public static readonly TimeSpan CoalesceWindow = TimeSpan.FromSeconds(2);

    private sealed class Node(FeedDraft draft)
    {
        public FeedDraft Draft = draft;
        public DateTimeOffset? DueAt;
        public LinkedList<Node>? Container;
        public LinkedListNode<Node>? Handle;
        public LinkedList<Node>? DropList;
        public LinkedListNode<Node>? DropHandle;
    }

    private readonly Lock gate = new();
    private readonly LinkedList<Node> ready = new();
    private readonly LinkedList<Node> held = new();
    private readonly LinkedList<Node> droppableQuiet = new();
    private readonly LinkedList<Node> droppableInfo = new();
    private readonly Dictionary<Guid, List<Node>> openByThread = [];
    private long dropped;

    /// <summary>Drafts waiting to be taken (ready and held back).</summary>
    public int Count
    {
        get { lock (gate) return ready.Count + held.Count; }
    }

    /// <summary>Drafts held back for coalescing.</summary>
    public int HeldCount
    {
        get { lock (gate) return held.Count; }
    }

    /// <summary>Drafts dropped for being over the limit since the queue was made.</summary>
    public long DroppedCount
    {
        get { lock (gate) return dropped; }
    }

    /// <summary>Adds a draft. O(1) apart from releasing the few drafts a thread has held back.</summary>
    public void Enqueue(FeedDraft draft)
    {
        lock (gate)
        {
            if (draft.Coalesces && draft.ThreadId is { } thread)
            {
                if (openByThread.TryGetValue(thread, out var open))
                {
                    var match = open.Find(node => node.Draft.Kind == draft.Kind && node.Draft.Stream == draft.Stream);
                    if (match is not null && draft.Time < match.Draft.Time + CoalesceWindow)
                    {
                        match.Draft = match.Draft.Merge(draft);
                        return;
                    }
                    ReleaseThread(thread);
                }
                var node = new Node(draft) { DueAt = draft.Time + CoalesceWindow };
                Append(held, node);
                if (!openByThread.TryGetValue(thread, out open)) openByThread[thread] = open = [];
                open.Add(node);
            }
            else
            {
                if (draft.ThreadId is { } other && openByThread.ContainsKey(other)) ReleaseThread(other);
                Append(ready, new Node(draft));
            }
            Trim();
        }
    }

    /// <summary>
    /// Takes the drafts that may be delivered at <paramref name="now"/>, in order: everything not held back, and every
    /// held draft whose window has ended. A held draft whose window is still open stays.
    /// </summary>
    public int TakeReady(DateTimeOffset now, List<FeedDraft> into, int max = int.MaxValue)
    {
        lock (gate)
        {
            MoveDue(now);
            var taken = 0;
            while (taken < max && ready.First is { } first)
            {
                Unlink(first.Value);
                into.Add(first.Value.Draft);
                taken++;
            }
            return taken;
        }
    }

    /// <summary>When the next held draft's window ends, or null when nothing is held back.</summary>
    public DateTimeOffset? NextDue
    {
        get
        {
            lock (gate)
            {
                DateTimeOffset? next = null;
                foreach (var node in held) if (node.DueAt is { } due && (next is null || due < next)) next = due;
                return next;
            }
        }
    }

    /// <summary>Ends every window now (a flush, or the bus closing), so the next take delivers everything.</summary>
    public void ReleaseAll()
    {
        lock (gate)
        {
            foreach (var thread in openByThread.Keys.ToArray()) ReleaseThread(thread);
        }
    }

    /// <summary>
    /// The owner hid a chat while drafts of it were waiting: their words are taken out and they will be built as the
    /// events of a hidden chat. Returns how many drafts were changed.
    /// </summary>
    public int Hide(Guid thread)
    {
        lock (gate)
        {
            var changed = 0;
            LinkedList<Node>[] lists = [ready, held];
            foreach (var list in lists)
                foreach (var node in list)
                {
                    if (node.Draft.ThreadId != thread || node.Draft.Chat is not { } chat) continue;
                    node.Draft = node.Draft with { Chat = chat with { Hidden = true }, Detail = null };
                    changed++;
                }
            return changed;
        }
    }

    private void Append(LinkedList<Node> list, Node node)
    {
        node.Container = list;
        node.Handle = list.AddLast(node);
        var drop = node.Draft.Severity switch
        {
            FeedSeverity.Quiet => droppableQuiet,
            FeedSeverity.Info => droppableInfo,
            _ => null,
        };
        if (drop is null) return;
        node.DropList = drop;
        node.DropHandle = drop.AddLast(node);
    }

    private void Unlink(Node node)
    {
        node.Container!.Remove(node.Handle!);
        node.Container = null;
        node.Handle = null;
        if (node.DropList is { } drop)
        {
            drop.Remove(node.DropHandle!);
            node.DropList = null;
            node.DropHandle = null;
        }
    }

    // Moves a thread's held drafts to the end of ready, in the order they were made, and closes their windows.
    private void ReleaseThread(Guid thread)
    {
        if (!openByThread.Remove(thread, out var open)) return;
        foreach (var node in open) Move(node);
    }

    private void MoveDue(DateTimeOffset now)
    {
        if (held.Count == 0) return;
        for (var node = held.First; node is not null;)
        {
            var next = node.Next;
            if (node.Value.DueAt is { } due && due <= now) CloseWindow(node.Value);
            node = next;
        }
    }

    private void CloseWindow(Node node)
    {
        if (node.Draft.ThreadId is { } thread && openByThread.TryGetValue(thread, out var open))
        {
            open.Remove(node);
            if (open.Count == 0) openByThread.Remove(thread);
        }
        Move(node);
    }

    // Held to ready. The drop handle stays valid: a draft keeps its place in the oldest-first drop order.
    private void Move(Node node)
    {
        if (node.Container == held)
        {
            held.Remove(node.Handle!);
            node.Handle = ready.AddLast(node);
            node.Container = ready;
        }
        node.DueAt = null;
    }

    private void Trim()
    {
        while (ready.Count + held.Count > limit)
        {
            var victim = droppableQuiet.First?.Value ?? droppableInfo.First?.Value;
            if (victim is null) return;
            if (victim.Container == held && victim.Draft.ThreadId is { } thread && openByThread.TryGetValue(thread, out var open))
            {
                open.Remove(victim);
                if (open.Count == 0) openByThread.Remove(thread);
            }
            Unlink(victim);
            dropped = checked(dropped + victim.Draft.Count);
        }
    }
}
