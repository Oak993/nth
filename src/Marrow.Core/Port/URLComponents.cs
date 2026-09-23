// PORT-ONLY: Foundation URLComponents and URLQueryItem have no .NET equivalent; UriBuilder, HttpUtility and Uri.EscapeDataString parse and encode queries differently (ED-169).
using System.Globalization;
using System.Text;
using static Marrow.Core.Port.SwiftURL;

namespace Marrow.Core.Port;

// Source: swift-foundation tag swift-6.2-RELEASE, the revision SwiftURL ports (the transfer question stays with ED-086):
//   Sources/FoundationEssentials/URL/URLComponents.swift:21-101 (storage and inits), :139-143 (scheme), :160-193 (user,
//     password, host), :230-232 (port), :242-245 (path), :274-293 (percent-encoded user and password), :343-348
//     (encoded host), :363-387 (percent-encoded path), :397-401 (percent-encoded query), :411-415 (percent-encoded
//     fragment), :425-490 (string), :590-667 (query items), :697-725 (public inits), :765-773 (url), :1016-1027
//     (percentEncodedQuery), :1137-1140 (queryItems), :1287-1307 (URLQueryItem).
//   Sources/FoundationEssentials/URL/URLParser.swift:204-233 (percent coding), :341-363 (validate), :1167-1270 (sets),
//     through SwiftURL.RFC3986Parser, which this file reuses rather than repeats.
// The same logic stands at main b898bbce (2026-09-22) with one difference no call site reaches: main's host getter
// keeps "%00" encoded (URLComponents.swift:189 there) and a file URL's path keeps "%00" and "%2F" (:253 there).
// The members are exactly those the pinned source uses (ED-169): MCPCustom.swift:174-176, MCPOAuth.swift:317, :526-528,
// :581-598, :772-778, :801-810, AppModel+HydraMerge.swift:384-387, LinkTitles.swift:25-26, :63-67.
// Not ported, no call site (ED-169): the setters of scheme, host, port, path, query and fragment and of the
// percent-encoded components (so the stores _scheme, _host, _port, _path and _fragment of :67-74 are always nil and
// didSetInvalidHost of :81 is always false), query, fragment, string, url(relativeTo:), the percent-encoded getters
// other than percentEncodedQuery, percentEncodedQueryItems, the range properties, init?(string:encodingInvalidCharacters:),
// Swift's == and hash (:1164-1193; the record's are generated and no call site compares components), description,
// Codable and the NSURLComponents bridge.

/// <summary>
/// Foundation <c>URLComponents</c> (ED-169). A Swift struct, so an immutable record (R-VAL-1): a setter is an
/// <c>init</c> accessor, <c>components.user = nil</c> is <c>components with { User = null }</c>.
/// </summary>
public sealed record URLComponents
{
    // URLComponents.swift:48-81 _URLComponents, flattened into the record (the public struct only wraps it, :21-22)
    private readonly URLParseInfo? _urlParseInfo; // :53
    private readonly bool _parseInfoIsValidForAllRanges = true; // :54
    private readonly bool _parseInfoIsValidForScheme = true; // :55
    private readonly bool _parseInfoIsValidForUser = true; // :56
    private readonly bool _parseInfoIsValidForPassword = true; // :57
    private readonly bool _parseInfoIsValidForHost = true; // :58
    private readonly bool _parseInfoIsValidForPort = true; // :59
    private readonly bool _parseInfoIsValidForPath = true; // :60
    private readonly bool _parseInfoIsValidForQuery = true; // :61
    private readonly bool _parseInfoIsValidForFragment = true; // :62
    private readonly string? _user; // :68
    private readonly string? _password; // :69
    private readonly string? _query; // :73
    private readonly bool _didPercentEncodeHost; // :77

    /// <summary><c>URLComponents()</c>: every component undefined. :697-699, :83-93.</summary>
    public URLComponents()
    {
        _parseInfoIsValidForAllRanges = false;
        _parseInfoIsValidForScheme = false;
        _parseInfoIsValidForUser = false;
        _parseInfoIsValidForPassword = false;
        _parseInfoIsValidForHost = false;
        _parseInfoIsValidForPort = false;
        _parseInfoIsValidForPath = false;
        _parseInfoIsValidForQuery = false;
        _parseInfoIsValidForFragment = false;
    }

    // :95-101
    private URLComponents(URLParseInfo parseInfo)
    {
        _urlParseInfo = parseInfo;
        _didPercentEncodeHost = parseInfo.DidPercentEncodeHost;
    }

    /// <summary>
    /// <c>URLComponents(string:)</c>: nil for text the parser refuses. Unlike <c>URL(string:)</c> an empty string parses
    /// and the scheme may not be empty. :720-725, :95-101.
    /// </summary>
    public static URLComponents? Create(string text)
    {
        // :96 Parser.parse(urlString:encodingInvalidCharacters: true), allowEmptyScheme left false (URLParser.swift:629)
        var parseInfo = RFC3986Parser.Parse(text, encodingInvalidCharacters: true);
        return parseInfo is null ? null : new URLComponents(parseInfo);
    }

    /// <summary><c>URLComponents(url:resolvingAgainstBaseURL:)</c>: the URL's string parsed again. :704-715.</summary>
    public static URLComponents? Create(SwiftURL url, bool resolvingAgainstBaseURL)
    {
        // :705-710 a SwiftURL has no base URL, so its absolute and relative strings are the same text
        var text = resolvingAgainstBaseURL ? url.AbsoluteString : url.RelativeString;
        return Create(text); // :711-714
    }

    /// <summary><c>components.scheme</c>, as written. :807-809, :139-143.</summary>
    public string? Scheme => _parseInfoIsValidForScheme ? _urlParseInfo?.Scheme : null; // :141 _scheme is nil

    /// <summary><c>components.user</c>: decoded; setting it percent-encodes what the user set does not allow. :835-838, :160-170.</summary>
    public string? User
    {
        get => RFC3986Parser.PercentDecode(PercentEncodedUser); // :161
        init
        {
            // :163 reset(.user), :113-114, :118-119
            _parseInfoIsValidForAllRanges = false;
            _parseInfoIsValidForUser = false;
            // :164-168
            _user = RFC3986Parser.Validate(value, URLComponent.User, percentEncodingAllowed: false)
                ? value
                : RFC3986Parser.PercentEncode(value, URLComponent.User);
        }
    }

    /// <summary><c>components.password</c>: decoded; setting it percent-encodes as the user does. :845-848, :172-182.</summary>
    public string? Password
    {
        get => RFC3986Parser.PercentDecode(PercentEncodedPassword); // :173
        init
        {
            // :175 reset(.password), :113-114, :120-121
            _parseInfoIsValidForAllRanges = false;
            _parseInfoIsValidForPassword = false;
            // :176-180
            _password = RFC3986Parser.Validate(value, URLComponent.Password, percentEncodingAllowed: false)
                ? value
                : RFC3986Parser.PercentEncode(value, URLComponent.Password);
        }
    }

    // UNVERIFIED O-59: macOS 26 IDNA-decodes the host (here over Windows icu.dll) and keeps an IP literal's brackets.
    /// <summary>
    /// <c>components.host</c>: percent-decoded when the parse percent-encoded it, IDNA-decoded otherwise; an IP literal
    /// keeps its brackets. :853-856, :184-193.
    /// </summary>
    public string? Host
    {
        get
        {
            if (EncodedHost is not { } encodedHost) return null; // :186
            if (encodedHost.Length == 0) return ""; // :187
            return _didPercentEncodeHost
                ? RFC3986Parser.PercentDecode(encodedHost) // :188-189
                : RFC3986Parser.IDNADecodeHost(encodedHost); // :190-191
        }
    }

    /// <summary><c>components.port</c>: <c>Int(portString)</c> of the parse. :862-864, :230-232.</summary>
    public long? Port => _parseInfoIsValidForPort ? _urlParseInfo?.Port : null; // :231 _port is nil

    /// <summary><c>components.path</c>: decoded, "" when it does not decode. :883-886, :242-245.</summary>
    public string Path => RFC3986Parser.PercentDecode(PercentEncodedPath) ?? ""; // :243

    // UNVERIFIED O-58: macOS 26 reads query items as swift-foundation does (split on "&", first "=", no "+" decoding).
    // UNVERIFIED O-57: macOS 26 encodes set query items with swift-foundation's queryItem set ("+", "/", "?" kept).
    /// <summary>
    /// <c>components.queryItems</c>: nil with no query, empty for an empty one; names and values decoded. Setting
    /// nil removes the query, an empty list makes it empty. :1137-1140, :590-667.
    /// </summary>
    public ValueList<URLQueryItem>? QueryItems
    {
        get => ReadQueryItems(); // :1138 queryItems(percentEncoded: false)
        init
        {
            // :1139 setQueryItems, :639 reset(.query), :113-114, :130-131
            _parseInfoIsValidForAllRanges = false;
            _parseInfoIsValidForQuery = false;
            _query = value is null ? null : EncodedQuery(value); // :640-643
        }
    }

    /// <summary><c>components.percentEncodedQuery</c>. :1016-1019, :397-401.</summary>
    public string? PercentEncodedQuery =>
        _query ?? (_parseInfoIsValidForQuery ? _urlParseInfo?.Query : null); // :398-400

    // UNVERIFIED O-60: macOS 26 builds url from the recomputed string through the Swift URL (foundation_swift_url_enabled).
    /// <summary>
    /// <c>components.url</c>: nil when the components make no URL string. :765-773. On the Darwin framework build the
    /// string goes to <c>URL(stringOrEmpty:)</c> when <c>foundation_swift_url_enabled()</c> (URL.swift:611-614), the
    /// path SwiftURL ports; the <c>CFURLCreateWithString</c> branch (:767-770) is not ported.
    /// </summary>
    public SwiftURL? Url
    {
        get
        {
            if (URLString is not { } text) return null; // :766
            return SwiftURL.CreateStringOrEmpty(text); // :772
        }
    }

    // :425-428 string
    private string? URLString => _parseInfoIsValidForAllRanges ? _urlParseInfo?.UrlString : ComputedString();

    // :274-279
    private string? PercentEncodedUser
    {
        get
        {
            if (_user is not null) return _user; // :275
            if (_parseInfoIsValidForUser && _urlParseInfo?.User is { } user) return user; // :276
            if (PercentEncodedPassword is not null) return ""; // :277
            return null; // :278
        }
    }

    // :289-293
    private string? PercentEncodedPassword
    {
        get
        {
            if (_password is not null) return _password; // :290
            if (_parseInfoIsValidForPassword && _urlParseInfo?.Password is { } password) return password; // :291
            return null; // :292
        }
    }

    // :343-348; _host is nil (:344)
    private string? EncodedHost
    {
        get
        {
            if (_parseInfoIsValidForHost && _urlParseInfo?.Host is { } host) return host; // :345
            if (Port is not null || PercentEncodedUser is not null) return ""; // :346
            return null; // :347
        }
    }

    // :363-367; _path is nil (:364)
    private string PercentEncodedPath =>
        _parseInfoIsValidForPath && _urlParseInfo is { } parseInfo ? parseInfo.Path : ""; // :365-366

    // :369-387
    private string PercentEncodedPathNoColon
    {
        get
        {
            var p = PercentEncodedPath; // :370
            var firstDelimiter = p.IndexOfAny([':', '/']);
            if (firstDelimiter < 0 || p[firstDelimiter] != ':') return p; // :371-373
            if (p[0] == ':') return p; // :374-380 an empty scheme passed straight through
            var firstSlash = p.IndexOf('/', StringComparison.Ordinal); // :381
            if (firstSlash < 0) firstSlash = p.Length;
            // :382-386 every ":" of the first segment becomes "%3A"
            return p[..firstSlash].Replace(":", "%3A", StringComparison.Ordinal) + p[firstSlash..];
        }
    }

    // :411-415; _fragment is nil (:412)
    private string? PercentEncodedFragment => _parseInfoIsValidForFragment ? _urlParseInfo?.Fragment : null;

    // :430-432
    private bool HasAuthority =>
        EncodedHost is not null || Port is not null || PercentEncodedUser is not null || PercentEncodedPassword is not null;

    // :434-490; didSetInvalidHost (:435) is always false, the host setters are not ported
    private string? ComputedString()
    {
        var result = new StringBuilder();
        if (Scheme is { } scheme) result.Append(scheme).Append(':'); // :437-439
        var path = PercentEncodedPath;
        if (HasAuthority)
        {
            if (path.Length > 0 && path[0] != '/') return null; // :441-443
            result.Append("//"); // :444
        }
        else
        {
            // :445-456 with no authority a path may not start with "//"
            if (path.Length >= 2 && path[0] == '/' && path[1] == '/') return null;
        }
        var percentEncodedUser = PercentEncodedUser;
        var percentEncodedPassword = PercentEncodedPassword;
        if (percentEncodedUser is not null) result.Append(percentEncodedUser); // :458-460
        if (percentEncodedPassword is not null) result.Append(':').Append(percentEncodedPassword); // :461-463
        if (percentEncodedUser is not null || percentEncodedPassword is not null) result.Append('@'); // :464-466
        if (EncodedHost is { } encodedHost) result.Append(encodedHost); // :467-469
        if (Port is { } port)
        {
            result.Append(':').Append(port.ToString(CultureInfo.InvariantCulture)); // :470-471
        }
        else if (_parseInfoIsValidForPort && _urlParseInfo?.PortString is { } portString)
        {
            result.Append(':').Append(portString); // :472-475 a port the parser let through (addressbook:)
        }
        // :476-482 with nothing before it, a ":" in the first segment would read as a scheme
        result.Append(result.Length == 0 ? PercentEncodedPathNoColon : path);
        if (PercentEncodedQuery is { } percentEncodedQuery) result.Append('?').Append(percentEncodedQuery); // :483-485
        if (PercentEncodedFragment is { } percentEncodedFragment) result.Append('#').Append(percentEncodedFragment); // :486-488
        return result.ToString(); // :489
    }

    // :590-636 queryItems(percentEncoded: false). "=" and "&" are ASCII, so UTF-16 indices split where UTF-8 ones do.
    private ValueList<URLQueryItem>? ReadQueryItems()
    {
        if (PercentEncodedQuery is not { } percentEncodedQuery) return null; // :591
        if (percentEncodedQuery.Length == 0) return ValueList<URLQueryItem>.Empty; // :592
        var result = new List<URLQueryItem>(); // :593

        var currentIndex = 0; // :595-596
        var itemStartIndex = 0; // :597
        int? equalSignIndex = null; // :598

        void AddItem()
        {
            // :600-616 called at an item's end ("&" or the end of the query)
            string name;
            string? value = null;
            if (equalSignIndex is { } equalSign)
            {
                name = percentEncodedQuery[itemStartIndex..equalSign]; // :605
                value = percentEncodedQuery[(equalSign + 1)..currentIndex]; // :606-607
            }
            else
            {
                name = percentEncodedQuery[itemStartIndex..currentIndex]; // :609
            }
            // :611-614 percentEncoded is false (percentEncodedQueryItems is not ported)
            name = RFC3986Parser.PercentDecode(name) ?? "";
            value = RFC3986Parser.PercentDecode(value);
            result.Add(new URLQueryItem(name, value)); // :615
        }

        while (currentIndex != percentEncodedQuery.Length)
        {
            // :618-632
            switch (percentEncodedQuery[currentIndex])
            {
                case '=':
                    equalSignIndex ??= currentIndex; // :620-623 the first "=" of an item
                    break;
                case '&':
                    AddItem(); // :625
                    itemStartIndex = currentIndex + 1;
                    equalSignIndex = null;
                    break;
            }
            currentIndex++;
        }
        AddItem(); // :634 the final item
        return new ValueList<URLQueryItem>(result); // :635
    }

    // :644-666 setQueryItems for a non-nil list
    private static string EncodedQuery(IReadOnlyList<URLQueryItem> items)
    {
        if (items.Count == 0) return ""; // :644-647
        return string.Join("&", items.Select(item =>
        {
            // :649-665
            var itemStr = new StringBuilder();
            itemStr.Append(RFC3986Parser.Validate(item.Name, URLComponent.QueryItem, percentEncodingAllowed: false)
                ? item.Name
                : RFC3986Parser.PercentEncode(item.Name, URLComponent.QueryItem)); // :651-655
            if (item.Value is not { } value) return itemStr.ToString(); // :656-658
            itemStr.Append('='); // :659
            itemStr.Append(RFC3986Parser.Validate(value, URLComponent.QueryItem, percentEncodingAllowed: false)
                ? value
                : RFC3986Parser.PercentEncode(value, URLComponent.QueryItem)); // :660-664
            return itemStr.ToString();
        }));
    }
}

/// <summary>
/// Foundation <c>URLQueryItem</c>, one name-value pair of a query (ED-169). URLComponents.swift:1287-1296. Equality is
/// the record's (ordinal); no call site compares items.
/// </summary>
public sealed record URLQueryItem
{
    /// <summary><c>URLQueryItem(name:value:)</c>. :1292-1295.</summary>
    public URLQueryItem(string name, string? value)
    {
        Name = name;
        Value = value;
    }

    /// <summary><c>item.name</c>. :1289.</summary>
    public string Name { get; }

    /// <summary><c>item.value</c>, nil for an item written with no "=". :1290.</summary>
    public string? Value { get; }
}
