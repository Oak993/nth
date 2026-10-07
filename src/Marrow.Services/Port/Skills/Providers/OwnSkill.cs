// PORT-ONLY: SK-11f / WU-12. One of the user's own skills as the catalogue block and use_skill see it.
namespace Marrow.Services.Port.Skills.Providers;

public sealed record OwnSkill(string Name, string Description, string Folder);
