using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Testing;

namespace Marrow.Services.Tests.Fixtures;

/// <summary>
/// PORT-ONLY (AS-16): a store in a throwaway folder, the reserve settings that hold the Asturs' day slices, and a team over them with ids that
/// count up ("a1", "a2"...), so a test can name the Astur it made.
/// </summary>
public sealed class AsturTeamRig : IDisposable
{
    private readonly AssistantTempRoot temp = new();
    private int next;

    public AsturTeamRig()
    {
        Store = AssistantStore.Open(temp.Roots);
        Open();
    }

    public AssistantStore Store { get; }

    public ReserveSettings Budgets { get; private set; } = new();

    public AssistantTeam Team { get; private set; } = null!;

    public ManualTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 13, 9, 0, 0, TimeSpan.Zero));

    public string Root => temp.Store;

    /// <summary>Opens a fresh team over the same store, as nth does at its next start; the earlier one is let go.</summary>
    public AssistantTeam Open(Func<string>? newId = null)
    {
        Team?.Dispose();
        Team = AssistantTeam.Open(Store, new AssistantTeamOptions { Budgets = Budgets, Time = Clock, NewId = newId ?? (() => "a" + Interlocked.Increment(ref next)) });
        return Team;
    }

    /// <summary>Dextra's own profile, made.</summary>
    public async Task<AssistantProfile> RootAsync() => (await Team.EnsureRootAsync()).Profile!;

    public static AsturDraft Draft(string name, AsturShape shape, string area = "Looks after something.") => new()
    {
        Name = name,
        Shape = shape,
        Area = area,
        Budget = new(15),
    };

    /// <summary>The path of an Astur's profile file in the store.</summary>
    public string ProfilePath(string id) => Path.Combine(temp.Store, "assistants", id, "profile.json");

    public void Dispose()
    {
        Team.Dispose();
        Store.Dispose();
        temp.Dispose();
    }
}
