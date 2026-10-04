// PORT-ONLY: SK-9 / DL-9; OD-0099 progressive disclosure and skills-PLAN.md 2.1-2.4, 2.7.

namespace Marrow.Services.Port.Skills.Generations;

public sealed record PublishedSkill(string Id, string Name, string Description, string File, bool ExplicitOnly)
{
    public string Folder => Path.GetDirectoryName(File)!;
    public string Listing => Name + ": " + Description;
}
