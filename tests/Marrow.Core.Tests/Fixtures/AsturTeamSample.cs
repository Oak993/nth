using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Fixtures;

/// <summary>PORT-ONLY (AS-16): a small team for the Astur tests: Dextra, and Asturs by name and shape.</summary>
public static class AsturTeamSample
{
    public static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    public const string DextraId = "dextra";

    public static AssistantProfile Dextra() => new(DextraId, "Dextra") { Created = Now };

    public static AssistantProfile Astur(string name, AsturShape shape, string? id = null, bool retired = false, params string[] aliases) =>
        new(id ?? name.ToLowerInvariant().Replace(' ', '-'), name)
        {
            ParentId = DextraId,
            Shape = shape,
            Area = name + " area",
            Aliases = new ValueList<string>(aliases),
            Retired = retired,
            Created = Now,
        };

    public static IReadOnlyList<AssistantProfile> Team(params AssistantProfile[] asturs) => [Dextra(), .. asturs];

    /// <summary>Dextra with Channel A (squircle) and Channel B (hexagon).</summary>
    public static IReadOnlyList<AssistantProfile> Channels() => Team(Astur("Channel A", AsturShape.Squircle), Astur("Channel B", AsturShape.Hexagon));
}
