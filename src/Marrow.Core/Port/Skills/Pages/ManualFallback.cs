// PORT-ONLY: first-release feature unit SK-14 (the "Download it yourself" fallback, OD-0099 review of version 3 and the final skills-downloads design page: sd-page.js fbBlock, FB). There is no Swift twin.
using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Core.Port.Skills.Pages;

/// <summary>What step 1 of the fallback says for one item: the file or files to get and where from.</summary>
public sealed record ManualFallback(string File, string Source, Uri Page, bool ForTool)
{
    /// <summary>
    /// <see cref="File"/> in the pieces it is drawn in (ED-NEW-dlhonest-1, owner 2026-10-11): the words as prose and only the literal values (a file name, a commit, wheels.txt) in code. Joined, they are
    /// <see cref="File"/> exactly. By default the whole of <see cref="File"/> is one literal.
    /// </summary>
    public IReadOnlyList<ManualFallbackPart> Parts { get; init; } = [new(File, Code: true)];

    /// <summary>Value equality, the parts compared piece by piece (a list compares by reference).</summary>
    public bool Equals(ManualFallback? other) =>
        other is not null && File == other.File && Source == other.Source && Page == other.Page && ForTool == other.ForTool && Parts.SequenceEqual(other.Parts);

    public override int GetHashCode() => HashCode.Combine(File, Source, Page, ForTool, Parts.Count);
}

/// <summary>One piece of step 1's text: prose, or a literal value drawn in code.</summary>
public sealed record ManualFallbackPart(string Text, bool Code);

/// <summary>
/// The fallback's step 1 from the catalogue, in the design's words (its FB table). ENGINEERING DECISIONS (ED-384): the design writes each item's two texts by hand; here they follow from what the
/// catalogue pins, by the rule the design's entries follow: (1) a skill set or plugin at one commit is "the repository at commit &lt;7 digits&gt;, as a .zip" from "github.com/&lt;owner&gt;/&lt;repo&gt;/tree/&lt;7 digits&gt;";
/// one at per-skill commits is "the &lt;n&gt; skill folders at their pinned commits (&lt;7 digits&gt;, ...)" from the repository; (2) a tool is its artifacts' file names joined by " and ", from the directory of its first
/// artifact's address (without "www."), or, where the catalogue gives an artifact no address, from the item's own source page (the vendor's releases page); (3) a Python package set is "the pinned wheels named
/// in wheels.txt in that folder" from "pypi.org"; (4) a Node package set, an item with no pin and one with no source have none (the design draws none). Python's embedded package (T1) is two items here (the
/// design's one entry names the pip wheel too), so its text names only its own file.
/// </summary>
public static class ManualFallbacks
{
    private static readonly string[] Words = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten"];

    public static ManualFallback? For(CatalogueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        switch (item.Kind)
        {
            case ItemKind.Skillset or ItemKind.Plugin:
            {
                if (item.SourceUrl is null || !Uri.TryCreate(item.SourceUrl, UriKind.Absolute, out var repo)) return null;
                var path = repo.AbsolutePath.Trim('/');
                if (!string.IsNullOrEmpty(item.Pin))
                {
                    var commit = item.Pin[..Math.Min(7, item.Pin.Length)];
                    return Make(Strip(repo.Host) + "/" + path + "/tree/" + commit, false, Prose("the repository at commit "), Code(commit), Prose(", as a .zip"));
                }
                var pins = item.Skills.Select(s => s.Pin).OfType<string>().Where(p => p.Length > 0).Select(p => p[..Math.Min(7, p.Length)]).Distinct(StringComparer.Ordinal).ToList();
                if (pins.Count == 0 || item.Skills.Any(s => string.IsNullOrEmpty(s.Pin))) return null;
                var count = pins.Count < Words.Length ? Words[pins.Count] : pins.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var listed = pins.SelectMany((p, i) => i == 0 ? new[] { Code(p) } : [Prose(", "), Code(p)]);
                return Make(Strip(repo.Host) + "/" + path, false, [Prose("the " + count + " skill folders at their pinned commits ("), .. listed, Prose(")")]);
            }
            case ItemKind.Pyset:
                return Make("pypi.org", true, Prose("the pinned wheels named in "), Code("wheels.txt"), Prose(" in that folder"));
            case ItemKind.Tool when item.Artifacts.Count > 0:
            {
                var file = item.Artifacts.SelectMany((a, i) => i == 0 ? new[] { Code(a.Name) } : [Prose(" and "), Code(a.Name)]).ToArray();
                var url = item.Artifacts[0].Url is { } address && Uri.TryCreate(address, UriKind.Absolute, out var parsed) ? parsed : null;
                if (url is not null) return Make(Strip(url.Host) + url.AbsolutePath[..(url.AbsolutePath.LastIndexOf('/') + 1)], true, file);
                if (item.SourceUrl is { } page && Uri.TryCreate(page, UriKind.Absolute, out var source)) return Make(Strip(source.Host) + source.AbsolutePath.TrimEnd('/'), true, file);
                return null;
            }
            default:
                return null;
        }
    }

    private static string Strip(string host) => host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;

    private static ManualFallbackPart Prose(string text) => new(text, Code: false);

    private static ManualFallbackPart Code(string text) => new(text, Code: true);

    private static ManualFallback Make(string source, bool tool, params ManualFallbackPart[] parts) =>
        new(string.Concat(parts.Select(p => p.Text)), source, new Uri("https://" + source), tool) { Parts = parts };
}
