// PORT-ONLY: SK-4 / DL-4, OD-0099 amendments; skills-ONDEMAND-PLAN 3.3-3.4 and skills-PLAN 2.1.

namespace Marrow.Services.Port.Skills.Install;

/// <summary>SK-9 supplies the content key, provider filtering and layout. Prefix and metadata support Claude/Grok plugin views.</summary>
public sealed record SpareProjection(string Provider, string Key, IReadOnlyList<string> Files)
{
    public string? Prefix { get; init; }
    public IReadOnlyDictionary<string, string> GeneratedFiles { get; init; } = new Dictionary<string, string>();
}
