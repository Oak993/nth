// PORT-ONLY: web surfaces W16b (DESIGN.md 2.6 "no snaps", 10 Reduce Motion; ED-NEW-web-w16b-1). No Swift counterpart. The
// gridlines of an axis while the data under them changes: when a slider moves a chart's range from 0-60 to 0-120 the round
// values change from every 10 to every 20, and drawing the new set at once would make the old lines and labels vanish with a
// snap. The bank keeps the set that was showing and fades it out as the new one fades in; a value both sets share stays at
// full strength (their strengths add up to one). Positions come from the (springing) domain at draw time, so every line also slides with the data.
using Marrow.UI.Port.Nth;

namespace Marrow.UI.NthUi.Charts;

/// <summary>The tick values an axis is showing, each with the strength it is drawn at.</summary>
internal sealed class NthUiTickBank
{
    private readonly List<Generation> generations = [];

    private sealed class Generation(IReadOnlyList<double> values, NthUiTween alpha)
    {
        public IReadOnlyList<double> Values { get; } = values;

        public NthUiTween Alpha { get; set; } = alpha;
    }

    /// <summary>Sets the values to show now. Without motion the bank holds only these at full strength.</summary>
    public void Set(IReadOnlyList<double> values, double now, bool animate, bool calm)
    {
        if (generations.Count > 0 && generations[^1].Values.SequenceEqual(values)) return;
        if (!animate || generations.Count == 0)
        {
            generations.Clear();
            generations.Add(new Generation(values, NthUiTween.At(1)));
            return;
        }
        var seconds = calm ? NthUiTokens.CalmFadeSeconds : NthUiTokens.FadeSeconds;
        foreach (var old in generations) old.Alpha = old.Alpha.Retarget(0, now, seconds, NthEase.Out);
        generations.Add(new Generation(values, NthUiTween.At(0).Retarget(1, now, seconds, NthEase.Out)));
    }

    /// <summary>Whether a fade is still running.</summary>
    public bool Moving(double now)
    {
        Prune(now);
        return generations.Any(g => !g.Alpha.Settled(now));
    }

    /// <summary>Every value showing and its strength (0 to 1). A value in two sets takes both strengths added, so one that stays
    /// while the others change (the fading set's strength and the arriving set's add up to one) never dims.</summary>
    public IEnumerable<(double Value, double Alpha)> Visible(double now)
    {
        Prune(now);
        var shown = new List<(double Value, double Alpha)>();
        foreach (var generation in generations)
        {
            var alpha = Math.Clamp(generation.Alpha.Value(now), 0, 1);
            foreach (var value in generation.Values)
            {
                var at = shown.FindIndex(s => Math.Abs(s.Value - value) < 1e-9);
                if (at < 0) shown.Add((value, alpha));
                else shown[at] = (value, Math.Min(1, shown[at].Alpha + alpha));
            }
        }
        return shown;
    }

    /// <summary>Forgets a set that has faded out.</summary>
    private void Prune(double now) =>
        generations.RemoveAll(g => g.Alpha.Settled(now) && g.Alpha.Target <= 0);
}
