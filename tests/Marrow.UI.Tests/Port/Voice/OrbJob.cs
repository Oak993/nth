using Marrow.Core.Port.Voice;
using Marrow.Core.Port.Voice.Orb;

namespace Marrow.UI.Tests.Port.Voice;

/// <summary>VT-8, V-T50: one frame of the fidelity matrix: an orb, a steady state of V-11.4's table, the ball size and the theme. The names are ref-orbs.js's.</summary>
internal sealed record OrbJob(string Orb, string State, int Ball, string Theme)
{
    public string Name => $"ref_{Orb}_{State}_{Ball}_{Theme}";

    public VoiceOrbKind Kind => Orb == "siri-wave" ? VoiceOrbKind.Siri : VoiceOrbKind.Membrane;

    public bool Dark => Theme == "dark";

    /// <summary>The bands of the three Listening frames; the voice follower rests on <c>All</c>.</summary>
    public OrbBands? Bands => State switch
    {
        "listen-quiet" => new OrbBands(0.1, 0.08, 0.04, 0.1),
        "listen-speech" => new OrbBands(0.5, 0.6, 0.35, 0.5),
        "listen-loud" => new OrbBands(0.95, 0.9, 0.8, 0.9),
        _ => null,
    };

    /// <summary>The time the frame is rendered at: 0, 1.3 or 4.0 for Rest, else 2.0.</summary>
    public double T => State switch { "rest-0" => 0, "rest-1.3" => 1.3, "rest-4.0" => 4.0, _ => 2.0 };

    /// <summary>The steady state's targets (V-11.4's table, from Core's OrbMotion).</summary>
    public OrbMotionTargets Targets
    {
        get
        {
            var (mode, hovered) = State switch
            {
                "hover" => (OrbMotionMode.Rest, true),
                "processing" => (OrbMotionMode.Processing, false),
                "blocked" => (OrbMotionMode.Blocked, false),
                var s when s.StartsWith("listen", StringComparison.Ordinal) => (OrbMotionMode.Listening, false),
                _ => (OrbMotionMode.Rest, false),
            };
            return OrbMotion.TargetsFor(mode, hovered, false, Bands?.All ?? 0);
        }
    }

    public OrbFrameState FrameState => new(Act: Targets.Act, Bands: Bands, Speed: Targets.Speed, Scale: Targets.Scale, Glow: Targets.Glow, Lift: Targets.Lift, Desat: Targets.Desat);

    /// <summary>The motion phase of a constant speed held for <see cref="T"/> seconds (ref-orbs.js sets the same).</summary>
    public double Phase => T * Targets.Speed * OrbSeeds.IdleSpeed(Kind);
}
