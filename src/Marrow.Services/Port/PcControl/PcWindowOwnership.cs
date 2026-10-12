// PORT-ONLY: Tactus, PC control (OD-0141, OD-0144, ED-NEW-pcctl-7, ED-NEW-pcctl-8; DESIGN.md 2.10). One window, one chat:
// a window is held by the chat (lead chat or Hydra head) whose action used it first, until that chat's turn ends or it
// has not used the window for a minute; another chat that acts on it gets busy_window, naming the holder. Shared by every
// chat's engine.
namespace Marrow.Services.Port.PcControl;

public sealed class PcWindowOwnership(IPcClock clock)
{
    /// <summary>A hold lapses when its chat has not used the window for this long.</summary>
    public const int IdleMs = 60_000;

    private readonly Lock gate = new();
    private readonly Dictionary<nint, Hold> holds = [];

    /// <summary>The label of another chat that holds the window now; null when it is free or this chat holds it.</summary>
    public string? Holder(nint window, string thread)
    {
        lock (gate)
        {
            return holds.TryGetValue(window, out var hold) && hold.Thread != thread && clock.NowMs - hold.LastUse < IdleMs ? hold.Label : null;
        }
    }

    /// <summary>Takes or refreshes the hold for this chat; returns the other holder's label when another chat holds it.</summary>
    public string? Claim(nint window, string thread, string label)
    {
        lock (gate)
        {
            if (holds.TryGetValue(window, out var hold) && hold.Thread != thread && clock.NowMs - hold.LastUse < IdleMs) return hold.Label;
            holds[window] = new Hold(thread, label, clock.NowMs);
            return null;
        }
    }

    /// <summary>The chat's turn ended: every window it held is free.</summary>
    public void Release(string thread)
    {
        lock (gate)
        {
            foreach (var window in holds.Where(h => h.Value.Thread == thread).Select(h => h.Key).ToList()) holds.Remove(window);
        }
    }

    private sealed record Hold(string Thread, string Label, long LastUse);
}
