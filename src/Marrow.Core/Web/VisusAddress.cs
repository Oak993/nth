// PORT-ONLY: web surfaces W8a (E:/Droppy/work/design/web-surfaces/DESIGN.md 5.2 "Typing a URL navigates. Anything else
// searches with the default engine (Settings: Bing, Google, DuckDuckGo, Brave, Startpage; default DuckDuckGo for privacy)
// ... Local paths and file: are refused at the top level ... Allowed schemes: https, http, about:blank. Blocked: javascript:,
// file:, data: at the top level, edge:, chrome: and other internal pages", 8.3 external protocols; 13.3 "Visus chrome:
// address field rules"; ED-NEW-web-w8a-1). No Swift counterpart. What the address field does with what was typed, decided
// here alone: go to an address, search, open an app (through the 8.3 prompt), or refuse with a reason.
using System.Globalization;
using Marrow.Core.Localisation;

namespace Marrow.Core.Web;

/// <summary>DESIGN 5.2: the search engines Settings offers.</summary>
public enum SearchEngine
{
    DuckDuckGo,
    Bing,
    Google,
    Brave,
    Startpage,
}

/// <summary>The engines' names and query addresses.</summary>
public static class SearchEngines
{
    /// <summary>DuckDuckGo, for privacy (5.2).</summary>
    public const SearchEngine Default = SearchEngine.DuckDuckGo;

    /// <summary>In the order Settings lists them (5.2).</summary>
    public static IReadOnlyList<SearchEngine> All { get; } =
        [SearchEngine.Bing, SearchEngine.Google, SearchEngine.DuckDuckGo, SearchEngine.Brave, SearchEngine.Startpage];

    public static string Name(SearchEngine engine) => engine switch
    {
        SearchEngine.Bing => "Bing",
        SearchEngine.Google => "Google",
        SearchEngine.Brave => "Brave",
        SearchEngine.Startpage => "Startpage",
        _ => "DuckDuckGo",
    };

    /// <summary>The results page for <paramref name="query"/>.</summary>
    public static string QueryUrl(SearchEngine engine, string query)
    {
        var q = Uri.EscapeDataString(query.Trim());
        return engine switch
        {
            SearchEngine.Bing => "https://www.bing.com/search?q=" + q,
            SearchEngine.Google => "https://www.google.com/search?q=" + q,
            SearchEngine.Brave => "https://search.brave.com/search?q=" + q,
            SearchEngine.Startpage => "https://www.startpage.com/sp/search?query=" + q,
            _ => "https://duckduckgo.com/?q=" + q,
        };
    }
}

/// <summary>What typed text does.</summary>
public enum AddressAction
{
    /// <summary>Go to <see cref="AddressResult.Url"/> (https, http or the blank page).</summary>
    Navigate,

    /// <summary>Search for <see cref="AddressResult.Query"/>; <see cref="AddressResult.Url"/> is the results page.</summary>
    Search,

    /// <summary>Open another app (<see cref="AddressResult.Url"/>), through the external-protocol prompt (8.3).</summary>
    External,

    /// <summary>Refused: <see cref="AddressResult.Refusal"/> says why.</summary>
    Refused,
}

/// <summary>The address field's decision.</summary>
public sealed record AddressResult(AddressAction Action, string? Url, string? Query = null, string? Refusal = null);

/// <summary>DESIGN 5.2: the address and search rules.</summary>
public static class VisusAddress
{
    public static string LocalFileRefusal => UiText.Get("visus.address.refusal_local_file");
    public static string ScriptRefusal => UiText.Get("visus.address.refusal_script");
    public static string DataRefusal => UiText.Get("visus.address.refusal_data");
    public static string InternalRefusal => UiText.Get("visus.address.refusal_internal");

    // The generic top-level domains people type without a scheme. Two-letter domains are the country codes below.
    private static readonly HashSet<string> GenericTlds = new(StringComparer.OrdinalIgnoreCase)
    {
        "com", "org", "net", "edu", "gov", "mil", "int", "info", "biz", "name", "pro", "mobi", "aero", "coop", "museum",
        "jobs", "travel", "app", "dev", "page", "blog", "cloud", "online", "site", "tech", "store", "shop", "xyz", "top",
        "club", "art", "design", "news", "live", "life", "world", "today", "space", "website", "wiki", "link", "email",
        "media", "studio", "network", "systems", "software", "solutions", "agency", "digital", "finance", "health",
        "academy", "center", "company", "group", "global", "help", "host", "inc", "ltd", "llc", "ventures", "works",
        "zone", "codes", "run", "new", "google", "microsoft", "apple", "amazon", "aws", "azure", "office", "windows",
        "bing", "youtube", "music", "game", "games", "video", "photo", "photos", "pics", "social", "chat", "fun", "lol",
        "wtf", "rocks", "ninja", "guru", "team", "tools", "dog", "cat", "fyi", "one", "red", "blue", "pink", "black",
        "green", "gold", "plus", "build", "foundation", "institute", "school", "university", "education", "science",
        "engineering", "consulting", "law", "legal", "bank", "insurance", "money", "capital", "fund", "market", "markets",
        "trade", "exchange", "energy", "solar", "city", "london", "berlin", "paris", "nyc", "tokyo", "africa", "asia",
        "eus", "gal", "scot", "wales", "quebec", "swiss", "realty", "house", "homes", "land", "estate", "events",
        "tickets", "careers", "work", "auto", "car", "cars", "bike", "fit", "fitness", "yoga", "golf", "football",
        "soccer", "hockey", "tennis", "bio", "eco", "earth", "ink", "ist", "kim", "moe", "nexus", "onl", "ooo", "phd",
        "rip", "sbs", "vip",
        // Reserved names that are hosts on a LAN or in tests (RFC 2606, 6761, 6762).
        "test", "example", "invalid", "localhost", "local", "internal", "lan", "arpa", "onion",
    };

    private static readonly HashSet<string> CountryTlds = new(
        ("ac ad ae af ag ai al am ao aq ar as at au aw ax az ba bb bd be bf bg bh bi bj bm bn bo br bs bt bw by bz ca cc cd cf cg " +
         "ch ci ck cl cm cn co cr cu cv cw cx cy cz de dj dk dm do dz ec ee eg er es et eu fi fj fk fm fo fr ga gd ge gf gg gh gi " +
         "gl gm gn gp gq gr gs gt gu gw gy hk hm hn hr ht hu id ie il im in io iq ir is it je jm jo jp ke kg kh ki km kn kp kr " +
         "kw ky kz la lb lc li lk lr ls lt lu lv ly ma mc md me mg mh mk ml mm mn mo mp mq mr ms mt mu mv mw mx my mz na nc ne " +
         "nf ng ni nl no np nr nu nz om pa pe pf pg ph pk pl pm pn pr ps pt pw py qa re ro rs ru rw sa sb sc sd se sg sh si sk " +
         "sl sm sn so sr ss st su sv sx sy sz tc td tf tg th tj tk tl tm tn to tr tt tv tw tz ua ug uk us uy uz va vc ve vg vi " +
         "vn vu wf ws ye yt za zm zw").Split(' '), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// DESIGN 5.2: what <paramref name="input"/> does, or null for nothing typed. A leading "?" always searches.
    /// <paramref name="isRegisteredScheme"/> answers whether an app on this PC handles a scheme (so "zoommtg:..." opens Zoom
    /// while "define:word" searches); null treats only the schemes nth knows as apps.
    /// </summary>
    public static AddressResult? Resolve(string? input, SearchEngine engine, Func<string, bool>? isRegisteredScheme = null)
    {
        var text = (input ?? "").Trim();
        if (text.Length == 0) return null;
        if (text[0] == '?')
        {
            var forced = text[1..].Trim();
            return forced.Length == 0 ? null : Search(forced, engine);
        }

        if (IsLocalPath(text)) return new AddressResult(AddressAction.Refused, null, Refusal: LocalFileRefusal);
        if (WebPolicy.SchemeOf(text) is { } scheme && !StartsWithPort(text[(scheme.Length + 1)..]))
            return ResolveScheme(text, scheme, engine, isRegisteredScheme);
        if (text.Any(char.IsWhiteSpace)) return Search(text, engine);
        return ResolveHost(text, engine);
    }

    /// <summary>
    /// The address as the field shows it when it is not being edited: no scheme ("localhost:5173",
    /// "docs.example.dev/css/container-queries"), the host in its ASCII form; empty for the blank page.
    /// </summary>
    public static string Display(string? url)
    {
        if (string.IsNullOrEmpty(url) || WebPolicy.IsBlank(url)) return "";
        if (!WebPolicy.IsWeb(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;
        var rest = uri.PathAndQuery == "/" ? "" : uri.PathAndQuery;
        return WebPolicy.SourceName(url) + rest + uri.Fragment;
    }

    /// <summary>The address as the field shows it while editing: the full address, so it can be copied as it is.</summary>
    public static string Editing(string? url) => string.IsNullOrEmpty(url) || WebPolicy.IsBlank(url) ? "" : url;

    private static AddressResult Search(string query, SearchEngine engine) =>
        new(AddressAction.Search, SearchEngines.QueryUrl(engine, query), query);

    private static AddressResult Go(string url) => new(AddressAction.Navigate, url);

    private static AddressResult Refuse(string reason) => new(AddressAction.Refused, null, Refusal: reason);

    private static AddressResult ResolveScheme(string text, string scheme, SearchEngine engine, Func<string, bool>? isRegisteredScheme)
    {
        switch (scheme)
        {
            case "http":
            case "https":
                if (text.Any(char.IsWhiteSpace)) return Search(text, engine);
                return Uri.TryCreate(text, UriKind.Absolute, out var web) && web.Host.Length > 0 && web.Scheme is "http" or "https"
                    ? Go(web.AbsoluteUri)
                    : Search(text, engine);
            case "about":
                return WebPolicy.IsBlank(text) ? Go(WebPolicy.Blank) : Refuse(InternalRefusal);
            case "file":
                return Refuse(LocalFileRefusal);
            case "javascript":
            case "vbscript":
                return Refuse(ScriptRefusal);
            case "data":
                return Refuse(DataRefusal);
        }

        if (WebPolicy.IsInternalScheme(scheme)) return Refuse(InternalRefusal);
        if (text.Any(char.IsWhiteSpace)) return Search(text, engine);
        if (ExternalProtocols.BlockList.Contains(scheme)) return Refuse(UiText.Format("visus.address.refusal_scheme", scheme));
        var rest = text[(scheme.Length + 1)..];
        var looksLikeApp = ExternalProtocols.IsKnownApp(scheme) || rest.StartsWith("//", StringComparison.Ordinal)
            || isRegisteredScheme?.Invoke(scheme) == true;
        if (!looksLikeApp) return Search(text, engine);
        return new AddressResult(AddressAction.External, text);
    }

    private static AddressResult ResolveHost(string text, SearchEngine engine)
    {
        var end = text.IndexOfAny(['/', '?', '#']);
        var authority = end < 0 ? text : text[..end];
        if (authority.Length == 0 || authority.Contains('@', StringComparison.Ordinal) || authority.Contains('\\', StringComparison.Ordinal)) return Search(text, engine);
        if (!Uri.TryCreate("http://" + text, UriKind.Absolute, out var probe) || probe.Host.Length == 0) return Search(text, engine);
        var hasPort = authority.LastIndexOf(':') > authority.LastIndexOf(']');
        if (probe.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
        {
            // A dotted number that is not four parts ("1.5") is a number, not an address.
            if (probe.HostNameType == UriHostNameType.IPv4 && authority.Split(':')[0].Split('.').Length != 4) return Search(text, engine);
            return Go(probe.AbsoluteUri);
        }

        var host = probe.IdnHost.TrimEnd('.');
        if (WebPolicy.IsDevHost(host)) return Go(probe.AbsoluteUri);
        var labels = host.Split('.');
        if (labels.Length == 1) return hasPort ? Go(probe.AbsoluteUri) : Search(text, engine);
        if (labels.Any(label => label.Length == 0 || label.Length > 63 || label.StartsWith('-') || label.EndsWith('-'))) return Search(text, engine);
        var tld = labels[^1];
        var known = tld.StartsWith("xn--", StringComparison.OrdinalIgnoreCase)
            || (tld.All(char.IsAsciiLetter) && (tld.Length == 2 ? CountryTlds.Contains(tld) : GenericTlds.Contains(tld)));
        if (!known && !hasPort) return Search(text, engine);
        var secure = new UriBuilder(probe) { Scheme = "https", Port = probe.IsDefaultPort ? -1 : probe.Port };
        return Go(secure.Uri.AbsoluteUri);
    }

    // "localhost:5173", "example.com:8080/x": what looked like a scheme is a host and this is its port.
    private static bool StartsWithPort(string rest)
    {
        var digits = 0;
        while (digits < rest.Length && char.IsAsciiDigit(rest[digits])) digits++;
        if (digits is 0 or > 5) return false;
        if (!int.TryParse(rest.AsSpan(0, digits), NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port > 65535) return false;
        return digits == rest.Length || rest[digits] is '/' or '?' or '#';
    }

    // C:\, C:/, C:, \\server\share, ~\ and ~/, .\ and ..\ (5.2: local paths are refused at the top level).
    private static bool IsLocalPath(string text)
    {
        if (text.Length >= 2 && char.IsAsciiLetter(text[0]) && text[1] == ':' && (text.Length == 2 || text[2] is '\\' or '/')) return true;
        if (text.StartsWith(@"\\", StringComparison.Ordinal)) return true;
        if (text.StartsWith("~/", StringComparison.Ordinal) || text.StartsWith(@"~\", StringComparison.Ordinal)) return true;
        return text.StartsWith("./", StringComparison.Ordinal) || text.StartsWith(@".\", StringComparison.Ordinal)
            || text.StartsWith("../", StringComparison.Ordinal) || text.StartsWith(@"..\", StringComparison.Ordinal);
    }
}
