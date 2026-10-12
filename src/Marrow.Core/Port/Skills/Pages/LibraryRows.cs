// PORT-ONLY: first-release feature unit SK-15 (DL-17). Builds the rows of one page from the catalogue, the state and the switches.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.State;
using Marrow.Core.Port.Skills.Terms;

namespace Marrow.Core.Port.Skills.Pages;

public static class LibraryRows
{
    private const string Dot = " · ";

    /// <summary>
    /// The page's rows, in the catalogue's order (the design's order). Plugins lists the plugin items and Skills the skill sets: no other kind, and nothing of the other page
    /// (OD-0099 Sections C). <paramref name="record"/> is the item's effective record, <paramref name="progress"/> the page's own run progress.
    /// </summary>
    public static IReadOnlyList<LibraryRow> Build(LibrarySection section, SkillsCatalogue catalogue, AddonsState state, Func<string, ItemRecord> record,
        Func<string, double?> progress, ISwitchReader switches)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        return catalogue.Items.Where(i => i.Kind == section.Kind()).Select(i => Row(section, catalogue, state, record(i.Id), progress(i.Id), switches, i)).ToList();
    }

    private static LibraryRow Row(LibrarySection section, SkillsCatalogue catalogue, AddonsState state, ItemRecord record, double? progress, ISwitchReader switches, CatalogueItem item)
    {
        var onPc = record.State is ItemState.Installed or ItemState.UpdateAvailable or ItemState.Damaged or ItemState.Removing;
        var lines = Switches(item, switches, onPc);
        var total = item.Kind == ItemKind.Plugin ? lines.Sum(l => FolderSkills(item, l.Key)) : lines.Count;
        var on = item.Kind == ItemKind.Plugin ? lines.Where(l => l.On).Sum(l => FolderSkills(item, l.Key)) : lines.Count(l => l.On);
        var needs = item.Needs.Select(id => Need(catalogue, state, id)).ToList();
        var update = record.State == ItemState.UpdateAvailable ? record.InstalledPin + " → " + record.TargetPin : null;
        return new LibraryRow(
            item.Id, item.Title, item.Summary, Meta(item), record.State, progress, record.Failure, item.ApproxSizeMb,
            item.TermsClass == TermsClass.Open ? null : SkillsNotices.UnderTerms(item.Vendor), ItemTerms.Required(item, state),
            SourceFull(item), item.LicenceName, update, needs, lines, onPc ? on : 0, total, section == LibrarySection.Plugins);
    }

    private static int FolderSkills(CatalogueItem item, string key) =>
        item.Folders.FirstOrDefault(f => key == item.Id + "/" + f.Name)?.SkillCount ?? 0;

    private static IReadOnlyList<SwitchLine> Switches(CatalogueItem item, ISwitchReader switches, bool onPc)
    {
        // ED-NEW-dl-13: a plugin folder is the plugin's path in the repository (its switch key); the line shows its last part, and its licences where they are recorded per plugin.
        if (item.Kind == ItemKind.Plugin)
            return item.Folders.Select(f => new SwitchLine(SwitchKind.PluginFolder, item.Id, item.Id + "/" + f.Name, f.Name[(f.Name.LastIndexOf('/') + 1)..],
                string.Join(Dot, new[] { LibraryWords.FolderSkills(f.SkillCount) }.Concat(PluginLicences.NamesOf(item, f))),
                onPc && switches.PluginFolder(item.Id, f.Name), onPc)).ToList();
        return item.Skills.Where(s => s.Path is not null).Select(s => new SwitchLine(SwitchKind.Skill, item.Id, item.Id + "/" + s.Path, "/" + (s.Name ?? s.Folder), "",
            onPc && switches.Skill(item.Id, s.Path!), onPc)).ToList();
    }

    private static NeedLine Need(SkillsCatalogue catalogue, AddonsState state, string id) =>
        new(id, catalogue.Items.FirstOrDefault(i => i.Id == id)?.Title ?? id, state.FoundPath(id));

    private static string Meta(CatalogueItem item)
    {
        var parts = new List<string>();
        if (item.Kind == ItemKind.Plugin && item.Folders.Count > 0) parts.Add(item.Folders.Count + (item.Folders.Count == 1 ? " plugin" : " plugins"));
        if (item.SkillCount is { } skills) parts.Add(skills + (skills == 1 ? " skill" : " skills"));
        if (item.ApproxSizeMb is { } mb) parts.Add(SizeText.Short(mb));
        parts.Add(SourceShort(item));
        if (item.LicenceName is { } licence) parts.Add(licence);
        return string.Join(Dot, parts.Where(p => p.Length > 0));
    }

    /// <summary>"anthropics/skills": the repository without the host when it is on GitHub, otherwise host and path.</summary>
    public static string SourceShort(CatalogueItem item)
    {
        if (item.SourceUrl is not { } url || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "";
        var path = uri.AbsolutePath.Trim('/');
        return uri.Host == "github.com" ? path : uri.Host + (path.Length > 0 ? "/" + path : "");
    }

    /// <summary>"github.com/anthropics/skills at 8a1541c": the source and the first seven characters of its pin.</summary>
    public static string SourceFull(CatalogueItem item)
    {
        if (item.SourceUrl is not { } url || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "";
        var text = uri.Host + uri.AbsolutePath.TrimEnd('/');
        var pin = item.Pin ?? item.PinHint;
        return pin is null ? text : text + " at " + (pin.Length >= 40 ? pin[..7] : pin);
    }

    /// <summary>The host a download comes from, for the confirmation's "From" ("github.com").</summary>
    public static string? Host(CatalogueItem item) => item.SourceUrl is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null;
}
