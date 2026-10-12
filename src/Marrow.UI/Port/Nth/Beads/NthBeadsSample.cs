// PORT-ONLY: Calculi (ED-NEW-calculi-wu1-1). What the painter reads each frame: three beads by identity, the flow offset, the
// Scriptio stroke and the move's labels. A plain value: sampling allocates nothing (DESIGN.md section 11).
namespace Marrow.UI.Port.Nth.Beads;

/// <summary>One bead: centre in n units, scale (1 is the logo's dot) and opacity (exactly 1 between entrance and exit).</summary>
public readonly record struct NthBead(double X, double Y, double S, double O);

/// <summary>What a move is (section 4): a looping motif, a designed or generic hub-to-hub path, a beat, a Reduce Motion slide or
/// still, an entrance or an exit.</summary>
public enum NthBeadsMoveKind : byte { Motif, Trans, Beat, Calm, Still, Enter, Exit }

/// <summary>The engine's turn mode.</summary>
public enum NthBeadsMode : byte { Gone, Running, Exiting }

/// <summary>The working states (section 6), plus the two an exit sets (<see cref="Error"/>, <see cref="Stopped"/>).</summary>
public enum NthBeadsState : byte { Connecting, Thinking, Tool, Streaming, Waiting, Tactus, Hydra, Background, Error, Stopped }

/// <summary>A frame of the beads (reference <c>frame()</c>).</summary>
public struct NthBeadsSample
{
    /// <summary>False when nothing is shown (before the first entrance, after an exit).</summary>
    public bool Present;
    /// <summary>The beads by identity (bead 0, 1, 2).</summary>
    public NthBead B0, B1, B2;
    /// <summary>Scriptio's write: the beads are not drawn, the stroke is.</summary>
    public bool Hidden;
    /// <summary>The reflow offset (section 9) the painter adds to every bead and the stroke.</summary>
    public double FlowX, FlowY;
    /// <summary>Scriptio's stroke as up to three wire segments [from, to] in wire units.</summary>
    public int StrokeCount;
    public double Stroke0From, Stroke0To, Stroke1From, Stroke1To, Stroke2From, Stroke2To;
    /// <summary>The glint's progress, or -1 for none (no shipped move has one).</summary>
    public double Glint;
    /// <summary>True while a bead may be outside the glyph's bounds (a Rejoin that starts outside the envelope).</summary>
    public bool Flight;
    public NthBeadsMoveId Move;
    public NthBeadsMoveKind Kind;
    public NthBeadsHub Hub;
    public NthBeadsMotif Motif;
    public NthBeadsState State;
    public NthBeadsMode Mode;
    /// <summary>The move's local time and its duration.</summary>
    public double T, Duration;

    /// <summary>Bead <paramref name="i"/> (0..2).</summary>
    public readonly NthBead this[int i] => i switch { 0 => B0, 1 => B1, _ => B2 };

    /// <summary>The move's reference name (diagnostics, fixtures).</summary>
    public readonly string MoveName => NthBeadsMoves.NameOf(Move);

    /// <summary>Total drawn length of the stroke, in wire units.</summary>
    public readonly double StrokeLength
    {
        get
        {
            double sum = 0;
            if (StrokeCount > 0) sum += Math.Max(0, Stroke0To - Stroke0From);
            if (StrokeCount > 1) sum += Math.Max(0, Stroke1To - Stroke1From);
            if (StrokeCount > 2) sum += Math.Max(0, Stroke2To - Stroke2From);
            return sum;
        }
    }
}
