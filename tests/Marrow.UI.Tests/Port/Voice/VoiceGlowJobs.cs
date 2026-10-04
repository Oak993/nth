namespace Marrow.UI.Tests.Port.Voice;

/// <summary>VT-9, V-T60: the matrix of ref-glow.js: 7 states x 2 composer sizes x 2 themes = 28 frames, in the script's order (state, then size, then theme).</summary>
internal static class VoiceGlowJobs
{
    public static IReadOnlyList<(int Width, int Height)> Sizes { get; } = [(720, 96), (480, 150)];

    public static IReadOnlyList<string> Themes { get; } = ["dark", "light"];

    /// <summary>The states: idle-active, the raw level held at 0.2, 0.6 and 1.0 for 3 s, and the processing sweep at 0.8, 1.35 and 1.9 s after 3 s of speech at 0.6.</summary>
    public static IReadOnlyList<(string State, VoiceGlowSegment[] Script)> States { get; } =
    [
        ("idle", [new(120, 0, false)]),
        ("level-0.2", [new(180, 0.2, false)]),
        ("level-0.6", [new(180, 0.6, false)]),
        ("level-1.0", [new(180, 1.0, false)]),
        ("proc-0.8", [new(180, 0.6, false), new(48, 0, true)]),
        ("proc-1.35", [new(180, 0.6, false), new(81, 0, true)]),
        ("proc-1.9", [new(180, 0.6, false), new(114, 0, true)]),
    ];

    public static IReadOnlyList<VoiceGlowJob> All { get; } =
    [
        .. from state in States from size in Sizes from theme in Themes select new VoiceGlowJob(state.State, size.Width, size.Height, theme, state.Script),
    ];

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var job in All) data.Add(job.Name);
        return data;
    }
}
