using Marrow.Setup.Motion;

namespace Marrow.Setup.Ui;

/// <summary>
/// PORT-ONLY (unit U2, ED-NEW-inst-u2-1). Things that happen at set times on the window's frame loop (Ready's sequence,
/// "Opening…" before the window closes, "Copied" going back): they follow the same clock as every animation, so a test with
/// a manual frame source steps them exactly, and <see cref="Clear"/> drops whatever has not happened yet.
/// </summary>
public sealed class Cues : IMotion
{
    private readonly List<(double At, Action Run)> waiting = [];
    private Animator? animator;
    private double clock;

    public void Attach(Animator frames) => animator = frames;

    /// <summary>The cues not yet run.</summary>
    public int Pending => waiting.Count;

    /// <summary>Runs <paramref name="run"/> <paramref name="seconds"/> from now (at once without a frame loop, or for 0).</summary>
    public void After(double seconds, Action run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (animator is null || seconds <= 0)
        {
            run();
            return;
        }
        waiting.Add((clock + seconds, run));
        animator.Start(this);
    }

    /// <summary>Drops every cue not yet run.</summary>
    public void Clear() => waiting.Clear();

    bool IMotion.Advance(double seconds)
    {
        clock += seconds;
        while (waiting.Count > 0)
        {
            var next = -1;
            for (var i = 0; i < waiting.Count; i++)
                if (waiting[i].At <= clock + 1e-9 && (next < 0 || waiting[i].At < waiting[next].At)) next = i;
            if (next < 0) break;
            var cue = waiting[next];
            waiting.RemoveAt(next);
            cue.Run();
        }
        return waiting.Count > 0;
    }
}
