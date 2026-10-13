using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Services.Web;

namespace Marrow.App.Views.Browser;

// PORT-ONLY: DESIGN 5.3, ED-NEW-web-w8c-1 and -9. Private records never serialize. The file is {"version":1,"profiles":{profile:{origin:factor}}}:
// a file from a newer nth is read as empty and never written over, members this version does not know come back as they were read,
// and each profile keeps its 500 most recently set sites (a site at 100 % has no record).
public sealed class VisusSiteZoom
{
    public const int FormatVersion = 1;
    public const int MaxSitesPerProfile = 500;
    private readonly Dictionary<string, OrderedDictionary<string, double>> profiles = new(StringComparer.Ordinal);
    private readonly OrderedDictionary<string, double> privateSites = new(StringComparer.Ordinal);
    private JsonObject? unknown;
    private bool newerFile;
    private int privateTabs;
    public event Action<string, string>? Changed;
    public static readonly double[] Steps = [0.25, 0.33, 0.5, 0.67, 0.75, 0.8, 0.9, 1, 1.1, 1.25, 1.5, 1.75, 2, 2.5, 3, 4, 5];
    public static string? Origin(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http")
        ? u.Scheme + "://" + (u.HostNameType == UriHostNameType.IPv6 ? "[" + u.IdnHost + "]" : u.IdnHost.ToLowerInvariant()) + ":" + u.Port.ToString(CultureInfo.InvariantCulture) : null;
    public static void Validate(double factor)
    {
        if (!double.IsFinite(factor) || factor < Steps[0] || factor > Steps[^1]) throw new ArgumentOutOfRangeException(nameof(factor));
    }
    public static double Step(double factor, bool increase) => increase ? Steps.FirstOrDefault(s => s > factor + 0.00001, Steps[^1]) : Steps.LastOrDefault(s => s < factor - 0.00001, Steps[0]);
    private OrderedDictionary<string, double> Sites(string profile)
    {
        if (profile == WebProfiles.PrivateProfile) return privateSites;
        if (!profiles.TryGetValue(profile, out var sites)) profiles[profile] = sites = new(StringComparer.Ordinal);
        return sites;
    }
    public double Get(string profile, string url) => Origin(url) is { } origin && Sites(profile).TryGetValue(origin, out var factor) ? factor : 1;
    public bool Contains(string profile, string url) => Origin(url) is { } origin && Sites(profile).ContainsKey(origin);
    public void Set(string profile, string url, double factor)
    {
        Validate(factor);
        if (Origin(url) is not { } origin) return;
        var sites = Sites(profile);
        var had = sites.TryGetValue(origin, out var previous);
        if (factor == 1) // 100 % is what no record means
        {
            if (!had) return;
            sites.Remove(origin);
            Changed?.Invoke(profile, origin);
            return;
        }
        if (had && previous == factor) return;
        sites.Remove(origin); // set again, it is the newest record: the oldest is the one a full profile drops
        sites[origin] = factor;
        while (sites.Count > MaxSitesPerProfile) sites.RemoveAt(0);
        Changed?.Invoke(profile, origin);
    }
    /// <summary>Settings › Visus › "Clear browsing data" (W10): every site zoom of a profile goes (a private tab keeps none).</summary>
    public void ClearProfile(string profile)
    {
        if (profile == WebProfiles.PrivateProfile || !profiles.TryGetValue(profile, out var sites) || sites.Count == 0) return;
        var origins = sites.Keys.ToList();
        sites.Clear();
        foreach (var origin in origins) Changed?.Invoke(profile, origin);
    }
    internal void RetainPrivate() => privateTabs++;
    internal void ReleasePrivate() { if (privateTabs > 0 && --privateTabs == 0) privateSites.Clear(); }
    /// <summary>The file's text; null when the file came from a newer nth and must stay as it is.</summary>
    public string? ToJson()
    {
        if (newerFile) return null;
        var root = new JsonObject { ["version"] = FormatVersion, ["profiles"] = JsonSerializer.SerializeToNode(profiles) };
        if (unknown is not null)
            foreach (var (name, value) in unknown) root[name] = value?.DeepClone();
        return root.ToJsonString();
    }
    public static VisusSiteZoom FromJson(string? json)
    {
        var store = new VisusSiteZoom();
        if (string.IsNullOrWhiteSpace(json)) return store;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root) return store;
            if (root["version"] is JsonValue found && found.TryGetValue<int>(out var version) && version > FormatVersion)
            {
                store.newerFile = true; // a newer nth wrote it: read none of it and write nothing over it
                return store;
            }
            foreach (var (name, value) in root)
            {
                if (name is "version" or "profiles") continue;
                (store.unknown ??= [])[name] = value?.DeepClone();
            }
            if (root["profiles"] is not JsonObject saved) return store;
            foreach (var (profile, sites) in saved)
            {
                if (profile != WebProfiles.BrowseProfile && profile != WebProfiles.PreviewProfile && !WebProfiles.IsAgentProfile(profile)) continue;
                if (sites is not JsonObject records) continue;
                foreach (var (origin, node) in records)
                    if (node is JsonValue record && record.TryGetValue<double>(out var factor) && double.IsFinite(factor) && factor >= Steps[0] && factor <= Steps[^1]) store.Set(profile, origin, factor);
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return new VisusSiteZoom(); // a file that does not read is as good as none; the next change writes a fresh one
        }
        return store;
    }
}
