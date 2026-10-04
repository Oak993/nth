// PORT-ONLY: SK-11d / WU-9. Either the user's effective skills.paths (verbatim) or the reason the bundle is left out.
namespace Marrow.Services.Port.Skills.Providers;

public sealed record OpenCodeSkillPathsResult(IReadOnlyList<string>? Paths, string? LeftOutReason)
{
    public bool LeftOut => LeftOutReason is not null;
    public static OpenCodeSkillPathsResult Of(IReadOnlyList<string>? paths) => new(paths ?? [], null);
    public static OpenCodeSkillPathsResult Out(string reason) => new(null, reason);
}
