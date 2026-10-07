// PORT-ONLY CZ-7b: the locked voice-composer.js SCRIPT/SYN/STREAM/synthAt inputs.
// These are simulated audio and recognition inputs, never a production model or microphone.
// Arrays extracted by tests/fixtures/customization/voice.generate.cjs; tested against its independent fixture.
using Marrow.Core.Port.Voice.Dsp;
using Marrow.Core.Port.Voice.Transcript;

namespace Marrow.Core.Port.Voice.Session;

public readonly record struct VoiceDesignEvent(double At, bool Add, int Index, string Text, double SpokenEnd);

public sealed class VoiceDesignScript
{
    public static VoiceDesignScript Default { get; } = new();
    private VoiceDesignScript() { }
    public double End => 6.810813040910289;
    public IReadOnlyList<VoiceDesignEvent> Events => events;
    private static readonly (string Text, double End)[] words =
    [
        ("Add", 0.7012801146227867),
        ("a", 0.9361406789300964),
        ("shortcut", 1.5180875290930271),
        ("for", 1.7515614814171567),
        ("the", 2.053661287371069),
        ("theme", 2.419041471914388),
        ("switch,", 2.749371185824275),
        ("and", 4.694224122711457),
        ("show", 5.038777777943761),
        ("it", 5.291301184990444),
        ("in", 5.52653423473239),
        ("the", 5.796396499262192),
        ("tooltip", 6.411951326010749),
        ("too.", 6.755813416927122),
    ];
    private static readonly (double Start, double Duration, double Amplitude, bool Fricative)[] syllables =
    [
        (0.45, 0.22628011462278663, 0.9052175261452794, false),
        (0.7589593071956189, 0.15218137173447757, 0.5052010924369097, false),
        (1.02595046245493, 0.24408575205132366, 0.8215928209945559, true),
        (1.2950362145062537, 0.1980513145867735, 0.6295376734808087, false),
        (1.5732330986484886, 0.15332838276866823, 0.5813122556358576, true),
        (1.8315926632285118, 0.19706862414255738, 0.6487065831944346, false),
        (2.1423472593631594, 0.2516942125512287, 0.908829099535942, true),
        (2.489544949280098, 0.23482623654417692, 0.941460731960833, true),
        (4.472860428821296, 0.1963636938901618, 0.6112975998967886, false),
        (4.772438776846975, 0.24133900109678505, 0.8865725454315543, true),
        (5.097761028045789, 0.16854015694465488, 0.6304063467681408, false),
        (5.349598103193567, 0.15193613153882324, 0.5893699683621526, false),
        (5.585668817609549, 0.18572768165264278, 0.5892451829463243, false),
        (5.9048644765838985, 0.25411549223586916, 0.9636746359989047, true),
        (6.183979968819767, 0.2029713571909815, 0.5066463916376233, false),
        (6.493005634984002, 0.23780778194312005, 0.8264078674465417, true),
    ];
    private static readonly VoiceDesignEvent[] events =
    [
        new(1.0967924505565316, true, 0, "Add", 0.7012801146227867),
        new(1.6015379848098383, true, 1, "a", 0.9361406789300964),
        new(2.0630841956287624, true, 2, "shortcut", 1.5180875290930271),
        new(2.4223540650727227, true, 3, "for", 1.7515614814171567),
        new(2.4823540650727227, true, 4, "the", 2.053661287371069),
        new(3.061354894642718, true, 5, "team", 2.419041471914388),
        new(3.121354894642718, true, 6, "switch", 2.749371185824275),
        new(3.488550903694704, false, 6, "switch,", 2.749371185824275),
        new(3.501601183679886, false, 5, "theme", 2.419041471914388),
        new(5.387798923146911, true, 7, "and", 4.694224122711457),
        new(5.727899790573866, true, 8, "show", 5.038777777943761),
        new(5.803801499684342, true, 9, "it", 5.291301184990444),
        new(6.007277284376324, true, 10, "in", 5.52653423473239),
        new(6.315532257263549, true, 11, "the", 5.796396499262192),
        new(6.953965724436567, true, 12, "tool", 6.411951326010749),
        new(7.367738364166581, true, 13, "too", 6.755813416927122),
        new(7.498320051308256, false, 12, "tooltip", 6.411951326010749),
    ];

    // voice-composer.js:70-86, including the floor of room noise in silence.
    public LevelBands Level(double t)
    {
        double lv = 0, hi = 0;
        foreach (var s in syllables)
        {
            var u = (t - s.Start) / s.Duration;
            if (u < -.2 || u > 1.3) continue;
            var shape = u < 0 ? 0 : u < .28 ? Math.Pow(Math.Sin(Math.PI / 2 * u / .28), 2)
                : u < 1 ? Math.Pow(Math.Cos(Math.PI / 2 * (u - .28) / .72), 1.4) : 0;
            lv = Math.Max(lv, s.Amplitude * shape);
            if (s.Fricative) hi = Math.Max(hi, .85 * Math.Exp(-Math.Pow((t - s.Start + .01) / .045, 2)));
        }
        var trem = 1 + .07 * Math.Sin(2 * Math.PI * 5.6 * t) + .1 * (Noise(t * 9) - .5);
        lv = Math.Clamp(lv * trem + .012 + .01 * Noise(t * 23), 0, 1);
        return new LevelBands(lv, Math.Min(1, lv * .85), lv, Math.Min(1, hi + .22 * lv));
    }

    private static double Hash(double n)
    {
        var x = Math.Sin(n * 127.1) * 43758.5453;
        return x - Math.Floor(x);
    }

    private static double Noise(double t)
    {
        var i = Math.Floor(t);
        var f = t - i;
        var u = f * f * (3 - 2 * f);
        return Hash(i) * (1 - u) + Hash(i + 1) * u;
    }

    // voice-composer.js:304-324: preserve the design's pending-event order (including its revisions).
    internal FinalPlan Final(double elapsed, IReadOnlyList<TranscriptWord> shown)
    {
        var plan = new List<WordOp>();
        foreach (var e in events)
        {
            if (e.At <= elapsed || e.SpokenEnd > elapsed + .12) continue;
            plan.Add(e.Add ? WordOp.Add(e.Index, words[e.Index].Text, true) : WordOp.Revise(e.Index, e.Text));
        }
        for (var i = 0; i < shown.Count; i++)
            if (words[i].End <= elapsed + .12 && shown[i].Text != words[i].Text) plan.Add(WordOp.Revise(i, words[i].Text));
        var at = TranscriptOps.FinalStart;
        var scheduled = new List<ScheduledOp>();
        foreach (var op in plan)
        {
            scheduled.Add(new ScheduledOp(at, op));
            at += op.Kind == WordOpKind.Add ? TranscriptOps.FinalAddSpacing : TranscriptOps.FinalReviseSpacing;
        }
        return new FinalPlan(scheduled, at);
    }
}
