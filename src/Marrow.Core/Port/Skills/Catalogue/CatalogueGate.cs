// PORT-ONLY: first-release feature unit SK-1. The gate of ONDEMAND-PLAN.md section 7 DL-1 (catalogue schema, embedded catalogue and its gate), in Core.
using System.Globalization;
using System.Text.RegularExpressions;

namespace Marrow.Core.Port.Skills.Catalogue;

/// <summary>The rule ids a <see cref="CatalogueViolation"/> carries.</summary>
public static class CatalogueRules
{
    public const string Schema = "schema";
    public const string Id = "id";
    public const string Kind = "kind";
    public const string Text = "text";
    public const string NeedsExist = "needs-exist";
    public const string NeedsCycle = "needs-cycle";
    public const string NeedsKind = "needs-kind";
    public const string Provides = "provides";
    public const string Host = "host";
    public const string SkillName = "skill-name";
    public const string Count = "count";
    public const string PinFormat = "pin-format";
    public const string PinState = "pin-state";
    public const string ValueFormat = "value-format";
    public const string Licence = "licence";
    public const string Unpinned = "unpinned";
}

public sealed record CatalogueViolation(string Rule, string? ItemId, string Message);

/// <summary>An item that still lacks real values, with the fields it lacks.</summary>
public sealed record UnpinnedItem(string ItemId, IReadOnlyList<string> Missing);

public sealed record CatalogueGateReport(IReadOnlyList<CatalogueViolation> Violations, IReadOnlyList<UnpinnedItem> Unpinned);

public sealed record CatalogueGateOptions
{
    /// <summary>The release gate: an item that still lacks a real value is a violation (it is only reported otherwise).</summary>
    public bool RequireAllPinned { get; init; }
}

/// <summary>
/// DL-1: every item has id, kind, pin, URLs, sizes, SHA-256s, licence name, URL and file, terms class, needs, provides; ids are unique; needs form no
/// cycle and name existing ids; every provides name maps to exactly one tool or the smallest package set; skill folder names follow the Agent Skills
/// name rule; hosts are on the allow-list (BUILD-PLAN.md 2.5 SK-1 adds the licence file). Counts live in <see cref="SkillsCatalogueCounts"/> and the
/// summaries' twelve-word check lives in the tests (ED-NEW-f-sk-1).
/// A value the plan does not give is an explicit <c>null</c>: it is no violation, the item is reported as lacking it and must declare itself
/// <see cref="PinState.Unpinned"/>; <see cref="CatalogueGateOptions.RequireAllPinned"/> is what turns that into a failure for a release.
/// </summary>
public static partial class CatalogueGate
{
    [GeneratedRegex("^[0-9a-f]{40}$")] private static partial Regex FullCommit();

    [GeneratedRegex("^[0-9a-f]{7,40}$")] private static partial Regex CommitHint();

    [GeneratedRegex("^[0-9a-f]{64}$")] private static partial Regex Sha256Hex();

    [GeneratedRegex("^[0-9a-f]{8,64}$")] private static partial Regex Sha256Prefix();

    public static CatalogueGateReport Check(SkillsCatalogue catalogue, CatalogueGateOptions? options = null)
    {
        var violations = new List<CatalogueViolation>();
        void Fail(string rule, string? id, string message) => violations.Add(new CatalogueViolation(rule, id, message));

        if (catalogue.Schema != SkillsCatalogueCounts.SchemaVersion) Fail(CatalogueRules.Schema, null, $"schema {catalogue.Schema} is not {SkillsCatalogueCounts.SchemaVersion}");
        if (string.IsNullOrWhiteSpace(catalogue.ReadOn)) Fail(CatalogueRules.Schema, null, "readOn is empty");
        if (catalogue.Items.Count == 0) Fail(CatalogueRules.Schema, null, "the catalogue has no items");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in catalogue.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || item.Id.Any(char.IsWhiteSpace)) Fail(CatalogueRules.Id, item.Id, $"'{item.Id}' is not a usable id");
            else if (!seen.Add(item.Id)) Fail(CatalogueRules.Id, item.Id, "the id is used twice");
        }

        foreach (var item in catalogue.Items)
        {
            void Item(string rule, string message) => Fail(rule, item.Id, message);
            CheckText(item, Item);
            CheckKind(item, Item);
            CheckPins(item, Item);
            CheckValues(item, Item);
            CheckLicence(item, Item);
            CheckUrls(item, Item);
            CheckSkills(item, Item);
            CheckCounts(item, Item);
            CheckProvides(item, Item);
        }

        CheckNeeds(catalogue, violations);
        foreach (var problem in ProvidesIndex.Build(catalogue).Problems) Fail(CatalogueRules.Provides, null, problem);

        var unpinned = new List<UnpinnedItem>();
        foreach (var item in catalogue.Items)
        {
            var missing = Missing(item);
            var derived = missing.Count == 0 ? PinState.Pinned : PinState.Unpinned;
            if (item.PinState != derived)
                Fail(CatalogueRules.PinState, item.Id, $"declared {item.PinState} but its values say {derived}" + (missing.Count == 0 ? "" : ": lacks " + string.Join(", ", missing)));
            if (missing.Count == 0) continue;
            unpinned.Add(new UnpinnedItem(item.Id, missing));
            if (options?.RequireAllPinned == true) Fail(CatalogueRules.Unpinned, item.Id, "lacks real values: " + string.Join(", ", missing));
        }
        return new CatalogueGateReport(violations, unpinned);
    }

    /// <summary>
    /// The fields an item still lacks before it can be called pinned: a pin (or a pin for every listed skill), the source, the licence name, file and URL,
    /// and by kind: every artifact's URL, size and SHA-256 (tools and package sets), every package's version (package sets), the file list, the skill count
    /// and each listed skill's frontmatter name and path (skill sets and plugins).
    /// </summary>
    public static IReadOnlyList<string> Missing(CatalogueItem item)
    {
        var missing = new List<string>();
        var perSkillPins = item.SkillsListed && item.Skills.Count > 0 && item.Skills.All(s => !string.IsNullOrEmpty(s.Pin));
        if (string.IsNullOrEmpty(item.Pin) && !perSkillPins) missing.Add("pin");
        if (item.SourceUrl is null) missing.Add("sourceUrl");
        if (item.LicenceName is null) missing.Add("licenceName");
        if (item.LicenceFile is null) missing.Add("licenceFile");
        if (item.LicenceUrl is null) missing.Add("licenceUrl");
        switch (item.Kind)
        {
            case ItemKind.Tool:
            case ItemKind.Pyset:
            case ItemKind.Nodeset:
                if (item.Artifacts.Count == 0) missing.Add("artifacts");
                for (var i = 0; i < item.Artifacts.Count; i++)
                {
                    var artifact = item.Artifacts[i];
                    if (artifact.Url is null) missing.Add($"artifacts[{i}].url");
                    if (artifact.Size is null) missing.Add($"artifacts[{i}].size");
                    if (artifact.Sha256 is null) missing.Add($"artifacts[{i}].sha256");
                }
                foreach (var package in item.Packages)
                    if (package.Version is null) missing.Add($"packages[{package.Name}].version");
                break;
            case ItemKind.Skillset:
            case ItemKind.Plugin:
                if (item.Files.Count == 0) missing.Add("files");
                if (item.SkillCount is null) missing.Add("skillCount");
                // REVIEW fix (unit/integ-sk): DL-1's name rule (every skill folder name equals its frontmatter name) can be checked only for
                // skills the item lists, so an item is pinned only when its skills are listed.
                if (!item.SkillsListed) missing.Add("skills");
                if (item.SkillsListed)
                    foreach (var skill in item.Skills)
                    {
                        if (skill.Name is null) missing.Add($"skills[{skill.Folder}].name");
                        if (skill.Path is null) missing.Add($"skills[{skill.Folder}].path");
                    }
                break;
        }
        return missing;
    }

    private static bool IsRelativePath(string path) =>
        path.Length > 0 && path[0] != '/' && !path.Contains('\\', StringComparison.Ordinal) && path.Split('/').All(s => s.Length > 0 && s != "." && s != "..");

    private static void CheckText(CatalogueItem item, Action<string, string> fail)
    {
        if (string.IsNullOrWhiteSpace(item.Title)) fail(CatalogueRules.Text, "title is empty");
        if (string.IsNullOrWhiteSpace(item.Vendor)) fail(CatalogueRules.Text, "vendor is empty");
        if (string.IsNullOrWhiteSpace(item.Summary)) fail(CatalogueRules.Text, "summary is empty");
        else if (item.Summary.Any(c => c is '\n' or '\r')) fail(CatalogueRules.Text, "the summary is not one line");
        foreach (var package in item.Packages)
            if (string.IsNullOrWhiteSpace(package.Name)) fail(CatalogueRules.Text, "a package has no name");
    }

    private static void CheckKind(CatalogueItem item, Action<string, string> fail)
    {
        var hasSkillFields = item.SkillCount is not null || item.Skills.Count > 0 || item.Folders.Count > 0 || item.Files.Count > 0 || item.SkillsListed;
        switch (item.Kind)
        {
            case ItemKind.Tool:
                if (item.Artifacts.Count == 0) fail(CatalogueRules.Kind, "a tool needs at least one artifact");
                if (item.Packages.Count > 0) fail(CatalogueRules.Kind, "a tool carries no packages");
                if (hasSkillFields) fail(CatalogueRules.Kind, "a tool carries no skill fields");
                break;
            case ItemKind.Pyset:
            case ItemKind.Nodeset:
                if (item.Packages.Count == 0) fail(CatalogueRules.Kind, "a package set lists its packages");
                if (hasSkillFields) fail(CatalogueRules.Kind, "a package set carries no skill fields");
                break;
            case ItemKind.Skillset:
            case ItemKind.Plugin:
                if (item.Artifacts.Count > 0) fail(CatalogueRules.Kind, "a skill set or plugin carries no artifacts (its files are listed in files)");
                if (item.Packages.Count > 0) fail(CatalogueRules.Kind, "a skill set or plugin carries no packages");
                if (item.Kind == ItemKind.Skillset && item.Folders.Count > 0) fail(CatalogueRules.Kind, "only a plugin lists plugin folders");
                if (item.CountState == CountState.Plan && item.SkillCount is null) fail(CatalogueRules.Kind, "a counted set states its skill count");
                break;
        }
    }

    private static void CheckPins(CatalogueItem item, Action<string, string> fail)
    {
        var repository = item.Kind is ItemKind.Skillset or ItemKind.Plugin;
        if (item.Pin is not null)
        {
            var ok = repository ? FullCommit().IsMatch(item.Pin) : item.Pin.Length > 0 && !item.Pin.Any(char.IsWhiteSpace);
            if (!ok) fail(CatalogueRules.PinFormat, repository ? "a repository pin is a full lowercase 40-hex commit" : "a version pin is a non-empty word");
        }
        if (item.PinHint is not null)
        {
            if (!CommitHint().IsMatch(item.PinHint)) fail(CatalogueRules.PinFormat, "pinHint is 7 to 40 lowercase hex digits");
            else if (item.Pin is not null && FullCommit().IsMatch(item.Pin) && !item.Pin.StartsWith(item.PinHint, StringComparison.Ordinal))
                fail(CatalogueRules.PinFormat, "pinHint is not the start of pin");
        }
        foreach (var skill in item.Skills)
            if (skill.Pin is not null && !FullCommit().IsMatch(skill.Pin)) fail(CatalogueRules.PinFormat, $"the pin of skill {skill.Folder} is not a full commit");
    }

    private static void CheckValues(CatalogueItem item, Action<string, string> fail)
    {
        if (item.ApproxSizeMb is { } mb && (double.IsNaN(mb) || double.IsInfinity(mb) || mb < 0)) fail(CatalogueRules.ValueFormat, "approxSizeMb is not a size");
        foreach (var artifact in item.Artifacts)
        {
            if (string.IsNullOrWhiteSpace(artifact.Name)) fail(CatalogueRules.ValueFormat, "an artifact has no name");
            if (artifact.Size is <= 0) fail(CatalogueRules.ValueFormat, $"artifact {artifact.Name}: the size must be positive");
            if (artifact.Sha256 is not null && !Sha256Hex().IsMatch(artifact.Sha256)) fail(CatalogueRules.ValueFormat, $"artifact {artifact.Name}: sha256 is not 64 lowercase hex digits");
            if (artifact.Sha256Hint is not null)
            {
                if (!Sha256Prefix().IsMatch(artifact.Sha256Hint)) fail(CatalogueRules.ValueFormat, $"artifact {artifact.Name}: sha256Hint is not 8 to 64 lowercase hex digits");
                else if (artifact.Sha256 is not null && !artifact.Sha256.StartsWith(artifact.Sha256Hint, StringComparison.Ordinal))
                    fail(CatalogueRules.ValueFormat, $"artifact {artifact.Name}: sha256 does not start with the hint the plan prints");
            }
        }
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in item.Files)
        {
            if (!IsRelativePath(file.Path)) fail(CatalogueRules.ValueFormat, $"file path '{file.Path}' is not a plain relative path");
            else if (!paths.Add(file.Path)) fail(CatalogueRules.ValueFormat, $"file path '{file.Path}' is listed twice");
            if (file.Size < 0) fail(CatalogueRules.ValueFormat, $"file {file.Path}: negative size");
            if (!Sha256Hex().IsMatch(file.Sha256)) fail(CatalogueRules.ValueFormat, $"file {file.Path}: sha256 is not 64 lowercase hex digits");
        }
    }

    private static void CheckLicence(CatalogueItem item, Action<string, string> fail)
    {
        if (item.LicenceName is not null && string.IsNullOrWhiteSpace(item.LicenceName)) fail(CatalogueRules.Licence, "licenceName is blank");
        if (item.LicenceFile is not null && !IsRelativePath(item.LicenceFile)) fail(CatalogueRules.Licence, "licenceFile is not a plain relative path");
        if (item.LicenceUrl is null || item.LicenceFile is null || item.Pin is null || item.SourceUrl is null) return;
        if (!FullCommit().IsMatch(item.Pin) || !Uri.TryCreate(item.SourceUrl, UriKind.Absolute, out var source) || source.Host != "github.com") return;
        var parts = source.AbsolutePath.Trim('/').Split('/');
        if (parts.Length != 2) return;
        var expected = $"https://raw.githubusercontent.com/{parts[0]}/{parts[1]}/{item.Pin}/{item.LicenceFile}";
        if (item.LicenceUrl != expected) fail(CatalogueRules.Licence, $"licenceUrl is not the licence file at the pin: expected {expected}");
    }

    private static void CheckUrls(CatalogueItem item, Action<string, string> fail)
    {
        void Url(string field, string? url)
        {
            if (url is not null && !CatalogueHosts.IsAllowed(url)) fail(CatalogueRules.Host, $"{field} '{url}' is not an https URL on the host list");
        }
        Url("sourceUrl", item.SourceUrl);
        Url("licenceUrl", item.LicenceUrl);
        foreach (var artifact in item.Artifacts) Url("artifact " + artifact.Name, artifact.Url);
    }

    private static void CheckSkills(CatalogueItem item, Action<string, string> fail)
    {
        var folders = new HashSet<string>(StringComparer.Ordinal);
        foreach (var skill in item.Skills)
        {
            if (!SkillName.IsValid(skill.Folder)) fail(CatalogueRules.SkillName, $"skill folder '{skill.Folder}' does not follow the name rule");
            else if (!folders.Add(skill.Folder)) fail(CatalogueRules.SkillName, $"skill folder '{skill.Folder}' is listed twice");
            if (skill.Name is not null && (!SkillName.IsValid(skill.Name) || skill.Name != skill.Folder))
                fail(CatalogueRules.SkillName, $"skill '{skill.Folder}': the frontmatter name '{skill.Name}' is not the folder name in the name rule's form");
            if (skill.Path is not null && (!IsRelativePath(skill.Path) || skill.Path.Split('/')[^1] != skill.Folder))
                fail(CatalogueRules.SkillName, $"skill '{skill.Folder}': the path '{skill.Path}' does not end in the folder name");
        }
    }

    private static void CheckCounts(CatalogueItem item, Action<string, string> fail)
    {
        if (item.SkillCount is < 0) fail(CatalogueRules.Count, "the skill count is negative");
        if (item.Folders.Count > 0)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var folder in item.Folders)
            {
                if (folder.SkillCount < 0) fail(CatalogueRules.Count, $"folder {folder.Name}: negative count");
                if (!names.Add(folder.Name)) fail(CatalogueRules.Count, $"folder {folder.Name} is listed twice");
            }
            var sum = item.Folders.Sum(f => (long)f.SkillCount);
            if (item.SkillCount is null) fail(CatalogueRules.Count, "plugin folders are listed but the skill count is not");
            else if (sum != item.SkillCount) fail(CatalogueRules.Count, $"the folders add up to {sum.ToString(CultureInfo.InvariantCulture)}, the skill count says {item.SkillCount.Value.ToString(CultureInfo.InvariantCulture)}");
        }
        if (item.SkillsListed)
        {
            if (item.SkillCount is null || item.Skills.Count != item.SkillCount) fail(CatalogueRules.Count, $"{item.Skills.Count.ToString(CultureInfo.InvariantCulture)} skills are listed as complete but the skill count is {item.SkillCount?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}");
        }
        else if (item.Skills.Count > 0) fail(CatalogueRules.Count, "skills are listed but the list is not marked complete");
    }

    private static void CheckProvides(CatalogueItem item, Action<string, string> fail)
    {
        var names = new HashSet<(ProvideKind, string)>();
        foreach (var provide in item.Provides)
        {
            var allowed = provide.Kind switch
            {
                ProvideKind.Exe or ProvideKind.Message => item.Kind == ItemKind.Tool,
                ProvideKind.PyModule => item.Kind == ItemKind.Pyset,
                ProvideKind.NodeModule => item.Kind == ItemKind.Nodeset,
            };
            if (!allowed) fail(CatalogueRules.Provides, $"a {item.Kind} cannot provide {provide.Kind} '{provide.Name}'");
            if (string.IsNullOrWhiteSpace(provide.Name)) fail(CatalogueRules.Provides, "a provided name is blank");
            else if (!names.Add((provide.Kind, provide.Kind == ProvideKind.Exe ? provide.Name.ToLowerInvariant() : provide.Name)))
                fail(CatalogueRules.Provides, $"{provide.Kind} '{provide.Name}' is listed twice");
        }
    }

    private static void CheckNeeds(SkillsCatalogue catalogue, List<CatalogueViolation> violations)
    {
        var byId = new Dictionary<string, CatalogueItem>(StringComparer.Ordinal);
        foreach (var item in catalogue.Items) byId.TryAdd(item.Id, item);

        foreach (var item in catalogue.Items)
        {
            foreach (var need in item.Needs)
            {
                if (!byId.TryGetValue(need, out var target))
                {
                    violations.Add(new CatalogueViolation(CatalogueRules.NeedsExist, item.Id, $"needs '{need}', which is not an item"));
                    continue;
                }
                var runnable = item.Kind is ItemKind.Tool or ItemKind.Pyset or ItemKind.Nodeset;
                if (runnable && target.Kind is ItemKind.Skillset or ItemKind.Plugin)
                    violations.Add(new CatalogueViolation(CatalogueRules.NeedsKind, item.Id, $"a {item.Kind} cannot need the {target.Kind} {need}"));
            }
            foreach (var skill in item.Skills)
                foreach (var need in skill.Needs)
                {
                    if (!byId.ContainsKey(need)) violations.Add(new CatalogueViolation(CatalogueRules.NeedsExist, item.Id, $"skill {skill.Folder} needs '{need}', which is not an item"));
                    else if (!item.Needs.Contains(need, StringComparer.Ordinal))
                        violations.Add(new CatalogueViolation(CatalogueRules.NeedsExist, item.Id, $"skill {skill.Folder} needs {need}, which the item does not list in needs"));
                }
        }

        // Depth-first search with three colours; each cycle is reported once, at the item the search entered it from.
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var path = new List<string>();
        void Visit(string id)
        {
            state[id] = 1;
            path.Add(id);
            foreach (var need in byId[id].Needs)
            {
                if (!byId.ContainsKey(need)) continue;
                state.TryGetValue(need, out var s);
                if (s == 1)
                {
                    var cycle = path.Skip(path.IndexOf(need)).Append(need);
                    violations.Add(new CatalogueViolation(CatalogueRules.NeedsCycle, id, "needs form a cycle: " + string.Join(" -> ", cycle)));
                }
                else if (s == 0) Visit(need);
            }
            path.RemoveAt(path.Count - 1);
            state[id] = 2;
        }
        foreach (var id in byId.Keys.OrderBy(k => k, StringComparer.Ordinal))
            if (!state.ContainsKey(id)) Visit(id);
    }
}
