// P11 (task packet T-M0-010): packaging and update smoke test. A throwaway probe, never production code.
//
// QUESTION: can a self-contained win-x64 .NET 10 app, packed with Velopack's vpk, be installed by the
// generated Setup.exe and launched as an ordinary desktop app, and does a hand-written port of the
// update check emit PR-UPDATE-019 / 020 / 027's literal strings byte for byte, with exactly one
// unauthenticated GET per run?
//
// SOURCE FACTS (pinned Droppy Code dd2b86efef9e77168f304a86e8a8411877578bbe):
//   apiURL                       DroppyCode/App/UpdateChecker.swift:67-70
//   session headers, timeouts    DroppyCode/App/UpdateChecker.swift:101-110
//   check() catch branch         DroppyCode/App/UpdateChecker.swift:181-185   (PR-UPDATE-019)
//   fetchReleases 2xx guard      DroppyCode/App/UpdateChecker.swift:189-195   (PR-UPDATE-020)
//   newestRelease                DroppyCode/App/UpdateChecker.swift:199-219
//   UpdateError                  DroppyCode/App/UpdateChecker.swift:265-275
//   GitLabRelease decode         DroppyCode/App/UpdateChecker.swift:278-332   (PR-UPDATE-027)
//   AppVersion                   DroppyCode/App/UpdateChecker.swift:7-38
//
// Modes (every mode sends at most ONE request; --fixture sends none):
//   (no args)            the source apiURL
//   --url <u>            another URL (the unreachable and non-2xx paths)
//   --fixture <file>     no network; the file is treated as a 200 body (PR-UPDATE-027 decode cases)
//   --label <name>       output file name, default "default"
// Output: <MARROW_PORT_DATA_DIR or %TEMP%\MarrowPortP11Probe-out>\<label>.txt, UTF-8 without BOM.

using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Velopack;

namespace P11;

static class Src
{
    // UpdateChecker.swift:67-70
    public const string ProjectId = "86415974";
    public const string ApiUrl = "https://gitlab.com/api/v4/projects/" + ProjectId + "/releases?per_page=20";
    // AppInfo.version at the pinned commit (build 19): the version the source compares against.
    public const string CurrentVersion = "1.6.0";
    // UpdateChecker.swift:108. UNRESOLVED for production (PR-UPDATE spec: the rename must be decided
    // explicitly); the probe sends the source literal and records that it did.
    public const string UserAgent = "Droppy Code/" + CurrentVersion;
    public const string Accept = "application/json";
    // UpdateChecker.swift:182, byte for byte (the source byte between "Couldn" and "t" is 0x27).
    public const string CouldNotReach = "Couldn't reach GitLab.";
}

// UpdateChecker.swift:265-275
sealed class UpdateError : Exception
{
    public string ErrorDescription { get; }
    UpdateError(string d) : base(d) { ErrorDescription = d; }
    public static UpdateError NoRelease() => new("No release with a disk image was found.");
    public static UpdateError BadResponse(int status) =>
        new("GitLab answered with HTTP " + status.ToString(CultureInfo.InvariantCulture) + ".");
}

sealed class DecodeError(string m) : Exception(m);

// UpdateChecker.swift:7-38
readonly record struct AppVersion(long A, long B, long C) : IComparable<AppVersion>
{
    public static AppVersion? Parse(string raw)
    {
        var text = raw.Trim();
        if (text.StartsWith('v') || text.StartsWith('V')) text = text[1..];
        var parts = text.Split('.');
        if (parts.Length < 1 || parts.Length > 3) return null;
        var n = new List<long>();
        foreach (var p in parts)
        {
            if (p.Length == 0 || !p.All(ch => ch >= '0' && ch <= '9')) return null;
            if (!long.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var v)) return null;
            n.Add(v);
        }
        while (n.Count < 3) n.Add(0);
        return new AppVersion(n[0], n[1], n[2]);
    }
    public static string TextOfTag(string tag)
    {
        var text = tag.Trim();
        if (text.StartsWith('v') || text.StartsWith('V')) text = text[1..];
        return text;
    }
    public int CompareTo(AppVersion o) =>
        A != o.A ? A.CompareTo(o.A) : B != o.B ? B.CompareTo(o.B) : C.CompareTo(o.C);
    public override string ToString() => $"{A}.{B}.{C}";
}

sealed record Link(string Name, string Url, string? DirectAssetUrl)
{
    public bool IsDiskImage
    {
        get
        {
            var path = (DirectAssetUrl ?? Url).ToLowerInvariant();
            return path.EndsWith(".dmg", StringComparison.Ordinal) ||
                   Name.ToLowerInvariant().EndsWith(".dmg", StringComparison.Ordinal);
        }
    }
}

sealed record GitLabRelease(string TagName, string? Name, string? Description, DateTimeOffset? ReleasedAt,
                            bool UpcomingRelease, List<Link> Links);

sealed record AvailableUpdate(string Version, string Tag, Uri DownloadUrl, string Notes, DateTimeOffset? ReleasedAt, string PageUrl);

// The six-field lenient, all-or-nothing decode (PR-UPDATE-027). Any failure throws DecodeError,
// which is not an UpdateError, so it reads as "Couldn't reach GitLab." exactly as in the source.
static class Decoder
{
    static JsonElement? Get(JsonElement o, string key) =>
        o.TryGetProperty(key, out var v) ? v : null;

    static string Req(JsonElement o, string key)
    {
        var v = Get(o, key) ?? throw new DecodeError($"keyNotFound {key}");
        if (v.ValueKind != JsonValueKind.String) throw new DecodeError($"typeMismatch {key} ({v.ValueKind})");
        return v.GetString()!;
    }

    // decodeIfPresent: missing or null gives nil; present with a wrong type throws.
    static string? OptStr(JsonElement o, string key)
    {
        var v = Get(o, key);
        if (v is null || v.Value.ValueKind == JsonValueKind.Null) return null;
        if (v.Value.ValueKind != JsonValueKind.String) throw new DecodeError($"typeMismatch {key} ({v.Value.ValueKind})");
        return v.Value.GetString();
    }

    public static List<GitLabRelease> Decode(byte[] body)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(body); }
        catch (JsonException e) { throw new DecodeError("dataCorrupted " + e.Message); }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array) throw new DecodeError($"typeMismatch root ({root.ValueKind})");
            var list = new List<GitLabRelease>();
            foreach (var r in root.EnumerateArray())
            {
                if (r.ValueKind != JsonValueKind.Object) throw new DecodeError($"typeMismatch element ({r.ValueKind})");
                var tag = Req(r, "tag_name");
                var name = OptStr(r, "name");
                var description = OptStr(r, "description");
                var releasedAt = OptStr(r, "released_at") is { } s ? ParseDate(s) : null;
                bool upcoming = false;
                var u = Get(r, "upcoming_release");
                if (u is { } uv && uv.ValueKind != JsonValueKind.Null)
                {
                    if (uv.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        throw new DecodeError($"typeMismatch upcoming_release ({uv.ValueKind})");
                    upcoming = uv.GetBoolean();
                }
                var links = new List<Link>();
                var a = Get(r, "assets");
                if (a is { } av && av.ValueKind != JsonValueKind.Null)
                {
                    if (av.ValueKind != JsonValueKind.Object) throw new DecodeError($"typeMismatch assets ({av.ValueKind})");
                    var l = Get(av, "links") ?? throw new DecodeError("keyNotFound links");
                    if (l.ValueKind != JsonValueKind.Array) throw new DecodeError($"typeMismatch links ({l.ValueKind})");
                    foreach (var le in l.EnumerateArray())
                    {
                        if (le.ValueKind != JsonValueKind.Object) throw new DecodeError($"typeMismatch link ({le.ValueKind})");
                        links.Add(new Link(Req(le, "name"), Req(le, "url"), OptStr(le, "direct_asset_url")));
                    }
                }
                list.Add(new GitLabRelease(tag, name, description, releasedAt, upcoming, links));
            }
            return list;
        }
    }

    // UpdateChecker.swift:323-331: internet date-time, fraction optional; unparseable gives nil.
    static DateTimeOffset? ParseDate(string text)
    {
        string[] formats = ["yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", "yyyy-MM-dd'T'HH:mm:ssK"];
        return DateTimeOffset.TryParseExact(text, formats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d) ? d : null;
    }

    // UpdateChecker.swift:199-219
    public static AvailableUpdate? NewestRelease(List<GitLabRelease> releases)
    {
        (AppVersion v, AvailableUpdate u)? best = null;
        foreach (var r in releases)
        {
            if (r.UpcomingRelease) continue;
            if (AppVersion.Parse(r.TagName) is not { } version) continue;
            if (r.ReleasedAt is { } ra && ra > DateTimeOffset.Now) continue;
            var link = r.Links.FirstOrDefault(x => x.IsDiskImage);
            if (link is null) continue;
            if (!Uri.TryCreate(link.DirectAssetUrl ?? link.Url, UriKind.Absolute, out var url)) continue;
            var cand = new AvailableUpdate(AppVersion.TextOfTag(r.TagName), r.TagName, url, r.Description ?? "",
                r.ReleasedAt, "https://gitlab.com/droppyformac1/droppy-code/-/releases/" + r.TagName);
            if (best is { } cur && cur.v.CompareTo(version) >= 0) continue;
            best = (version, cand);
        }
        return best?.u;
    }
}

// Counts every request that leaves the client and records its headers: evidence for "exactly one
// unauthenticated GET per run", measured rather than assumed.
sealed class CountingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    public int Count;
    public readonly List<string> Log = [];
    // Both paths are counted: HttpClient.Send (sync, used below) reaches Send, not SendAsync. The first
    // build overrode only SendAsync and reported 0 requests for a run that got a 200; see RESULT.md.
    void Record(HttpRequestMessage req)
    {
        Interlocked.Increment(ref Count);
        var h = string.Join(" | ", req.Headers.Select(x => x.Key + ": " + string.Join(",", x.Value)));
        Log.Add($"{req.Method} {req.RequestUri} headers[{h}]");
    }
    protected override HttpResponseMessage Send(HttpRequestMessage req, CancellationToken ct)
    {
        Record(req);
        return base.Send(req, ct);
    }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        Record(req);
        return base.SendAsync(req, ct);
    }
}

static class Program
{
    static int Main(string[] args)
    {
        // Velopack install/uninstall/update hooks. Must run first; exits the process on a hook.
        VelopackApp.Build().Run();

        string? Arg(string name)
        {
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        var label = Arg("--label") ?? "default";
        var url = Arg("--url") ?? Src.ApiUrl;
        var fixture = Arg("--fixture");

        var outDir = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        if (string.IsNullOrEmpty(outDir)) outDir = Path.Combine(Path.GetTempPath(), "MarrowPortP11Probe-out");
        Directory.CreateDirectory(outDir);

        var o = new StringBuilder();
        void W(string k, string v) => o.Append(k).Append('=').Append(v).Append('\n');

        W("probe", "MarrowPortP11Probe");
        W("started_utc", DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        W("process_path", Environment.ProcessPath ?? "?");
        W("args", string.Join(" ", args));
        var appDir = AppContext.BaseDirectory.TrimEnd('\\');
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location) ?? "?";
        W("app_dir", appDir);
        W("corelib_dir", runtimeDir);
        W("self_contained", string.Equals(appDir, runtimeDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ? "true" : "false");
        W("framework", RuntimeInformation.FrameworkDescription);
        W("rid", RuntimeInformation.RuntimeIdentifier);
        W("mode", fixture is null ? "network" : "fixture");
        W("url", fixture is null ? url : "(none) fixture " + fixture);

        string? lastError = null;
        AvailableUpdate? update = null;
        var handler = new CountingHandler(new SocketsHttpHandler
        {
            UseCookies = false,
            UseProxy = true,
            Credentials = null,
            AllowAutoRedirect = true,
        });
        try
        {
            byte[] body;
            if (fixture is not null)
            {
                body = File.ReadAllBytes(fixture);
                W("http_status", "(fixture) 200");
            }
            else
            {
                // UpdateChecker.swift:101-110. .NET has no separate per-request idle timeout, so the
                // 30 s resource timeout is the whole-request timeout here (recorded as a deviation).
                using var client = new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(30) };
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.TryAddWithoutValidation("User-Agent", Src.UserAgent);
                req.Headers.TryAddWithoutValidation("Accept", Src.Accept);
                // No Cache-Control header: the source's reloadIgnoringLocalCacheData is a local cache
                // policy, not a header, and HttpClient keeps no response cache.
                using var resp = client.Send(req, HttpCompletionOption.ResponseContentRead);
                W("http_status", ((int)resp.StatusCode).ToString(CultureInfo.InvariantCulture));
                body = resp.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                // UpdateChecker.swift:191-193
                var status = (int)resp.StatusCode;
                if (status < 200 || status > 299) throw UpdateError.BadResponse(status);
            }
            W("body_bytes", body.Length.ToString(CultureInfo.InvariantCulture));
            W("body_sha256", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(body)));
            var releases = Decoder.Decode(body);
            W("releases_decoded", releases.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var r in releases)
                W("release", $"tag_name={r.TagName}; name={r.Name ?? "<nil>"}; description_chars={(r.Description?.Length.ToString(CultureInfo.InvariantCulture) ?? "<nil>")}; released_at={(r.ReleasedAt?.ToString("o", CultureInfo.InvariantCulture) ?? "<nil>")}; upcoming_release={(r.UpcomingRelease ? "true" : "false")}; links={r.Links.Count}; dmg={(r.Links.Any(l => l.IsDiskImage) ? "yes" : "no")}");
            update = Decoder.NewestRelease(releases) ?? throw UpdateError.NoRelease();
            lastError = null;
        }
        catch (Exception e)
        {
            W("exception", e.GetType().FullName + ": " + e.Message.Replace('\n', ' ').Replace('\r', ' '));
            if (e.InnerException is { } ie) W("inner_exception", ie.GetType().FullName + ": " + ie.Message.Replace('\n', ' '));
            // UpdateChecker.swift:182
            lastError = (e as UpdateError)?.ErrorDescription ?? Src.CouldNotReach;
        }

        W("requests_sent", handler.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var l in handler.Log) W("request", l);
        if (update is not null)
        {
            W("update.version", update.Version);
            W("update.tag", update.Tag);
            W("update.downloadURL", update.DownloadUrl.ToString());
            W("update.pageURL", update.PageUrl);
            W("update.releasedAt", update.ReleasedAt?.ToString("o", CultureInfo.InvariantCulture) ?? "<nil>");
            var avail = AppVersion.Parse(update.Version) is { } lv && AppVersion.Parse(Src.CurrentVersion) is { } cv && lv.CompareTo(cv) > 0;
            W("updateAvailable_vs_" + Src.CurrentVersion, avail ? "true" : "false");
        }
        W("lastError", lastError ?? "<nil>");
        W("lastError_utf8_hex", lastError is null ? "<nil>" : Convert.ToHexString(Encoding.UTF8.GetBytes(lastError)));
        W("finished_utc", DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture));

        File.WriteAllBytes(Path.Combine(outDir, label + ".txt"), new UTF8Encoding(false).GetBytes(o.ToString()));
        return lastError is null ? 0 : 2;
    }
}
