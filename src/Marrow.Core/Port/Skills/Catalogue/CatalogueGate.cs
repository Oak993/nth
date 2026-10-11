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
    public const string Install = "install";
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
/// summaries' twelve-word check lives in the tests (ED-294).
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
            CheckInstall(item, Item);
        }
        CheckShimLinks(catalogue, violations);

        CheckNeeds(catalogue, violations);
        foreach (var problem in ProvidesIndex.Build(catalogue).Problems) Fail(CatalogueRules.Provides, null, problem);

        var unpinned = new List<UnpinnedItem>();
        foreach (var item in catalogue.Items)
        {
            var missing = Missing(item);
            var derived = missing.Count == 0 ? PinState.Pinned : PinState.Unpinned;
            if (item.PinState != derived)
                Fail(CatalogueRules.PinState, item.Id, $"declared {item.PinState} but its values say {derived}" + (missing.Count == 0 ? "" : ": lacks " + string.Join(", ", missing)));
            if (missing.Count == 0)
            {
                if (options?.RequireAllPinned == true && InstallMissing(item) is { } why) Fail(CatalogueRules.Install, item.Id, why);
                continue;
            }
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

    /// <summary>
    /// ED-NEW-dl-7: what a fully pinned tool or package set still lacks before it can be installed: its installer parameters and its licence file pinned in <c>files</c> (the installer
    /// checks the licence bytes against that pin). Null when nothing is lacking or the item is a skill set or plugin. Only the release gate treats it as a failure.
    /// </summary>
    public static string? InstallMissing(CatalogueItem item)
    {
        if (item.Kind is ItemKind.Skillset or ItemKind.Plugin) return null;
        if (item.Install is null) return "has no installer parameters";
        if (item.LicenceFile is null || !item.Files.Any(f => f.Path == item.LicenceFile)) return "its licence file is not pinned in files";
        return null;
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
        var hasSkillFields = item.SkillCount is not null || item.Skills.Count > 0 || item.Folders.Count > 0 || item.SkillsListed;
        // ED-NEW-dl-7: a tool or package set may list one file, its licence file, so the installer can check the licence bytes it lays down against a pin.
        if (item.Kind is ItemKind.Tool or ItemKind.Pyset or ItemKind.Nodeset && item.Files.Count > 0 && (item.Files.Count != 1 || item.Files[0].Path != item.LicenceFile))
            fail(CatalogueRules.Kind, "a tool or package set lists only its licence file in files");
        if (item.Kind is ItemKind.Skillset or ItemKind.Plugin && item.Install is not null) fail(CatalogueRules.Kind, "a skill set or plugin carries no installer parameters");
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

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")] private static partial Regex EnvironmentName();

    [GeneratedRegex("^[0-9]+\\.[0-9]+$")] private static partial Regex PythonVersion();

    private static bool IsFileName(string name) => name.Length > 0 && IsRelativePath(name) && !name.Contains('/', StringComparison.Ordinal);

    /// <summary>ED-NEW-dl-7: the installer parameters agree with the item's kind, artifacts, needs and provides (ONDEMAND-PLAN.md 3.4 and 3.6).</summary>
    private static void CheckInstall(CatalogueItem item, Action<string, string> fail)
    {
        if (item.Install is not { } install) return;
        void Fail(string message) => fail(CatalogueRules.Install, message);
        InstallKind[] allowed = item.Kind switch
        {
            ItemKind.Tool => [InstallKind.Zip, InstallKind.PythonEmbed, InstallKind.MsiAdmin, InstallKind.Sfx7z],
            ItemKind.Pyset => [InstallKind.Pyset],
            ItemKind.Nodeset => [InstallKind.Nodeset],
            _ => [],
        };
        if (!allowed.Contains(install.Kind)) { Fail($"a {item.Kind} is not installed as {install.Kind}"); return; }
        var artifacts = item.Artifacts.Select(a => a.Name).ToHashSet(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        void Use(string name, string what)
        {
            if (!artifacts.Contains(name)) Fail($"{what} '{name}' is not an artifact of the item");
            else if (!used.Add(name)) Fail($"artifact '{name}' is installed twice");
        }
        foreach (var archive in install.Archives)
        {
            Use(archive.Artifact, "the archive");
            if (archive.Prefix is not null && !IsRelativePath(archive.Prefix)) Fail($"the folder of archive '{archive.Artifact}' is not a plain relative path");
        }
        if (install.Python is not null && install.Kind != InstallKind.PythonEmbed) Fail("only a Python embed install names a pip wheel");
        if (install.NodeTree.Count > 0 && install.Kind != InstallKind.Nodeset) Fail("only a Node package set lists a node tree");
        if (install.PythonPackages != (install.Kind == InstallKind.Pyset)) Fail("exactly the Python package sets put their folder on the Python path");
        switch (install.Kind)
        {
            case InstallKind.Zip:
                if (install.Archives.Count == 0) Fail("a zip install lists its archives");
                break;
            case InstallKind.PythonEmbed:
                if (install.Archives.Count != 1 || install.Archives[0].Prefix is not null) Fail("a Python embed install unpacks exactly one archive into its folder");
                if (install.Python is not { } python) Fail("a Python embed install names its pip wheel and its ._pth layout");
                else
                {
                    Use(python.PipArtifact, "the pip wheel");
                    if (!python.PipArtifact.EndsWith(".whl", StringComparison.Ordinal)) Fail("the pip artifact is not a wheel");
                    if (!IsFileName(python.PthFile) || !python.PthFile.EndsWith("._pth", StringComparison.Ordinal)) Fail("the ._pth file is not a file name ending in ._pth");
                    if (!IsFileName(python.StandardLibraryZip) || !python.StandardLibraryZip.EndsWith(".zip", StringComparison.Ordinal)) Fail("the standard library is not a zip file name");
                    if (!PythonVersion().IsMatch(python.AgentVersion)) Fail("the agent folder version is not major.minor");
                }
                break;
            case InstallKind.Pyset:
                if (install.Archives.Count > 0) Fail("a Python package set installs its wheels, not archives");
                foreach (var artifact in item.Artifacts)
                {
                    if (!artifact.Name.EndsWith(".whl", StringComparison.Ordinal) || artifact.Name.Split('-').Length is < 5 or > 6) Fail($"artifact '{artifact.Name}' is not a wheel file name");
                    used.Add(artifact.Name);
                }
                break;
            case InstallKind.Nodeset:
                if (install.Archives.Count > 0) Fail("a Node package set installs its tarballs by the node tree, not archives");
                var trees = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var package in install.NodeTree)
                {
                    Use(package.Artifact, "the node package");
                    if (!IsRelativePath(package.Path) || !package.Path.StartsWith("node_modules/", StringComparison.Ordinal)) Fail($"node package '{package.Artifact}' is not placed under node_modules");
                    else if (!trees.Add(package.Path)) Fail($"node path '{package.Path}' is listed twice");
                    if (!package.Integrity.StartsWith("sha512-", StringComparison.Ordinal) || package.Integrity.Length != 95) Fail($"node package '{package.Artifact}' has no sha512 integrity");
                }
                break;
            case InstallKind.MsiAdmin:
            case InstallKind.Sfx7z:
                if (install.Archives.Count > 0 || item.Artifacts.Count != 1) Fail($"a {install.Kind} install runs its one artifact");
                else used.Add(item.Artifacts[0].Name);
                break;
        }
        foreach (var name in artifacts.Except(used, StringComparer.Ordinal).Order(StringComparer.Ordinal)) Fail($"artifact '{name}' is downloaded but never installed");
        var markers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var marker in install.Markers)
            if (!IsRelativePath(marker) || !markers.Add(marker)) Fail($"marker '{marker}' is not a plain relative path listed once");
        var shims = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var shim in install.Shims)
        {
            var extension = Path.GetExtension(shim.Name);
            if (!IsFileName(shim.Name) || shim.Name.IndexOfAny(['"', '%', '\\', ':', ' ']) >= 0 || !(extension == ".exe" || extension == ".cmd")) Fail($"shim '{shim.Name}' is not an .exe or .cmd file name");
            else if (!shims.Add(shim.Name)) Fail($"shim '{shim.Name}' is listed twice");
            if ((shim.Target is null) == (shim.Item is null)) Fail($"shim '{shim.Name}' names either a target of its own or a shim of a needed item");
            if (shim.Target is not null && !IsRelativePath(shim.Target)) Fail($"shim '{shim.Name}': the target is not a plain relative path");
            if (shim.Item is not null && (shim.Shim is null || !item.Needs.Contains(shim.Item, StringComparer.Ordinal))) Fail($"shim '{shim.Name}': the linked item is not a need or names no shim");
            if (shim.Item is null && shim.Shim is not null) Fail($"shim '{shim.Name}': a shim name without an item");
            if (shim.Item is not null && (shim.Python || shim.LibreOffice)) Fail($"shim '{shim.Name}': a linked shim takes its kind from the shim it links");
            if (shim.Python && install.Python is null) Fail($"shim '{shim.Name}': only the Python embed install has the ._pth a Python shim rewrites");
            if (shim.Python && shim.LibreOffice) Fail($"shim '{shim.Name}' is not both Python and LibreOffice");
            if (shim.Arguments.Any(a => a.Length == 0 || a.Any(char.IsControl))) Fail($"shim '{shim.Name}': an argument is empty or holds a control character");
        }
        var exes = item.Provides.Where(p => p.Kind == ProvideKind.Exe).Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var shimmed = install.Shims.Select(s => Path.GetFileNameWithoutExtension(s.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!exes.SetEquals(shimmed)) Fail("the shims are not exactly the executables the item provides");
        var exports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var export in install.Exports)
        {
            if (!EnvironmentName().IsMatch(export.Name) || export.Name.Equals("PATH", StringComparison.OrdinalIgnoreCase)) Fail($"'{export.Name}' is not an environment name an item may export");
            else if (!exports.Add(export.Name)) Fail($"'{export.Name}' is exported twice");
            if (export.Value.Length == 0 || export.Value.Any(char.IsControl)) Fail($"the value of '{export.Name}' is empty or holds a control character");
        }
    }

    /// <summary>ED-NEW-dl-7: a linked shim (pip runs Python's interpreter) names a shim the needed item publishes with a target of its own; an exported name belongs to one item.</summary>
    private static void CheckShimLinks(SkillsCatalogue catalogue, List<CatalogueViolation> violations)
    {
        var byId = new Dictionary<string, CatalogueItem>(StringComparer.Ordinal);
        foreach (var item in catalogue.Items) byId.TryAdd(item.Id, item);
        foreach (var item in catalogue.Items)
            foreach (var shim in item.Install?.Shims ?? [])
            {
                if (shim.Item is null || shim.Shim is null) continue;
                if (!byId.TryGetValue(shim.Item, out var target) || target.Install?.Shims.SingleOrDefault(s => s.Name.Equals(shim.Shim, StringComparison.OrdinalIgnoreCase)) is not { Target: not null })
                    violations.Add(new CatalogueViolation(CatalogueRules.Install, item.Id, $"shim '{shim.Name}' links '{shim.Item}' shim '{shim.Shim}', which that item does not publish with a target of its own"));
            }
        foreach (var name in catalogue.Items.SelectMany(i => (i.Install?.Exports ?? []).Select(e => (i.Id, e.Name))).GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            violations.Add(new CatalogueViolation(CatalogueRules.Install, null, $"'{name.Key}' is exported by {string.Join(" and ", name.Select(e => e.Id))}"));
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
