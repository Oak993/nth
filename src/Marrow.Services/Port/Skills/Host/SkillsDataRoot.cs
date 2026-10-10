// PORT-ONLY: SK-13. Where the skills and tools data root is. There is no Swift twin.
using Marrow.Core.Support;

namespace Marrow.Services.Port.Skills.Host;

/// <summary>
/// <c>%LOCALAPPDATA%\Marrow Code</c> through <see cref="DevDataRoot"/>, the same root as <c>StorageLocations.CreateProduction</c> (OD-0008 C7, ED-212), but resolved on its own: the
/// uninstall hook runs before any binding of the app exists (Storage.Locations is a trap until Program.Main binds it), so it cannot ask for it.
/// </summary>
public static class SkillsDataRoot
{
    public const string FolderName = AppInfo.DataFolderName; // OD-0135: "nth"

    public static string Resolve() => Path.Combine(DevDataRoot.LocalApplicationData(), FolderName);
}
