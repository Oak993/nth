using System.Globalization;
using System.Text.Json;
using Marrow.Services.Web;

namespace Marrow.App.Views.Browser;

// PORT-ONLY: DESIGN 5.3, ED-NEW-web-w8c-1. Private records never serialize.
public sealed class VisusSiteZoom
{
    private readonly Dictionary<string, Dictionary<string, double>> profiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> privateSites = new(StringComparer.Ordinal);
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
    private Dictionary<string, double> Sites(string profile)
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
        if (sites.TryGetValue(origin, out var previous) && previous == factor) return;
        sites[origin] = factor;
        Changed?.Invoke(profile, origin);
    }
    internal void RetainPrivate() => privateTabs++;
    internal void ReleasePrivate() { if (privateTabs > 0 && --privateTabs == 0) privateSites.Clear(); }
    public string ToJson() => JsonSerializer.Serialize(profiles);
    public static VisusSiteZoom FromJson(string? json)
    {
        var store = new VisusSiteZoom();
        if (string.IsNullOrWhiteSpace(json)) return store;
        try
        {
            var saved = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, double>>>(json);
            if (saved is null) return store;
            foreach (var (profile, sites) in saved)
            {
                if (profile != WebProfiles.BrowseProfile && profile != WebProfiles.PreviewProfile && !WebProfiles.IsAgentProfile(profile)) continue;
                if (sites is null) continue;
                foreach (var (origin, factor) in sites)
                    if (double.IsFinite(factor) && factor >= Steps[0] && factor <= Steps[^1]) store.Set(profile, origin, factor);
            }
        }
        catch (JsonException) { }
        return store;
    }
}
