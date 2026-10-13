using Marrow.App.Assistant.Views;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// PORT-ONLY (AS-16): the host a profile sheet is opened by: a team (Dextra and Channel A) that a test may change while the sheet is open, what Create did with
/// the cards it was given, what it answers, and how many times the sheet was cancelled.
/// </summary>
public sealed class ProfileSheetHost
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    public List<AssistantProfile> Team { get; } =
    [
        new("dextra", "Dextra") { Created = Now },
        new("a", "Channel A") { ParentId = "dextra", Shape = AsturShape.Squircle, Area = "Looks after something.", Created = Now },
    ];

    public List<AsturDraft> Created { get; } = [];

    public int Cancelled { get; set; }

    /// <summary>What Create answers: what is still wrong, or nothing.</summary>
    public IReadOnlyList<string> Answer { get; set; } = [];

    /// <summary>When set, Create waits for it or fails with it.</summary>
    public Func<AsturDraft, Task<IReadOnlyList<string>>>? Slow { get; set; }

    public static AssistantProfile Astur(string name, AsturShape shape, string id) =>
        new(id, name) { ParentId = "dextra", Shape = shape, Area = "Looks after something.", Created = Now };

    public ProfileSheetModel Sheet(ProfileSheetMode mode = ProfileSheetMode.Add, AsturDraft? draft = null, string? editing = null,
        string reserve = "Keeps 30% on Claude 20x (your standing rule).") =>
        new(mode, () => Team, async made =>
        {
            Created.Add(made);
            return Slow is null ? Answer : await Slow(made);
        }, () => Cancelled++, draft, editing, reserve);
}
