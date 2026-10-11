// PORT-ONLY: ED-NEW-dl-13 (the lead's 2026-10-11 decision: a plugin set's licences may be recorded per plugin, nth shows each plugin's own, and the set is offered when every plugin
// in it has one). There is no Swift twin.
using System.Security.Cryptography;
using System.Text;

namespace Marrow.Core.Port.Skills.Catalogue;

/// <summary>
/// The licences of a plugin item recorded per plugin: every plugin folder names its licence and the file that states it, and a skill with a licence file of its own names that.
/// Such an item has no single licence file; its terms are all of these files together (<see cref="Hash"/> keys their acceptance, so a change to any of them asks again).
/// </summary>
public static class PluginLicences
{
    /// <summary>True for a plugin item without an item licence file whose every plugin folder names its licence and licence file.</summary>
    public static bool PerPlugin(CatalogueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Kind == ItemKind.Plugin && item.LicenceFile is null && item.Folders.Count > 0 && item.Folders.All(f => f.LicenceName is not null && f.LicenceFile is not null);
    }

    /// <summary>The plugin folder a path of the item lies in (the longest one that holds it), or null.</summary>
    public static CatalogueFolder? FolderOf(CatalogueItem item, string path)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(path);
        return item.Folders.Where(f => path.StartsWith(f.Name + "/", StringComparison.Ordinal)).MaxBy(f => f.Name.Length);
    }

    /// <summary>The licence a skill is under: its own, or its plugin folder's.</summary>
    public static string? NameFor(CatalogueItem item, CatalogueSkill skill)
    {
        ArgumentNullException.ThrowIfNull(skill);
        return skill.LicenceName ?? (skill.Path is { } path ? FolderOf(item, path)?.LicenceName : null);
    }

    /// <summary>The licence names a plugin folder's line shows: its own, then the skills' own ones, most used first.</summary>
    public static IReadOnlyList<string> NamesOf(CatalogueItem item, CatalogueFolder folder)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(folder);
        var names = new List<string>();
        if (folder.LicenceName is { } own) names.Add(own);
        var skills = item.Skills.Where(s => s.LicenceName is not null && s.Path is { } p && FolderOf(item, p) == folder).GroupBy(s => s.LicenceName!, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key);
        foreach (var name in skills) if (!names.Contains(name, StringComparer.Ordinal)) names.Add(name);
        return names;
    }

    /// <summary>Every licence file the item's plugins and skills name, once each, in ordinal path order.</summary>
    public static IReadOnlyList<string> Files(CatalogueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Folders.Select(f => f.LicenceFile).Concat(item.Skills.Select(s => s.LicenceFile)).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// The key of the item's terms: SHA-256 over each licence file's path and pinned SHA-256 ("path NUL sha256 LF", in <see cref="Files"/> order). Null when the item does not
    /// record its licences per plugin or a licence file is not pinned in its files.
    /// </summary>
    public static string? Hash(CatalogueItem item)
    {
        if (!PerPlugin(item)) return null;
        var text = new StringBuilder();
        foreach (var path in Files(item))
        {
            var pinned = item.Files.FirstOrDefault(f => f.Path == path);
            if (pinned is null) return null;
            text.Append(path).Append('\0').Append(pinned.Sha256).Append('\n');
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
