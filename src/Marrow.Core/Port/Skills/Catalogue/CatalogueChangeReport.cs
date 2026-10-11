// PORT-ONLY: first-release feature unit SK-2 (DL-2 of porting/first-release/skills-ONDEMAND-PLAN.md section 7); there is no Swift twin.
// The change report the catalogue builder writes for the owner (ONDEMAND-PLAN.md 3.7, 2.1, 2.5). Pure: two catalogues in, one report out.
using System.Globalization;
using System.Text;
using Marrow.Core.Port.Skills.State;

namespace Marrow.Core.Port.Skills.Catalogue;

/// <summary>What kind of change a line is, in the order a report lists them.</summary>
public enum ChangeCategory
{
    Item,
    Version,
    Source,
    Hash,
    Size,
    Licence,
    Terms,
    Needs,
    Provides,
    Skills,
    State,
    Text,

    /// <summary>ED-NEW-dl-7: the item's installer parameters (how a tool or package set is unpacked, and the shims, links and exports it publishes).</summary>
    Install,
}

/// <summary>What the owner must look at (ONDEMAND-PLAN.md 2.5, 3.7).</summary>
public enum ChangeFlag
{
    /// <summary>A non-open item's licence text changed: the acceptance keyed by the old hash no longer covers it, so the user is asked again.</summary>
    AsksTermsAgain,

    /// <summary>An item that was open is not any more: the terms step now applies to it.</summary>
    TermsNowRequired,

    /// <summary>A non-open item whose catalogue holds no hash of its licence file: its terms cannot be accepted (SK-7), so it cannot be downloaded yet.</summary>
    LicenceTextNotRecorded,
}

public sealed record CatalogueChange(string ItemId, ChangeCategory Category, string Field, string? Old, string? New, string Message);

public sealed record AttentionItem(string ItemId, ChangeFlag Flag, string Reason);

/// <summary>One item's changes, its flags, and its download size before and after (artifacts, else files; null when the catalogue does not know).</summary>
public sealed record ItemChangeSet(string ItemId, string Title, ItemKind Kind, IReadOnlyList<CatalogueChange> Changes, IReadOnlyList<ChangeFlag> Flags, long? DownloadBytesBefore, long? DownloadBytesAfter);

public sealed record ChangeReportRenderOptions
{
    /// <summary>Show at most this many changed files per item (the rest are counted); null shows every one, as DL-2 asks.</summary>
    public int? MaxFilesPerItem { get; init; }
}

/// <summary>
/// DL-2: "a fixture 'previous' and 'next' catalogue produce a change report listing every version, hash, size and licence-text change; a licence change in a non-open item
/// is flagged 'asks the terms again'". Every field of SK-1's schema is compared (a test lists them), so nothing a pin run changes goes unreported. What the plan leaves open
/// is decided in ED-298.
/// </summary>
public sealed record CatalogueChangeReport(
    string PreviousReadOn,
    string NextReadOn,
    int PreviousItems,
    int NextItems,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Unchanged,
    IReadOnlyList<ItemChangeSet> Items,
    IReadOnlyList<AttentionItem> Attention)
{
    private const int ShortLength = 12;

    public IReadOnlyList<string> AsksTermsAgain => Attention.Where(a => a.Flag == ChangeFlag.AsksTermsAgain).Select(a => a.ItemId).ToList();

    public IReadOnlyList<CatalogueChange> ChangesFor(string itemId) => Items.FirstOrDefault(i => i.ItemId == itemId)?.Changes ?? [];

    public static CatalogueChangeReport Compare(SkillsCatalogue previous, SkillsCatalogue next)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(next);
        var before = previous.Items.ToDictionary(i => i.Id, StringComparer.Ordinal);
        var after = next.Items.ToDictionary(i => i.Id, StringComparer.Ordinal);
        var added = new List<string>();
        var removed = new List<string>();
        var unchanged = new List<string>();
        var sets = new List<ItemChangeSet>();
        foreach (var id in before.Keys.Concat(after.Keys).Distinct(StringComparer.Ordinal).OrderBy(i => i, StringComparer.Ordinal))
        {
            before.TryGetValue(id, out var old);
            after.TryGetValue(id, out var now);
            if (old is null && now is not null)
            {
                added.Add(id);
                sets.Add(new ItemChangeSet(id, now.Title, now.Kind, [new CatalogueChange(id, ChangeCategory.Item, "item", null, Word(now.Kind), $"added ({Word(now.Kind)})")], Flags(null, now).Select(f => f.Flag).ToList(), null, DownloadBytes(now)));
            }
            else if (old is not null && now is null)
            {
                removed.Add(id);
                sets.Add(new ItemChangeSet(id, old.Title, old.Kind, [new CatalogueChange(id, ChangeCategory.Item, "item", Word(old.Kind), null, $"removed (was {Word(old.Kind)})")], [], DownloadBytes(old), null));
            }
            else
            {
                var changes = Diff(old!, now!).OrderBy(c => c.Category).ToList();
                if (changes.Count == 0) unchanged.Add(id);
                else sets.Add(new ItemChangeSet(id, now!.Title, now.Kind, changes, Flags(old, now).Select(f => f.Flag).ToList(), DownloadBytes(old!), DownloadBytes(now)));
            }
        }
        var attention = next.Items.OrderBy(i => i.Id, StringComparer.Ordinal).SelectMany(i => Flags(before.GetValueOrDefault(i.Id), i).Select(f => new AttentionItem(i.Id, f.Flag, f.Reason))).ToList();
        return new CatalogueChangeReport(previous.ReadOn, next.ReadOn, previous.Items.Count, next.Items.Count, added, removed, unchanged, sets, attention);
    }

    private static string Word<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();

    // A 40- or 64-digit hash or commit is shown as its first twelve digits; the data keeps the whole value.
    private static string Short(string value) =>
        value.Length >= 40 && value.All(Uri.IsHexDigit) ? value[..ShortLength] : value;

    private static string Show(string? value) =>
        value is null ? "(none)" : Short(value).Replace("\r\n", " ", StringComparison.Ordinal).Replace('\n', ' ').Replace('\r', ' ');

    private static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Size(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static long? DownloadBytes(CatalogueItem item)
    {
        if (item.Artifacts.Count > 0 && item.Artifacts.All(a => a.Size is not null)) return item.Artifacts.Sum(a => a.Size!.Value);
        return item.Files.Count > 0 ? item.Files.Sum(f => f.Size) : null;
    }

    private static IEnumerable<(ChangeFlag Flag, string Reason)> Flags(CatalogueItem? old, CatalogueItem now)
    {
        var nonOpen = now.TermsClass != TermsClass.Open;
        var after = ItemTerms.LicenceHash(now);
        var before = old is null ? null : ItemTerms.LicenceHash(old);
        if (nonOpen && before is not null && after is not null && before != after)
            yield return (ChangeFlag.AsksTermsAgain, $"the licence text changed: {Short(before)} -> {Short(after)}");
        if (old is not null && old.TermsClass == TermsClass.Open && nonOpen)
            yield return (ChangeFlag.TermsNowRequired, $"was open, now {Word(now.TermsClass)}");
        if (nonOpen && after is null)
            yield return (ChangeFlag.LicenceTextNotRecorded, "the catalogue holds no hash of its licence file, so its terms cannot be accepted yet");
    }

    // The order the changes are generated in is the order inside a category.
    private static IEnumerable<CatalogueChange> Diff(CatalogueItem old, CatalogueItem now)
    {
        var id = now.Id;
        CatalogueChange Make(ChangeCategory category, string field, string? from, string? to, string? message = null) =>
            new(id, category, field, from, to, message ?? $"{field}: {Show(from)} -> {Show(to)}");
        IEnumerable<CatalogueChange> Scalar(ChangeCategory category, string field, string? from, string? to) =>
            from == to ? [] : [Make(category, field, from, to)];
        IEnumerable<CatalogueChange> Names(ChangeCategory category, string field, IEnumerable<string> from, IEnumerable<string> to, string prefix)
        {
            var a = from.ToHashSet(StringComparer.Ordinal);
            var b = to.ToHashSet(StringComparer.Ordinal);
            foreach (var name in a.Except(b).OrderBy(n => n, StringComparer.Ordinal)) yield return Make(category, field, name, null, $"{prefix}removed {name}");
            foreach (var name in b.Except(a).OrderBy(n => n, StringComparer.Ordinal)) yield return Make(category, field, null, name, $"{prefix}added {name}");
        }

        foreach (var c in Scalar(ChangeCategory.Item, "kind", Word(old.Kind), Word(now.Kind))) yield return c;

        foreach (var c in Scalar(ChangeCategory.Version, "pin", old.Pin, now.Pin)) yield return c;
        foreach (var c in Scalar(ChangeCategory.Version, "pinHint", old.PinHint, now.PinHint)) yield return c;
        foreach (var folder in Union(old.Skills.Select(s => s.Folder), now.Skills.Select(s => s.Folder)))
        {
            var from = old.Skills.FirstOrDefault(s => s.Folder == folder);
            var to = now.Skills.FirstOrDefault(s => s.Folder == folder);
            if (from is not null && to is not null)
                foreach (var c in Scalar(ChangeCategory.Version, $"skills[{folder}].pin", from.Pin, to.Pin)) yield return c;
        }
        foreach (var name in Union(old.Packages.Select(p => p.Name), now.Packages.Select(p => p.Name)))
        {
            var from = old.Packages.FirstOrDefault(p => p.Name == name);
            var to = now.Packages.FirstOrDefault(p => p.Name == name);
            if (from is not null && to is not null) { foreach (var c in Scalar(ChangeCategory.Version, $"packages[{name}].version", from.Version, to.Version)) yield return c; }
            else if (to is not null) yield return Make(ChangeCategory.Version, $"packages[{name}]", null, to.Version, $"packages[{name}]: added ({Show(to.Version)})");
            else yield return Make(ChangeCategory.Version, $"packages[{name}]", from!.Version, null, $"packages[{name}]: removed (was {Show(from.Version)})");
        }

        foreach (var c in Scalar(ChangeCategory.Source, "sourceUrl", old.SourceUrl, now.SourceUrl)) yield return c;
        foreach (var name in Union(old.Artifacts.Select(a => a.Name), now.Artifacts.Select(a => a.Name)))
        {
            var from = old.Artifacts.FirstOrDefault(a => a.Name == name);
            var to = now.Artifacts.FirstOrDefault(a => a.Name == name);
            if (from is not null && to is not null)
                foreach (var c in Scalar(ChangeCategory.Source, $"artifacts[{name}].url", from.Url, to.Url)) yield return c;
        }

        foreach (var name in Union(old.Artifacts.Select(a => a.Name), now.Artifacts.Select(a => a.Name)))
        {
            var from = old.Artifacts.FirstOrDefault(a => a.Name == name);
            var to = now.Artifacts.FirstOrDefault(a => a.Name == name);
            if (from is not null && to is not null) { foreach (var c in Scalar(ChangeCategory.Hash, $"artifacts[{name}].sha256", from.Sha256, to.Sha256)) yield return c; }
            else if (to is not null) yield return Make(ChangeCategory.Hash, $"artifacts[{name}]", null, to.Sha256, $"artifacts[{name}]: added ({Show(to.Sha256)})");
            else yield return Make(ChangeCategory.Hash, $"artifacts[{name}]", from!.Sha256, null, $"artifacts[{name}]: removed (was {Show(from.Sha256)})");
        }
        foreach (var path in Union(old.Files.Select(f => f.Path), now.Files.Select(f => f.Path)))
        {
            var from = old.Files.FirstOrDefault(f => f.Path == path);
            var to = now.Files.FirstOrDefault(f => f.Path == path);
            if (from is not null && to is not null) { foreach (var c in Scalar(ChangeCategory.Hash, $"files[{path}]", from.Sha256, to.Sha256)) yield return c; }
            else if (to is not null) yield return Make(ChangeCategory.Hash, $"files[{path}]", null, to.Sha256, $"files[{path}]: added ({Show(to.Sha256)})");
            else yield return Make(ChangeCategory.Hash, $"files[{path}]", from!.Sha256, null, $"files[{path}]: removed (was {Show(from.Sha256)})");
        }

        if (old.ApproxSizeMb != now.ApproxSizeMb)
            yield return Make(ChangeCategory.Size, "approxSizeMb", old.ApproxSizeMb is { } a ? Size(a) : null, now.ApproxSizeMb is { } b ? Size(b) : null);
        foreach (var name in Union(old.Artifacts.Select(a => a.Name), now.Artifacts.Select(a => a.Name)))
        {
            var from = old.Artifacts.FirstOrDefault(a => a.Name == name)?.Size;
            var to = now.Artifacts.FirstOrDefault(a => a.Name == name)?.Size;
            if (old.Artifacts.Any(a => a.Name == name) && now.Artifacts.Any(a => a.Name == name) && from != to)
                yield return Make(ChangeCategory.Size, $"artifacts[{name}].size", from?.ToString(CultureInfo.InvariantCulture), to?.ToString(CultureInfo.InvariantCulture),
                    $"artifacts[{name}].size: {(from is { } f ? Number(f) : "(none)")} -> {(to is { } t ? Number(t) : "(none)")}");
        }
        long? oldTotal = old.Files.Count == 0 ? null : old.Files.Sum(f => f.Size);
        long? newTotal = now.Files.Count == 0 ? null : now.Files.Sum(f => f.Size);
        if (oldTotal != newTotal)
            yield return Make(ChangeCategory.Size, "files.totalBytes", oldTotal?.ToString(CultureInfo.InvariantCulture), newTotal?.ToString(CultureInfo.InvariantCulture),
                $"files.totalBytes: {(oldTotal is { } x ? Number(x) : "(none)")} -> {(newTotal is { } y ? Number(y) : "(none)")}");

        foreach (var c in Scalar(ChangeCategory.Licence, "licenceName", old.LicenceName, now.LicenceName)) yield return c;
        foreach (var c in Scalar(ChangeCategory.Licence, "licenceUrl", old.LicenceUrl, now.LicenceUrl)) yield return c;
        foreach (var c in Scalar(ChangeCategory.Licence, "licenceFile", old.LicenceFile, now.LicenceFile)) yield return c;
        foreach (var c in Scalar(ChangeCategory.Licence, "licenceText", ItemTerms.LicenceHash(old), ItemTerms.LicenceHash(now))) yield return c;
        foreach (var name in Union(old.Packages.Select(p => p.Name), now.Packages.Select(p => p.Name)))
        {
            var from = old.Packages.FirstOrDefault(p => p.Name == name);
            var to = now.Packages.FirstOrDefault(p => p.Name == name);
            if (from is not null && to is not null)
                foreach (var c in Scalar(ChangeCategory.Licence, $"packages[{name}].licence", from.Licence, to.Licence)) yield return c;
        }

        foreach (var c in Scalar(ChangeCategory.Terms, "termsClass", Word(old.TermsClass), Word(now.TermsClass))) yield return c;
        foreach (var folder in Union(old.Skills.Select(s => s.Folder), now.Skills.Select(s => s.Folder)))
        {
            var from = old.Skills.FirstOrDefault(s => s.Folder == folder);
            var to = now.Skills.FirstOrDefault(s => s.Folder == folder);
            if (from is not null && to is not null)
                foreach (var c in Scalar(ChangeCategory.Terms, $"skills[{folder}].termsClass", from.TermsClass is { } p ? Word(p) : null, to.TermsClass is { } q ? Word(q) : null)) yield return c;
        }

        foreach (var c in Names(ChangeCategory.Needs, "needs", old.Needs, now.Needs, "needs: ")) yield return c;
        foreach (var c in Names(ChangeCategory.Provides, "provides", old.Provides.Select(Provided), now.Provides.Select(Provided), "provides: ")) yield return c;

        foreach (var c in Scalar(ChangeCategory.Skills, "skillCount", old.SkillCount?.ToString(CultureInfo.InvariantCulture), now.SkillCount?.ToString(CultureInfo.InvariantCulture))) yield return c;
        foreach (var folder in Union(old.Skills.Select(s => s.Folder), now.Skills.Select(s => s.Folder)))
        {
            var from = old.Skills.FirstOrDefault(s => s.Folder == folder);
            var to = now.Skills.FirstOrDefault(s => s.Folder == folder);
            if (from is null) yield return Make(ChangeCategory.Skills, $"skills[{folder}]", null, folder, $"skills[{folder}]: added");
            else if (to is null) yield return Make(ChangeCategory.Skills, $"skills[{folder}]", folder, null, $"skills[{folder}]: removed");
            else
            {
                foreach (var c in Scalar(ChangeCategory.Skills, $"skills[{folder}].name", from.Name, to.Name)) yield return c;
                foreach (var c in Scalar(ChangeCategory.Skills, $"skills[{folder}].path", from.Path, to.Path)) yield return c;
                foreach (var c in Names(ChangeCategory.Skills, $"skills[{folder}].needs", from.Needs, to.Needs, $"skills[{folder}].needs: ")) yield return c;
            }
        }
        foreach (var name in Union(old.Folders.Select(f => f.Name), now.Folders.Select(f => f.Name)))
        {
            var from = old.Folders.FirstOrDefault(f => f.Name == name);
            var to = now.Folders.FirstOrDefault(f => f.Name == name);
            if (from is not null && to is not null) { if (from.SkillCount != to.SkillCount) yield return Make(ChangeCategory.Skills, $"folders[{name}]", from.SkillCount.ToString(CultureInfo.InvariantCulture), to.SkillCount.ToString(CultureInfo.InvariantCulture)); }
            else if (to is not null) yield return Make(ChangeCategory.Skills, $"folders[{name}]", null, to.SkillCount.ToString(CultureInfo.InvariantCulture), $"folders[{name}]: added ({to.SkillCount.ToString(CultureInfo.InvariantCulture)})");
            else yield return Make(ChangeCategory.Skills, $"folders[{name}]", from!.SkillCount.ToString(CultureInfo.InvariantCulture), null, $"folders[{name}]: removed (was {from.SkillCount.ToString(CultureInfo.InvariantCulture)})");
        }
        if (old.SkillsListed != now.SkillsListed)
            yield return Make(ChangeCategory.Skills, "skillsListed", old.SkillsListed ? "true" : "false", now.SkillsListed ? "true" : "false");

        foreach (var c in Scalar(ChangeCategory.State, "pinState", Word(old.PinState), Word(now.PinState))) yield return c;
        foreach (var c in Scalar(ChangeCategory.State, "countState", Word(old.CountState), Word(now.CountState))) yield return c;

        foreach (var c in Scalar(ChangeCategory.Text, "title", old.Title, now.Title)) yield return c;
        foreach (var c in Scalar(ChangeCategory.Text, "summary", old.Summary, now.Summary)) yield return c;
        foreach (var c in Scalar(ChangeCategory.Text, "vendor", old.Vendor, now.Vendor)) yield return c;
        foreach (var c in Scalar(ChangeCategory.Text, "licenceNotes", old.LicenceNotes, now.LicenceNotes)) yield return c;

        var oldInstall = SkillsCatalogueWriter.InstallText(old.Install);
        var newInstall = SkillsCatalogueWriter.InstallText(now.Install);
        if (oldInstall != newInstall)
        {
            static string Kind(InstallKind kind) => System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(kind.ToString());
            yield return Make(ChangeCategory.Install, "install", oldInstall, newInstall,
                old.Install is null ? $"install: added ({Kind(now.Install!.Kind)})"
                : now.Install is null ? $"install: removed (was {Kind(old.Install.Kind)})"
                : old.Install.Kind != now.Install.Kind ? $"install: {Kind(old.Install.Kind)} -> {Kind(now.Install.Kind)}"
                : $"install: the {Kind(now.Install.Kind)} parameters changed");
        }
    }

    private static string Provided(CatalogueProvide provide)
    {
        var kind = provide.Kind.ToString();
        return char.ToLowerInvariant(kind[0]) + kind[1..] + ":" + provide.Name;
    }

    private static IEnumerable<string> Union(IEnumerable<string> a, IEnumerable<string> b) =>
        a.Concat(b).Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal);

    private static string Phrase(ChangeFlag flag) => flag switch
    {
        ChangeFlag.AsksTermsAgain => "asks the terms again",
        ChangeFlag.TermsNowRequired => "terms now required",
        ChangeFlag.LicenceTextNotRecorded => "licence text not recorded",
    };

    private static string OneLine(string text) => text.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\n', ' ').Replace('\r', ' ');

    /// <summary>The report as markdown: line feeds, one text for one report, sizes with invariant digit groups, every changed file unless <paramref name="options"/> limits them.</summary>
    public string ToMarkdown(ChangeReportRenderOptions? options = null)
    {
        var limit = options?.MaxFilesPerItem;
        if (limit is < 0) throw new ArgumentOutOfRangeException(nameof(options), "MaxFilesPerItem cannot be negative.");
        var text = new StringBuilder();
        string List(IReadOnlyList<string> ids) => ids.Count == 0 ? "0" : $"{ids.Count.ToString(CultureInfo.InvariantCulture)} ({string.Join(", ", ids)})";
        var changed = Items.Where(i => !Added.Contains(i.ItemId) && !Removed.Contains(i.ItemId)).Select(i => i.ItemId).ToList();
        var kinds = Items.SelectMany(i => i.Changes).GroupBy(c => c.Category).OrderBy(g => g.Key).Select(g => $"{g.Key.ToString().ToLowerInvariant()} {g.Count().ToString(CultureInfo.InvariantCulture)}").ToList();
        var asking = AsksTermsAgain;

        text.Append("# Catalogue change report\n\n");
        text.Append(CultureInfo.InvariantCulture, $"Previous catalogue read on {PreviousReadOn} ({PreviousItems.ToString(CultureInfo.InvariantCulture)} items); new catalogue read on {NextReadOn} ({NextItems.ToString(CultureInfo.InvariantCulture)} items).\n\n");
        text.Append("## Summary\n\n");
        text.Append(CultureInfo.InvariantCulture, $"- Items added: {List(Added)}\n- Items removed: {List(Removed)}\n- Items changed: {List(changed)}\n- Items unchanged: {Unchanged.Count.ToString(CultureInfo.InvariantCulture)}\n");
        text.Append(CultureInfo.InvariantCulture, $"- Changes: {(kinds.Count == 0 ? "none" : string.Join(", ", kinds))}\n- Asking the terms again: {(asking.Count == 0 ? "none" : string.Join(", ", asking))}\n\n");
        text.Append("## Needs the owner's attention\n\n");
        if (Attention.Count == 0) text.Append("- None.\n");
        foreach (var item in Attention) text.Append(CultureInfo.InvariantCulture, $"- {item.ItemId}: {Phrase(item.Flag)} ({OneLine(item.Reason)})\n");
        text.Append("\n## Changes by item\n\n");
        if (Items.Count == 0) text.Append("- None.\n");
        var first = true;
        foreach (var set in Items)
        {
            if (!first) text.Append('\n');
            first = false;
            text.Append(CultureInfo.InvariantCulture, $"### {set.ItemId} - {OneLine(set.Title)} ({Word(set.Kind)})\n\n");
            if (!Added.Contains(set.ItemId) && !Removed.Contains(set.ItemId) && set.DownloadBytesBefore != set.DownloadBytesAfter)
            {
                var from = set.DownloadBytesBefore is { } b ? Number(b) + " B" : "unknown";
                var to = set.DownloadBytesAfter is { } a ? Number(a) + " B" : "unknown";
                var delta = set.DownloadBytesBefore is { } x && set.DownloadBytesAfter is { } y ? $" ({(y >= x ? "+" : "-")}{Number(Math.Abs(y - x))} B)" : "";
                text.Append(CultureInfo.InvariantCulture, $"Download: {from} -> {to}{delta}\n\n");
            }
            var files = 0;
            foreach (var change in set.Changes)
            {
                var isFile = change.Category == ChangeCategory.Hash && change.Field.StartsWith("files[", StringComparison.Ordinal);
                if (isFile && limit is { } max)
                {
                    files++;
                    if (files == max + 1)
                    {
                        var rest = set.Changes.Count(c => c.Category == ChangeCategory.Hash && c.Field.StartsWith("files[", StringComparison.Ordinal)) - max;
                        text.Append(CultureInfo.InvariantCulture, $"- hash: and {rest.ToString(CultureInfo.InvariantCulture)} more file changes (not shown; the full list is in the report data)\n");
                    }
                    if (files > max) continue;
                }
                text.Append(CultureInfo.InvariantCulture, $"- {change.Category.ToString().ToLowerInvariant()}: {OneLine(change.Message)}\n");
            }
        }
        return text.ToString();
    }
}
