// PORT-ONLY: shared helpers of the SK-7 tests (first-release feature unit SK-7, DL-7 of porting/first-release/skills-ONDEMAND-PLAN.md section 7).
// No network, no real user data folder: scratch folders are made under MARROW_PORT_DATA_DIR (R-TEST-9) and the test refuses to run without it.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Core.Tests.Port.Skills.Catalogue;
using Marrow.Testing;

namespace Marrow.Core.Tests.Port.Skills.State;

internal static class StateFixtures
{
    public static readonly string Hash1 = new('a', 64);
    public static readonly string Hash2 = new('b', 64);
    public static readonly DateTimeOffset Noon = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    public static SkillsCatalogue Catalogue => CatalogueFixtures.Real;

    public static ItemRecord Rec(string id, ItemState state, string? installed = null, string? target = null, string? failure = null) =>
        new(id, state, installed, target, failure);

    // The three flavours of an in-flight or failed item: a first install, an update (target differs from the installed pin) and a repair (same pin).
    public static IEnumerable<(string Flavour, string? Installed, string? Target)> Flavours { get; } =
    [
        ("fresh", null, "2.0"),
        ("update", "1.0", "2.0"),
        ("repair", "1.0", "1.0"),
    ];

    // One valid record per state and flavour (the invariants of ItemRecord.Problem).
    public static IEnumerable<(string Name, ItemRecord Record)> ValidRecords()
    {
        yield return ("available", Rec("X", ItemState.Available));
        foreach (var (flavour, installed, target) in Flavours)
        {
            yield return ("queued-" + flavour, Rec("X", ItemState.Queued, installed, target));
            yield return ("downloading-" + flavour, Rec("X", ItemState.Downloading, installed, target));
            yield return ("checking-" + flavour, Rec("X", ItemState.Checking, installed, target));
            yield return ("failed-" + flavour, Rec("X", ItemState.Failed, installed, target, "boom"));
        }
        yield return ("installed", Rec("X", ItemState.Installed, "1.0"));
        yield return ("update-available", Rec("X", ItemState.UpdateAvailable, "1.0", "2.0"));
        yield return ("damaged", Rec("X", ItemState.Damaged, "1.0"));
        yield return ("removing", Rec("X", ItemState.Removing, "1.0"));
    }

    public static string Text(AddonsState state) => AddonsStateJson.Serialize(state);

    public static string ScratchFolder()
    {
        var root = OperatingSystem.IsWindows() ? TestDataDirectory.Read() : Environment.GetEnvironmentVariable(TestDataDirectory.VariableName);
        if (string.IsNullOrEmpty(root)) throw new InvalidOperationException("MARROW_PORT_DATA_DIR is not set; the SK-7 store tests refuse to touch any other folder (R-TEST-9).");
        var folder = Path.Combine(root, "sk7-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    // A state that uses every section, for the round-trip tests.
    public static AddonsState Full() => AddonsState.Restore(
        items:
        [
            Rec("T3", ItemState.Installed, "26.2.6"),
            Rec("A1", ItemState.UpdateAvailable, "8a1541c4a3ffa5a20a5a91de0dcf3f0bab1d1ef4", "1111111111111111111111111111111111111111"),
            Rec("T2", ItemState.Failed, null, "24.21.0", "Couldn't reach nodejs.org."),
            Rec("P1", ItemState.Damaged, "lock-1"),
        ],
        acceptedTerms: [new AcceptedTerm(Hash1, "A2", Noon), new AcceptedTerm(Hash2, "O5", Noon.AddDays(1))],
        declined: [new ChatDeclined("chat-2", ["T3", "T1"]), new ChatDeclined("chat-1", ["T10"])],
        found: [new FoundOnPc("T1", @"C:\Python314\python.exe"), new FoundOnPc("T8", @"C:\Program Files\GitHub CLI\gh.exe")]);
}
