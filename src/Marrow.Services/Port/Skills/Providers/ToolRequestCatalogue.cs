// PORT-ONLY: SK-11f / DL-14 (skills-ONDEMAND-PLAN.md 5.3 and SK-10's ModelRequest). "Refuses names outside the catalogue" is the detector's own lookup, so a request_tool
// that is accepted here is one the detector resolves too.
using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Services.Port.Skills.Providers;

public static class ToolRequestCatalogue
{
    /// <summary>True when <paramref name="name"/> is a catalogue item id, or a program a catalogue item provides (program names compare without regard to case).</summary>
    public static bool Contains(SkillsCatalogue catalogue, string name)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        if (string.IsNullOrWhiteSpace(name)) return false;
        return catalogue.Items.Any(i => i.Id == name) || ProvidesIndex.Build(catalogue).Resolve(ProvideKind.Exe, name) is not null;
    }
}
