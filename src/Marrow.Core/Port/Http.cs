// PORT-ONLY: Foundation's URLSession, URLRequest, HTTPURLResponse, URLError, URLSessionConfiguration and URLCache as the source uses them, on one HttpClient (R-NET-1, ED-118, ED-119).
using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Threading.Channels;
using Marrow.Core.Support;

namespace Marrow.Core.Port;

// Foundation unit F.HTTP (IMPLEMENTING.md 4.3), the HTTP client behind every URLSession site:
//   URLSession.shared.data(for:)   MCPHub.swift:455-477, MCPOAuth.swift:250-268, :715-742, :744-797, MCPProbe.swift:309-325,
//                                  CommandCodeAPI.swift:168-174, ZaiAPI.swift:182-194, PlanLimits.swift:243-249,
//                                  PiPlanLimits.swift:45-68, MarkdownView.swift:946-952
//   URLSession.shared.data(from:)  LinkTitles.swift:47-49
//   URLSession.shared.bytes(for:)  MCPProxy.swift:344-405, OpenAICompatibleSession.swift:623-643, :740-745
//   URLSession(configuration:)     UpdateChecker.swift:101-110, :190, :222-228
// AppUpdater.swift:459-530 (a downloadTask with a delegate) is not served: PA-0014 replaces that install path (ED-119).
//
// R-NET-1: one shared HttpClient whose own Timeout is infinite; the unit follows redirects itself (the .NET handler
// drops Authorization on a same-origin redirect, HttpTests' negative control), decodes gzip, deflate and br itself
// (so allHeaderFields keeps Content-Encoding and Content-Length as the server sent them), hands a non-2xx status over
// as a response, and runs the inactivity and resource limits on timers from SwiftClock.Provider (ED-068). A Swift
// task's cancellation, and a timer, end the exchange as URLError(.cancelled) and URLError(.timedOut) (R-TASK-2).
// Cookies and cached answers are kept in memory for the run (ED-118 item 5); both stores keep every entry in one
// collection behind a few methods, so the persistence unit F.HTTP.Persist can back them without changing callers.

/// <summary>
/// Swift <c>URLRequest</c> (a struct, so an immutable record: <see cref="SetValue"/> returns the changed copy,
/// R-VAL-1). <c>URLRequest(url:)</c> and <c>URLRequest(url:timeoutInterval:)</c> (MCPHub.swift:456,
/// CommandCodeAPI.swift:169, UpdateChecker.swift:223, MCPProxy.swift:313).
/// </summary>
public sealed record URLRequest
{
    /// <summary>URLRequest's documented default <c>timeoutInterval</c>, 60 seconds.</summary>
    internal const double DefaultTimeoutInterval = 60;

    // Header fields in the order they were first set; names match case-insensitively and keep their first spelling.
    private (string Name, string Value)[] fields = [];

    // ED-209 (OD-0022, extends ED-152). PORT-ONLY: upstream URLRequest carries no such marking; this is F.HTTP's
    // hook for "a server's custom headers only to that server" (OD-0022's Services.MCP.MCPHub wording), the same
    // treatment ED-152 already gives Authorization on a cross-origin redirect. Not part of URLRequest's Equatable
    // conformance (Equals/GetHashCode below), since it is a port-only routing hint, not an observable Swift field.
    private string[] originBoundFields = [];

    public URLRequest(Uri url, double timeoutInterval = DefaultTimeoutInterval)
    {
        Url = url;
        TimeoutInterval = timeoutInterval;
    }

    /// <summary>Swift: <c>url</c>.</summary>
    public Uri Url { get; init; }

    /// <summary>Swift: <c>timeoutInterval</c>, in seconds: the inactivity limit (MCPProxy.swift:315 sets 3,600).</summary>
    public double TimeoutInterval { get; init; }

    /// <summary>Swift: <c>httpMethod</c>, "GET" unless set (MCPProxy.swift:314).</summary>
    public string HttpMethod { get; init; } = "GET";

    // PORT-ONLY (ED-410): Local requests stay at the user's chosen server; other requests retain redirect semantics.
    public bool FollowRedirects { get; init; } = true;
    public bool UseCookies { get; init; } = true; // ED-410: independent keyless servers never share HTTP cookies

    /// <summary>Swift: <c>httpBody</c>, nil unless set (MCPHub.swift:458, MCPProxy.swift:340-342).</summary>
    public byte[]? HttpBody { get; init; }

    /// <summary>
    /// <c>setValue(_:forHTTPHeaderField:)</c>: replaces a field of the same name in any case; nil removes it
    /// (MCPHub.swift:459-462, MCPProxy.swift:323-339).
    /// </summary>
    public URLRequest SetValue(string? value, string forHTTPHeaderField)
    {
        var index = Array.FindIndex(fields, f => SameName(f.Name, forHTTPHeaderField));
        var changed = fields.ToList();
        if (value is null)
        {
            if (index >= 0) changed.RemoveAt(index);
        }
        else if (index >= 0)
        {
            changed[index] = (changed[index].Name, value);
        }
        else
        {
            changed.Add((forHTTPHeaderField, value));
        }
        return this with { fields = [.. changed] };
    }

    /// <summary><c>value(forHTTPHeaderField:)</c>, matched case-insensitively.</summary>
    public string? Value(string forHTTPHeaderField)
    {
        foreach (var (name, value) in fields)
            if (SameName(name, forHTTPHeaderField)) return value;
        return null;
    }

    /// <summary><c>allHTTPHeaderFields</c>, in the order they were set.</summary>
    internal IReadOnlyList<(string Name, string Value)> AllHTTPHeaderFields => fields;

    /// <summary>
    /// ED-209 (OD-0022): marks a header field (already set via <see cref="SetValue"/>) as origin-bound, so
    /// <c>Redirected</c> drops it, the same as Authorization (ED-152), the moment a redirect changes scheme,
    /// host or port, and keeps it across a same-origin hop. Idempotent; matches an existing mark case-insensitively.
    /// </summary>
    public URLRequest MarkOriginBound(string forHTTPHeaderField)
    {
        if (Array.Exists(originBoundFields, name => SameName(name, forHTTPHeaderField))) return this;
        return this with { originBoundFields = [.. originBoundFields, forHTTPHeaderField] };
    }

    /// <summary>ED-209: the field names <see cref="MarkOriginBound"/> marked, for <c>Bytes</c> to seed the first hop.</summary>
    internal IReadOnlyList<string> OriginBoundHeaderFields => originBoundFields;

    /// <summary><c>URLRequest.CachePolicy</c>, the two values the source uses (UpdateChecker.swift:103), with Foundation's raw values.</summary>
    public enum CachePolicy
    {
        UseProtocolCachePolicy = 0,
        ReloadIgnoringLocalCacheData = 1,
    }

    // URLRequest is Equatable: header fields and the body compare by content, not by array identity.
    public bool Equals(URLRequest? other) =>
        other is not null && Url == other.Url && TimeoutInterval.Equals(other.TimeoutInterval) &&
        HttpMethod == other.HttpMethod &&
        (HttpBody is null ? other.HttpBody is null : other.HttpBody is not null && HttpBody.AsSpan().SequenceEqual(other.HttpBody)) &&
        fields.AsSpan().SequenceEqual(other.fields);

    public override int GetHashCode() => HashCode.Combine(Url, TimeoutInterval, HttpMethod, HttpBody?.Length, fields.Length);

    internal static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Swift <c>URLResponse</c>, the type <c>data(for:)</c> and <c>bytes(for:)</c> return.</summary>
public abstract class URLResponse
{
    private protected URLResponse(Uri url, long expectedContentLength)
    {
        Url = url;
        ExpectedContentLength = expectedContentLength;
    }

    /// <summary>Swift: <c>url</c>, the URL the answer came from (after redirects).</summary>
    public Uri Url { get; }

    /// <summary>
    /// Swift: <c>expectedContentLength</c>, the Content-Length the server sent, or NSURLResponseUnknownLength (-1)
    /// when it sent none (UpdateChecker.swift:227).
    /// </summary>
    public long ExpectedContentLength { get; }
}

/// <summary>Swift <c>HTTPURLResponse</c> (MCPHub.swift:466-472, MCPOAuth.swift:255-268, UpdateChecker.swift:191, :226).</summary>
public sealed class HTTPURLResponse : URLResponse
{
    private readonly Dictionary<string, string> headers;

    internal HTTPURLResponse(Uri url, int statusCode, Dictionary<string, string> headers, long expectedContentLength)
        : base(url, expectedContentLength)
    {
        StatusCode = statusCode;
        this.headers = headers;
    }

    /// <summary>Swift: <c>statusCode</c>. Any status is a response, never an error (R-NET-1).</summary>
    public int StatusCode { get; }

    /// <summary>
    /// Swift: <c>allHeaderFields</c>. UNVERIFIED O-30: a field the server repeats is one entry, its values joined
    /// with ", " in arrival order. Names match case-insensitively.
    /// </summary>
    public IReadOnlyDictionary<string, string> AllHeaderFields => headers;

    /// <summary>Swift: <c>value(forHTTPHeaderField:)</c>, matched case-insensitively; nil when absent.</summary>
    public string? Value(string forHTTPHeaderField) => headers.TryGetValue(forHTTPHeaderField, out var value) ? value : null;
}

/// <summary>
/// Swift <c>URLError</c> (MCPProxy.swift:311, :354; OpenAICompatibleSession.swift:88-97 retries by code). It is not
/// a CancellationError: a cancelled request throws URLError(.cancelled) (R-TASK-2).
/// OD-0056: Foundation's English text for it ("The request timed out.") cannot be reproduced, and the owner chose
/// the Windows networking error's own message in its place. <see cref="ErrorDescription"/> (what
/// <c>SwiftErrors.LocalizedDescription</c> returns, the one place the decision lives) is the message of the
/// exception the failure came from (a SocketException's, an HttpRequestException's), or, for a limit this port's own
/// timer ended, Windows' text for the matching error. No other text is invented (R-ERR-2): a code that has neither
/// (badURL, unsupportedURL, httpTooManyRedirects, badServerResponse thrown by the port itself) still has none.
/// </summary>
public sealed class URLError : Exception, ILocalizedError
{
    /// <summary>Swift: <c>URLError(code)</c>; <paramref name="underlying"/> is the Windows/.NET failure it stands for, if any (OD-0056).</summary>
    public URLError(Code code, Exception? underlying = null) : base(null, underlying)
    {
        URLErrorCode = code;
    }

    /// <summary>Swift: <c>code</c> (R-NAME-3).</summary>
    public Code URLErrorCode { get; }

    /// <summary>PORT-ONLY (OD-0056): the Windows/.NET exception this error was made from; null for a limit the port's own timer ended.</summary>
    public Exception? Underlying => InnerException;

    /// <summary>
    /// Swift: <c>localizedDescription</c> (OD-0056, deviation from Foundation's wording): the underlying Windows/.NET
    /// exception's message; with none, Windows' own text for the error the code stands for (WSAETIMEDOUT for
    /// .timedOut, an aborted operation for .cancelled); otherwise null (R-ERR-2).
    /// </summary>
    public string? ErrorDescription => (InnerException is { } underlying ? WindowsText(underlying) : null) ?? URLErrorCode switch
    {
        Code.TimedOut => new SocketException((int)SocketError.TimedOut).Message,
        Code.Cancelled => new OperationCanceledException().Message,
        _ => null,
    };

    // The underlying exception's message, unless it is only .NET's wrapper ("An error occurred while sending the
    // request.") around a Winsock error: then the Windows error's own text, as OD-0056 asks. An outer message that
    // already carries the Windows text (and adds the address) is kept.
    private static string WindowsText(Exception underlying)
    {
        for (var inner = underlying.InnerException; inner is not null; inner = inner.InnerException)
            if (inner is SocketException socket)
                return underlying.Message.Contains(socket.Message, StringComparison.Ordinal) ? underlying.Message : socket.Message;
        return underlying.Message;
    }

    public override string Message => ErrorDescription ?? base.Message;

    /// <summary><c>URLError.Code</c>, with the NSURLError raw values.</summary>
    public enum Code
    {
        Unknown = -1,
        Cancelled = -999,
        BadURL = -1000,
        TimedOut = -1001,
        UnsupportedURL = -1002,
        CannotFindHost = -1003,
        CannotConnectToHost = -1004,
        NetworkConnectionLost = -1005,
        HTTPTooManyRedirects = -1007,
        BadServerResponse = -1011,
        CannotDecodeContentData = -1016,
        SecureConnectionFailed = -1200,
    }
}

/// <summary>
/// Swift <c>URLCache</c>. Before B3, the source gave it one value, nil (UpdateChecker.swift:104); the unit keeps
/// the stores behind it: <c>URLCache.shared</c> for URLSession.shared, and a new memory-only cache for each
/// <see cref="URLSessionConfiguration.Ephemeral"/> (ED-118 item 4). B3 52bc792 adds AppDelegate.swift:13
/// <c>URLCache.shared = URLCache(memoryCapacity: 8 * 1024 * 1024, diskCapacity: 0)</c> at launch, so the
/// constructor and <see cref="Shared"/>'s setter are now public for App.AppDelegate to call.
/// UNVERIFIED O-33: a GET answer is stored when its status is 200-299, it came from the server, it carries explicit
/// freshness (Cache-Control max-age, or Expires) and it is not no-store; it is answered from the store while fresh,
/// its age measured on SwiftClock; no heuristic freshness and no size cut-off (ED-118 item 4 parks both numbers
/// below: they are recorded, never enforced). Memory only (ED-118 item 5); disk capacity 0 needs no extra code,
/// because this store has never written to disk.
/// </summary>
public sealed class URLCache
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, CachedResponse> entries = new(StringComparer.Ordinal);

    internal URLCache() : this(0, 0) { }

    /// <summary>Swift: <c>URLCache(memoryCapacity:diskCapacity:)</c> (AppDelegate.swift:13, B3 52bc792).</summary>
    public URLCache(long memoryCapacity, long diskCapacity)
    {
        MemoryCapacity = memoryCapacity;
        DiskCapacity = diskCapacity;
    }

    /// <summary>Swift: <c>memoryCapacity</c>. Recorded, not enforced (ED-118 item 4, O-33 parks "no size cut-off").</summary>
    public long MemoryCapacity { get; }

    /// <summary>Swift: <c>diskCapacity</c>. Recorded, not enforced; this store is memory only regardless of its value.</summary>
    public long DiskCapacity { get; }

    /// <summary>
    /// Swift: <c>URLCache.shared</c>, the cache of URLSession.shared. A plain settable static var, so
    /// AppDelegate.swift:13 (B3 52bc792) can replace it at launch, before anything reads
    /// <see cref="URLSession.Shared"/> or calls <see cref="URLSessionConfiguration.SharedDefault"/>.
    /// </summary>
    public static URLCache Shared { get; set; } = new();

    /// <summary>The stored answer for a GET of <paramref name="url"/> if it is still fresh at <paramref name="now"/>.</summary>
    internal CachedResponse? Fresh(Uri url, ContinuousInstant now)
    {
        CachedResponse? entry;
        lock (gate)
        {
            if (!entries.TryGetValue(Key(url), out entry)) return null;
        }
        var resident = now - entry.StoredAt;
        if (resident < TimeSpan.Zero) resident = TimeSpan.Zero;
        return entry.InitialAge + resident < entry.Lifetime ? entry : null;
    }

    internal void Store(Uri url, CachedResponse entry)
    {
        lock (gate) entries[Key(url)] = entry;
    }

    /// <summary>
    /// The freshness lifetime an answer declares (RFC 9111 4.2.1): Cache-Control max-age, else Expires minus Date
    /// (minus now when there is no Date); null when it declares none, is no-store, or must be revalidated
    /// (no-cache: the unit never revalidates, so such an answer is never served from the store).
    /// </summary>
    internal static TimeSpan? DeclaredLifetime(IReadOnlyDictionary<string, string> headers)
    {
        TimeSpan? maxAge = null;
        if (headers.TryGetValue("Cache-Control", out var control))
        {
            foreach (var part in control.Split(','))
            {
                var directive = part.Trim(' ', '\t');
                var equals = directive.IndexOf('=', StringComparison.Ordinal);
                var name = equals < 0 ? directive : directive[..equals].TrimEnd(' ', '\t');
                if (name.Equals("no-store", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("no-cache", StringComparison.OrdinalIgnoreCase)) return null;
                if (name.Equals("max-age", StringComparison.OrdinalIgnoreCase) && equals >= 0 && maxAge is null)
                {
                    var text = directive[(equals + 1)..].Trim(' ', '\t').Trim('"');
                    // RFC 9111 1.2.2: an invalid delta-seconds is stale.
                    maxAge = long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                        ? TimeSpan.FromSeconds(Math.Min(seconds, int.MaxValue))
                        : TimeSpan.Zero;
                }
            }
        }
        if (maxAge is not null) return maxAge;
        if (!headers.TryGetValue("Expires", out var expiresText)) return null;
        // RFC 9111 5.3: an invalid Expires means already expired.
        if (HttpDate(expiresText) is not { } expires) return TimeSpan.Zero;
        var date = headers.TryGetValue("Date", out var dateText) && HttpDate(dateText) is { } sent ? sent : SwiftDate.Now;
        return expires - date;
    }

    /// <summary>The Age header's delta-seconds (RFC 9111 5.1), zero when absent or invalid.</summary>
    internal static TimeSpan InitialAge(IReadOnlyDictionary<string, string> headers) =>
        headers.TryGetValue("Age", out var text) &&
        long.TryParse(text.Trim(' ', '\t'), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(Math.Min(seconds, int.MaxValue))
            : TimeSpan.Zero;

    private static readonly string[] HttpDateFormats =
    [
        "ddd, dd MMM yyyy HH:mm:ss 'GMT'", // IMF-fixdate
        "dddd, dd-MMM-yy HH:mm:ss 'GMT'", // rfc850-date
        "ddd MMM d HH:mm:ss yyyy", // asctime-date
        "ddd MMM  d HH:mm:ss yyyy",
    ];

    /// <summary>An HTTP-date (RFC 9110 5.6.7) in any of its three forms, or null.</summary>
    private static DateTimeOffset? HttpDate(string text) =>
        DateTimeOffset.TryParseExact(text.Trim(' ', '\t'), HttpDateFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date) ? date : null;

    private static string Key(Uri url) => url.GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped);

    /// <summary>One stored answer, with its age at storage and its lifetime; StoredAt is on SwiftClock.</summary>
    internal sealed record CachedResponse(
        int StatusCode,
        Dictionary<string, string> Headers,
        long ExpectedContentLength,
        byte[] Body,
        ContinuousInstant StoredAt,
        TimeSpan InitialAge,
        TimeSpan Lifetime);
}

/// <summary>
/// Swift <c>HTTPCookieStorage</c>: an RFC 6265 store (UNVERIFIED O-28, ED-118 item 3). One process-wide store serves
/// URLSession.shared; each session made from <see cref="URLSessionConfiguration.Ephemeral"/> has its own, which ends
/// with the session. Memory only (ED-118 item 5). Expiry is measured on SwiftDate.Now (SwiftClock.Provider).
/// No public suffix list is consulted.
/// </summary>
internal sealed class HTTPCookieStorage
{
    private readonly Lock gate = new();
    private readonly List<Cookie> cookies = [];
    private long sequence;

    /// <summary>Swift: <c>HTTPCookieStorage.shared</c>.</summary>
    internal static HTTPCookieStorage Shared { get; } = new();

    /// <summary>RFC 6265 5.3: stores each Set-Cookie of an answer to <paramref name="url"/>.</summary>
    internal void Receive(Uri url, IEnumerable<string> setCookieValues)
    {
        var now = SwiftDate.Now;
        foreach (var header in setCookieValues)
        {
            if (Parse(header, url, now) is not { } cookie) continue;
            lock (gate)
            {
                var index = cookies.FindIndex(c => c.Name == cookie.Name && c.Domain == cookie.Domain && c.Path == cookie.Path);
                var created = sequence++;
                if (index >= 0)
                {
                    created = cookies[index].Created;
                    cookies.RemoveAt(index);
                }
                if (cookie.Expires is { } expires && expires <= now) continue;
                cookies.Add(cookie with { Created = created });
            }
        }
    }

    /// <summary>RFC 6265 5.4: the Cookie header for a request to <paramref name="url"/>, or null when none matches.</summary>
    internal string? CookieHeader(Uri url)
    {
        var now = SwiftDate.Now;
        var host = url.IdnHost;
        var path = url.AbsolutePath.Length == 0 ? "/" : url.AbsolutePath;
        var secure = url.Scheme == Uri.UriSchemeHttps;
        List<Cookie> matching;
        lock (gate)
        {
            cookies.RemoveAll(c => c.Expires is { } expires && expires <= now);
            matching = cookies.Where(c =>
                    (c.HostOnly ? string.Equals(host, c.Domain, StringComparison.OrdinalIgnoreCase) : DomainMatches(host, c.Domain))
                    && PathMatches(path, c.Path) && (!c.Secure || secure))
                .OrderByDescending(c => c.Path.Length).ThenBy(c => c.Created).ToList();
        }
        return matching.Count == 0 ? null : string.Join("; ", matching.Select(c => c.Name + "=" + c.Value));
    }

    private static Cookie? Parse(string header, Uri url, DateTimeOffset now)
    {
        // 5.2: the name-value pair up to the first ';', then the attributes.
        var semicolon = header.IndexOf(';', StringComparison.Ordinal);
        var pair = semicolon < 0 ? header : header[..semicolon];
        var equals = pair.IndexOf('=', StringComparison.Ordinal);
        if (equals < 0) return null;
        var name = pair[..equals].Trim(' ', '\t');
        var value = pair[(equals + 1)..].Trim(' ', '\t');
        if (name.Length == 0) return null;
        DateTimeOffset? expires = null;
        DateTimeOffset? maxAge = null;
        string? domain = null;
        string? path = null;
        var secure = false;
        foreach (var attribute in semicolon < 0 ? [] : header[(semicolon + 1)..].Split(';'))
        {
            var split = attribute.IndexOf('=', StringComparison.Ordinal);
            var key = (split < 0 ? attribute : attribute[..split]).Trim(' ', '\t');
            var text = split < 0 ? "" : attribute[(split + 1)..].Trim(' ', '\t');
            if (key.Equals("Expires", StringComparison.OrdinalIgnoreCase))
            {
                if (CookieDate(text) is { } date) expires = date;
            }
            else if (key.Equals("Max-Age", StringComparison.OrdinalIgnoreCase))
            {
                if (text.Length == 0 || !(char.IsAsciiDigit(text[0]) || text[0] == '-')) continue;
                if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var delta)) continue;
                maxAge = delta <= 0 ? DateTimeOffset.MinValue : now + TimeSpan.FromSeconds(Math.Min(delta, int.MaxValue));
            }
            else if (key.Equals("Domain", StringComparison.OrdinalIgnoreCase))
            {
                if (text.Length == 0) continue;
                domain = AsciiLower(text.StartsWith('.') ? text[1..] : text);
            }
            else if (key.Equals("Path", StringComparison.OrdinalIgnoreCase))
            {
                path = text.Length == 0 || text[0] != '/' ? null : text;
            }
            else if (key.Equals("Secure", StringComparison.OrdinalIgnoreCase))
            {
                secure = true;
            }
        }
        var host = url.IdnHost;
        var hostOnly = domain is null || domain.Length == 0;
        if (!hostOnly && !DomainMatches(host, domain!)) return null;
        return new Cookie(name, value, hostOnly ? host : domain!, hostOnly, path ?? DefaultPath(url), secure, maxAge ?? expires, 0);
    }

    /// <summary>5.1.4: the directory of the request path.</summary>
    private static string DefaultPath(Uri url)
    {
        var path = url.AbsolutePath;
        if (path.Length == 0 || path[0] != '/') return "/";
        var last = path.LastIndexOf('/');
        return last <= 0 ? "/" : path[..last];
    }

    /// <summary>5.1.3.</summary>
    private static bool DomainMatches(string host, string domain)
    {
        if (string.Equals(host, domain, StringComparison.OrdinalIgnoreCase)) return true;
        if (System.Net.IPAddress.TryParse(host, out _)) return false;
        return host.Length > domain.Length && host.EndsWith(domain, StringComparison.OrdinalIgnoreCase) &&
               host[host.Length - domain.Length - 1] == '.';
    }

    /// <summary>5.1.4.</summary>
    private static bool PathMatches(string requestPath, string cookiePath) =>
        requestPath == cookiePath ||
        (requestPath.StartsWith(cookiePath, StringComparison.Ordinal) &&
         (cookiePath.EndsWith('/') || requestPath[cookiePath.Length] == '/'));

    private static string AsciiLower(string text) =>
        string.Create(text.Length, text, (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
                span[i] = source[i] is >= 'A' and <= 'Z' ? (char)(source[i] + 32) : source[i];
        });

    private static readonly string[] Months = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    /// <summary>5.1.1: the cookie-date algorithm; null when the date is not valid.</summary>
    private static DateTimeOffset? CookieDate(string text)
    {
        int? hour = null, minute = null, second = null, day = null, month = null, year = null;
        foreach (var token in text.Split(Delimiters, StringSplitOptions.RemoveEmptyEntries))
        {
            if (hour is null && TimeToken(token) is { } time)
            {
                (hour, minute, second) = time;
            }
            else if (day is null && LeadingDigits(token, 1, 2) is { } d)
            {
                day = d;
            }
            else if (month is null && token.Length >= 3 &&
                     Array.IndexOf(Months, AsciiLower(token[..3])) is var m and >= 0)
            {
                month = m + 1;
            }
            else if (year is null && LeadingDigits(token, 2, 4) is { } y)
            {
                year = y;
            }
        }
        if (hour is null || day is null || month is null || year is null) return null;
        var fullYear = year.Value switch { >= 70 and <= 99 => year.Value + 1900, >= 0 and <= 69 => year.Value + 2000, _ => year.Value };
        if (day is < 1 or > 31 || fullYear < 1601 || hour > 23 || minute > 59 || second > 59) return null;
        if (day > DateTime.DaysInMonth(fullYear, month.Value)) return null;
        return new DateTimeOffset(fullYear, month.Value, day.Value, hour.Value, minute!.Value, second!.Value, TimeSpan.Zero);
    }

    private static readonly char[] Delimiters = BuildDelimiters();

    private static char[] BuildDelimiters()
    {
        var list = new List<char> { '\t' };
        for (var c = 0x20; c <= 0x2F; c++) list.Add((char)c);
        for (var c = 0x3B; c <= 0x40; c++) list.Add((char)c);
        for (var c = 0x5B; c <= 0x60; c++) list.Add((char)c);
        for (var c = 0x7B; c <= 0x7E; c++) list.Add((char)c);
        return [.. list];
    }

    /// <summary>1*2DIGIT ":" 1*2DIGIT ":" 1*2DIGIT ( non-digit *OCTET ).</summary>
    private static (int, int, int)? TimeToken(string token)
    {
        var parts = new int[3];
        var index = 0;
        for (var part = 0; part < 3; part++)
        {
            var start = index;
            while (index < token.Length && index - start < 3 && char.IsAsciiDigit(token[index])) index++;
            var length = index - start;
            if (length is < 1 or > 2) return null;
            parts[part] = int.Parse(token.AsSpan(start, length), NumberStyles.None, CultureInfo.InvariantCulture);
            if (part < 2)
            {
                if (index >= token.Length || token[index] != ':') return null;
                index++;
            }
        }
        if (index < token.Length && char.IsAsciiDigit(token[index])) return null;
        return (parts[0], parts[1], parts[2]);
    }

    /// <summary><paramref name="min"/> to <paramref name="max"/> digits, then a non-digit or the end.</summary>
    private static int? LeadingDigits(string token, int min, int max)
    {
        var count = 0;
        while (count < token.Length && char.IsAsciiDigit(token[count])) count++;
        if (count < min || count > max) return null;
        return int.Parse(token.AsSpan(0, count), NumberStyles.None, CultureInfo.InvariantCulture);
    }

    internal sealed record Cookie(
        string Name, string Value, string Domain, bool HostOnly, string Path, bool Secure, DateTimeOffset? Expires, long Created);
}

/// <summary>
/// Swift <c>URLSessionConfiguration</c>, the surface UpdateChecker.swift:101-110 uses (ED-119). The properties are
/// the Swift class's <c>var</c>s; a session copies them when it is made, as URLSession(configuration:) does.
/// </summary>
public sealed class URLSessionConfiguration
{
    private URLSessionConfiguration(URLCache? urlCache) => UrlCache = urlCache;

    /// <summary>
    /// Swift: <c>URLSessionConfiguration.ephemeral</c> (UpdateChecker.swift:102), a new configuration on every read.
    /// Its cache is a new memory-only URLCache private to it (ED-118 item 4); its sessions keep cookies to themselves.
    /// </summary>
    public static URLSessionConfiguration Ephemeral => new(new URLCache());

    /// <summary>The configuration URLSession.shared is made from: URLCache.shared, and the process-wide cookie store.</summary>
    internal static URLSessionConfiguration SharedDefault() => new(URLCache.Shared);

    /// <summary>Swift: <c>requestCachePolicy</c>, <c>.useProtocolCachePolicy</c> unless set (UpdateChecker.swift:103).</summary>
    public URLRequest.CachePolicy RequestCachePolicy { get; set; } = URLRequest.CachePolicy.UseProtocolCachePolicy;

    /// <summary>Swift: <c>urlCache</c>; nil means no cache at all (UpdateChecker.swift:104).</summary>
    public URLCache? UrlCache { get; set; }

    /// <summary>Swift: <c>timeoutIntervalForRequest</c>, 60 s unless set (UpdateChecker.swift:105): the inactivity limit.</summary>
    public double TimeoutIntervalForRequest { get; set; } = 60;

    /// <summary>Swift: <c>timeoutIntervalForResource</c>, 7 days unless set (UpdateChecker.swift:106): the limit on a whole task.</summary>
    public double TimeoutIntervalForResource { get; set; } = 604_800;

    /// <summary>
    /// Swift: <c>waitsForConnectivity</c>, false unless set (UpdateChecker.swift:107). With false a connection that
    /// cannot be made fails the task at once, the one behaviour the source uses.
    /// </summary>
    public bool WaitsForConnectivity { get; set; }

    /// <summary>Swift: <c>httpAdditionalHeaders</c> (UpdateChecker.swift:108); a request field of the same name wins.</summary>
    public IReadOnlyDictionary<string, string>? HttpAdditionalHeaders { get; set; }
}

/// <summary>
/// Swift <c>URLSession</c>: <c>URLSession.shared</c> and <c>URLSession(configuration:)</c>, over one HttpClient (R-NET-1).
/// </summary>
public sealed class URLSession
{
    // R-NET-1: one HttpClient for every session. No automatic redirect (the unit follows them itself, keeping
    // Authorization to the same origin, O-29), no automatic decompression (the unit decodes, keeping the headers as
    // sent), no cookie container (the sessions' stores), and no timeout of its own (the unit's timers).
    private static readonly SocketsHttpHandler DefaultHandler = new()
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = System.Net.DecompressionMethods.None,
        UseCookies = false,
    };

    private static readonly HttpClient Client = new(DefaultHandler)
    {
        Timeout = System.Threading.Timeout.InfiniteTimeSpan,
    };

    /// <summary>
    /// PORT-ONLY (OD-0057): the test seam on the HTTP client. Every request of every session is sent through the
    /// handler in <c>Transport.Current</c>, and its process default is the handler the port has always used, so
    /// production behaviour is unchanged: nothing outside a test overrides it. A test overrides it for its own
    /// async flow with a handler that answers in-process (an https upstream no stand-in server can be, R-TEST-9,
    /// R-TEST-10). Only the wire is replaced: the redirect, decoding, timer and error rules of this unit still run
    /// on top, and an overriding handler gets the same client settings (no timeout of its own, R-NET-1). It is
    /// read once per request, in the caller's flow, so a background loop sees the override only if it was
    /// started inside it.
    /// </summary>
    public static Ambient<HttpMessageHandler> Transport { get; } = new(DefaultHandler);

    // UNVERIFIED O-48: URLSession follows 20 redirects and fails the 21st with URLError(.httpTooManyRedirects);
    // swift-corelibs-foundation swift-6.2-RELEASE FoundationNetworking/URLSession/HTTP/HTTPURLProtocol.swift:459-465
    // ("This value mirrors Darwin"); CFNetwork's value is not documented.
    private const int MaximumRedirects = 20;

    // ED-118: the constant Accept-Language (UNVERIFIED O-27; the app ships English only, project.pbxproj:107-112).
    private const string AcceptLanguage = "en-US,en;q=0.9";
    private const string AcceptEncoding = "gzip, deflate, br";

    private readonly URLRequest.CachePolicy requestCachePolicy;
    private readonly URLCache? urlCache;
    private readonly double timeoutIntervalForRequest;
    private readonly double timeoutIntervalForResource;
    private readonly (string Name, string Value)[] additionalHeaders;
    private readonly HTTPCookieStorage cookies;

    /// <summary>Swift: <c>URLSession(configuration:)</c> (UpdateChecker.swift:109): copies the configuration.</summary>
    public URLSession(URLSessionConfiguration configuration) : this(configuration, new HTTPCookieStorage()) { }

    private URLSession(URLSessionConfiguration configuration, HTTPCookieStorage cookies)
    {
        requestCachePolicy = configuration.RequestCachePolicy;
        urlCache = configuration.UrlCache;
        timeoutIntervalForRequest = configuration.TimeoutIntervalForRequest;
        timeoutIntervalForResource = configuration.TimeoutIntervalForResource;
        additionalHeaders = configuration.HttpAdditionalHeaders?.Select(pair => (pair.Key, pair.Value)).ToArray() ?? [];
        this.cookies = cookies;
    }

    /// <summary>Swift: <c>URLSession.shared</c> (41 sites): URLCache.shared and HTTPCookieStorage.shared.</summary>
    public static URLSession Shared { get; } = new(URLSessionConfiguration.SharedDefault(), HTTPCookieStorage.Shared);

    /// <summary>
    /// PORT-ONLY (accepted by ED-119): the Timeout of the HttpClient underneath, which R-NET-1 requires to be
    /// infinite; the manual clock cannot see .NET's own 100 s limit, so tests read it here.
    /// </summary>
    public TimeSpan HttpClientTimeout => Client.Timeout;

    /// <summary>Swift: <c>data(for:)</c>. Returns when the whole body has arrived; any status is a response.</summary>
    public async Task<(byte[] Data, URLResponse Response)> Data(URLRequest request)
    {
        var (response, bytes) = await Start(request);
        return (await bytes.ReadAll(), response);
    }

    /// <summary>Swift: <c>data(from:)</c> (LinkTitles.swift:47, UpdateChecker.swift:190): a GET of the URL.</summary>
    public Task<(byte[] Data, URLResponse Response)> Data(Uri @from) => Data(new URLRequest(@from));

    /// <summary>
    /// Swift: <c>bytes(for:)</c> (MCPProxy.swift:344, OpenAICompatibleSession.swift:623). Returns at the response head;
    /// the body arrives through the byte sequence while the exchange goes on.
    /// </summary>
    public async Task<(AsyncBytes Bytes, URLResponse Response)> Bytes(URLRequest request)
    {
        var (response, bytes) = await Start(request);
        return (bytes, response);
    }

    private async Task<(HTTPURLResponse Response, AsyncBytes Bytes)> Start(URLRequest request)
    {
        // Read in the caller's flow, when the request is built: the clock, the Swift task and the app (ED-118, ED-119).
        var clock = SwiftClock.Provider.Current;
        var task = SwiftTask.CancellationToken;
        var app = AppInfo.Current;
        // PORT-ONLY (OD-0057): the handler a test put on this flow; the process default is today's client.
        var handler = Transport.Current;
        var transport = ReferenceEquals(handler, DefaultHandler) ? Client
            : new HttpClient(handler, disposeHandler: false) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        if (task.IsCancellationRequested) throw new URLError(URLError.Code.Cancelled);
        if (!request.Url.IsAbsoluteUri || (request.Url.Scheme != Uri.UriSchemeHttp && request.Url.Scheme != Uri.UriSchemeHttps))
            throw new URLError(URLError.Code.UnsupportedURL);

        // UNVERIFIED O-31: a request that keeps URLRequest's default 60 s takes the configuration's
        // timeoutIntervalForRequest; a request that sets its own interval keeps it (MCPProxy.swift:315 on the shared session).
        var idle = request.TimeoutInterval == URLRequest.DefaultTimeoutInterval ? timeoutIntervalForRequest : request.TimeoutInterval;
        var exchange = new Exchange(clock, SwiftTask.Seconds(idle), SwiftTask.Seconds(timeoutIntervalForResource), task);
        var hop = new Hop(request.Url, request.HttpMethod, request.HttpBody, [.. request.AllHTTPHeaderFields], LeftOrigin: false,
            [.. request.OriginBoundHeaderFields]);
        var readsCache = urlCache is not null && requestCachePolicy == URLRequest.CachePolicy.UseProtocolCachePolicy;
        for (var redirects = 0; ; redirects++)
        {
            if (readsCache && hop.Method == "GET" && urlCache!.Fresh(hop.Url, SwiftClock.Now) is { } cached)
            {
                exchange.Complete();
                return (new HTTPURLResponse(hop.Url, cached.StatusCode, new(cached.Headers, StringComparer.OrdinalIgnoreCase),
                    cached.ExpectedContentLength), AsyncBytes.Stored(cached.Body));
            }
            HttpResponseMessage response;
            try
            {
                using var message = Build(hop, app, request.UseCookies);
                exchange.ArmIdle();
                response = await transport.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, exchange.Token);
            }
            catch (Exception error) when (SwiftTry.IsThrownError(error))
            {
                throw exchange.End(error, inBody: false);
            }
            exchange.ArmIdle();
            if (request.UseCookies && response.Headers.NonValidated.TryGetValues("Set-Cookie", out var setCookies))
                cookies.Receive(hop.Url, setCookies);
            var status = (int)response.StatusCode;
            if (request.FollowRedirects && (status is 301 or 302 or 303 or 307 or 308) && Location(response, hop.Url) is { } location)
            {
                response.Dispose();
                if (redirects == MaximumRedirects) throw exchange.End(new URLError(URLError.Code.HTTPTooManyRedirects), inBody: false);
                hop = Redirected(hop, status, location);
                continue;
            }
            var headers = HeaderFields(response);
            var length = headers.TryGetValue("Content-Length", out var lengthText) &&
                         long.TryParse(lengthText.Trim(' ', '\t'), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : -1;
            var answer = new HTTPURLResponse(hop.Url, status, headers, length);
            // UNVERIFIED O-33: what URLCache stores (ED-118 item 4).
            Action<byte[]>? store = null;
            if (urlCache is { } cache && hop.Method == "GET" && status is >= 200 and <= 299 &&
                URLCache.DeclaredLifetime(headers) is { } lifetime && lifetime > TimeSpan.Zero)
            {
                var url = hop.Url;
                var initialAge = URLCache.InitialAge(headers);
                store = body => cache.Store(url, new URLCache.CachedResponse(status, headers, length, body,
                    SwiftClock.Now, initialAge, lifetime));
            }
            return (answer, AsyncBytes.Live(exchange, response, store));
        }
    }

    /// <summary>The request of one hop, as the session sends it (ED-118 defaults, the session's cookies).</summary>
    private HttpRequestMessage Build(Hop hop, AppInfo app, bool useCookies)
    {
        var fields = new List<(string Name, string Value)>();
        void Put(string name, string value)
        {
            var index = fields.FindIndex(f => URLRequest.SameName(f.Name, name));
            if (index >= 0) fields[index] = (fields[index].Name, value);
            else fields.Add((name, value));
        }
        bool Has(string name) => fields.Exists(f => URLRequest.SameName(f.Name, name));
        foreach (var (name, value) in additionalHeaders)
        {
            // UNVERIFIED O-29 (safety default): once a chain has left an origin, a configuration Authorization is
            // not sent again, as for a request field. ED-152.
            if (hop.LeftOrigin && name.Equals("Authorization", StringComparison.OrdinalIgnoreCase)) continue;
            Put(name, value);
        }
        foreach (var (name, value) in hop.Fields) Put(name, value);
        // DEVIATION OD-0012|ED-088: the default User-Agent is "Marrow/<AppInfo.version>", not the source's
        // "Droppy%20Code/<build> CFNetwork/<v> Darwin/<v>" (ED-118 item 1); one set by the configuration or the request replaces it.
        if (!Has("User-Agent")) Put("User-Agent", "nth/" + app.Version);
        // UNVERIFIED O-27: the fields CFNetwork adds when none is set (ED-118 item 2).
        if (!Has("Accept")) Put("Accept", "*/*");
        if (!Has("Accept-Language")) Put("Accept-Language", AcceptLanguage);
        if (!Has("Accept-Encoding")) Put("Accept-Encoding", AcceptEncoding);
        if (hop.Body is not null && !Has("Content-Type")) Put("Content-Type", "application/x-www-form-urlencoded");
        // UNVERIFIED O-28: the session's cookies go to every request they match.
        if (useCookies && !Has("Cookie") && cookies.CookieHeader(hop.Url) is { } cookie) Put("Cookie", cookie);

        var message = new HttpRequestMessage(new HttpMethod(hop.Method), hop.Url);
        HttpContent? content = hop.Body is { } body ? new ByteArrayContent(body) : null;
        foreach (var (name, value) in fields)
        {
            // The URL loading system owns these (Apple's list of reserved fields): the length comes from the body.
            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
            if (message.Headers.TryAddWithoutValidation(name, value)) continue;
            content ??= new ByteArrayContent([]);
            content.Headers.TryAddWithoutValidation(name, value);
        }
        message.Content = content;
        return message;
    }

    /// <summary>The Location of a redirect, resolved against the URL it answered; null when missing or unusable.</summary>
    private static Uri? Location(HttpResponseMessage response, Uri current)
    {
        if (!response.Headers.NonValidated.TryGetValues("Location", out var values)) return null;
        var text = values.FirstOrDefault();
        if (text is null || !Uri.TryCreate(current, text.Trim(' ', '\t'), out var target)) return null;
        return target.Scheme == Uri.UriSchemeHttp || target.Scheme == Uri.UriSchemeHttps ? target : null;
    }

    /// <summary>
    /// The next hop: 303 becomes GET (RFC 9110 15.4.4), and so does a POST answered with 301 or 302 (15.4.2, 15.4.3);
    /// 307 and 308 repeat the method and body. A body dropped takes its content fields with it. UNVERIFIED O-29:
    /// to the same origin every other field is kept; after leaving the first origin (scheme, host and port),
    /// Authorization is dropped for the rest of the chain, including configuration headers (ED-152). ED-209
    /// (OD-0022): a caller-marked origin-bound field (URLRequest.MarkOriginBound; MCPWire.Post's server headers)
    /// is dropped the same way, the moment the chain leaves the origin it started at, and never restored.
    /// </summary>
    private static Hop Redirected(Hop hop, int status, Uri target)
    {
        var method = hop.Method;
        var body = hop.Body;
        var fields = hop.Fields.ToList();
        if ((status == 303 && method != "HEAD") || (status is 301 or 302 && method == "POST"))
        {
            method = "GET";
            body = null;
            fields.RemoveAll(f => f.Name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase));
        }
        var sameOrigin = hop.Url.Scheme == target.Scheme &&
                         string.Equals(hop.Url.IdnHost, target.IdnHost, StringComparison.OrdinalIgnoreCase) &&
                         hop.Url.Port == target.Port;
        if (!sameOrigin)
        {
            fields.RemoveAll(f => f.Name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
                                   hop.OriginBoundFields.Any(name => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
        }
        return new Hop(target, method, body, [.. fields], hop.LeftOrigin || !sameOrigin, hop.OriginBoundFields);
    }

    /// <summary>allHeaderFields: every field as received; a repeated one joined with ", " in arrival order (O-30).</summary>
    private static Dictionary<string, string> HeaderFields(HttpResponseMessage response)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Add(HttpHeadersNonValidated headers)
        {
            foreach (var (name, values) in headers)
            {
                var joined = string.Join(", ", values);
                fields[name] = fields.TryGetValue(name, out var earlier) ? earlier + ", " + joined : joined;
            }
        }
        Add(response.Headers.NonValidated);
        Add(response.Content.Headers.NonValidated);
        return fields;
    }

    private sealed record Hop(Uri Url, string Method, byte[]? Body, (string Name, string Value)[] Fields, bool LeftOrigin,
        string[] OriginBoundFields);

    /// <summary>
    /// One task's limits and end (R-NET-1, ED-068): the inactivity timer, re-armed before the request is written, at
    /// the response head and whenever body bytes arrive (before they reach the caller); the resource timer, armed when
    /// the task starts and never re-armed; and the Swift task's cancellation. The first of them to fire decides the
    /// error; the exchange is then aborted. Both timers come from SwiftClock.Provider as the caller saw it.
    /// </summary>
    internal sealed class Exchange
    {
        // TimeProvider.System's timers take at most uint.MaxValue - 1 ms; a longer limit is never reached in a run.
        private static readonly TimeSpan LongestTimer = TimeSpan.FromMilliseconds(uint.MaxValue - 1d);

        private readonly Lock gate = new();
        private readonly CancellationTokenSource abort = new();
        private readonly TimeSpan idleInterval;
        private readonly ITimer idle;
        private readonly ITimer resource;
        private readonly CancellationTokenRegistration taskCancellation;
        private URLError.Code? failure;
        private bool ended;

        internal Exchange(TimeProvider clock, TimeSpan idleInterval, TimeSpan resourceInterval, CancellationToken task)
        {
            this.idleInterval = idleInterval;
            idle = clock.CreateTimer(_ => Fail(URLError.Code.TimedOut), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            resource = clock.CreateTimer(_ => Fail(URLError.Code.TimedOut), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            if (resourceInterval <= LongestTimer) resource.Change(Clamp(resourceInterval), Timeout.InfiniteTimeSpan);
            taskCancellation = task.Register(() => Fail(URLError.Code.Cancelled));
        }

        internal CancellationToken Token => abort.Token;

        internal void ArmIdle()
        {
            lock (gate)
            {
                if (ended || failure is not null || idleInterval > LongestTimer) return;
                idle.Change(Clamp(idleInterval), Timeout.InfiniteTimeSpan);
            }
        }

        /// <summary>Ends the exchange with <paramref name="code"/> unless it already ended or failed.</summary>
        internal void Fail(URLError.Code code)
        {
            lock (gate)
            {
                if (ended || failure is not null) return;
                failure = code;
            }
            // Aborted off the firing thread: a manual clock fires timers on the thread that advances it.
            ThreadPool.UnsafeQueueUserWorkItem(static source => source.Cancel(), abort, preferLocal: false);
        }

        /// <summary>The task finished with its whole body: the limits stop. False when a limit or a cancel came first.</summary>
        internal bool Complete()
        {
            lock (gate)
            {
                if (failure is not null) return false;
                if (!ended) Stop();
                return true;
            }
        }

        /// <summary>The URLError the task fails with for <paramref name="error"/>; the limits stop.</summary>
        internal URLError End(Exception error, bool inBody)
        {
            URLError.Code code;
            bool fromTransport;
            lock (gate)
            {
                fromTransport = failure is null;
                code = failure ?? Translate(error, inBody);
                failure ??= code;
                if (!ended) Stop();
            }
            // OD-0056: an error the port's own timer or a cancel ended carries no exception, one a transport raised keeps it.
            return error is URLError same && same.URLErrorCode == code ? same : new URLError(code, fromTransport ? error : null);
        }

        internal URLError.Code? Failure
        {
            get { lock (gate) return failure; }
        }

        internal bool Ended
        {
            get { lock (gate) return ended; }
        }

        private void Stop()
        {
            ended = true;
            idle.Dispose();
            resource.Dispose();
            // A cancellation callback may already be waiting for gate. Unregister does not wait for it here.
            taskCancellation.Unregister();
        }

        private static TimeSpan Clamp(TimeSpan interval) => interval < TimeSpan.Zero ? TimeSpan.Zero : interval;

        /// <summary>The URLError code for a .NET transport failure.</summary>
        private static URLError.Code Translate(Exception error, bool inBody) => error switch
        {
            URLError known => known.URLErrorCode,
            InvalidDataException => URLError.Code.CannotDecodeContentData,
            HttpRequestException { HttpRequestError: var kind } => kind switch
            {
                HttpRequestError.NameResolutionError => URLError.Code.CannotFindHost,
                HttpRequestError.ConnectionError or HttpRequestError.ProxyTunnelError =>
                    inBody ? URLError.Code.NetworkConnectionLost : URLError.Code.CannotConnectToHost,
                HttpRequestError.SecureConnectionError => URLError.Code.SecureConnectionFailed,
                HttpRequestError.InvalidResponse or HttpRequestError.ConfigurationLimitExceeded or
                    HttpRequestError.HttpProtocolError => URLError.Code.BadServerResponse,
                _ => URLError.Code.NetworkConnectionLost,
            },
            HttpIOException { HttpRequestError: HttpRequestError.InvalidResponse } => URLError.Code.BadServerResponse,
            IOException or SocketException or ObjectDisposedException => URLError.Code.NetworkConnectionLost,
            // A cancellation nobody recorded (the exchange's own abort always records its reason first).
            OperationCanceledException => URLError.Code.Cancelled,
            _ => URLError.Code.Unknown,
        };
    }

    /// <summary>
    /// Swift <c>URLSession.AsyncBytes</c> (MCPProxy.swift:377-383, OpenAICompatibleSession.swift:636, :742): the body
    /// as bytes, read while the exchange goes on. The session receives the body in the background, as URLSession does;
    /// a transport failure, a limit or a cancel is thrown by the read that reaches it, after the bytes that came first.
    /// Disposing an enumerator before the body is complete cancels the task.
    /// </summary>
    public sealed class AsyncBytes : IAsyncEnumerable<byte>
    {
        private readonly Channel<byte[]> chunks = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleWriter = true });
        private readonly Exchange? exchange;
        private URLError? error;

        private AsyncBytes(Exchange? exchange) => this.exchange = exchange;

        internal static AsyncBytes Stored(byte[] body)
        {
            var bytes = new AsyncBytes(null);
            if (body.Length > 0) bytes.chunks.Writer.TryWrite(body);
            bytes.chunks.Writer.TryComplete();
            return bytes;
        }

        internal static AsyncBytes Live(Exchange exchange, HttpResponseMessage response, Action<byte[]>? store)
        {
            var bytes = new AsyncBytes(exchange);
            _ = bytes.Receive(response, store);
            return bytes;
        }

        /// <summary>Swift: <c>lines</c> (UNVERIFIED O-26): LF, CR, CRLF, NEL, LS and PS end a line; no empty line is yielded.</summary>
        public IAsyncEnumerable<string> Lines => ReadLines();

        public IAsyncEnumerator<byte> GetAsyncEnumerator(CancellationToken cancellationToken = default) => new ByteReader(this);

        internal async Task<byte[]> ReadAll()
        {
            using var all = new MemoryStream();
            try
            {
                while (await NextChunk() is { } chunk) all.Write(chunk);
            }
            finally
            {
                Release();
            }
            return all.ToArray();
        }

        private async Task Receive(HttpResponseMessage response, Action<byte[]>? store)
        {
            var exchange = this.exchange!;
            using var kept = store is null ? null : new MemoryStream();
            try
            {
                using (response)
                {
                    var raw = await response.Content.ReadAsStreamAsync(exchange.Token);
                    await using var body = Decoded(new ArrivalStream(raw, exchange), response);
                    var buffer = new byte[16 * 1024];
                    while (true)
                    {
                        var read = await body.ReadAsync(buffer, exchange.Token);
                        if (read == 0) break;
                        var chunk = buffer.AsSpan(0, read).ToArray();
                        kept?.Write(chunk);
                        chunks.Writer.TryWrite(chunk);
                    }
                }
                if (!exchange.Complete()) error = exchange.Failure is { } limit ? new URLError(limit) : null;
                else store?.Invoke(kept!.ToArray());
            }
            catch (Exception failure) when (SwiftTry.IsThrownError(failure))
            {
                error = exchange.End(failure, inBody: true);
            }
            finally
            {
                chunks.Writer.TryComplete();
            }
        }

        /// <summary>The body with its Content-Encoding removed: gzip, deflate (zlib, RFC 9110 8.4.1.2) and br (R-NET-1).</summary>
        private static Stream Decoded(Stream raw, HttpResponseMessage response)
        {
            if (!response.Content.Headers.NonValidated.TryGetValues("Content-Encoding", out var values)) return raw;
            var codings = values.SelectMany(v => v.Split(',')).Select(c => c.Trim(' ', '\t'))
                .Where(c => c.Length > 0 && !c.Equals("identity", StringComparison.OrdinalIgnoreCase)).ToList();
            if (codings.Exists(c => !(c.Equals("gzip", StringComparison.OrdinalIgnoreCase) || c.Equals("x-gzip", StringComparison.OrdinalIgnoreCase) ||
                                      c.Equals("deflate", StringComparison.OrdinalIgnoreCase) || c.Equals("br", StringComparison.OrdinalIgnoreCase))))
                return raw;
            var stream = raw;
            for (var i = codings.Count - 1; i >= 0; i--)
            {
                var coding = codings[i];
                stream = coding.Equals("br", StringComparison.OrdinalIgnoreCase) ? new BrotliStream(stream, CompressionMode.Decompress)
                    : coding.Equals("deflate", StringComparison.OrdinalIgnoreCase) ? new ZLibStream(stream, CompressionMode.Decompress)
                    : new GZipStream(stream, CompressionMode.Decompress);
            }
            return stream;
        }

        /// <summary>The next piece of the body, null at its end; throws the URLError the exchange ended with.</summary>
        private async Task<byte[]?> NextChunk()
        {
            while (await chunks.Reader.WaitToReadAsync())
                if (chunks.Reader.TryRead(out var chunk)) return chunk;
            if (error is { } ended) throw new URLError(ended.URLErrorCode, ended.Underlying);
            return null;
        }

        private void Release()
        {
            if (exchange is { Ended: false }) exchange.Fail(URLError.Code.Cancelled);
        }

        private async IAsyncEnumerable<string> ReadLines()
        {
            var line = new List<byte>();
            try
            {
                while (await NextChunk() is { } chunk)
                {
                    foreach (var value in chunk)
                    {
                        line.Add(value);
                        if (TerminatorLength(line) is var cut and > 0)
                        {
                            if (line.Count > cut) yield return SwiftText.DecodeUtf8Repairing(line.GetRange(0, line.Count - cut).ToArray());
                            line.Clear();
                        }
                    }
                }
                if (line.Count > 0) yield return SwiftText.DecodeUtf8Repairing(line.ToArray());
            }
            finally
            {
                Release();
            }
        }

        /// <summary>
        /// UNVERIFIED O-26: the length of the line terminator the buffer ends with: LF or CR (one byte), NEL (C2 85),
        /// LS (E2 80 A8) or PS (E2 80 A9); 0 when it ends with none. A CRLF pair ends one line, because the LF after a
        /// CR ends an empty line, which is not yielded.
        /// </summary>
        private static int TerminatorLength(List<byte> line)
        {
            var n = line.Count;
            var last = line[n - 1];
            if (last is 0x0A or 0x0D) return 1;
            if (last == 0x85 && n >= 2 && line[n - 2] == 0xC2) return 2;
            if (last is 0xA8 or 0xA9 && n >= 3 && line[n - 2] == 0x80 && line[n - 3] == 0xE2) return 3;
            return 0;
        }

        private sealed class ByteReader(AsyncBytes owner) : IAsyncEnumerator<byte>
        {
            private byte[] chunk = [];
            private int index = -1;

            public byte Current => chunk[index];

            public async ValueTask<bool> MoveNextAsync()
            {
                if (index + 1 < chunk.Length)
                {
                    index += 1;
                    return true;
                }
                if (await owner.NextChunk() is not { } next) return false;
                chunk = next;
                index = 0;
                return true;
            }

            public ValueTask DisposeAsync()
            {
                owner.Release();
                return ValueTask.CompletedTask;
            }
        }
    }

    /// <summary>The raw body as it arrives from the connection: every read that brings bytes re-arms the idle limit.</summary>
    private sealed class ArrivalStream(Stream inner, Exchange exchange) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            if (read > 0) exchange.ArmIdle();
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            if (read > 0) exchange.ArmIdle();
            return read;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
