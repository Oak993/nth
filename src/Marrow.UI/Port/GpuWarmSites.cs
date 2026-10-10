namespace Marrow.UI.Port;

// ED-NEW-warmupsplit-1 (GR-1, ED-252; the owner's zero-stutter bar): a GPU warm-up step must fit one 60 Hz frame on a launch
// whose programs come from the cache, so a frame of the composer light, the slider liquid or the strike, which makes several
// programs, is warmed one GPU draw a step. The owner's drawing code numbers its GPU draws (its sites: a pass, a bake, the
// bubbles, the glass) with Draw(); while a warm-up part draws (Only), only its site is drawn and every other site still makes
// its surfaces, snapshots and paints as a real frame does, so the drawn site makes exactly the program a real frame makes.
// The family's last part also draws every site after its own, so a site added later is never left unwarmed. Outside a part
// every site draws (the cost of a thread-static read). The scope is thread-static: a part's draw runs synchronously on the
// thread that opened it (the render thread, or the boot preparation's worker), so no real frame is drawn inside it.

/// <summary>The numbered GPU draws of a warm-up part (see the file note).</summary>
public static class GpuWarmSites
{
    [ThreadStatic] private static Part? current;
    [ThreadStatic] private static int lastMet, lastDrawn;

    /// <summary>How many sites the last part closed on this thread met (a frame's sites, counted by tests and the harness).</summary>
    public static int LastMet => lastMet;

    /// <summary>How many of them it drew.</summary>
    public static int LastDrawn => lastDrawn;

    private sealed class Part(int only, bool andAfter, Part? enclosing)
    {
        public readonly int Only = only;
        public readonly bool AndAfter = andAfter;
        public readonly Part? Enclosing = enclosing;
        public int Met, Drawn;
    }

    /// <summary>True when the next numbered GPU draw is to be drawn: always, except inside a warm-up part, where only the
    /// part's own site is (and, for a family's last part, every site after it).</summary>
    public static bool Draw()
    {
        if (current is not { } part) return true;
        var site = part.Met++;
        var drawn = site == part.Only || (part.AndAfter && site > part.Only);
        if (drawn) part.Drawn++;
        return drawn;
    }

    /// <summary>
    /// Opens a warm-up part on this thread: until it is disposed, only site <paramref name="only"/> draws (and every later
    /// site when <paramref name="andAfter"/>). <paramref name="only"/> -1 draws none: a count of the sites only.
    /// </summary>
    public static Scope Only(int only, bool andAfter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(only, -1);
        var part = new Part(only, andAfter, current);
        current = part;
        return new Scope(part);
    }

    /// <summary>A part open on this thread; disposing it closes it (the part it was opened in, if any, is current again).</summary>
    public readonly struct Scope : IDisposable
    {
        private readonly Part? part;

        internal Scope(object part) => this.part = (Part)part;

        /// <summary>How many sites the part has met so far.</summary>
        public int Met => part?.Met ?? 0;

        /// <summary>How many of them it drew.</summary>
        public int Drawn => part?.Drawn ?? 0;

        public void Dispose()
        {
            if (part is null || !ReferenceEquals(current, part)) return;
            current = part.Enclosing;
            (lastMet, lastDrawn) = (part.Met, part.Drawn);
        }
    }
}
