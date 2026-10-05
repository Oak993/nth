// PORT-ONLY: SK-15 Core test double. The switch truth of the page: a set of the keys that are on, "itemId/path" for a skill and "itemId/folder" for a plugin folder.
using Marrow.Core.Port.Skills.Pages;

namespace Marrow.Core.Tests.Port.Skills.Pages;

internal sealed class Switches(params string[] on) : ISwitchReader
{
    public bool Skill(string itemId, string path) => on.Contains(itemId + "/" + path);

    public bool PluginFolder(string itemId, string folder) => on.Contains(itemId + "/" + folder);
}
