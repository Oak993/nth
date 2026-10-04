namespace Marrow.UI.Tests.Port.Voice;

/// <summary>VT-8, V-T50: the matrix of ref-orbs.js: 2 orbs x 9 states x 2 ball sizes x 2 themes, in the script's order.</summary>
internal static class OrbJobs
{
    public static IReadOnlyList<string> Orbs { get; } = ["siri-wave", "voice-membrane"];

    public static IReadOnlyList<string> States { get; } = ["rest-0", "rest-1.3", "rest-4.0", "hover", "listen-quiet", "listen-speech", "listen-loud", "processing", "blocked"];

    public static IReadOnlyList<int> Balls { get; } = [32, 26];

    public static IReadOnlyList<string> Themes { get; } = ["dark", "light"];

    public static IReadOnlyList<OrbJob> All { get; } =
    [
        .. from orb in Orbs from state in States from ball in Balls from theme in Themes select new OrbJob(orb, state, ball, theme),
    ];

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var job in All) data.Add(job.Name);
        return data;
    }
}
