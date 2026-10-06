// PORT-ONLY: first-release feature unit SK-14 (the "Download it yourself" fallback, OD-0099 review of version 3 and the final skills-downloads design page: sd-page.js fbBlock, FB). There is no Swift twin.
using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Core.Port.Skills.Pages;

/// <summary>What step 1 of the fallback says for one item: the file or files to get and where from.</summary>
public sealed record ManualFallback(string File, string Source, Uri Page, bool ForTool);

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
                    return Make("the repository at commit " + commit + ", as a .zip", Strip(repo.Host) + "/" + path + "/tree/" + commit, false);
                }
                var pins = item.Skills.Select(s => s.Pin).OfType<string>().Where(p => p.Length > 0).Select(p => p[..Math.Min(7, p.Length)]).Distinct(StringComparer.Ordinal).ToList();
                if (pins.Count == 0 || item.Skills.Any(s => string.IsNullOrEmpty(s.Pin))) return null;
                var count = pins.Count < Words.Length ? Words[pins.Count] : pins.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return Make("the " + count + " skill folders at their pinned commits (" + string.Join(", ", pins) + ")", Strip(repo.Host) + "/" + path, false);
            }
            case ItemKind.Pyset:
                return Make("the pinned wheels named in wheels.txt in that folder", "pypi.org", true);
            case ItemKind.Tool when item.Artifacts.Count > 0:
            {
                var file = string.Join(" and ", item.Artifacts.Select(a => a.Name));
                var url = item.Artifacts[0].Url is { } address && Uri.TryCreate(address, UriKind.Absolute, out var parsed) ? parsed : null;
                if (url is not null) return Make(file, Strip(url.Host) + url.AbsolutePath[..(url.AbsolutePath.LastIndexOf('/') + 1)], true);
                if (item.SourceUrl is { } page && Uri.TryCreate(page, UriKind.Absolute, out var source)) return Make(file, Strip(source.Host) + source.AbsolutePath.TrimEnd('/'), true);
                return null;
            }
            default:
                return null;
        }
    }

    private static string Strip(string host) => host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;

    private static ManualFallback Make(string file, string source, bool tool) => new(file, source, new Uri("https://" + source), tool);
}
